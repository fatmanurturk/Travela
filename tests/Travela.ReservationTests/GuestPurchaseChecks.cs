using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Travela.Api.Models;
using Travela.Api.Payments;
using Travela.Api.Security;
using Travela.Api.Trips;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

internal static class GuestPurchaseChecks
{
    public static void Unit()
    {
        var count = 0;
        void Check(bool ok) { if (!ok) throw new Exception("Guest unit check failed"); count++; }
        foreach (var input in new[] { "0532 123 45 67", "+90 (532) 123-4567", "5321234567", "905321234567", "00905321234567" })
            Check(BookingContact.Phone(input) == "+905321234567");
        foreach (var bad in new[] { "", "abc", "0532123", "++905321234567", "0", "555555555555555555555" }) Check(BookingContact.Phone(bad) == null);
        Check(BookingContact.Matches("05321234567", null, "+905321234567"));
        Check(BookingContact.Matches(" BUYER@EXAMPLE.COM ", "buyer@example.com", null));
        Check(!BookingContact.Matches("bad@example.com", "buyer@example.com", "+905321234567"));
        Check(BookingContact.MaskIdentity("10000000146") == "1********46");
        Check(BookingContact.Pnr(" abc123 ") == "ABC123");
        using var db = new TravelaDbContext(new DbContextOptionsBuilder<TravelaDbContext>().UseSqlServer("Server=unused;Database=unused;Integrated Security=true").Options);
        var clock = new MutableClock();
        var service = new GuestTicketAccess(db, new EphemeralDataProtectionProvider(), clock, new TestEnvironment(), new ConfigurationBuilder().Build());
        var response = new DefaultHttpContext(); var id = Guid.NewGuid(); service.IssueGrant(response, id);
        var cookie = response.Response.Headers.SetCookie.ToString();
        Check(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase));
        var request = new DefaultHttpContext(); request.Request.Headers.Cookie = cookie.Split(';')[0];
        Check(service.Grant(request)?.OrderId == id);
        clock.Now = clock.Now.AddMinutes(16); Check(service.Grant(request) == null);
        request.Request.Headers.Cookie = GuestTicketAccess.TicketCookie + "=tampered"; Check(service.Grant(request) == null);
        Console.WriteLine($"Guest contact/capability unit checks: {count} passed.");
    }
    public static async Task Run(TravelaDbContext db, HttpClient server, Action<bool, string> check, string root, bool disabled = false)
    {
        var company = new Company { Name = "Misafir Test Firması" };
        var route = new Travela.Api.Models.Route { DepartureCity = "İzmir", ArrivalCity = "Ankara" };
        var bus = new Bus { Company = company, PlateNumber = "35 GUEST 1", NormalizedPlateNumber = "35GUEST1", SeatCapacity = 40, SeatLayout = "2+2", SeatPlanJson = SeatPlans.Write(SeatPlans.Create("2+2", 40)) };
        var trip = new Trip { Company = company, Bus = bus, Route = route, Price = 500, AvailableSeats = 40, SellableSeatCount = 40, SeatCapacitySnapshot = 40,
            SeatLayout = "2+2", SeatPlanJson = bus.SeatPlanJson, DepartureTime = DateTime.UtcNow.AddDays(15), ArrivalTime = DateTime.UtcNow.AddDays(15).AddHours(8) };
        var user = new User { FullName = "Üye Hesap", Email = "member@guest.example", Role = "Passenger", PhoneNumber = "+905551112233" };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "TestPass123!");
        db.AddRange(trip, user); await db.SaveChangesAsync();
        var userCount = await db.Users.CountAsync();
        using var guest = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = server.BaseAddress };
        using var stranger = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = server.BaseAddress };
        async Task<JsonElement> Json(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode) throw new Exception(response.StatusCode + ": " + await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
        }
        StartGroupPurchaseDto Cart(params int[] seats) => new() { TripId = trip.Id, RequestKey = Guid.NewGuid(), ContactEmail = " GUEST@EXAMPLE.COM ", ContactPhone = "0532 123 45 67",
            Passengers = seats.Select(s => new TicketPassengerDto { SeatNumber = s, FullName = "Misafir Yolcu " + s, IdentityNumber = "10000000146", Gender = "Female" }).ToList(), LegalAcceptances = GroupPurchaseChecks.SalesConsent() };
        async Task<JsonElement> Pay(Guid id, string outcome = "Succeeded") => await Json(await guest.PostAsJsonAsync($"/api/guest/purchases/{id}/local-complete", new { outcome }));
        async Task<JsonElement> Lookup(string pnr, string contact = "05321234567", HttpClient? client = null) => await Json(await (client ?? guest).PostAsJsonAsync("/api/ticket-lookup", new { pnr, contact }));
        check((await guest.PostAsJsonAsync("/api/guest/purchases", Cart(1))).StatusCode == HttpStatusCode.Unauthorized, "Misafir siparişi önce güvenli oturum ister");
        var session = await guest.PostAsJsonAsync("/api/guest/purchases/session", new { }); await Json(session);
        check(session.Headers.GetValues("Set-Cookie").Any(c => c.Contains("httponly", StringComparison.OrdinalIgnoreCase)), "Misafir erişimi yalnızca HttpOnly cookie ile kurulur");
        var cart = Cart(1, 2); var order = await Json(await guest.PostAsJsonAsync("/api/guest/purchases", cart)); var id = order.GetProperty("id").GetGuid();
        check(order.GetProperty("amount").GetDecimal() == 1000 && order.GetProperty("tickets").GetArrayLength() == 2, "Misafir çoklu yolcu sunucu fiyatıyla tek sipariş oluşturur");
        check(order.GetProperty("tickets").EnumerateArray().All(t => t.GetProperty("pnr").ValueKind == JsonValueKind.Null), "Ödeme öncesi hiçbir PNR oluşmaz");
        check(await db.Users.CountAsync() == userCount && await db.PurchaseGroups.AnyAsync(g => g.Id == id && g.PassengerId == null && g.GuestSessionId != null && g.ContactPhone == "+905321234567" && g.ContactEmail == "guest@example.com"), "Misafir hesap oluşturmaz; sahiplik ve normalize iletişim ayrıdır");
        check(await db.Reservations.CountAsync(r => r.Payment!.PurchaseGroupId == id && r.PassengerId == null && r.PassengerIdentityNumberSnapshot == "10000000146" && r.PassengerEmailSnapshot == null) == 2, "Yolcu anlık kayıtları koltuk bazındadır; iletişim ortak siparişte tutulur");
        var again = await Json(await guest.PostAsJsonAsync("/api/guest/purchases", cart)); check(again.GetProperty("id").GetGuid() == id, "Yanıt kaybından sonra aynı anahtar mevcut siparişi döndürür");
        var resumed = await Json(await guest.GetAsync("/api/guest/purchases/resume")); check(resumed.GetProperty("id").GetGuid() == id, "Sayfa yenilemede misafir siparişi güvenli oturumla bulunur");
        check((await stranger.GetAsync($"/api/guest/purchases/{id}")).StatusCode == HttpStatusCode.NotFound && (await stranger.PostAsJsonAsync($"/api/guest/purchases/{id}/local-complete", new { outcome = "Succeeded" })).StatusCode == HttpStatusCode.NotFound, "Sipariş numarası tek başına okuma veya ödeme yetkisi vermez");
        await Json(await stranger.PostAsJsonAsync("/api/guest/purchases/session", new { }));
        check((await stranger.GetAsync($"/api/guest/purchases/{id}")).StatusCode == HttpStatusCode.NotFound, "Başka misafir oturumu siparişi açamaz");
        var bad = Cart(3); bad.ExpectedAmount = 1;
        check((await guest.PostAsJsonAsync("/api/guest/purchases", bad)).StatusCode == HttpStatusCode.Conflict && !await db.Reservations.AnyAsync(r => r.TripId == trip.Id && r.SeatNumber == 3), "İstemci fiyatı kabul edilmez; hata kısmi tutma bırakmaz");
        check((await guest.PostAsJsonAsync("/api/purchases", Cart(3))).StatusCode == HttpStatusCode.Unauthorized, "Mevcut üye satın alma servisi giriş zorunluluğunu korur");
        check((await stranger.PostAsJsonAsync("/api/ticket-lookup", new { pnr = id.ToString("N"), contact = "guest@example.com" })).StatusCode == HttpStatusCode.NotFound, "Sipariş kimliği PNR yerine kullanılamaz");
        order = await Pay(id); var tickets = order.GetProperty("tickets").EnumerateArray().ToArray();
        var pnr = tickets[0].GetProperty("pnr").GetString()!; var secondPnr = tickets[1].GetProperty("pnr").GetString()!; var lineId = tickets[0].GetProperty("id").GetGuid();
        check(pnr != secondPnr && tickets.All(t => t.GetProperty("kind").GetString() == "Ticket"), "Doğrulanmış tek ödeme iki yolcuya ayrı PNR üretir");
        await Pay(id); await Json(await guest.PostAsJsonAsync("/api/guest/purchases", cart));
        check(await db.PaymentOrders.CountAsync(p => p.PurchaseGroupId == id) == 2 && await db.LocalPaymentRecords.CountAsync(p => p.Id == id) == 1, "Tekrarlanan ödeme ve sipariş ikinci tahsilat veya bilet oluşturmaz");
        check(await db.TravelNotifications.CountAsync(n => n.Reservation.Payment!.PurchaseGroupId == id && n.Type == "Purchase") == 4, "Misafir bildirimleri mevcut altyapıda ortak iletişime kuyruğa alınır");
        check((await stranger.GetAsync("/api/ticket-lookup/pdf")).StatusCode == HttpStatusCode.Unauthorized, "PNR veya sipariş numarası doğrudan PDF erişimi sağlamaz");
        var wrong = await stranger.PostAsJsonAsync("/api/ticket-lookup", new { pnr, contact = "wrong@example.com" }); var nonexistent = await stranger.PostAsJsonAsync("/api/ticket-lookup", new { pnr = "NONEXISTENT", contact = "guest@example.com" });
        check(wrong.StatusCode == HttpStatusCode.NotFound && await wrong.Content.ReadAsStringAsync() == await nonexistent.Content.ReadAsStringAsync(), "Yanlış PNR ve iletişim aynı genel mesajı döndürür");
        var found = await Lookup(" " + pnr.ToLowerInvariant() + " ", "+905321234567", stranger); var detail = found.GetProperty("ticket");
        check(detail.GetProperty("seatNumber").GetInt32() == tickets[0].GetProperty("seatNumber").GetInt32() && detail.GetProperty("pnr").GetString() == pnr && detail.GetProperty("purchaseGroupId").ValueKind == JsonValueKind.Null, "Tek PNR yalnızca doğru yolcu ve koltuğu döndürür; sipariş bağlantısı açılmaz");
        var raw = detail.GetRawText(); check(!raw.Contains("10000000146") && !raw.Contains("guest@example.com") && !raw.Contains("+905321234567") && raw.Contains("1********46"), "Sorgu yanıtı kimlik ve iletişim bilgilerini maskeler");
        var pdf = await stranger.GetAsync("/api/ticket-lookup/pdf");
        check(pdf.IsSuccessStatusCode && (await pdf.Content.ReadAsByteArrayAsync()).AsSpan(0, 4).SequenceEqual("%PDF"u8) && pdf.Headers.CacheControl!.NoStore, "Kapsamlı kısa erişim mevcut altyapıyla önbelleksiz PDF verir");
        check((await stranger.GetAsync($"/api/guest/purchases/{id}/tickets/{secondPnr}/pdf")).StatusCode == HttpStatusCode.NotFound && (await stranger.GetAsync($"/api/tickets/{secondPnr}/pdf")).StatusCode == HttpStatusCode.Unauthorized, "Sorgulama yetkisi diğer yolcuya ve üye PDF servisine geçmez");
        check((await guest.GetAsync($"/api/guest/purchases/{id}/tickets/{pnr}/pdf")).IsSuccessStatusCode, "Satın alan misafir kendi sonuç ekranından PDF indirebilir");
        await Lookup(pnr, " GUEST@EXAMPLE.COM ");
        if (disabled)
        {
            check((await guest.PostAsJsonAsync("/api/ticket-lookup/cancellation-code", new { })).StatusCode == HttpStatusCode.ServiceUnavailable && !await db.TicketCancellationChallenges.AnyAsync(), "Gönderim yapılandırılmadığında kod başarısı veya doğrulamasız iptal yoktur");
            return;
        }
        var sent = await Json(await guest.PostAsJsonAsync("/api/ticket-lookup/cancellation-code", new { })); var challengeId = sent.GetProperty("challengeId").GetGuid();
        var pickupDirectory = Environment.GetEnvironmentVariable("TRAVELA_GUEST_TEST_PICKUP")!;
        var mail = await File.ReadAllTextAsync(Path.Combine(pickupDirectory, challengeId + ".html")); var code = Regex.Match(mail, @"kodunuz: (\d{6})").Groups[1].Value;
        check(code.Length == 6 && !sent.GetRawText().Contains(code), "Kod yalnızca test posta çıktısına gider, HTTP yanıtında yer almaz");
        check((await guest.PostAsJsonAsync("/api/ticket-lookup/cancellation-code", new { })).StatusCode == HttpStatusCode.TooManyRequests, "Kod gönderiminde bilet başına bekleme ve saatlik sınır uygulanır");
        check((await stranger.PostAsJsonAsync("/api/ticket-lookup/cancel", new { challengeId, code })).StatusCode == HttpStatusCode.BadRequest, "Aynı bilete başka sorgu erişimi bile kodun kapsamını devralamaz");
        var wrongCode = code == "000000" ? "111111" : "000000";
        check((await guest.PostAsJsonAsync("/api/ticket-lookup/cancel", new { challengeId, code = wrongCode })).StatusCode == HttpStatusCode.BadRequest && await db.Reservations.AnyAsync(r => r.Pnr == pnr && r.Status == "Active"), "Yanlış kod iptal veya iade başlatmaz");
        check(await db.TicketCancellationChallenges.AnyAsync(c => c.Id == challengeId && c.Attempts == 1 && c.CodeHash != code), "Kod veritabanında anahtarlı özetle tutulur; denemeler kalıcı sayılır");
        var cancelled = await Json(await guest.PostAsJsonAsync("/api/ticket-lookup/cancel", new { challengeId, code }));
        check(cancelled.GetProperty("ticket").GetProperty("ticketStatus").GetString() == "Cancelled" && await db.PaymentOrders.AnyAsync(p => p.Id == lineId && p.RefundStatus == "Pending" && p.RefundAmount == 500), "Doğru tek kullanımlık kod yalnızca ilgili bileti iptal edip orijinal sağlayıcı iadesini başlatır");
        check((await guest.PostAsJsonAsync("/api/ticket-lookup/cancel", new { challengeId, code })).StatusCode == HttpStatusCode.BadRequest && await db.Reservations.AnyAsync(r => r.Pnr == secondPnr && r.Status == "Active"), "Kod tekrar kullanılamaz ve diğer yolcu bileti aktif kalır");
        var cancelledPdf = await guest.GetAsync("/api/ticket-lookup/pdf"); check(cancelledPdf.IsSuccessStatusCode, "İptal edilmiş bilet mevcut iptal ibareli PDF altyapısında açılır");
        await using (var refundDb = new TravelaDbContext(db.Database.GetDbConnection() is Microsoft.Data.SqlClient.SqlConnection sql
            ? new DbContextOptionsBuilder<TravelaDbContext>().UseSqlServer(sql.ConnectionString).Options : throw new Exception()))
        {
            var provider = new LocalTestPaymentProvider(refundDb, new LocalWebhookKey(), TimeProvider.System);
            await new PaymentService(refundDb, provider, TimeProvider.System).ProcessRefund(lineId, default);
            await new PaymentService(refundDb, provider, TimeProvider.System).ProcessRefund(lineId, default);
        }
        check(await db.PaymentOrders.AnyAsync(p => p.Id == lineId && p.RefundStatus == "Succeeded" && p.RefundReference != null), "Misafir iadesi orijinal ödeme sağlayıcısından doğrulanır; tekrar işlem güvenlidir");
        var pdfDirectory = Path.Combine(root, "tmp", "guest-ticket-qa"); Directory.CreateDirectory(pdfDirectory);
        await File.WriteAllBytesAsync(Path.Combine(pdfDirectory, "guest-lookup.pdf"), await pdf.Content.ReadAsByteArrayAsync());
        await File.WriteAllBytesAsync(Path.Combine(pdfDirectory, "guest-cancelled.pdf"), await cancelledPdf.Content.ReadAsByteArrayAsync());
        // Verify attempt limits and expiry against the real HTTP endpoint and persisted challenges.
        await Lookup(secondPnr);
        var grantCookie = (await guest.GetAsync("/api/ticket-lookup/ticket")).IsSuccessStatusCode; check(grantCookie, "Sorgu erişimi tek bilete yenilenir");
        var limited = await Json(await guest.PostAsJsonAsync("/api/ticket-lookup/cancellation-code", new { })); var limitedId = limited.GetProperty("challengeId").GetGuid();
        var limitedCode = Regex.Match(await File.ReadAllTextAsync(Path.Combine(pickupDirectory, limitedId + ".html")), @"kodunuz: (\d{6})").Groups[1].Value;
        for (var attempt = 0; attempt < 5; attempt++)
            check((await guest.PostAsJsonAsync("/api/ticket-lookup/cancel", new { challengeId = limitedId, code = limitedCode == "000000" ? "111111" : "000000" })).StatusCode == HttpStatusCode.BadRequest, "Hatalı kod denemesi sayılır ve bilet korunur");
        check((await guest.PostAsJsonAsync("/api/ticket-lookup/cancel", new { challengeId = limitedId, code = limitedCode })).StatusCode == HttpStatusCode.BadRequest &&
            await db.TicketCancellationChallenges.AnyAsync(c => c.Id == limitedId && c.Attempts == 5), "Beş hatalı denemeden sonra doğru kod da kullanılamaz");
        await db.TicketCancellationChallenges.Where(c => c.Id == limitedId).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, DateTime.UtcNow.AddMinutes(-2)));
        var expiring = await Json(await guest.PostAsJsonAsync("/api/ticket-lookup/cancellation-code", new { })); var expiringId = expiring.GetProperty("challengeId").GetGuid();
        var expiringCode = Regex.Match(await File.ReadAllTextAsync(Path.Combine(pickupDirectory, expiringId + ".html")), @"kodunuz: (\d{6})").Groups[1].Value;
        await db.TicketCancellationChallenges.Where(c => c.Id == expiringId).ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        check((await guest.PostAsJsonAsync("/api/ticket-lookup/cancel", new { challengeId = expiringId, code = expiringCode })).StatusCode == HttpStatusCode.BadRequest &&
            await db.Reservations.AnyAsync(r => r.Pnr == secondPnr && r.Status == "Active"), "Süresi geçmiş doğru kod bile iptal yapamaz");
        // Member checkout remains owned by the signed-in account; public lookup uses immutable order contact.
        user.Email = "guest@example.com"; await db.SaveChangesAsync();
        var login = await Json(await server.PostAsJsonAsync("/api/auth/login", new { user.Email, password = "TestPass123!" }));
        var token = login.GetProperty("token").GetString();
        async Task<HttpResponseMessage> Member(string method, string path, object? body = null)
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), path); req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) req.Content = JsonContent.Create(body); return await server.SendAsync(req);
        }
        var memberCart = Cart(5); var memberOrder = await Json(await Member("POST", "/api/purchases", memberCart)); var memberId = memberOrder.GetProperty("id").GetGuid();
        memberOrder = await Json(await Member("POST", $"/api/purchases/{memberId}/local-complete", new { outcome = "Succeeded" }));
        var memberPnr = memberOrder.GetProperty("tickets")[0].GetProperty("pnr").GetString()!;
        await Json(await Member("PUT", "/api/profile", new { fullName = "Değişen Profil", phoneNumber = "+905009998877" }));
        var memberLookup = await Lookup(memberPnr, "guest@example.com");
        check(memberLookup.GetProperty("ticket").GetProperty("passengerName").GetString() == "Misafir Yolcu 5", "Üye bileti profil değişse de satın alma iletişimi ve yolcu kaydıyla sorgulanır");
        var mine = await Json(await Member("GET", "/api/tickets/mine")); check(mine.EnumerateArray().All(t => t.GetProperty("purchaseGroupId").GetGuid() != id) && mine.GetArrayLength() == 1, "Üye Biletlerim yalnızca hesap sahipliğini gösterir; aynı iletişim misafir biletini bağlamaz");
        check((await Member("GET", $"/api/purchases/{id}")).StatusCode == HttpStatusCode.NotFound, "Üye sahipliği denetimi misafir siparişini açmaz");
        var failedOrder = await Json(await guest.PostAsJsonAsync("/api/guest/purchases", Cart(7))); var failedId = failedOrder.GetProperty("id").GetGuid();
        var pending = await Json(await guest.PostAsJsonAsync($"/api/guest/purchases/{failedId}/refresh", new { }));
        check(pending.GetProperty("paymentStatus").GetString() == "Pending" && pending.GetProperty("tickets")[0].GetProperty("pnr").ValueKind == JsonValueKind.Null, "Bekleyen ödeme açık durumda kalır ve bilet üretmez");
        var failed = await Pay(failedId, "Failed"); check(failed.GetProperty("paymentStatus").GetString() == "Failed" && failed.GetProperty("tickets")[0].GetProperty("pnr").ValueKind == JsonValueKind.Null, "Başarısız ödeme bilet üretmez ve durumu korunur");
        var restricted = new Campaign { CompanyId = company.Id, Name = "Üye Limiti", Code = "MEMBER10", Value = 10, PerUserLimit = 1, StartsAt = DateTime.UtcNow.AddDays(-1), EndsAt = DateTime.UtcNow.AddDays(5) };
        var unrestricted = new Campaign { CompanyId = company.Id, Name = "Herkese", Code = "GUEST10", Value = 10, PerUserLimit = null, StartsAt = restricted.StartsAt, EndsAt = restricted.EndsAt };
        db.AddRange(restricted, unrestricted); await db.SaveChangesAsync();
        var coupon = Cart(9); coupon.CouponCode = "MEMBER10";
        check((await guest.PostAsJsonAsync("/api/guest/purchases", coupon)).StatusCode == HttpStatusCode.BadRequest && !await db.Reservations.AnyAsync(r => r.TripId == trip.Id && r.SeatNumber == 9), "Hesap limitli kampanya misafirle aşılamaz; reddetme koltuk tutmaz");
        coupon.CouponCode = "GUEST10"; var discounted = await Json(await guest.PostAsJsonAsync("/api/guest/purchases", coupon));
        check(discounted.GetProperty("amount").GetDecimal() == 450, "Hesap limiti olmayan kampanya misafir satın almada uygulanır");
        var concurrent = Cart(11); var replies = await Task.WhenAll(guest.PostAsJsonAsync("/api/guest/purchases", concurrent), guest.PostAsJsonAsync("/api/guest/purchases", concurrent));
        check(replies.All(r => r.IsSuccessStatusCode) && (await Json(replies[0])).GetProperty("id").GetGuid() == (await Json(replies[1])).GetProperty("id").GetGuid(), "Eş zamanlı tekrar aynı misafir siparişini döndürür");
        var legacyOrder = await Json(await Member("POST", "/api/tickets/checkout", new { tripId = trip.Id, seatNumber = 13, gender = "Female", requestKey = Guid.NewGuid(), legalAcceptances = GroupPurchaseChecks.SalesConsent() }));
        var legacyId = legacyOrder.GetProperty("id").GetGuid();
        legacyOrder = await Json(await Member("POST", $"/api/tickets/local/{legacyId}/complete", new { outcome = "Succeeded" }));
        var legacyPnr = legacyOrder.GetProperty("pnr").GetString()!;
        await Json(await Member("PUT", "/api/profile", new { fullName = "Sonraki Profil", phoneNumber = "+905000000000" }));
        var legacyLookup = await Lookup(legacyPnr, "+905009998877");
        check(legacyLookup.GetProperty("ticket").GetProperty("pnr").GetString() == legacyPnr, "Eski tekli bilette satın alma anındaki rezervasyon iletişim kaydı kullanılır");
        await db.Reservations.Where(r => r.Pnr == legacyPnr).ExecuteUpdateAsync(s => s.SetProperty(r => r.PassengerEmailSnapshot, (string?)null).SetProperty(r => r.PassengerPhoneSnapshot, (string?)null));
        check((await guest.PostAsJsonAsync("/api/ticket-lookup", new { pnr = legacyPnr, contact = "guest@example.com" })).StatusCode == HttpStatusCode.NotFound &&
            (await Member("GET", $"/api/tickets/{legacyPnr}")).IsSuccessStatusCode, "İletişim kaydı olmayan eski bilet güncel profille açılmaz; üye sahiplik erişimi korunur");
        check(await db.Users.CountAsync() == userCount, "Tüm misafir satın alma, sorgulama ve iptal işlemleri kullanıcı hesabı oluşturmaz");
    }
    private sealed class MutableClock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing"; public string ApplicationName { get; set; } = "Tests";
        public string WebRootPath { get; set; } = ""; public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = ""; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
