using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Trips;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

internal static class SeatRulesChecks
{
    public static void Unit()
    {
        var count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; Console.WriteLine("OK: " + message); }
        var now = DateTime.UtcNow; var plan = SeatPlans.Create("2+2", 12);
        foreach (var gender in new[] { "Female", "Male" })
        {
            var other = gender == "Female" ? "Male" : "Female";
            Check(SeatCompatibility.Validate(plan, [new(1, gender)], [], now) == null, "Boş koltuğa " + gender);
            Check(SeatCompatibility.Validate(plan, [new(2, gender)], [new(1, gender, "Ticket", null)], now) == null, "Aynı cinsiyet eş koltuk");
            Check(SeatCompatibility.Validate(plan, [new(2, other)], [new(1, gender, "Ticket", null)], now) != null, "Farklı cinsiyet eş koltuk engeli");
            Check(SeatCompatibility.Validate(plan, [new(3, other)], [new(2, gender, "Ticket", null)], now) == null, "Koridor karşısına kısıtlama yok");
        }
        Check(SeatCompatibility.Validate(plan, [new(1, "Female"), new(2, "Male")], [], now) != null, "Aynı sepet eş koltuk uyumu");
        Check(SeatCompatibility.Validate(SeatPlans.Create("2+1", 6), [new(3, "Male")], [new(2, "Female", "Ticket", null)], now) == null, "Tekli koltuk bağımsız");
        Check(SeatCompatibility.Validate(plan, [new(2, "Male")], [new(1, "Female", "Hold", now.AddMinutes(-1))], now) == null, "Süresi dolan tutma engellemez");
        Check(SeatCompatibility.Validate(plan, [new(2, "Male")], [new(1, "Female", "Hold", now.AddMinutes(1))], now) != null, "Aktif tutma eş koltuğu sınırlar");
        Check(SeatCompatibility.Validate(plan, [new(1, "Female")], [new(1, "Female", "Ticket", null)], now) != null, "Satılmış koltuk engeli");
        Check(SeatCompatibility.Validate(plan, [new(2, "Female")], [new(1, null, "Legacy", null)], now) != null, "Bilinmeyen eski cinsiyet tahmin edilmez");
        Check(SeatCompatibility.Validate(plan, [new(1, null)], [], now) != null, "Eksik cinsiyet engeli");
        var reordered = plan.Select(s => s with { Number = 13 - s.Number }).ToList();
        Check(SeatCompatibility.Adjacent(reordered, reordered[0])?.Number == 11, "Komşuluk numaradan bağımsız");
        Check(SeatPlans.ReadVerified(SeatPlans.Write(reordered), "Custom", 12).Count == 12, "Özel numaralı plan doğrulanır");
        var rejected = false; try { SeatPlans.ReadVerified(null, "Unspecified", 46); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Doğrulanmamış plan üretilmez");
        var equinox = DateTimeOffset.Parse("2026-03-20T12:00:00Z");
        Check(SunExposure.Position(equinox, new(0, 0)).Elevation > 87, "Ekinoks ekvator öğlesi referansı");
        Check(SunExposure.Position(equinox.AddHours(12), new(0, 0)).Elevation < -87, "Ekvator gece referansı");
        Check(Math.Abs(SunExposure.Position(equinox.ToOffset(TimeSpan.FromHours(3)), new(0, 0)).Elevation - SunExposure.Position(equinox, new(0, 0)).Elevation) < 0.0001, "Türkiye UTC+3 aynı anı korur");
        var points = Enumerable.Range(0, 11).Select(i => new RoutePoint(39, 30 + i * 0.05)).ToList();
        string Geometry(List<RoutePoint> p) => JsonSerializer.Serialize(new VerifiedRouteGeometry(p, "https://example.test/verified-road", now), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var day = DateTimeOffset.Parse("2026-06-21T09:00:00Z");
        Check(SunExposure.Calculate(day, day.AddHours(1), Geometry(points)).Side == "Right", "Doğuya giderken öğle güneşi sağda");
        Check(SunExposure.Calculate(day, day.AddHours(1), Geometry(points.AsEnumerable().Reverse().ToList())).Side == "Left", "Ters güzergâhta güneş solda");
        Check(SunExposure.Calculate(day.AddHours(12), day.AddHours(13), Geometry(points)).Side == "Night", "Gece boyunca güneş gösterilmez");
        var loop = points.Concat(points.Take(points.Count - 1).Reverse()).ToList();
        var mixed = SunExposure.Calculate(day, day.AddHours(2), Geometry(loop));
        Check(mixed.Side == "Mixed" && mixed.LeftMinutes > 0 && mixed.RightMinutes > 0, "Yol boyunca yön değişikliği süreyle değerlendirilir");
        Check(SunExposure.Calculate(day, day.AddHours(1), null).Side == "Unknown" && !SunExposure.ValidGeometry([new(39, 30), new(40, 40), new(41, 50)]), "Eksik veya seyrek geometri kesin sonuç üretmez");
        Console.WriteLine($"SONUÇ: {count} koltuk/güneş birim kontrolü başarılı.");
    }

    public static async Task Integration(TravelaDbContext db, HttpClient http, Action<bool, string> check)
    {
        var company = new Company { Name = "Koltuk Kuralı Test" };
        var bus = new Bus { Company = company, PlateNumber = "34 RULE 1", NormalizedPlateNumber = "34RULE1", SeatCapacity = 20, SeatLayout = "2+2", SeatPlanJson = SeatPlans.Write(SeatPlans.Create("2+2", 20)) };
        var route = new Travela.Api.Models.Route { DepartureCity = "İstanbul", ArrivalCity = "Ankara" };
        Trip NewTrip(int day) => new() { Company = company, Bus = bus, Route = route, SeatLayout = "2+2", SeatPlanJson = bus.SeatPlanJson, SeatCapacitySnapshot = 20, SellableSeatCount = 20, AvailableSeats = 20, Price = 400, DepartureTime = DateTime.UtcNow.AddDays(day), ArrivalTime = DateTime.UtcNow.AddDays(day).AddHours(6) };
        var trip = NewTrip(30); var secondTrip = NewTrip(31);
        var user = new User { FullName = "Kural Yolcusu", Email = "seat-rules@travela.test", Role = "Passenger" };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "TestPass123!"); db.AddRange(trip, secondTrip, user); await db.SaveChangesAsync();
        var login = await http.PostAsJsonAsync("/api/auth/login", new { user.Email, password = "TestPass123!" }); login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        async Task<HttpResponseMessage> Send(int tripId, params (int Seat, string Gender)[] passengers)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchases"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new StartGroupPurchaseDto { TripId = tripId, RequestKey = Guid.NewGuid(), LegalAcceptances = GroupPurchaseChecks.SalesConsent(), Passengers = passengers.Select(p => new TicketPassengerDto { SeatNumber = p.Seat, Gender = p.Gender, FullName = "Yolcu " + p.Seat, Email = "traveler@travela.test", Phone = "+905321234567" }).ToList() });
            return await http.SendAsync(request);
        }
        var first = await Send(trip.Id, (1, "Female")); check(first.IsSuccessStatusCode, "Kadın yolcu boş koltuğu API üzerinden tutar");
        check((await Send(trip.Id, (2, "Male"))).StatusCode == HttpStatusCode.Conflict, "Aktif kadın tutmasının eş koltuğuna erkek API isteği reddedilir");
        check((await Send(trip.Id, (2, "Female"))).IsSuccessStatusCode, "Kadın eş koltuğa kadın kabul edilir");
        check((await Send(trip.Id, (3, "Male"))).IsSuccessStatusCode, "Koridor karşısına erkek kabul edilir");
        check((await Send(trip.Id, (4, "Female"))).StatusCode == HttpStatusCode.Conflict, "Erkek eş koltuğa kadın API isteği reddedilir");
        check((await Send(trip.Id, (5, "Female"), (6, "Male"))).StatusCode == HttpStatusCode.Conflict && !await db.Reservations.AnyAsync(r => r.TripId == trip.Id && r.SeatNumber == 5), "Aynı sepet uyumsuzsa hiçbir tutma oluşmaz");
        check((await Send(secondTrip.Id, (1, "Male"))).IsSuccessStatusCode, "Aynı otobüs farklı seferde bağımsız doluluk kullanır");
        var races = await Task.WhenAll(Send(trip.Id, (7, "Female")), Send(trip.Id, (7, "Female")));
        check(races.Count(r => r.IsSuccessStatusCode) == 1 && races.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 1, "Gerçek SQL eşzamanlı aynı koltuk yarışında bir kazanan");
        var neighborRaces = await Task.WhenAll(Send(trip.Id, (9, "Female")), Send(trip.Id, (10, "Male")));
        check(neighborRaces.Count(r => r.IsSuccessStatusCode) == 1 && neighborRaces.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 1, "Gerçek SQL eş koltuk farklı cinsiyet yarışında bir kazanan");
        await db.Reservations.Where(r => r.TripId == trip.Id && r.SeatNumber <= 2).ExecuteUpdateAsync(s => s.SetProperty(r => r.HoldExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        check((await Send(trip.Id, (1, "Male"))).IsSuccessStatusCode, "Süresi dolan tutma ve cinsiyet kısıtı transaction içinde temizlenir");
        check((await Send(trip.Id, (12, ""))).StatusCode == HttpStatusCode.BadRequest, "Eksik cinsiyet HTTP 400");
        var valid = await Send(trip.Id, (13, "Female")); valid.EnsureSuccessStatusCode(); var id = (await valid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await db.Reservations.Where(r => r.TripId == trip.Id && r.SeatNumber == 13).ExecuteUpdateAsync(s => s.SetProperty(r => r.PassengerGender, "Male"));
        var neighbor = await Send(trip.Id, (14, "Male")); neighbor.EnsureSuccessStatusCode();
        await db.Reservations.Where(r => r.TripId == trip.Id && r.SeatNumber == 13).ExecuteUpdateAsync(s => s.SetProperty(r => r.PassengerGender, "Female"));
        using var complete = new HttpRequestMessage(HttpMethod.Post, $"/api/purchases/{id}/local-complete"); complete.Headers.Authorization = new("Bearer", token); complete.Content = JsonContent.Create(new { outcome = "Succeeded" });
        var completed = await http.SendAsync(complete); completed.EnsureSuccessStatusCode();
        check(await db.PaymentOrders.AnyAsync(p => p.PurchaseGroupId == id && p.RefundStatus == "Pending" && p.Reservation.Pnr == null), "Bilet kesinleştirmesinde yeniden cinsiyet kontrolü ve tam telafi iadesi");
        var publicTrip = await http.GetFromJsonAsync<PublicTripDetailDto>($"/api/trips/public/{trip.Id}");
        check(publicTrip!.SeatOccupancies.Any(s => s.SeatNumber == 1 && s.Gender == "Male") && publicTrip.SunExposure.Side == "Unknown", "API gerçek sefer cinsiyeti ve eksik geometri mesajı döndürür");
        var admin = new User { FullName = "Plan Yöneticisi", Email = "seat-admin@travela.test", Role = "Admin" };
        admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, "TestPass123!"); db.Add(admin);
        var role = await db.Roles.SingleAsync(r => r.Name == "Admin");
        foreach (var name in new[] { "trip.view", "bus.update" })
        {
            if (await db.RolePermissions.AnyAsync(r => r.Role.Name == "Admin" && r.Permission.Name == name)) continue;
            var permission = await db.Permissions.SingleOrDefaultAsync(p => p.Name == name) ?? new Permission { Name = name };
            db.RolePermissions.Add(new RolePermission { Role = role, Permission = permission });
        }
        await db.SaveChangesAsync();
        var adminLogin = await http.PostAsJsonAsync("/api/auth/login", new { admin.Email, password = "TestPass123!" }); adminLogin.EnsureSuccessStatusCode();
        var adminToken = (await adminLogin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        async Task<HttpResponseMessage> Put(string path, object body, bool asAdmin = true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, path); request.Headers.Authorization = new("Bearer", asAdmin ? adminToken : token); request.Content = JsonContent.Create(body); return await http.SendAsync(request);
        }
        var unverified = NewTrip(32); unverified.SeatLayout = "Unspecified"; unverified.SeatPlanJson = null; db.Add(unverified); await db.SaveChangesAsync();
        check((await Send(unverified.Id, (1, "Female"))).StatusCode == HttpStatusCode.Conflict && !(await http.GetFromJsonAsync<PublicTripDetailDto>($"/api/trips/public/{unverified.Id}"))!.SeatPlanAvailable, "Doğrulanmamış eski plan hem API hem arayüz satışını kapatır");
        var planRequest = new { layout = "Custom", seats = SeatPlans.Create("2+2", 20).Select(s => s with { Number = 21 - s.Number }), confirmed = true };
        check((await Put($"/api/trips/{unverified.Id}/seat-plan", planRequest, false)).StatusCode == HttpStatusCode.Forbidden, "Yolcu fiziksel planı değiştiremez");
        check((await Put($"/api/trips/{unverified.Id}/seat-plan", planRequest)).IsSuccessStatusCode && (await Send(unverified.Id, (1, "Female"))).IsSuccessStatusCode, "Yönetici numaradan bağımsız gerçek özel planı tanımlar ve satış açılır");
        check((await Put($"/api/trips/{unverified.Id}/seat-plan", planRequest)).StatusCode == HttpStatusCode.Conflict, "Aktif rezervasyonlu doğrulanmış fiziksel plan değiştirilemez");
        var points = Enumerable.Range(0, 11).Select(i => new RoutePoint(39, 30 + i * 0.05)).ToList();
        var geometryRequest = new { points, source = "https://example.test/verified-road", confirmed = true };
        check((await Put($"/api/routes/{route.Id}/geometry", geometryRequest, false)).StatusCode == HttpStatusCode.Forbidden && (await Put($"/api/routes/{route.Id}/geometry", new { points, source = "https://example.test/road", confirmed = false })).StatusCode == HttpStatusCode.BadRequest, "Yol geometrisi yalnızca yetkili ve açık doğrulamayla kaydedilir");
        check((await Put($"/api/routes/{route.Id}/geometry", geometryRequest)).IsSuccessStatusCode && (await http.GetFromJsonAsync<PublicTripDetailDto>($"/api/trips/public/{trip.Id}"))!.SunExposure.Side != "Unknown", "Doğrulanmış geometri API üzerinden sefer saatine bağlı güneş hesabını etkinleştirir");
    }
}
