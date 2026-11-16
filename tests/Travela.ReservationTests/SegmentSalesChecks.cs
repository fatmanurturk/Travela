using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Routes;
using Travela.Api.Trips;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

internal static class SegmentSalesChecks
{
    public static async Task Run(TravelaDbContext db, HttpClient http, Action<bool,string> check)
    {
        var company = new Company { Name = "Ara Durak Satış Testi" };
        var route = new Travela.Api.Models.Route { DepartureCity = "Diyarbakır", ArrivalCity = "İstanbul" };
        var terminals = new[] { "Diyarbakır", "Elazığ", "Ankara", "İstanbul", "Ankara" }.Select((city,i) => new Terminal { City = city, Name = city + " test terminali " + i, Address = "Test adresi" }).ToList();
        var passenger = new User { FullName = "Kesim Yolcusu", Email = "segment-sales@travela.test", Role = "Passenger", SmsNotificationsEnabled = true };
        var admin = new User { FullName = "Kesim Yöneticisi", Email = "segment-admin@travela.test", Role = "Admin" };
        foreach (var user in new[] { passenger, admin }) user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "TestPass123!");
        db.AddRange(company, route, passenger, admin); db.AddRange(terminals); await db.SaveChangesAsync();
        var adminRole = await db.Roles.SingleAsync(r => r.Name == "Admin");
        foreach (var name in new[] { "trip.view", "trip.create", "trip.update" }) {
            var permission = await db.Permissions.SingleOrDefaultAsync(p => p.Name == name) ?? new Permission { Name = name };
            if (!await db.RolePermissions.AnyAsync(r => r.RoleId == adminRole.Id && r.Permission.Name == name)) db.RolePermissions.Add(new RolePermission { Role = adminRole, Permission = permission });
        }
        await db.SaveChangesAsync();
        async Task<HttpResponseMessage> Send(string method, string path, object? body = null, string? token = null) { using var request = new HttpRequestMessage(new(method), path); if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",token); if (body != null) request.Content = JsonContent.Create(body); return await http.SendAsync(request); }
        async Task<JsonElement> Json(HttpResponseMessage response) { var body = await response.Content.ReadAsStringAsync(); if (!response.IsSuccessStatusCode) throw new Exception(response.StatusCode + ": " + body); return JsonDocument.Parse(body).RootElement.Clone(); }
        async Task<string> Login(User user) => (await Json(await Send("POST","/api/auth/login", new { email = user.Email, password = "TestPass123!" }))).GetProperty("token").GetString()!;
        var token = await Login(passenger); var adminToken = await Login(admin);
        var start = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(30).AddHours(18), DateTimeKind.Utc);
        async Task<int> NewTrip(bool duplicateCity = false, bool template = false)
        {
            var bus = new Bus { CompanyId = company.Id, PlateNumber = Guid.NewGuid().ToString("N")[..12], SeatCapacity = 6, SeatLayout = "2+1", SeatPlanJson = SeatPlans.Write(SeatPlans.Create("2+1",6)) }; bus.NormalizedPlateNumber = bus.PlateNumber;
            db.Buses.Add(bus); await db.SaveChangesAsync();
            var selected = duplicateCity ? new[] { terminals[0], terminals[1], terminals[2], terminals[4], terminals[3] } : terminals.Take(4).ToArray();
            var stops = selected.Select((t,i) => new TripStopInputDto { CityId = TurkishCities.Id(t.City)!.Value, TerminalId = t.Id, ArrivalTime = new(start.AddHours(i * 6)), DepartureTime = new(start.AddHours(i * 6)), CanBoard = i < selected.Length - 1, CanAlight = i > 0 }).ToList();
            var fares = Enumerable.Range(1,selected.Length - 1).SelectMany(a => Enumerable.Range(a + 1,selected.Length - a).Select(b => new SegmentFareInputDto { FromStopOrder = a, ToStopOrder = b, Amount = 100 * (b - a), IsApproved = true })).ToList();
            var created = await Json(await Send("POST","/api/trips", new CreateTripDto { CompanyId = company.Id, RouteId = route.Id, BusId = bus.Id, DepartureTime = start, ArrivalTime = start.AddHours((selected.Length - 1) * 6), DepartureTerminalId = selected[0].Id, ArrivalTerminalId = selected[^1].Id, Price = 900, Stops = template ? null : stops, Fares = template ? null : fares }, adminToken));
            return created.GetProperty("tripId").GetInt32();
        }
        async Task<JsonElement> Search(int from, int to, DateTime departure) => await Json(await Send("GET", $"/api/trips/search?departureCityId={from}&arrivalCityId={to}&date={departure.AddHours(3):yyyy-MM-dd}&pageSize=100"));
        var id = await NewTrip();
        async Task<SaveTripStopsDto> Draft(int tripId) => new() {
            ExpectedVersion = await db.Trips.AsNoTracking().Where(t => t.Id == tripId).Select(t => t.ChangeVersion).SingleAsync(),
            Stops = (await db.TripStops.AsNoTracking().Where(s => s.TripId == tripId).OrderBy(s => s.StopOrder).ToListAsync()).Select(s => new TripStopInputDto { CityId = s.CityId!.Value, TerminalId = s.TerminalId!.Value,
                ArrivalTime = new(DateTime.SpecifyKind(s.ArrivalTime,DateTimeKind.Utc)), DepartureTime = new(DateTime.SpecifyKind(s.DepartureTime,DateTimeKind.Utc)), CanBoard = s.CanBoard, CanAlight = s.CanAlight }).ToList(),
            Fares = (await db.TripSegmentFares.AsNoTracking().Where(f => f.TripId == tripId).ToListAsync()).Select(f => new SegmentFareInputDto { FromStopOrder = f.FromStopOrder, ToStopOrder = f.ToStopOrder, Amount = f.Amount, IsApproved = f.IsApproved }).ToList()
        };
        var edit = await Draft(id);
        check((await Send("PUT",$"/api/trips/{id}/stops",edit,adminToken)).IsSuccessStatusCode, "Yönetici seferin sıralı durak ve fiyatlarını atomik kaydeder");
        check((await Send("PUT",$"/api/trips/{id}/stops",edit,adminToken)).StatusCode == HttpStatusCode.Conflict, "Eski durak sürümü eşzamanlı düzenlemeyi ezemez");
        var templateResponse = await Send("PUT",$"/api/routes/{route.Id}/stops",new SaveRouteStopsDto { Stops = terminals.Take(4).Select((t,i) => new RouteStopInputDto { CityId = TurkishCities.Id(t.City)!.Value, TerminalId = t.Id, ArrivalOffsetMinutes = i * 360, DepartureOffsetMinutes = i * 360, CanBoard = i < 3, CanAlight = i > 0 }).ToList() },adminToken);
        check(templateResponse.IsSuccessStatusCode, "Güzergâh durak şablonu dakika ofsetleri ve satış izinleriyle kaydedilir");
        var templateId = await NewTrip(template:true);
        check(await db.TripStops.CountAsync(s => s.TripId == templateId) == 4 && await db.TripStops.CountAsync(s => s.TripId == id) == 4, "Şablon yeni sefere kopyalanır; mevcut seferin durakları değişmez");
        var missing = await Json(await Send("GET",$"/api/trips/{templateId}/stops",token:adminToken));
        check(missing.GetProperty("missingFares").GetArrayLength() == 5, "Yöneticiye fiyatı eksik beş ara kesim açıkça bildirilir; rastgele fiyat atanmaz");
        for (var from = 1; from < 4; from++) for (var to = from + 1; to <= 4; to++) {
            var search = await Search(TurkishCities.Id(terminals[from - 1].City)!.Value, TurkishCities.Id(terminals[to - 1].City)!.Value, start.AddHours((from - 1) * 6));
            var result = search.GetProperty("items").EnumerateArray().Single(s => s.GetProperty("id").GetInt32() == id);
            check(result.GetProperty("boardingStopOrder").GetInt32() == from && result.GetProperty("alightingStopOrder").GetInt32() == to && result.GetProperty("price").GetDecimal() == (from == 1 && to == 4 ? 900 : 100 * (to - from)) && result.GetProperty("availableSeats").GetInt32() == 6,
                $"Geçerli durak çifti {from}→{to} kendi fiyatı, sırası ve müsaitliğiyle aranır");
        }
        var wrongDay = await Search(TurkishCities.Id("Ankara")!.Value, TurkishCities.Id("İstanbul")!.Value, start);
        check(!wrongDay.GetProperty("items").EnumerateArray().Any(s => s.GetProperty("id").GetInt32() == id), "Ara durak araması ana kalkış gününde değil, yerel biniş gününde bulunur");
        check((await Search(TurkishCities.Id("Ankara")!.Value, TurkishCities.Id("Elazığ")!.Value, start.AddDays(1))).GetProperty("items").GetArrayLength() == 0, "Ters yönde sonuç yok");
        check((await Search(TurkishCities.Id("İzmir")!.Value, TurkishCities.Id("İstanbul")!.Value, start)).GetProperty("items").GetArrayLength() == 0, "Güzergâhta olmayan şehirde sonuç yok");
        await db.TripStops.Where(s => s.TripId == id && s.StopOrder == 2).ExecuteUpdateAsync(s => s.SetProperty(x => x.CanBoard, false));
        check((await Search(TurkishCities.Id("Elazığ")!.Value, TurkishCities.Id("İstanbul")!.Value, start.AddHours(6))).GetProperty("items").GetArrayLength() == 0, "Bindirme yasağı aramada uygulanır");
        await db.TripStops.Where(s => s.TripId == id && s.StopOrder == 2).ExecuteUpdateAsync(s => s.SetProperty(x => x.CanBoard, true));
        await db.TripStops.Where(s => s.TripId == id && s.StopOrder == 3).ExecuteUpdateAsync(s => s.SetProperty(x => x.CanAlight, false));
        check((await Search(TurkishCities.Id("Elazığ")!.Value, TurkishCities.Id("Ankara")!.Value, start.AddHours(6))).GetProperty("items").GetArrayLength() == 0, "İndirme yasağı aramada uygulanır");
        await db.TripStops.Where(s => s.TripId == id && s.StopOrder == 3).ExecuteUpdateAsync(s => s.SetProperty(x => x.CanAlight, true));
        await db.TripSegmentFares.Where(f => f.TripId == id && f.FromStopOrder == 1 && f.ToStopOrder == 2).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsApproved, false));
        check((await Search(TurkishCities.Id("Diyarbakır")!.Value, TurkishCities.Id("Elazığ")!.Value, start)).GetProperty("items").GetArrayLength() == 0, "Onaysız fiyat kesimi satışa açmaz");
        var multiId = await NewTrip(true);
        var multi = await Search(TurkishCities.Id("Ankara")!.Value, TurkishCities.Id("İstanbul")!.Value, start.AddHours(12));
        check(multi.GetProperty("items").EnumerateArray().Count(s => s.GetProperty("id").GetInt32() == multiId) == 1 && multi.GetProperty("items").EnumerateArray().Single(s => s.GetProperty("id").GetInt32() == multiId).GetProperty("journeyOptions").GetArrayLength() == 1,
            "Aynı şehirde iki terminal tek sefer kartında seçilebilir seçenekler üretir");
        StartGroupPurchaseDto Request(int tripId, int from, int to, int seat = 3) => new() { TripId = tripId, RequestKey = Guid.NewGuid(), BoardingStopOrder = from, AlightingStopOrder = to, ContactEmail = "shared-segment@travela.test", ContactPhone = "+905321234567", LegalAcceptances = GroupPurchaseChecks.SalesConsent(), Passengers = [new() { SeatNumber = seat, Gender = "Female", FullName = "Gerçek Kesim Yolcusu", IdentityNumber = "10000000146" }] };
        var quote = await Json(await Send("POST","/api/campaigns/quote", new CampaignQuoteDto { TripId = id, BoardingStopOrder = 2, AlightingStopOrder = 3, PassengerCount = 1 },token));
        check(quote.GetProperty("amount").GetDecimal() == 100, "Ödeme fiyatı tam güzergâhtan değil seçilen kesimden hesaplanır");
        var request = Request(id,2,3); request.ExpectedAmount = 999;
        check((await Send("POST","/api/purchases",request,token)).StatusCode == HttpStatusCode.Conflict, "İstemcinin değiştirdiği fiyat sunucuda reddedilir");
        request.ExpectedAmount = 100;
        var order = await Json(await Send("POST","/api/purchases",request,token)); var groupId = order.GetProperty("id").GetGuid();
        var next = await Send("POST","/api/purchases",Request(id,3,4),token);
        check(next.IsSuccessStatusCode, "Aynı koltuk yolcunun indiği duraktan itibaren tekrar satılır");
        check((await Send("POST","/api/purchases",Request(id,2,4),token)).StatusCode == HttpStatusCode.Conflict, "Ortak bacağı olan kesimler aynı koltuğa satılamaz");
        var detail = await Json(await Send("GET",$"/api/trips/public/{id}?boardingStopOrder=2&alightingStopOrder=3"));
        check(detail.GetProperty("availableSeats").GetInt32() == 5 && detail.GetProperty("seatOccupancies").GetArrayLength() == 1, "Koltuk planı yalnızca seçilen kesimin doluluğunu gösterir");
        var paid = await Json(await Send("POST",$"/api/purchases/{groupId}/local-complete",new { outcome = "Succeeded" },token));
        var ticket = paid.GetProperty("tickets")[0]; var pnr = ticket.GetProperty("pnr").GetString();
        check(ticket.GetProperty("departureCity").GetString() == "Elazığ" && ticket.GetProperty("arrivalCity").GetString() == "Ankara" && ticket.GetProperty("amount").GetDecimal() == 100 && ticket.GetProperty("departureTime").GetDateTime() == start.AddHours(6),
            "Ödeme ve bilet gerçek biniş/iniş, tarih-saat ve satın alınan kesim fiyatını korur");
        check(await db.TravelNotifications.AnyAsync(n => n.Reservation.Pnr == pnr && n.Body.Contains("Elazığ → Ankara") && n.Recipient == "shared-segment@travela.test"), "Bilet bildirimi gerçek yolculuk kesimini ve ortak iletişimi kullanır");
        await Json(await Send("POST",$"/api/tickets/{pnr}/cancel",token:token));
        detail = await Json(await Send("GET",$"/api/trips/public/{id}?boardingStopOrder=2&alightingStopOrder=3"));
        check(detail.GetProperty("availableSeats").GetInt32() == 6, "İptal yalnızca ilgili kesimin koltuğunu serbest bırakır");
        var update = await Draft(id); update.Fares.Single(f => f.FromStopOrder == 2 && f.ToStopOrder == 3).Amount = 125;
        check((await Send("PUT",$"/api/trips/{id}/fares",update,adminToken)).IsSuccessStatusCode && await db.Reservations.AnyAsync(r => r.Pnr == pnr && r.Price == 100), "Fiyat güncellemesi yeni satışları etkiler; alınmış biletin 100 TL tutarı korunur");
        var race = await NewTrip();
        var overlap = await Task.WhenAll(Send("POST","/api/purchases",Request(race,1,3),token),Send("POST","/api/purchases",Request(race,2,4),token));
        check(overlap.Count(r => r.IsSuccessStatusCode) == 1 && overlap.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 1, "Gerçek API ve SQL eşzamanlı çakışan kesim satışını engeller");
        var disjoint = await Task.WhenAll(Send("POST","/api/purchases",Request(race,1,2,6),token),Send("POST","/api/purchases",Request(race,2,4,6),token));
        check(disjoint.All(r => r.IsSuccessStatusCode), "Eşzamanlı çakışmayan kesimler aynı koltuğa satılabilir");
    }
}
