using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Travela.Api.Models;
using Travela.Api.Trips;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

internal static class JourneyChecks
{
    public static async Task Run(TravelaDbContext db, HttpClient http, Action<bool, string> check)
    {
        foreach (var roleName in new[] { "Company", "Admin" })
        foreach (var permissionName in new[] { "bus.create", "bus.view", "bus.update", "trip.create", "trip.update", "trip.view" })
        {
            var role = await db.Roles.SingleOrDefaultAsync(r => r.Name == roleName) ?? new Role { Name = roleName };
            var permission = await db.Permissions.SingleOrDefaultAsync(p => p.Name == permissionName) ?? new Permission { Name = permissionName };
            if (!await db.RolePermissions.AnyAsync(rp => rp.Role.Name == roleName && rp.Permission.Name == permissionName))
            { db.RolePermissions.Add(new RolePermission { Role = role, Permission = permission }); await db.SaveChangesAsync(); }
        }
        var company = new Company { Name = "Koltuk Terminal Test Firması" }; db.Add(company); await db.SaveChangesAsync();
        User Account(string email, string role)
        {
            var user = new User { FullName = "Terminal Yolcusu", Email = email + "@journey.example", Role = role, CompanyId = role == "Company" ? company.Id : null };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "TestPass123!"); return user;
        }
        var owner = Account("owner", "Passenger"); var admin = Account("admin", "Admin"); var manager = Account("manager", "Company");
        db.AddRange(owner, admin, manager); await db.SaveChangesAsync();
        async Task<HttpResponseMessage> Send(string method, string path, string? token = null, object? body = null)
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
        async Task<string> Login(User user) => (await Json(await Send("POST", "/api/auth/login", body: new { user.Email, password = "TestPass123!" }))).GetProperty("token").GetString()!;
        var ownerToken = await Login(owner); var adminToken = await Login(admin); var companyToken = await Login(manager);
        var departureDto = new { name = "İstanbul Test Otogarı", city = "İstanbul", address = "Test Mahallesi 1. Cadde No 12", isActive = true };
        var arrivalDto = new { name = "Ankara Test Terminali", city = "Ankara", address = "Test Bulvarı No 20", isActive = true };
        check((await Send("POST", "/api/terminals", companyToken, departureDto)).StatusCode == HttpStatusCode.Forbidden, "Firma terminal katalogunu değiştiremez");
        var departureId = (await Json(await Send("POST", "/api/terminals", adminToken, departureDto))).GetProperty("terminal").GetProperty("id").GetInt32();
        var arrivalId = (await Json(await Send("POST", "/api/terminals", adminToken, arrivalDto))).GetProperty("terminal").GetProperty("id").GetInt32();
        check((await Send("GET", "/api/terminals?city=" + Uri.EscapeDataString("İstanbul"))).IsSuccessStatusCode &&
            (await Send("POST", "/api/terminals", adminToken, departureDto)).StatusCode == HttpStatusCode.Conflict, "Terminal listesi şehirden okunur ve tekrar kayıt engellenir");
        var busPayload = new { companyId = company.Id, plateNumber = "34 JRN 777", seatCapacity = 9, seatLayout = "2+1", seatPlanConfirmed = true, blockedSeats = new[] { 3, 8 }, isActive = true };
        var busId = (await Json(await Send("POST", "/api/buses", companyToken, busPayload))).GetProperty("busId").GetInt32();
        var busDetail = await Json(await Send("GET", "/api/buses/" + busId, companyToken));
        check(busDetail.GetProperty("seatLayout").GetString() == "2+1", "Firma otobüs eklerken 2+1 düzenini ve kapalı koltukları kaydedebilir");
        check((await Send("POST", "/api/buses", companyToken, new { plateNumber = "34 JRN 778", seatCapacity = 9, seatLayout = "2+3" })).StatusCode == HttpStatusCode.BadRequest,
            "Geçersiz otobüs koltuk düzeni reddedilir");
        var route = new Travela.Api.Models.Route { DepartureCity = "İstanbul", ArrivalCity = "Ankara", EstimatedDurationMinutes = 360 };
        db.Routes.Add(route); await db.SaveChangesAsync();
        var start = DateTime.UtcNow.AddDays(8);
        object TripPayload(int offset = 0, int? arrival = null) => new { busId, routeId = route.Id, departureTime = start.AddDays(offset), arrivalTime = start.AddDays(offset).AddHours(6), price = 750,
            departureTerminalId = departureId, arrivalTerminalId = arrival ?? arrivalId, boardingInstructions = "Peron 12. Kalkıştan 20 dakika önce hazır olun." };
        check((await Send("POST", "/api/trips", companyToken, TripPayload(arrival: departureId))).StatusCode == HttpStatusCode.BadRequest, "Terminalin şehri güzergâhla eşleşmelidir");
        var tripId = (await Json(await Send("POST", "/api/trips", companyToken, TripPayload()))).GetProperty("tripId").GetInt32();
        var publicTrip = (await Send("GET", "/api/trips/public/" + tripId)).Content;
        var plan = (await publicTrip.ReadFromJsonAsync<PublicTripDetailDto>())!;
        check(plan.SeatLayout == "2+1" && plan.Seats.Count == 9 && plan.SellableSeatCount == 7 && plan.AvailableSeats == 7 && plan.SeatPlanAvailable,
            "Sefer fiziksel ve satışa açık kapasiteyi ayırır; yolcu firma ile aynı 2+1 planını görür");
        check(plan.Seats.All(s => s.Column != 3) && plan.Seats[2].Column == 4 && plan.Seats[2].IsWindow && plan.Seats[2].IsAisle && plan.Seats[2].IsBlocked,
            "2+1 planında koridor boşluğu, pencere ve tekli koltuğun konumu doğrudur");
        check(plan.DepartureTerminalName == departureDto.name && plan.DepartureMapUrl?.StartsWith("https://www.google.com/maps/") == true && plan.BoardingInstructions?.Contains("Peron 12") == true,
            "Sefer detayında terminal, açık adres, güvenli harita bağlantısı ve biniş açıklaması bulunur");
        Task<HttpResponseMessage> StartSeat(int seat) => Send("POST", "/api/tickets/checkout", ownerToken,
            new { tripId, seatNumber = seat, gender = "Female", requestKey = Guid.NewGuid(), legalAcceptances = GroupPurchaseChecks.SalesConsent() });
        check((await StartSeat(3)).StatusCode == HttpStatusCode.Conflict && (await StartSeat(10)).StatusCode == HttpStatusCode.BadRequest,
            "API manipülasyonuyla kapalı veya kapasite dışı koltuk alınamaz");
        var hold = (await Json(await StartSeat(1))).GetProperty("id").GetGuid();
        check((await Send("PUT", $"/api/trips/{tripId}/seat-blocks", companyToken, new { blockedSeats = new[] { 1, 3, 8 } })).StatusCode == HttpStatusCode.Conflict,
            "Ödeme için tutulmuş koltuk satışa kapatılamaz");
        await Json(await Send("PUT", $"/api/trips/{tripId}/seat-blocks", companyToken, new { blockedSeats = new[] { 2, 3, 8 } }));
        check(await db.Trips.AnyAsync(t => t.Id == tripId && t.SellableSeatCount == 6 && t.AvailableSeats == 5), "Sefer blokajı müsait koltuk sayısını aktif tutmaları koruyarak günceller");
        await Json(await Send("POST", $"/api/tickets/local/{hold}/complete", ownerToken, new { outcome = "Failed" }));
        check(await db.Trips.AnyAsync(t => t.Id == tripId && t.AvailableSeats == 6), "Başarısız ödeme koltuğu açar; kapalı koltuklar kontenjana eklenmez");
        var paidId = (await Json(await StartSeat(4))).GetProperty("id").GetGuid();
        var paid = await Json(await Send("POST", $"/api/tickets/local/{paidId}/complete", ownerToken, new { outcome = "Succeeded" }));
        var pnr = paid.GetProperty("pnr").GetString()!;
        check(paid.GetProperty("departureTerminalName").GetString() == departureDto.name, "Bilet satın alma yanıtı kayıtlı biniş bilgilerini taşır");
        await Json(await Send("PUT", "/api/buses/" + busId, companyToken, new { busPayload.companyId, busPayload.plateNumber, seatCapacity = 9, seatLayout = "2+2", seatPlanConfirmed = true, blockedSeats = new[] { 5 }, isActive = true }));
        plan = (await (await Send("GET", "/api/trips/public/" + tripId)).Content.ReadFromJsonAsync<PublicTripDetailDto>())!;
        check(plan.SeatLayout == "2+1" && plan.Seats[1].IsBlocked && plan.SellableSeatCount == 6, "Otobüs düzeni değişse de mevcut sefer ve biletin planı korunur");
        await Json(await Send("PUT", "/api/terminals/" + departureId, adminToken, new { name = "Yeni Terminal Adı", departureDto.city, address = "Yeni Terminal Adresi", isActive = true }));
        var ticket = await Json(await Send("GET", "/api/tickets/" + pnr, ownerToken));
        check(ticket.GetProperty("departureTerminalName").GetString() == departureDto.name && ticket.GetProperty("departureTerminalAddress").GetString()!.Contains(departureDto.address),
            "Terminal kataloğu değişse de alınmış biletin biniş adresi değişmez");
        var nextId = (await Json(await Send("POST", "/api/trips", companyToken, TripPayload(1)))).GetProperty("tripId").GetInt32();
        var next = (await (await Send("GET", "/api/trips/public/" + nextId)).Content.ReadFromJsonAsync<PublicTripDetailDto>())!;
        check(next.SeatLayout == "2+2" && next.SellableSeatCount == 8 && next.DepartureTerminalName == "Yeni Terminal Adı", "Yeni sefer güncel otobüs planını ve terminal adresini kullanır");
        var pdf = await Send("GET", "/api/tickets/" + pnr + "/pdf", ownerToken); pdf.EnsureSuccessStatusCode();
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "output", "pdf"); Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "journey-ticket.pdf"), await pdf.Content.ReadAsByteArrayAsync());
        check(pdf.Content.Headers.ContentType?.MediaType == "application/pdf", "Terminal ve biniş açıklaması içeren bilet PDF'si üretildi");
        await Json(await Send("PUT", "/api/terminals/" + departureId, adminToken, new { name = "Yeni Terminal Adı", departureDto.city, address = "Yeni Terminal Adresi", isActive = false }));
        plan = (await (await Send("GET", "/api/trips/public/" + tripId)).Content.ReadFromJsonAsync<PublicTripDetailDto>())!;
        check(!plan.CanReserve && (await StartSeat(6)).StatusCode == HttpStatusCode.Conflict, "Pasif terminale bağlı sefer için yeni satış engellenir");
        check((await Send("POST", "/api/trips", companyToken, TripPayload(2))).StatusCode == HttpStatusCode.BadRequest, "Pasif terminal yeni sefere atanamaz");
        var blocked = false;
        try { await db.GetService<IMigrator>().MigrateAsync("20261104073635_AddTicketPassengerSnapshotsAndPaymentHistory"); }
        catch (SqlException e) when (e.Number == 51000) { blocked = true; }
        check(blocked && await db.Trips.AnyAsync(t => t.Id == tripId && t.SeatLayout == "2+1"), "Koltuk ve terminal bilgilerini kaybettirecek geri migration engellenir");
        await db.Database.MigrateAsync();
    }
}
