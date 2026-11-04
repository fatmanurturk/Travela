using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Payments;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

internal static class TicketOperationsChecks
{
    public static async Task Run(TravelaDbContext db, HttpClient http, Action<bool, string> check)
    {
        var company = new Company { Name = "Merkezi İşlem Firması" };
        var bus = new Bus { Company = company, PlateNumber = "34 OPS 777", NormalizedPlateNumber = "34OPS777", SeatCapacity = 20 };
        var route = new Travela.Api.Models.Route { DepartureCity = "Bursa", ArrivalCity = "İzmir", EstimatedDurationMinutes = 240 };
        var trip = new Trip { Company = company, Bus = bus, Route = route, DepartureTime = DateTime.UtcNow.AddDays(5), ArrivalTime = DateTime.UtcNow.AddDays(5).AddHours(4), AvailableSeats = 20, Price = 600 };
        User Account(string name, string role)
        {
            var user = new User { FullName = name, Email = name + "@operations.example", Role = role, PhoneNumber = "+905321234567" };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "TestPass123!"); return user;
        }
        var passenger = Account("Sabit Yolcu", "Passenger"); passenger.Email = "fixed@operations.example";
        var admin = Account("Merkezi Yönetici", "Admin"); admin.Email = "admin@operations.example";
        var companyUser = Account("Firma Yetkilisi", "Company"); companyUser.Email = "company@operations.example";
        db.AddRange(company, bus, route, trip, passenger, admin); await db.SaveChangesAsync();
        companyUser.CompanyId = company.Id; db.Add(companyUser); await db.SaveChangesAsync();
        async Task<HttpResponseMessage> Send(string method, string path, string? token, object? body = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) request.Content = JsonContent.Create(body);
            return await http.SendAsync(request);
        }
        async Task<JsonElement> Json(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode) throw new Exception($"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        async Task<string> Login(User user) => (await Json(await Send("POST", "/api/auth/login", null, new { user.Email, password = "TestPass123!" }))).GetProperty("token").GetString()!;
        var ownerToken = await Login(passenger); var adminToken = await Login(admin); var companyToken = await Login(companyUser);
        async Task<PurchaseDto> Purchase(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode) throw new Exception(await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<PurchaseDto>())!;
        }
        Task<HttpResponseMessage> Start(int seat, Guid key) => Send("POST", "/api/tickets/checkout", ownerToken,
            new { tripId = trip.Id, seatNumber = seat, requestKey = key, passengerName = "Enjekte edilen ad" });
        async Task Service(Func<PaymentService, Task> action)
        {
            await using var context = new TravelaDbContext(new DbContextOptionsBuilder<TravelaDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options);
            await action(new PaymentService(context, new LocalTestPaymentProvider(context, new LocalWebhookKey(), TimeProvider.System), TimeProvider.System));
        }
        const string list = "/api/admin/tickets";
        check((await Send("GET", list, null)).StatusCode == HttpStatusCode.Unauthorized &&
            (await Send("GET", list, ownerToken)).StatusCode == HttpStatusCode.Forbidden && (await Send("GET", list, companyToken)).StatusCode == HttpStatusCode.Forbidden,
            "Merkezi bilet listesi yalnızca yöneticiye açıktır");
        var key = Guid.NewGuid(); var order = await Purchase(await Start(1, key));
        check(order.PassengerName == "Sabit Yolcu" && order.PassengerEmail == passenger.Email && order.PassengerPhone == passenger.PhoneNumber &&
            order.PassengerSnapshotSource == "Checkout" && order.PassengerCapturedAt.HasValue, "Checkout yolcu adını ve iletişimini sunucudaki hesaptan sabitler; istemci sahte adı kullanılamaz");
        await Json(await Send("PUT", "/api/profile", ownerToken, new { fullName = "Güncel Hesap Adı", phoneNumber = "+905551112233" }));
        await db.Users.Where(u => u.Id == passenger.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.Email, "new@operations.example"));
        var retried = await Purchase(await Start(1, key));
        check(retried.Id == order.Id && retried.PassengerName == "Sabit Yolcu" && retried.PassengerEmail == "fixed@operations.example" && retried.PassengerPhone == "+905321234567",
            "Profil değişimi ve idempotent checkout tekrarı sabit yolcu bilgilerini değiştirmez");
        var paid = await Purchase(await Send("POST", $"/api/tickets/local/{order.Id}/complete", ownerToken, new { outcome = "Succeeded", refundOutcome = "Failed" }));
        var ticket = await Purchase(await Send("GET", "/api/tickets/" + paid.Pnr, ownerToken));
        check(ticket.PassengerName == "Sabit Yolcu" && ticket.PassengerEmail == "fixed@operations.example", "Ödeme sonrası bilet/PDF veri kaynağı güncel profilden bağımsız kalır");
        var reservationId = await db.PaymentOrders.Where(p => p.Id == order.Id).Select(p => p.ReservationId).SingleAsync();
        var detailResponse = await Send("GET", list + "/" + reservationId, adminToken); var detail = await Json(detailResponse);
        check(detail.GetProperty("ticket").GetProperty("accountName").GetString() == "Güncel Hesap Adı" && detail.GetProperty("ticket").GetProperty("passengerName").GetString() == "Sabit Yolcu" && detailResponse.Headers.CacheControl?.NoStore == true,
            "Yönetici detayı güncel hesabı ve sabit bilet yolcusunu ayrı, no-store olarak sunar");
        check((await Send("GET", list + "/" + reservationId, ownerToken)).StatusCode == HttpStatusCode.Forbidden &&
            (await Send("GET", list + "/99999999", adminToken)).StatusCode == HttpStatusCode.NotFound, "Merkezi detay için rol ve bulunamayan kayıt sınırları korunur");
        foreach (var term in new[] { paid.Pnr!, order.Id.ToString(), "Sabit Yolcu", "new@operations.example", company.Name })
        {
            var found = await Json(await Send("GET", list + "?search=" + Uri.EscapeDataString(term), adminToken));
            check(found.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetInt32() == reservationId), "Yönetici araması çalışır: " + (term == paid.Pnr || term == order.Id.ToString() ? "PNR/sipariş" : "yolcu/firma"));
        }
        check((await Send("GET", list + "?pageNumber=0", adminToken)).StatusCode == HttpStatusCode.BadRequest &&
            (await Send("GET", list + "?paymentStatus=Injected", adminToken)).StatusCode == HttpStatusCode.BadRequest, "Geçersiz merkezi liste filtreleri reddedilir");
        var companyItems = await Json(await Send("GET", "/api/reservations/company", companyToken));
        check(companyItems.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("pnr").GetString() == paid.Pnr).GetProperty("passengerName").GetString() == "Sabit Yolcu",
            "Firma listesi de değişmeyen bilet yolcusunu kullanır");
        var body = new { reason = "Sağlayıcı kontrolünden sonra iade takibi", requestKey = Guid.NewGuid() };
        check((await Send("POST", list + $"/{reservationId}/retry-refund", companyToken, body)).StatusCode == HttpStatusCode.Forbidden &&
            (await Send("POST", list + $"/{reservationId}/retry-refund", adminToken, body)).StatusCode == HttpStatusCode.Conflict, "Yetkisiz kullanıcı veya iade talebi olmayan ödeme için yeniden iade yapılamaz");
        await Purchase(await Send("POST", "/api/tickets/" + paid.Pnr + "/cancel", ownerToken));
        await Service(service => service.ProcessRefund(order.Id, default));
        await db.PaymentOrders.Where(p => p.Id == order.Id).ExecuteUpdateAsync(update => update.SetProperty(p => p.RefundAttempts, 8).SetProperty(p => p.NeedsAttention, true));
        var refundKey = await db.PaymentOrders.Where(p => p.Id == order.Id).Select(p => p.RefundKey).SingleAsync();
        check((await Send("POST", list + $"/{reservationId}/retry-refund", adminToken, new { reason = "kısa", requestKey = Guid.NewGuid() })).StatusCode == HttpStatusCode.BadRequest,
            "Yönetici yeniden denemesinde açıklayıcı gerekçe zorunludur");
        var responses = await Task.WhenAll(Send("POST", list + $"/{reservationId}/retry-refund", adminToken, body), Send("POST", list + $"/{reservationId}/retry-refund", adminToken, body));
        check(responses.Any(response => response.StatusCode == HttpStatusCode.Accepted) &&
            await db.PaymentEvents.CountAsync(e => e.OrderId == order.Id && e.RequestKey == body.requestKey) == 1 &&
            (await Send("POST", list + $"/{reservationId}/retry-refund", adminToken, body)).IsSuccessStatusCode,
            "Eş zamanlı yönetici tekrarları tek gerekçeli işlem kaydı ve tek yeniden deneme oluşturur");
        var queued = await db.PaymentOrders.AsNoTracking().SingleAsync(p => p.Id == order.Id);
        var audit = await db.PaymentEvents.AsNoTracking().SingleAsync(e => e.RequestKey == body.requestKey);
        check(queued.RefundKey == refundKey && queued.RefundAttempts == 0 && queued.NextRefundAttemptAt <= DateTime.UtcNow && audit.ActorUserId == admin.Id && audit.ActorName == admin.FullName && audit.Reason == body.reason,
            "İade yeniden denemesi sabit sağlayıcı anahtarını korur; gerekçe, yönetici ve zaman kaydedilir");
        await db.LocalPaymentRecords.Where(p => p.Id == order.Id).ExecuteUpdateAsync(update => update.SetProperty(p => p.RefundOutcome, "Succeeded"));
        await Service(service => service.ProcessRefund(order.Id, default)); await Service(service => service.ProcessRefund(order.Id, default));
        check(await db.LocalPaymentRecords.AnyAsync(p => p.Id == order.Id && p.RefundTransfers == 1) &&
            await db.PaymentEvents.AnyAsync(e => e.OrderId == order.Id && e.Type == "RefundSucceeded") &&
            (await Send("POST", list + $"/{reservationId}/retry-refund", adminToken, new { body.reason, requestKey = Guid.NewGuid() })).StatusCode == HttpStatusCode.Conflict,
            "Yeniden deneme sağlayıcıdan doğrulanır, tek transfer oluşur ve tamamlanmış iade tekrar açılamaz");
        var pending = await Purchase(await Start(2, Guid.NewGuid()));
        var pendingId = await db.PaymentOrders.Where(p => p.Id == pending.Id).Select(p => p.ReservationId).SingleAsync();
        check((await Send("POST", list + $"/{pendingId}/reconcile", adminToken, new { reason = "Kayıp ödeme bildirimi için sağlayıcı sorgusu", requestKey = Guid.NewGuid() })).StatusCode == HttpStatusCode.Accepted,
            "Yönetici bekleyen ödemeyi gerekçeyle mutabakat kuyruğuna alabilir");
        await db.LocalPaymentRecords.Where(p => p.Id == pending.Id).ExecuteUpdateAsync(update => update.SetProperty(p => p.Status, "Succeeded"));
        await Service(service => service.Reconcile(pending.Id, default));
        check(await db.PaymentOrders.AnyAsync(p => p.Id == pending.Id && p.PaymentStatus == "Paid" && p.LastReconciledAt != null && p.NextReconciliationAt == null) &&
            await db.PaymentEvents.AnyAsync(e => e.OrderId == pending.Id && e.Type == "ManualReconciliationQueued"), "Manuel mutabakat PNR'yi yalnızca sağlayıcı doğrulamasıyla üretir ve geçmişte görünür");
        var waiting = await Purchase(await Start(3, Guid.NewGuid()));
        await Service(service => service.Reconcile(waiting.Id, default));
        check(await db.PaymentOrders.AnyAsync(p => p.Id == waiting.Id && p.PaymentStatus == "Pending" && p.NextReconciliationAt > DateTime.UtcNow && p.ReconciliationAttempts == 1),
            "Sonuçlanmayan ödeme bir sonraki sorgulamaya planlanır; worker'da sürekli ilk sırada kalmaz");
        var legacy = await db.Reservations.AsNoTracking().FirstAsync(r => r.Kind == "Legacy" && r.PassengerSnapshotSource == "Migration");
        var oldName = legacy.PassengerNameSnapshot;
        await db.Users.Where(u => u.Id == legacy.PassengerId).ExecuteUpdateAsync(update => update.SetProperty(u => u.FullName, "Geçiş Sonrası Yeni Ad"));
        check((await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == legacy.Id)).PassengerNameSnapshot == oldName && oldName != null,
            "Migration eski ücretsiz kaydı mevcut bilgilerle sabitler; sonraki profil değişimi eski kaydı değiştirmez");
        check((await Send("POST", list + $"/{legacy.Id}/retry-refund", adminToken, new { body.reason, requestKey = Guid.NewGuid() })).StatusCode == HttpStatusCode.Conflict,
            "Eski ücretsiz kayıtlar listelenir; bunlara sahte ödeme/iade oluşturulamaz");
    }
}
