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

internal static class BusSeatPlanChecks
{
    public static async Task Run(TravelaDbContext db, HttpClient http, Action<bool, string> check)
    {
        var company = new Company { Name = "Plan Yönetimi Test" }; var other = new Company { Name = "Diğer Plan Firması" };
        var bus = new Bus { Company = company, PlateNumber = "34 PLAN 46", NormalizedPlateNumber = "34PLAN46", SeatCapacity = 46, SeatLayout = "Unspecified" };
        var otherBus = new Bus { Company = other, PlateNumber = "34 PLAN 47", NormalizedPlateNumber = "34PLAN47", SeatCapacity = 46, SeatLayout = "Unspecified" };
        var route = new Travela.Api.Models.Route { DepartureCity = "İstanbul", ArrivalCity = "Ankara" };
        Trip NewTrip(Bus b, int day) => new() { Company = b.Company!, Bus = b, Route = route, SeatLayout = "Unspecified", SeatCapacitySnapshot = 46, SellableSeatCount = 46, AvailableSeats = 46, Price = 400, DepartureTime = DateTime.UtcNow.AddDays(day), ArrivalTime = DateTime.UtcNow.AddDays(day).AddHours(6) };
        var target = NewTrip(bus, 40); target.SeatPlanJson = SeatPlans.Write(SeatPlans.Create("Unspecified", 46, [46])); target.AvailableSeats = 45; target.SellableSeatCount = 45;
        var untouched = NewTrip(bus, 41); var occupied = NewTrip(bus, 42); var verified = NewTrip(bus, 43); var foreign = NewTrip(otherBus, 44);
        verified.SeatLayout = "2+1"; verified.SeatPlanJson = SeatPlans.Write(SeatPlans.Create("2+1", 46)); var verifiedJson = verified.SeatPlanJson;
        User Account(string email, string role) { var u = new User { FullName = "Plan Test", Email = email, Role = role }; u.PasswordHash = new PasswordHasher<User>().HashPassword(u, "TestPass123!"); return u; }
        var manager = Account("plan-manager@travela.test", "Company"); var stranger = Account("plan-stranger@travela.test", "Company"); var passenger = Account("plan-passenger@travela.test", "Passenger");
        db.AddRange(target, untouched, occupied, verified, foreign, manager, stranger, passenger); await db.SaveChangesAsync(); manager.CompanyId = company.Id; stranger.CompanyId = other.Id;
        var companyRole = await db.Roles.SingleOrDefaultAsync(r => r.Name == "Company") ?? new Role { Name = "Company" };
        foreach (var name in new[] { "bus.view", "bus.update" })
        {
            if (await db.RolePermissions.AnyAsync(r => r.Role.Name == "Company" && r.Permission.Name == name)) continue;
            var permission = await db.Permissions.SingleOrDefaultAsync(p => p.Name == name) ?? new Permission { Name = name };
            db.RolePermissions.Add(new RolePermission { Role = companyRole, Permission = permission });
        }
        var old = new Reservation { TripId = occupied.Id, PassengerId = passenger.Id, SeatNumber = 1, Price = 400, Kind = "Legacy", PassengerGender = "Female" }; db.Add(old); occupied.AvailableSeats--; await db.SaveChangesAsync();
        async Task<string> Login(User u) { var r = await http.PostAsJsonAsync("/api/auth/login", new { u.Email, password = "TestPass123!" }); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!; }
        var token = await Login(manager); var otherToken = await Login(stranger); var passengerToken = await Login(passenger);
        async Task<HttpResponseMessage> Send(string method, string path, object? data = null, string? auth = null)
        {
            using var r = new HttpRequestMessage(new HttpMethod(method), path); r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth ?? token); if (data != null) r.Content = JsonContent.Create(data); return await http.SendAsync(r);
        }
        async Task<JsonElement> Read(HttpResponseMessage r) { if (!r.IsSuccessStatusCode) throw new Exception(await r.Content.ReadAsStringAsync()); return await r.Content.ReadFromJsonAsync<JsonElement>(); }
        var path = $"/api/buses/{bus.Id}/seat-plan";
        var initial = await Read(await Send("GET", path)); var version = initial.GetProperty("version").GetString();
        check(!initial.GetProperty("verified").GetBoolean() && initial.GetProperty("seats").GetArrayLength() == 0 && initial.GetProperty("capacity").GetInt32() == 46, "Firma 46 koltuklu otobüste doğrulanmamış planı boş olarak görür");
        var available = initial.GetProperty("trips").EnumerateArray().ToDictionary(t => t.GetProperty("id").GetInt32());
        check(available[target.Id].GetProperty("canApply").GetBoolean() && !available[occupied.Id].GetProperty("canApply").GetBoolean() && !available[verified.Id].GetProperty("canApply").GetBoolean(), "Plan aktarımı için yalnızca güvenli mevcut seferler seçilebilir");
        var seats = SeatPlans.Create("2+2", 46);
        object Draft(List<SeatPosition>? positions = null, bool confirmed = true, int[]? ids = null, string? expected = null) => new { layout = "2+2", seats = positions ?? seats, confirmed, applyToTripIds = ids ?? new[] { target.Id }, expectedPlanVersion = expected ?? version };
        check((await Send("GET", path, auth: otherToken)).StatusCode == HttpStatusCode.NotFound && (await Send("PUT", path, Draft(), otherToken)).StatusCode == HttpStatusCode.NotFound, "Başka firma otobüs planını göremez veya değiştiremez");
        check((await Send("PUT", path, Draft(confirmed: false))).StatusCode == HttpStatusCode.BadRequest, "Açık doğrulama olmadan plan kaydedilemez");
        var duplicate = seats.ToList(); duplicate[1] = duplicate[1] with { Number = 1 };
        var collision = seats.ToList(); collision[1] = collision[1] with { Column = 1 };
        var aisle = seats.ToList(); aisle[0] = aisle[0] with { Column = 3 };
        check((await Send("PUT", path, Draft(duplicate))).StatusCode == HttpStatusCode.BadRequest && (await Send("PUT", path, Draft(collision))).StatusCode == HttpStatusCode.BadRequest && (await Send("PUT", path, Draft(aisle))).StatusCode == HttpStatusCode.BadRequest && (await Send("PUT", path, Draft(seats.Take(45).ToList()))).StatusCode == HttpStatusCode.BadRequest, "Tekrarlı numara/konum, koridor koltuğu ve eksik kapasite API'de reddedilir");
        check((await Send("PUT", path, Draft(ids: [foreign.Id]))).StatusCode == HttpStatusCode.NotFound, "Başka otobüs veya firmanın seferine plan aktarılamaz");
        check((await Send("PUT", path, Draft(ids: [target.Id, occupied.Id]))).StatusCode == HttpStatusCode.Conflict && await db.Buses.AsNoTracking().AnyAsync(b => b.Id == bus.Id && b.SeatLayout == "Unspecified") && await db.Trips.AsNoTracking().AnyAsync(t => t.Id == target.Id && t.SeatLayout == "Unspecified"), "Sepette uygun olmayan tek sefer varsa otobüs ve tüm sefer planları atomik olarak korunur");
        var saved = await Send("PUT", path, Draft()); saved.EnsureSuccessStatusCode();
        var result = await http.GetFromJsonAsync<PublicTripDetailDto>($"/api/trips/public/{target.Id}");
        check(result!.Seats.Count == 46 && result.SeatPlanAvailable && result.CanReserve && result.Seats.Single(s => s.Number == 46).IsBlocked && result.SellableSeatCount == 45, "Doğrulanan 46 koltuklu plan seçilen sefere aktarılır; önceki blokaj korunur ve satış koşullu açılır");
        check(await db.Trips.AsNoTracking().AnyAsync(t => t.Id == untouched.Id && t.SeatLayout == "Unspecified") && await db.Trips.AsNoTracking().AnyAsync(t => t.Id == verified.Id && t.SeatPlanJson == verifiedJson) && await db.Reservations.AsNoTracking().AnyAsync(r => r.Id == old.Id && r.Status == "Active" && r.SeatNumber == 1), "Seçilmemiş seferler, doğrulanmış planlar ve mevcut rezervasyonlar değiştirilmez");
        check((await Send("PUT", path, Draft(ids: [], expected: version))).StatusCode == HttpStatusCode.Conflict, "Eski plan sürümüyle eş zamanlı değişiklik üzerine yazılamaz");
        async Task<HttpResponseMessage> Book(int seat, string gender) => await Send("POST", "/api/purchases", new StartGroupPurchaseDto { TripId = target.Id, RequestKey = Guid.NewGuid(), LegalAcceptances = GroupPurchaseChecks.SalesConsent(), Passengers = [new() { SeatNumber = seat, Gender = gender, FullName = "Plan Yolcusu", Email = "traveler@travela.test", Phone = "+905321234567" }] }, passengerToken);
        check((await Book(1, "Female")).IsSuccessStatusCode && (await Book(2, "Male")).StatusCode == HttpStatusCode.Conflict && (await Book(2, "Female")).IsSuccessStatusCode && (await Book(3, "Male")).IsSuccessStatusCode, "Aktarılan fiziksel planda rezervasyon, eş koltuk cinsiyet kuralı ve koridor ayrımı çalışır");
        check((await Send("POST", "/api/buses", new { plateNumber = "34 AUTO 46", seatCapacity = 46, seatLayout = "2+2", isActive = true })).StatusCode == HttpStatusCode.BadRequest, "Genel otobüs ekleme endpoint'i doğrulama olmadan fiziksel planı satışa açamaz");
    }
}
