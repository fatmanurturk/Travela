using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Payments;
using Travela.Api.Trips;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

internal static class SeatSelectionChecks
{
    private sealed class Clock(DateTime start) : TimeProvider { public DateTime Now = start; public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc)); }
    private sealed record Scope(Guid Id, string Token);
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    public static async Task Run(TravelaDbContext fixture, HttpClient http, Action<bool,string> check, Func<Task>? restart = null)
    {
        var connection = fixture.Database.GetConnectionString()!;
        TravelaDbContext Db() => new(new DbContextOptionsBuilder<TravelaDbContext>().UseSqlServer(connection).Options);
        var clock = new Clock(DateTime.UtcNow);
        var company = new Company { Name = "Süreli Koltuk Testi" };
        var route = new Travela.Api.Models.Route { DepartureCity = "İstanbul", ArrivalCity = "Ankara" };
        var bus = new Bus { Company = company, PlateNumber = "34 HOLD 1", NormalizedPlateNumber = "34HOLD1", SeatCapacity = 40,
            SeatLayout = "2+2", SeatPlanJson = SeatPlans.Write(SeatPlans.Create("2+2", 40)) };
        var trip = new Trip { Company = company, Bus = bus, Route = route, DepartureTime = clock.Now.AddDays(20), ArrivalTime = clock.Now.AddDays(20).AddHours(6),
            Price = 500, AvailableSeats = 40, SellableSeatCount = 40, SeatCapacitySnapshot = 40, SeatLayout = "2+2", SeatPlanJson = bus.SeatPlanJson };
        var user = new User { FullName = "Süre Test Yolcusu", Email = "seat-time@example.test", Role = "Passenger" }; user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "TestPass123!");
        fixture.AddRange(trip, user); await fixture.SaveChangesAsync();
        async Task<Scope> Create(int? from = null, int? to = null, int? userId = null, string ip = "seat-tests")
        {
            var scope = new Scope(Guid.NewGuid(), Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
            await using var db = Db(); var service = new SeatSelectionService(db, clock, new(db, clock));
            await service.Create(scope.Id, scope.Token, trip.Id, from, to, ip, userId, default); return scope;
        }
        async Task<JsonElement> Change(Scope s, int seat, string? gender = "Female") { await using var db = Db(); return Json(await new SeatSelectionService(db, clock, new(db, clock)).Change(s.Id, s.Token, seat, gender, gender == null, default)); }
        async Task<JsonElement> Read(Scope s) { await using var db = Db(); return Json(await new SeatSelectionService(db, clock, new(db, clock)).Get(s.Id, s.Token, default)); }
        async Task<PaymentFlowException?> Failure(Func<Task> action) { try { await action(); return null; } catch (PaymentFlowException e) { return e; } }
        async Task<PurchaseGroup> Checkout(Scope s, int? account, Guid? guest, params int[] seats)
        {
            await using var db = Db(); var provider = new LocalTestPaymentProvider(db, new(), clock);
            return await new PaymentService(db, provider, clock).StartGroup(account, Cart(s, seats), null, default, "127.0.0.1", guest, s.Token);
        }
        StartGroupPurchaseDto Cart(Scope s, params int[] seats) => new() { TripId = trip.Id, SelectionSessionId = s.Id, RequestKey = Guid.NewGuid(), ContactEmail = "contact@seat.example", ContactPhone = "05321234567",
            Passengers = seats.Select(seat => new TicketPassengerDto { SeatNumber = seat, Gender = "Female", FullName = "Yolcu " + seat, IdentityNumber = "10000000146" }).ToList(), LegalAcceptances = GroupPurchaseChecks.SalesConsent() };
        var empty = await Create(); check((await Read(empty)).GetProperty("expiresAt").ValueKind == JsonValueKind.Null && !await fixture.Reservations.AnyAsync(r => r.SelectionSessionId == empty.Id), "Ekran/oturum açılması koltuk ayırmaz ve süre başlatmaz");
        var a = await Create(userId: user.Id); var b = await Create(userId: user.Id);
        var first = await Change(a, 1); var expiry = first.GetProperty("expiresAt").GetDateTime();
        check(expiry == clock.Now.AddMinutes(5), "İlk cinsiyet onaylı seçim sunucu saatinden 5 dakika ayrılır");
        check(await fixture.Reservations.AnyAsync(r => r.SelectionSessionId == a.Id && r.SeatNumber == 1 && r.PassengerNameSnapshot == null && r.PassengerIdentityNumberSnapshot == null && r.Payment == null), "Seçim, yolcu adı/kimlik/iletişim ve ödeme olmadan aynı envanterde tutulur");
        check(!await fixture.PurchaseGroups.AnyAsync(g => g.TripId == trip.Id), "Seçim tek başına sipariş veya kampanya kontenjanı tüketmez");
        check((await Failure(async () => await Change(b, 1)))?.Code == "seat_unavailable", "Aynı hesabın bağımsız oturumu koltuğu devralamaz (409)");
        await using (var db = Db()) check((await Failure(async () => await new SeatSelectionService(db, clock, new(db, clock)).Change(a.Id, b.Token, 1, null, true, default)))?.Status == 404, "Oturum kimliği veya aynı hesap, başka tutmayı bırakmaya yetmez");
        clock.Now = clock.Now.AddMinutes(1);
        check((await Change(a, 2)).GetProperty("expiresAt").GetDateTime() == expiry && (await Read(a)).GetProperty("expiresAt").GetDateTime() == expiry, "Ek koltuk, yenileme ve arka plan sorgusu ortak süreyi uzatmaz");
        check((await Failure(async () => await Change(a, 2, "Male")))?.Code == "seat_incompatible", "Geçici komşu koltuğun cinsiyet kuralı sunucuda uygulanır");
        await Change(a, 1, null); check((await Read(a)).GetProperty("seats").GetArrayLength() == 1 && (await Read(a)).GetProperty("expiresAt").GetDateTime() == expiry, "Koltuk çıkarma yalnız ilgili kaydı bırakır; ortak süre değişmez");
        await Change(a, 1); check((await Read(a)).GetProperty("expiresAt").GetDateTime() == expiry, "Çıkarıp yeniden eklemek süre kazandırmaz");
        var c = await Create(); var d = await Create();
        var race = await Task.WhenAll(Failure(async () => await Change(c, 5)), Failure(async () => await Change(d, 5)));
        check(race.Count(e => e == null) == 1 && race.Count(e => e?.Status == 409) == 1, "İki bağımsız uygulama DbContext'i aynı koltuğu eş zamanlı seçer: yalnız biri başarılı");
        clock.Now = expiry.AddSeconds(1);
        check((await Read(a)).GetProperty("phase").GetString() == "Expired" && await fixture.Reservations.AnyAsync(r => r.SelectionSessionId == a.Id && r.Status == "Active"), "Süre dolması temizlik çalışmadan sunucuda geçersiz sayılır");
        await using (var db = Db()) check(!await new TripSegmentService(db, clock).OccupiedLegs(trip.Id, 1, 2).AnyAsync(l => l.SeatNumber == 1), "Uygunluk sorgusu gecikmiş temizliğe rağmen süresi dolan koltuğu boş sayar");
        check((await Failure(async () => await Change(a, 4)))?.Code == "selection_expired" && (await Failure(async () => await Checkout(a, user.Id, null, 1, 2)))?.Code == "selection_expired", "Süresi dolan oturum yenilenemez veya ödeme tutmasına dönüştürülemez");
        await Change(b, 1); check((await Read(b)).GetProperty("seats").GetArrayLength() == 1, "Yeni seçim eski tutmayı işlem içinde temizleyerek aynı koltuğu güvenle alır");
        await Change(b, 1, null);
        foreach (var member in new[] { true, false })
        {
            var session = await Create(userId: member ? user.Id : null, ip: member ? "member-checkout" : "guest-checkout");
            var seat = member ? 9 : 13; var hold = await Change(session, seat); await Change(session, seat + 1);
            var reservationIds = await fixture.Reservations.Where(r => r.SelectionSessionId == session.Id && r.Status == "Active").OrderBy(r => r.Id).Select(r => r.Id).ToArrayAsync();
            var selectionExpiry = hold.GetProperty("expiresAt").GetDateTime();
            Guid? guest = null;
            if (!member) { var g = new GuestCheckoutSession { ExpiresAt = clock.Now.AddDays(1) }; fixture.GuestCheckoutSessions.Add(g); await fixture.SaveChangesAsync(); guest = g.Id; }
            await using (var db = Db())
            {
                var invalid = Cart(session, seat, seat + 1); invalid.ExpectedAmount = 1;
                var p = new LocalTestPaymentProvider(db, new(), clock);
                check((await Failure(async () => await new PaymentService(db, p, clock).StartGroup(member ? user.Id : null, invalid, null, default, "127.0.0.1", guest, session.Token)))?.Status == 409,
                    "Fiyat/koşul hatası çoklu dönüşümün tamamını reddeder");
            }
            check(!await fixture.PaymentOrders.AnyAsync(p => reservationIds.Contains(p.ReservationId)) && await fixture.Reservations.CountAsync(r => reservationIds.Contains(r.Id) && r.HoldExpiresAt == selectionExpiry) == 2, "Hata ödeme, sipariş veya kısmi süre dönüşümü bırakmaz");
            clock.Now = clock.Now.AddSeconds(30); var transition = clock.Now;
            var order = await Checkout(session, member ? user.Id : null, guest, seat, seat + 1);
            check(order.HoldExpiresAt == transition.AddMinutes(10), "Üye ve misafir için ödeme süresi başarılı geçişten itibaren 10 dakikadır");
            check(await fixture.Reservations.CountAsync(r => reservationIds.Contains(r.Id) && r.Payment!.PurchaseGroupId == order.Id && r.HoldExpiresAt == order.HoldExpiresAt && r.Status == "Active") == 2,
                "Atomik geçiş aynı rezervasyon kimliklerini ve aktif bacaklarını korur; koltuk serbest kalmaz");
            clock.Now = clock.Now.AddMinutes(1);
            var repeat = await Checkout(session, member ? user.Id : null, guest, seat, seat + 1);
            check(repeat.Id == order.Id && repeat.HoldExpiresAt == order.HoldExpiresAt && await fixture.PurchaseGroups.CountAsync(g => g.Id == order.Id) == 1, "Yeni işlem anahtarı/yenileme/geri dönüş bile aynı sipariş ve bitiş zamanını döndürür");
            check((await Failure(async () => await Change(session, seat, null)))?.Code == "selection_in_payment", "Ödeme aşamasına geçen koltuk seçim endpoint'iyle bırakılamaz");
            if (member)
            {
                await using var db = Db(); var p = new LocalTestPaymentProvider(db, new(), clock); var payments = new PaymentService(db, p, clock);
                var notice = await p.Simulate(order.Id, "Succeeded", "Succeeded", default); await payments.HandleNotice(notice, p.Sign(notice), default);
                check(await fixture.Reservations.CountAsync(r => reservationIds.Contains(r.Id) && r.Kind == "Ticket" && r.Pnr != null) == 2, "Başarılı ödeme mevcut bilet/PNR süreciyle aynı tutmayı satışa çevirir");
            }
            else
            {
                clock.Now = order.HoldExpiresAt.AddSeconds(1);
                var next = await Create(ip: "next-guest"); await Change(next, seat);
                await using var db = Db(); var p = new LocalTestPaymentProvider(db, new(), clock); var payments = new PaymentService(db, p, clock);
                var notice = await p.Simulate(order.Id, "Succeeded", "Succeeded", default); await payments.HandleNotice(notice, p.Sign(notice), default);
                check(await fixture.PaymentOrders.CountAsync(p => p.PurchaseGroupId == order.Id && p.RefundStatus == "Pending" && p.Reservation.Pnr == null) == 2 && (await Read(next)).GetProperty("seats").GetArrayLength() == 1,
                    "Geç ödeme ikinci bilet üretmez; tüm sipariş telafi iadesine girer, yeni tutma korunur");
            }
        }
        await using (var db = Db()) { await new PaymentService(db, new LocalTestPaymentProvider(db, new(), clock), clock).ExpireHolds(default); }
        check(await fixture.Reservations.CountAsync(r => r.TripId == trip.Id && r.Kind == "Ticket" && r.Status == "Active") == 2, "Arka plan temizliği satılmış koltukları bırakmaz");
        // Append a third stop to a new trip to exercise non-overlapping legs without rewriting history.
        var segmented = new Trip { Company = company, Bus = bus, Route = route, DepartureTime = clock.Now.AddDays(10), ArrivalTime = clock.Now.AddDays(10).AddHours(6), Price = 500,
            AvailableSeats = 40, SellableSeatCount = 40, SeatCapacitySnapshot = 40, SeatLayout = "2+2", SeatPlanJson = bus.SeatPlanJson };
        segmented.Stops = Enumerable.Range(1,3).Select(i => new TripStop { Trip = segmented, StopOrder = i, City = i == 1 ? "İstanbul" : i == 2 ? "Bolu" : "Ankara", CanBoard = i < 3, CanAlight = i > 1,
            ArrivalTime = segmented.DepartureTime.AddHours((i-1)*3), DepartureTime = segmented.DepartureTime.AddHours((i-1)*3) }).ToList();
        segmented.Legs = [new() { Trip = segmented, LegNumber = 1, FromStopOrder = 1, ToStopOrder = 2 },new() { Trip = segmented, LegNumber = 2, FromStopOrder = 2, ToStopOrder = 3 }];
        segmented.SegmentFares = [new() { Trip = segmented, FromStopOrder = 1, ToStopOrder = 2, Amount = 250, Source = "Manual", IsApproved = true, ApprovedAt = clock.Now },new() { Trip = segmented, FromStopOrder = 2, ToStopOrder = 3, Amount = 250, Source = "Manual", IsApproved = true, ApprovedAt = clock.Now }];
        fixture.Trips.Add(segmented); await fixture.SaveChangesAsync();
        async Task<Scope> Leg(int from, int to) { var s = new Scope(Guid.NewGuid(), new string('a',64)); await using var db = Db(); await new SeatSelectionService(db, clock, new(db, clock)).Create(s.Id,s.Token,segmented.Id,from,to,"segment-ip",null,default); return s; }
        var ab = await Leg(1,2); var bc = await Leg(2,3); var ac = await Leg(1,3); await Change(ab,1); await Change(bc,1,"Male");
        check((await Failure(async () => await Change(ac,1)))?.Code == "seat_unavailable", "Aynı koltuk ayrık kesimlerde iki cinsiyetle tutulabilir; çakışan kesim engellenir");
        for (var seat = 5; seat < 15; seat++) await Change(ac, seat);
        check((await Failure(async () => await Change(ac,15)))?.Code == "selection_limit" && (await Read(ac)).GetProperty("seats").GetArrayLength() == 10, "On koltuk sınırı sunucuda uygulanır; fazladan koltuk ayrılmaz");
        var concurrent = await Create(ip: "concurrent-checkout"); await Change(concurrent,33);
        var orders = await Task.WhenAll(Checkout(concurrent,user.Id,null,33),Checkout(concurrent,user.Id,null,33));
        check(orders[0].Id == orders[1].Id && orders[0].HoldExpiresAt == orders[1].HoldExpiresAt, "Eş zamanlı ödeme geçişi farklı istek anahtarlarıyla bile tek sipariş ve tek bitiş üretir");
        var quotaUser = new User { FullName = "Sınır Testi", Email = "quota-seat@example.test", Role = "Passenger", PasswordHash = user.PasswordHash };
        fixture.Users.Add(quotaUser); await fixture.SaveChangesAsync();
        Trip? quotaTrip = null;
        for (var i = 0; i < 4; i++)
        {
            quotaTrip = new Trip { Company = company, Bus = bus, Route = route, DepartureTime = clock.Now.AddDays(30+i), ArrivalTime = clock.Now.AddDays(30+i).AddHours(6),
                Price = 500, AvailableSeats = 40, SellableSeatCount = 40, SeatCapacitySnapshot = 40, SeatLayout = "2+2", SeatPlanJson = bus.SeatPlanJson };
            fixture.Trips.Add(quotaTrip); await fixture.SaveChangesAsync();
            var scope = new Scope(Guid.NewGuid(), new string('e',64)); await using var db = Db();
            await new SeatSelectionService(db,clock,new(db,clock)).Create(scope.Id,scope.Token,quotaTrip.Id,null,null,"quota-network",quotaUser.Id,default);
            for (var seat = 1; seat <= 10; seat++) await Change(scope,seat);
        }
        foreach (var (ip, account) in new[] { ("quota-network",(int?)null), ("different-network",(int?)quotaUser.Id) })
        {
            var scope = new Scope(Guid.NewGuid(), new string('f',64)); await using var db = Db();
            await new SeatSelectionService(db,clock,new(db,clock)).Create(scope.Id,scope.Token,quotaTrip!.Id,null,null,ip,account,default);
            check((await Failure(async () => await Change(scope,11)))?.Code == "active_hold_limit", "IP veya hesap başına aktif tutma sınırı bağımsız oturum ve seferlerle aşılamaz");
        }
        // Real API ownership + member and guest checkout boundary checks.
        var login = await http.PostAsJsonAsync("/api/auth/login", new { user.Email, password = "TestPass123!" }); login.EnsureSuccessStatusCode();
        var jwt = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var paidPnr = await fixture.Reservations.Where(r => r.TripId == trip.Id && r.Kind == "Ticket").Select(r => r.Pnr).FirstAsync();
        using (var request = new HttpRequestMessage(HttpMethod.Get,$"/api/tickets/{paidPnr}/pdf")) {
            request.Headers.Authorization = new("Bearer",jwt); var response = await http.SendAsync(request);
            check(response.IsSuccessStatusCode && response.Headers.CacheControl!.NoStore, "Yeni tutmadan üretilen üye bileti mevcut sahiplik kontrollü PDF akışında çalışır");
        }
        using (var request = new HttpRequestMessage(HttpMethod.Post,$"/api/tickets/{paidPnr}/cancel")) {
            request.Headers.Authorization = new("Bearer",jwt); var response = await http.SendAsync(request);
            check(response.IsSuccessStatusCode && await fixture.PaymentOrders.AnyAsync(p => p.Reservation.Pnr == paidPnr && p.RefundStatus == "Pending"), "Mevcut üye iptal ve orijinal ödeme üzerinden iade talebi korunur");
        }
        using var guestClient = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = http.BaseAddress };
        foreach (var member in new[] { true, false })
        {
            var s = new Scope(Guid.NewGuid(), new string(member ? 'c' : 'd',64));
            using var create = new HttpRequestMessage(HttpMethod.Post,"/api/seat-selections") { Content = JsonContent.Create(new { s.Id, s.Token, tripId = trip.Id }) };
            if (member) create.Headers.Authorization = new("Bearer",jwt);
            (await guestClient.SendAsync(create)).EnsureSuccessStatusCode();
            using var select = new HttpRequestMessage(HttpMethod.Put,$"/api/seat-selections/{s.Id}/seats") { Content = JsonContent.Create(new { seatNumber = member ? 21 : 25, gender = "Female" }) };
            select.Headers.Add("X-Seat-Token",s.Token); (await guestClient.SendAsync(select)).EnsureSuccessStatusCode();
            check((await http.GetAsync($"/api/seat-selections/{s.Id}")).StatusCode == HttpStatusCode.NotFound, "HTTP'de seçim kimliği tek başına tutmayı okuyamaz");
            if (!member) (await guestClient.PostAsJsonAsync("/api/guest/purchases/session",new { })).EnsureSuccessStatusCode();
            using var request = new HttpRequestMessage(HttpMethod.Post,member ? "/api/purchases" : "/api/guest/purchases") { Content = JsonContent.Create(Cart(s,member ? 21 : 25)) };
            request.Headers.Add("X-Seat-Token",s.Token); if (member) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",jwt);
            var response = await guestClient.SendAsync(request); if (!response.IsSuccessStatusCode) throw new Exception(await response.Content.ReadAsStringAsync());
            check((await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("serverTime",out _), "Üye/misafir HTTP ödeme dönüşümü sunucu ve bitiş saatini döndürür");
            if (restart != null)
            {
                await restart(); using var read = new HttpRequestMessage(HttpMethod.Get,$"/api/seat-selections/{s.Id}"); read.Headers.Add("X-Seat-Token",s.Token);
                var restored = await http.SendAsync(read); restored.EnsureSuccessStatusCode();
                check((await restored.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("phase").GetString() == "Payment", "Gerçek API süreci yeniden başlatılınca oturum ve ödeme tutması SQL üzerinden geri yüklenir");
            }
        }
    }
}
