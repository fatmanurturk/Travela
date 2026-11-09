using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Travela.Api.Models;
using Travela.Api.Notifications;
using Travela.Api.Security;
using Travela.Api.Trips;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

internal static class NotificationChangeChecks
{
    public static async Task Run(TravelaDbContext db, HttpClient http, Action<bool, string> check, string root)
    {
        var company = new Company { Name = "Bildirim Değişiklik Firması" };
        var route = new Travela.Api.Models.Route { DepartureCity = "İzmir", ArrivalCity = "Bursa", EstimatedDurationMinutes = 180 };
        var bus = new Bus { Company = company, PlateNumber = "35 NOTE 1", NormalizedPlateNumber = "35NOTE1", SeatCapacity = 8, SeatLayout = "2+2", SeatPlanJson = SeatPlans.Write(SeatPlans.Create("2+2", 8)) };
        var replacement = new Bus { Company = company, PlateNumber = "35 NOTE 2", NormalizedPlateNumber = "35NOTE2", SeatCapacity = 8, SeatLayout = "2+2", SeatPlanJson = SeatPlans.Write(SeatPlans.Create("2+2", 8)) };
        var incompatible = new Bus { Company = company, PlateNumber = "35 NOTE 3", NormalizedPlateNumber = "35NOTE3", SeatCapacity = 8, SeatLayout = "2+1" };
        var trip = new Trip { Company = company, Bus = bus, Route = route, SeatLayout = "2+2", SeatPlanJson = bus.SeatPlanJson, SeatCapacitySnapshot = 8, SellableSeatCount = 8, AvailableSeats = 8, Price = 400, DepartureTime = DateTime.UtcNow.AddDays(8), ArrivalTime = DateTime.UtcNow.AddDays(8).AddHours(3) };
        User Account(string name, string role) { var u = new User { FullName = name, Email = name + "@notice.example", Role = role, CompanyId = role == "Company" ? company.Id : null, PhoneNumber = "+905321234567", EmailNotificationsEnabled = true, SmsNotificationsEnabled = true, TripRemindersEnabled = true, CancellationNotificationsEnabled = true, RefundNotificationsEnabled = true }; u.PasswordHash = new PasswordHasher<User>().HashPassword(u, "TestPass123!"); return u; }
        db.AddRange(trip, replacement, incompatible); await db.SaveChangesAsync();
        var passenger = Account("passenger", "Passenger"); var manager = Account("manager", "Company"); var admin = Account("admin", "Admin"); var stranger = Account("stranger", "Passenger"); db.AddRange(passenger, manager, admin, stranger); await db.SaveChangesAsync();
        async Task<HttpResponseMessage> Send(string method, string path, string? token, object? body = null) { using var request = new HttpRequestMessage(new HttpMethod(method), path); if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); if (body != null) request.Content = JsonContent.Create(body); return await http.SendAsync(request); }
        async Task<JsonElement> Json(HttpResponseMessage response) { if (!response.IsSuccessStatusCode) throw new Exception(response.StatusCode + ": " + await response.Content.ReadAsStringAsync()); return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone(); }
        async Task<string> Login(User u) => (await Json(await Send("POST", "/api/auth/login", null, new { email = u.Email, password = "TestPass123!" }))).GetProperty("token").GetString()!;
        var pt = await Login(passenger); var mt = await Login(manager); var at = await Login(admin); var st = await Login(stranger);
        async Task<PurchaseDto> Buy(int seat) => (await (await Send("POST", "/api/tickets/checkout", pt,
            new { tripId = trip.Id, seatNumber = seat, requestKey = Guid.NewGuid(), legalAcceptances = GroupPurchaseChecks.SalesConsent() })).Content.ReadFromJsonAsync<PurchaseDto>())!;
        var purchase = await Buy(1); purchase = (await (await Send("POST", $"/api/tickets/local/{purchase.Id}/complete", pt, new { outcome = "Succeeded" })).Content.ReadFromJsonAsync<PurchaseDto>())!;
        check(purchase.Pnr != null && await db.TravelNotifications.CountAsync(n => n.Reservation.Payment!.Id == purchase.Id && n.Type == "Purchase" && n.Status == "Pending") == 2, "Ödeme doğrulanınca PNR ve iki kanallı bildirim aynı işlemde kaydedilir");
        await Send("POST", $"/api/tickets/local/{purchase.Id}/complete", pt, new { outcome = "Succeeded" });
        check(await db.TravelNotifications.CountAsync(n => n.Reservation.Payment!.Id == purchase.Id && n.Type == "Purchase") == 2, "Tekrarlanan ödeme aynı PNR için bildirim çoğaltmaz");
        var hold = await Buy(2);
        var original = await db.Trips.AsNoTracking().SingleAsync(t => t.Id == trip.Id);
        var departure = DateTime.UtcNow.AddMinutes(90); var arrival = departure.AddHours(3); var key = Guid.NewGuid();
        object Change(bool preview, int vehicle, int version = 0, int affected = 1) => new { requestKey = key, expectedVersion = version, expectedAffectedTickets = affected, preview, busId = vehicle, departureTime = departure, arrivalTime = arrival, reason = "Araç bakımı ve operasyon saati değişikliği" };
        check((await Send("POST", $"/api/trips/{trip.Id}/changes", pt, Change(true, replacement.Id))).StatusCode == HttpStatusCode.Forbidden, "Yolcu firma adına kontrollü değişiklik yapamaz");
        check((await Send("POST", $"/api/trips/{trip.Id}/changes", mt, Change(true, incompatible.Id))).StatusCode == HttpStatusCode.Conflict, "Aktif yolcunun koltuk yerleşimini bozan araç değişikliği reddedilir");
        var preview = await Json(await Send("POST", $"/api/trips/{trip.Id}/changes", mt, Change(true, replacement.Id)));
        check(preview.GetProperty("affectedTickets").GetInt32() == 1 && preview.GetProperty("releasedHolds").GetInt32() == 1 && await db.TripChanges.CountAsync(c => c.TripId == trip.Id) == 0 && await db.Reservations.AnyAsync(r => r.Payment!.Id == hold.Id && r.Status == "Active"), "Önizleme etkiyi gösterir; bilet, tutma ve seferi değiştirmez");
        check((await Send("POST", $"/api/trips/{trip.Id}/changes", mt, Change(false, replacement.Id, affected: 2))).StatusCode == HttpStatusCode.Conflict, "Önizleme sonrası farklı etkilenen bilet sayısıyla uygulama engellenir");
        await Json(await Send("POST", $"/api/trips/{trip.Id}/changes", mt, Change(false, replacement.Id)));
        await Json(await Send("POST", $"/api/trips/{trip.Id}/changes", mt, Change(false, replacement.Id)));
        db.ChangeTracker.Clear();
        var changed = await db.Trips.SingleAsync(t => t.Id == trip.Id);
        check(changed.BusId == replacement.Id && changed.ChangeVersion == 1 && changed.AvailableSeats == 7 && await db.TripChanges.CountAsync(c => c.TripId == trip.Id) == 1, "Kontrollü işlem idempotent; araç/saat ve kontenjan bir kez değişir");
        check(await db.Reservations.AnyAsync(r => r.Payment!.Id == hold.Id && r.Status == "Cancelled") && await db.TravelNotifications.CountAsync(n => n.Type == "TripChanged" && n.Reservation.Payment!.Id == purchase.Id) == 2, "Açık ödeme tutması kapatılır; etkilenen bilet için değişiklik bildirimi kuyruğa alınır");
        var ticket = (await (await Send("GET", $"/api/tickets/{purchase.Pnr}", pt)).Content.ReadFromJsonAsync<PurchaseDto>())!;
        check(ticket.ChangeCancellationUntil == ticket.DepartureTime && ticket.CanCancel && ticket.BusPlateNumber == replacement.PlateNumber && Math.Abs((ticket.DepartureTime - departure).TotalSeconds) < 1, "Yeni kalkışa 90 dakika kalsa da yolcu değişiklik nedeniyle tam iade hakkına sahiptir");
        var changedPdf = await Send("GET", $"/api/tickets/{purchase.Pnr}/pdf", pt); changedPdf.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.Combine(root, "output", "pdf")); await File.WriteAllBytesAsync(Path.Combine(root, "output", "pdf", "changed-trip-ticket.pdf"), await changedPdf.Content.ReadAsByteArrayAsync());
        var comm = await Json(await Send("GET", $"/api/tickets/{purchase.Pnr}/communications", pt));
        check(comm.GetProperty("changes").GetArrayLength() == 1 && (await Send("GET", $"/api/tickets/{purchase.Pnr}/communications", st)).StatusCode == HttpStatusCode.NotFound, "Değişiklik geçmişi yalnızca etkilenen bilet sahibine görünür");
        check((await Send("POST", $"/api/trips/{trip.Id}/changes", mt, Change(true, bus.Id))).StatusCode == HttpStatusCode.Conflict, "Eski sefer sürümüyle yeni değişiklik engellenir");
        await TravelNotifications.Reminders(db, DateTime.UtcNow, default); await TravelNotifications.Reminders(db, DateTime.UtcNow, default);
        check(await db.TravelNotifications.CountAsync(n => n.Type == "Reminder" && n.Reservation.Payment!.Id == purchase.Id) == 2, "24 saatlik hatırlatma yeni sefer sürümü için kanal başına bir kez oluşturulur");
        var env = new TestEnvironment(root); var mailDirectory = Path.Combine(root, "tmp", "notification-qa", "email");
        var emailOptions = Options.Create(new AccountEmailOptions { PickupDirectory = mailDirectory });
        var smsOptions = Options.Create(new SmsOptions { PickupDirectory = Path.Combine(root, "tmp", "notification-qa", "sms") });
        var delivery = new NotificationDelivery(db, new EmailTransport(emailOptions, env), new SmsTransport(new HttpClient(), smsOptions, env), TimeProvider.System);
        for (var i = 0; i < 5; i++) await delivery.Process(default);
        check(await db.TravelNotifications.CountAsync(n => n.Reservation.Payment!.Id == purchase.Id && n.Status == "Preview") == 6 && Directory.GetFiles(mailDirectory, "*.html").Length > 0 && Directory.GetFiles(smsOptions.Value.PickupDirectory, "*.txt").Length > 0, "E-posta .eml/.html ve SMS .txt önizlemeleri gerçekten oluşturulur; teslim edildi olarak işaretlenmez");
        var late = (await (await Send("POST", $"/api/tickets/local/{hold.Id}/complete", pt, new { outcome = "Succeeded" })).Content.ReadFromJsonAsync<PurchaseDto>())!;
        check(late.PaymentStatus == "Paid" && late.Pnr == null && late.RefundStatus == "Pending", "Değişiklik öncesi tutmaya geç gelen para yeni bilet üretmez; telafi iadesi başlar");
        var cancelled = (await (await Send("POST", $"/api/tickets/{purchase.Pnr}/cancel", pt)).Content.ReadFromJsonAsync<PurchaseDto>())!;
        check(cancelled.TicketStatus == "Cancelled" && cancelled.RefundStatus == "Pending", "Değişiklik hakkıyla iki saat kuralından bağımsız iptal ve tam iade başlar");
        await Send("POST", $"/api/tickets/local/{purchase.Id}/refund-result", pt, new { outcome = "Succeeded" });
        check(await db.TravelNotifications.CountAsync(n => n.Type == "RefundSucceeded" && n.Reservation.Payment!.Id == purchase.Id) == 2, "Doğrulanmış iade sonucu tercih edilen kanallarda kuyruğa alınır");
        var updatedDeparture = DateTime.UtcNow.AddDays(14);
        await Json(await Send("PUT", $"/api/trips/{trip.Id}", mt, new { busId = bus.Id, routeId = route.Id, departureTime = updatedDeparture, arrivalTime = updatedDeparture.AddHours(3), price = 450 }));
        var historical = (await (await Send("GET", $"/api/tickets/{purchase.Pnr}", pt)).Content.ReadFromJsonAsync<PurchaseDto>())!;
        check(historical.DepartureTime == ticket.DepartureTime && historical.BusPlateNumber == ticket.BusPlateNumber, "İptal edilmiş bilet ve eski ödeme tutmaları genel düzenlemeyi engellemez; geçmiş biletin saati/araç bilgisi değişmez");
        check((await Send("GET", "/api/travel-notifications", pt)).StatusCode == HttpStatusCode.Forbidden && (await Send("GET", "/api/travel-notifications", mt)).IsSuccessStatusCode, "Teslimat yönetimi yolcuya kapalı, firma kapsamına açıktır");
        // Deterministic provider tests; never sends a real SMS.
        var targetId = await db.Reservations.Where(r => r.Payment!.Id == purchase.Id).Select(r => r.Id).SingleAsync();
        await db.TravelNotifications.Where(n => n.Status == "Pending").ExecuteUpdateAsync(s => s.SetProperty(n => n.NextAttemptAt, DateTime.UtcNow.AddDays(2)));
        db.ChangeTracker.Clear();
        var message = new TravelNotification { ReservationId = targetId, EventKey = "provider-test", Type = "TripChanged", Channel = "Sms", Recipient = "+905321234567", Body = "Provider test", Subject = "Test", CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow };
        db.TravelNotifications.Add(message); await db.SaveChangesAsync();
        var providerOptions = Options.Create(new SmsOptions { Mode = "Twilio", AccountSid = "AC" + new string('1', 32), AuthToken = "test-only", From = "+15005550006" });
        var handler = new FakeSms();
        var providerDelivery = new NotificationDelivery(db, new EmailTransport(emailOptions, env), new SmsTransport(new HttpClient(handler), providerOptions, env), TimeProvider.System);
        await providerDelivery.Process(default); db.ChangeTracker.Clear();
        check((await db.TravelNotifications.FindAsync(message.Id))!.Status == "Accepted" && handler.Posts == 1, "SMS sağlayıcı kabulü teslim edildi sayılmaz; sağlayıcı kimliği saklanır");
        await db.TravelNotifications.Where(n => n.Id == message.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.NextAttemptAt, DateTime.UtcNow.AddSeconds(-1))); db.ChangeTracker.Clear();
        await providerDelivery.Process(default); db.ChangeTracker.Clear();
        check((await db.TravelNotifications.FindAsync(message.Id))!.Status == "Delivered" && handler.Posts == 1, "SMS teslimatı sağlayıcı sorgusuyla doğrulanır; tekrar SMS oluşturulmaz");
        var uncertain = new SmsTransport(new HttpClient(new FakeSms(true)), providerOptions, env);
        try { await uncertain.Send(message, default); throw new Exception("Belirsiz sonuç kabul edildi."); } catch (AmbiguousDeliveryException) { check(true, "SMS ağ sonucu belirsizse körlemesine otomatik yeniden gönderim yapılmaz"); }
        var failure = new TravelNotification { ReservationId = targetId, EventKey = "failure-test", Type = "TripChanged", Channel = "Email", Recipient = "bad@example.com", Subject = "Fail", Body = "Test", CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow };
        db.TravelNotifications.Add(failure); await db.SaveChangesAsync();
        var badDirectory = Path.Combine(root, "tmp", "notification-qa", "not-a-directory.txt"); await File.WriteAllTextAsync(badDirectory, "file");
        var failingDelivery = new NotificationDelivery(db, new EmailTransport(Options.Create(new AccountEmailOptions { PickupDirectory = badDirectory }), env), new SmsTransport(new HttpClient(), smsOptions, env), TimeProvider.System);
        await failingDelivery.Process(default); db.ChangeTracker.Clear();
        var firstFailure = (await db.TravelNotifications.FindAsync(failure.Id))!;
        check(firstFailure.Status == "Pending" && firstFailure.Attempts == 1 && firstFailure.NextAttemptAt > DateTime.UtcNow, "Gönderim hatası artan beklemeyle otomatik yeniden denemeye planlanır");
        await db.TravelNotifications.Where(n => n.Id == failure.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.Attempts, 7).SetProperty(n => n.NextAttemptAt, DateTime.UtcNow.AddSeconds(-1)));
        await failingDelivery.Process(default); db.ChangeTracker.Clear();
        check((await db.TravelNotifications.FindAsync(failure.Id))!.Status == "Failed" && await db.NotificationAttempts.AnyAsync(a => a.NotificationId == failure.Id && a.Status == "Failed"), "Sekizinci gönderim hatası kalıcı başarısız durum ve deneme geçmişiyle izlenir");
        check((await Send("POST", $"/api/travel-notifications/{failure.Id}/retry", mt, new { reason = "Gönderim ayarları düzeltildi" })).StatusCode == HttpStatusCode.Forbidden, "Firma yönetici yetkisi olmadan yeniden gönderemez");
        await Json(await Send("POST", $"/api/travel-notifications/{failure.Id}/retry", at, new { reason = "Gönderim ayarları düzeltildi" }));
        check(await db.NotificationAttempts.AnyAsync(a => a.NotificationId == failure.Id && a.Status == "ManualRetry" && a.ActorId == admin.Id && a.Reason != null), "Yönetici yeniden denemesi gerekçe ve kullanıcı kimliğiyle kaydedilir");
        await delivery.Process(default); db.ChangeTracker.Clear();
        check((await db.TravelNotifications.FindAsync(failure.Id))!.Status == "Preview", "Yapılandırma düzelince başarısız bildirim tekrar teslim sürecine girer");
        var optout = await db.Reservations.SingleAsync(r => r.Id == targetId); await db.Users.Where(u => u.Id == passenger.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.TripRemindersEnabled, false)); db.ChangeTracker.Clear();
        await TravelNotifications.Queue(db, optout, "Reminder", "optout-test", DateTime.UtcNow); await db.SaveChangesAsync();
        check(await db.TravelNotifications.CountAsync(n => n.ReservationId == targetId && n.EventKey == "optout-test" && n.Status == "Skipped") == 2, "Kapalı bildirim tercihi gönderime dönüşmez; atlama nedeni kaydedilir");
        var simultaneous = new TravelNotification { ReservationId = targetId, EventKey = "concurrent-workers", Type = "TripChanged", Channel = "Sms", Recipient = "+905321234567", Body = "concurrent", Subject = "Test", CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow };
        db.TravelNotifications.Add(simultaneous); await db.SaveChangesAsync();
        var racingHandler = new FakeSms();
        async Task Worker() { await using var otherDb = new TravelaDbContext(new DbContextOptionsBuilder<TravelaDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options); await new NotificationDelivery(otherDb, new EmailTransport(emailOptions, env), new SmsTransport(new HttpClient(racingHandler), providerOptions, env), TimeProvider.System).Process(default); }
        await Task.WhenAll(Worker(), Worker());
        check(racingHandler.Posts == 1, "İki eş zamanlı bildirim çalışanı aynı SMS'i bir kez sağlayıcıya iletir");
        var expired = await Buy(3);
        await db.Reservations.Where(r => r.Payment!.Id == expired.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.HoldExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        await Json(await Send("PUT", $"/api/trips/{trip.Id}", mt, new { busId = bus.Id, routeId = route.Id, departureTime = updatedDeparture.AddDays(1), arrivalTime = updatedDeparture.AddDays(1).AddHours(3), price = 450 }));
        check(await db.Reservations.AnyAsync(r => r.Payment!.Id == expired.Id && r.Status == "Cancelled") && await db.Trips.AnyAsync(t => t.Id == trip.Id && t.AvailableSeats == 8), "Süresi dolmuş aktif ödeme tutması düzenlemede serbest kalır; hayalet kontenjan oluşturmaz");
        var cancellationTicket = await Buy(4); await Json(await Send("POST", $"/api/tickets/local/{cancellationTicket.Id}/complete", pt, new { outcome = "Succeeded" }));
        await Json(await Send("POST", $"/api/trips/{trip.Id}/cancel", mt, new { reason = "Operasyon nedeniyle test seferi iptal edildi" }));
        check(await db.TravelNotifications.CountAsync(n => n.Reservation.Payment!.Id == cancellationTicket.Id && n.Type == "Cancellation" && n.Status == "Pending") == 2, "Sefer iptali tercihleri açık yolcuya e-posta ve SMS bildirimi oluşturur");
        var otherCompany = new Company { Name = "Bildirim Kapsamı Dışındaki Firma" }; db.Add(otherCompany); await db.SaveChangesAsync();
        var outsider = Account("outside-manager", "Company"); outsider.CompanyId = otherCompany.Id; db.Add(outsider); await db.SaveChangesAsync(); var ot = await Login(outsider);
        var outside = await Json(await Send("GET", "/api/travel-notifications?q=" + purchase.Pnr, ot));
        check(outside.GetProperty("totalCount").GetInt32() == 0 && (await Send("GET", $"/api/travel-notifications/{message.Id}", ot)).StatusCode == HttpStatusCode.NotFound && (await Send("GET", $"/api/trips/{trip.Id}/changes", ot)).StatusCode == HttpStatusCode.NotFound, "Başka firma teslimat ve sefer değişikliği geçmişine erişemez");
    }
    private sealed class FakeSms(bool fail = false) : HttpMessageHandler
    {
        public int Posts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (fail) throw new HttpRequestException("Lost response");
            if (request.Method == HttpMethod.Post) Interlocked.Increment(ref Posts);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { sid = "SM" + new string('2', 32), status = request.Method == HttpMethod.Post ? "queued" : "delivered" }) });
        }
    }
    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "NotificationChecks";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
