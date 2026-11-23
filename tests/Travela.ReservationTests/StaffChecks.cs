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

internal static class StaffChecks
{
    public static async Task Run(TravelaDbContext db, HttpClient http, Action<bool, string> check)
    {
        db.ChangeTracker.Clear();
        var oldIds = await db.Trips.Select(t => t.Id).ToListAsync();
        check(await db.TripCrews.CountAsync() == 0 && oldIds.Count > 0, "Eski seferler ekip ataması olmadan çalışmaya devam eder");
        var company = new Company { Name = "Ekip Test Firması" }; var foreign = new Company { Name = "Diğer Ekip Firması" };
        var route = new Travela.Api.Models.Route { DepartureCity = "İstanbul", ArrivalCity = "Ankara" };
        var terminals = new[] { new Terminal { City = "İstanbul", Name = "Ekip İstanbul", Address = "Test" }, new Terminal { City = "Ankara", Name = "Ekip Ankara", Address = "Test" } };
        var buses = Enumerable.Range(1, 7).Select(n => new Bus { Company = company, PlateNumber = "34 EKIP " + n, NormalizedPlateNumber = "34EKIP" + n,
            SeatCapacity = 6, SeatLayout = "2+1", SeatPlanJson = SeatPlans.Write(SeatPlans.Create("2+1", 6)) }).ToArray();
        db.AddRange(company, foreign, route); db.AddRange(buses); db.AddRange(terminals); await db.SaveChangesAsync();
        User Account(string name, string role, int? companyId) { var user = new User { FullName = name, Email = name + "@staff.test", Role = role, CompanyId = companyId }; user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "TestPass123!"); return user; }
        var owner = Account("staff-owner", "Company", company.Id); var other = Account("staff-other", "Company", foreign.Id); var passenger = Account("staff-passenger", "Passenger", null);
        db.AddRange(owner, other, passenger); await db.SaveChangesAsync();
        var accounts = await db.Users.CountAsync();
        async Task<HttpResponseMessage> Send(string method, string path, string? token, object? body = null) {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) request.Content = JsonContent.Create(body);
            return await http.SendAsync(request);
        }
        async Task<JsonElement> Json(HttpResponseMessage response) { if (!response.IsSuccessStatusCode) throw new Exception(response.StatusCode + ": " + await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
        async Task<string> Login(User user) => (await Json(await Send("POST", "/api/auth/login", null, new { email = user.Email, password = "TestPass123!" }))).GetProperty("token").GetString()!;
        var token = await Login(owner); var otherToken = await Login(other); var passengerToken = await Login(passenger);
        async Task<int> Staff(string name, string duty, string? session = null) => (await Json(await Send("POST", "/api/company/staff", session ?? token,
            new { fullName = name, duty, phone = (string?)null, isActive = true, companyId = foreign.Id }))).GetProperty("id").GetInt32();
        var d1 = await Staff("Birinci Şoför", "Driver"); var d2 = await Staff("İkinci Şoför", "Driver");
        var a1 = await Staff("Birinci Muavin", "Attendant"); var a2 = await Staff("İkinci Muavin", "Attendant"); var outsider = await Staff("Yabancı Şoför", "Driver", otherToken);
        check(await db.Users.CountAsync() == accounts && await db.CompanyStaff.CountAsync(s => s.CompanyId == company.Id) == 4,
            "Personeller ayrı tabloda ve oturum firmasına kaydedilir; kullanıcı hesabı oluşturulmaz");
        check((await Send("GET", "/api/company/staff", null)).StatusCode == HttpStatusCode.Unauthorized &&
            (await Send("GET", "/api/company/staff", passengerToken)).StatusCode == HttpStatusCode.Forbidden, "Personel listesi yalnız yetkili firma hesabına açıktır");
        var listed = await Json(await Send("GET", "/api/company/staff?companyId=" + foreign.Id + "&search=" + Uri.EscapeDataString("şoför") + "&duty=Driver&isActive=true", token));
        check(listed.GetArrayLength() == 2 && listed.EnumerateArray().All(s => s.GetProperty("id").GetInt32() != outsider), "Ad, görev ve aktiflik filtreleri yalnız oturum firmasını kapsar");
        check((await Send("PUT", "/api/company/staff/" + outsider, token, new { fullName = "Sahte", duty = "Driver", isActive = false })).StatusCode == HttpStatusCode.NotFound,
            "Başka firmanın personeli değiştirilemez");
        check((await Send("DELETE", "/api/company/staff/" + d1, token)).StatusCode == HttpStatusCode.MethodNotAllowed, "Personel için silme endpoint'i yoktur");
        var start = DateTime.UtcNow.Date.AddDays(40).AddHours(8);
        CreateTripDto Body(int bus, DateTime at, CrewSelectionDto? crew = null, double hours = 6) => new() { BusId = buses[bus].Id, RouteId = route.Id,
            DepartureTime = at, ArrivalTime = at.AddHours(hours), Price = 100, DepartureTerminalId = terminals[0].Id, ArrivalTerminalId = terminals[1].Id, Crew = crew };
        CrewSelectionDto Crew(int[] drivers, int[]? attendants = null) => new() { Drivers = drivers.ToList(), Attendants = attendants?.ToList() ?? [] };
        async Task<int> Create(CreateTripDto body) => (await Json(await Send("POST", "/api/trips", token, body))).GetProperty("tripId").GetInt32();
        async Task<JsonElement> GetCrew(int id) => await Json(await Send("GET", $"/api/trips/{id}/crew", token));
        async Task<HttpResponseMessage> Save(int id, CrewSelectionDto crew) {
            var current = await GetCrew(id);
            return await Send("PUT", $"/api/trips/{id}/crew", token, new SaveCrewDto { Crew = crew, ExpectedVersion = current.GetProperty("changeVersion").GetInt32() });
        }
        var plain = await Create(Body(6, start.AddDays(3)));
        check((await GetCrew(plain)).GetProperty("crew").GetArrayLength() == 0, "Atama olmadan yeni sefer oluşturulabilir");
        var first = await Create(Body(0, start, Crew([d1, d2], [a1, a2])));
        check((await GetCrew(first)).GetProperty("crew").GetArrayLength() == 4, "İki şoför ve iki muavin seferle birlikte atomik kaydedilir");
        var before = await db.Trips.CountAsync();
        foreach (var invalid in new[] { Crew([d1, d1]), Crew([a1]), Crew([outsider]) })
            check((await Send("POST", "/api/trips", token, Body(1, start.AddDays(2), invalid))).StatusCode == HttpStatusCode.BadRequest, "Tekrarlı, görevi yanlış veya başka firmaya ait ekip reddedilir");
        check(await db.Trips.CountAsync() == before, "Geçersiz atama yarım sefer bırakmaz");
        check((await Send("GET", $"/api/trips/{first}/crew", otherToken)).StatusCode == HttpStatusCode.NotFound &&
            (await Send("PUT", $"/api/trips/{first}/crew", otherToken, new SaveCrewDto { Crew = Crew([outsider]) })).StatusCode == HttpStatusCode.NotFound,
            "Başka firmanın sefer ekibi okunamaz veya atanamaz");
        await Json(await Send("PUT", "/api/company/staff/" + d1, token, new { fullName = "Birinci Şoför Güncel", duty = "Driver", phone = "0532 123 45 67", isActive = false }));
        check((await GetCrew(first)).GetProperty("crew").EnumerateArray().Any(s => s.GetProperty("id").GetInt32() == d1 && !s.GetProperty("isActive").GetBoolean()), "Pasife alma mevcut ekip ilişkisini silmez; güncel ad ve pasiflik görünür");
        check((await Send("POST", "/api/trips", token, Body(1, start.AddDays(1), Crew([d1])))).StatusCode == HttpStatusCode.BadRequest, "Pasif personele yeni sefer atanamaz");
        check((await Send("PUT", $"/api/trips/{first}", token, Body(0, start))).IsSuccessStatusCode && (await GetCrew(first)).GetProperty("crew").GetArrayLength() == 4,
            "Ekip alanını göndermeyen sefer güncellemesi pasif atamayı korur; sefer kendisiyle çakışmaz");
        await Json(await Send("PUT", "/api/company/staff/" + d1, token, new { fullName = "Birinci Şoför", duty = "Driver", isActive = true }));
        async Task<string> Snapshot(int id) => JsonSerializer.Serialize(new {
            trip = await db.Trips.AsNoTracking().Where(t => t.Id == id).Select(t => new { t.CompanyId, t.BusId, t.RouteId, t.DepartureTime, t.ArrivalTime, t.Price, t.Status, t.ChangeVersion, t.UpdatedAt }).SingleAsync(),
            crew = await db.TripCrews.AsNoTracking().Where(c => c.TripId == id).OrderBy(c => c.StaffId).Select(c => new { c.StaffId, c.Duty }).ToListAsync(),
            stops = await db.TripStops.AsNoTracking().Where(s => s.TripId == id).OrderBy(s => s.StopOrder).Select(s => new { s.CityId, s.TerminalId, s.DepartureTime, s.ArrivalTime }).ToListAsync(),
            fares = await db.TripSegmentFares.AsNoTracking().Where(f => f.TripId == id).OrderBy(f => f.FromStopOrder).ThenBy(f => f.ToStopOrder).Select(f => new { f.FromStopOrder, f.ToStopOrder, f.Amount }).ToListAsync(),
            changes = await db.TripChanges.CountAsync(c => c.TripId == id)
        });
        async Task<string> Counts() => JsonSerializer.Serialize(new {
            trips = await db.Trips.CountAsync(), crew = await db.TripCrews.CountAsync(), stops = await db.TripStops.CountAsync(),
            legs = await db.TripLegs.CountAsync(), fares = await db.TripSegmentFares.CountAsync()
        });
        System.Text.Json.Nodes.JsonObject LegacyApproval(object body) {
            var value = JsonSerializer.SerializeToNode(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
            value["crewConfirmation"] = "old-client-approval-token";
            value["confirmCrewConflict"] = true;
            return value;
        }
        async Task<JsonElement> Conflict(HttpResponseMessage response, string message) {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            check(response.StatusCode == HttpStatusCode.Conflict && body.GetProperty("code").GetString() == "crew_conflict" &&
                body.GetProperty("conflicts").GetArrayLength() > 0 && !body.TryGetProperty("confirmation", out _), message);
            return body;
        }
        var baseline = await Counts();
        var warning = await Conflict(await Send("POST", "/api/trips", token, Body(1, start.AddHours(1), Crew([d2]), 4)), "Şoför çakışması kesin olarak reddedilir");
        check(warning.GetProperty("conflicts")[0].GetProperty("tripId").GetInt32() == first &&
            warning.GetProperty("conflicts")[0].GetProperty("duty").GetString() == "Driver" &&
            warning.GetProperty("conflicts")[0].GetProperty("departureCity").GetString() == "İstanbul" &&
            warning.GetProperty("conflicts")[0].GetProperty("departureTime").GetString()!.EndsWith('Z'), "Hata personel adı, görev, sefer, güzergâh ve UTC saatlerini içerir");
        var attendantWarning = await Conflict(await Send("POST", "/api/trips", token, Body(1, start.AddHours(1), Crew([], [a1]), 4)), "Muavin çakışması da kesin olarak reddedilir");
        check(attendantWarning.GetProperty("conflicts")[0].GetProperty("duty").GetString() == "Attendant", "Muavin görevi çakışma ayrıntılarında belirtilir");
        await Conflict(await Send("POST", "/api/trips", token, LegacyApproval(Body(1, start.AddHours(1), Crew([d2]), 4))), "Eski istemcinin onay parametresi sefer oluşturma engelini aşamaz");
        check(await Counts() == baseline, "Reddedilen oluşturma sefer, ekip, durak veya fiyat kaydı bırakmaz");
        var boundary = await Create(Body(2, start.AddHours(6), Crew([d2]), 2));
        check(boundary > 0, "Bitiş ve başlangıç eşitse aralıklar çakışmaz");
        var target = await Create(Body(3, start.AddHours(1), null, 4));
        var free = await Staff("Boştaki Şoför", "Driver");
        var targetSnapshot = await Snapshot(target);
        var current = await GetCrew(target);
        await Conflict(await Send("PUT", $"/api/trips/{target}/crew", token, LegacyApproval(new SaveCrewDto {
            Crew = Crew([free, d1], [a1]), ExpectedVersion = current.GetProperty("changeVersion").GetInt32()
        })), "Atama/değiştirmede eski onay yok sayılır; tek çakışma tüm ekip kaydını engeller");
        check(await Snapshot(target) == targetSnapshot, "Atama hatasında müsait personel dahi kısmen atanmaz ve sefer sürümü değişmez");
        await Conflict(await Save(target, Crew([], [a1])), "Muavin atama/değiştirmesinde de çakışma kesin olarak engellenir");
        check(await Snapshot(target) == targetSnapshot, "Reddedilen muavin ataması sefer veya ekip bilgisini değiştirmez");
        // A fixture representing records saved before the strict rule; production data is never repaired automatically.
        var legacy = await Create(Body(1, start.AddHours(1), null, 4));
        db.TripCrews.Add(new TripCrew { TripId = legacy, CompanyId = company.Id, StaffId = d2, Duty = "Driver" });
        await db.SaveChangesAsync();
        var firstSnapshot = await Snapshot(first); var legacySnapshot = await Snapshot(legacy);
        var edit = Body(0, start, Crew([d1, d2])); edit.Price = 777;
        await Conflict(await Send("PUT", $"/api/trips/{first}", token, LegacyApproval(edit)), "Önceden var olan çakışma ilgili sefer düzenlenirken de engellenir");
        check(await Snapshot(first) == firstSnapshot && await Snapshot(legacy) == legacySnapshot,
            "Mevcut çakışmada fiyat/ekip değişmez; eski personel atamaları kendiliğinden silinmez");
        await Json(await Send("POST", $"/api/trips/{legacy}/cancel", token, new { reason = "Ekip test iptali" }));
        var cancelledSnapshot = await Snapshot(legacy);
        await Json(await Save(first, Crew([d1])));
        check((await GetCrew(first)).GetProperty("crew").GetArrayLength() == 1 && (await GetCrew(legacy)).GetProperty("crew").GetArrayLength() == 1,
            "Elle ekip kaldırılabilir; iptal edilen seferin geçmiş ataması korunur");
        await Json(await Save(target, Crew([d2])));
        check((await GetCrew(target)).GetProperty("crew").GetArrayLength() == 1, "İptal edilmiş sefer çakışma sayılmaz; aynı personel uygun sefere atanabilir");
        check((await Send("PATCH", $"/api/trips/{legacy}/status", token, new { status = "Scheduled" })).StatusCode == HttpStatusCode.Gone &&
            await Snapshot(legacy) == cancelledSnapshot, "İptal edilen seferi yeniden aktifleştirme endpoint'i kapalıdır");
        await Json(await Save(first, Crew([d1])));
        check((await GetCrew(first)).GetProperty("crew").GetArrayLength() == 1, "Aynı seferi düzenleme kendisiyle çakışmaz");
        var nextDay = await Create(Body(4, start.AddDays(1), Crew([d1])));
        firstSnapshot = await Snapshot(first);
        var changedTime = Body(0, start.AddDays(1)); changedTime.Price = 888;
        await Conflict(await Send("PUT", $"/api/trips/{first}", token, LegacyApproval(changedTime)), "Sefer saati değişikliği mevcut ekip için yeniden kontrol edilir; eski onay geçersizdir");
        check(await Snapshot(first) == firstSnapshot, "Saat çakışması sefer, fiyat, durak, ekip ve sürüm değişikliklerinin tamamını geri alır");
        await Json(await Send("PUT", $"/api/trips/{first}", token, Body(0, start.AddDays(2))));
        firstSnapshot = await Snapshot(first);
        var operational = new Travela.Api.Controllers.OperationalChangeDto { BusId = buses[0].Id, DepartureTime = start.AddDays(1).AddHours(1), ArrivalTime = start.AddDays(1).AddHours(7),
            Reason = "Personel saat kontrolü", Preview = true, RequestKey = Guid.NewGuid(), ExpectedVersion = (await GetCrew(first)).GetProperty("changeVersion").GetInt32(), ExpectedAffectedTickets = 0 };
        await Conflict(await Send("POST", $"/api/trips/{first}/changes", token, LegacyApproval(operational)), "Kontrollü değişiklik önizlemesinde çakışma engellenir");
        operational.Preview = false;
        await Conflict(await Send("POST", $"/api/trips/{first}/changes", token, LegacyApproval(operational)), "Önizleme atlanıp eski onay gönderilse de saat değişikliği kaydedilemez");
        check(await Snapshot(first) == firstSnapshot, "Reddedilen kontrollü değişiklik tarih, ekip veya değişiklik geçmişini kısmen kaydetmez");
        operational.DepartureTime = start.AddDays(2).AddHours(1); operational.ArrivalTime = start.AddDays(2).AddHours(7); operational.Preview = true;
        await Json(await Send("POST", $"/api/trips/{first}/changes", token, operational));
        operational.Preview = false; await Json(await Send("POST", $"/api/trips/{first}/changes", token, operational));
        firstSnapshot = await Snapshot(first);
        var stopDto = new SaveTripStopsDto { ExpectedVersion = (await GetCrew(first)).GetProperty("changeVersion").GetInt32(), FullPrice = 999,
            Stops = [new() { CityId = 40, TerminalId = terminals[0].Id, ArrivalTime = start.AddDays(1).AddHours(2), DepartureTime = start.AddDays(1).AddHours(2), CanBoard = true, CanAlight = false },
                new() { CityId = 7, TerminalId = terminals[1].Id, ArrivalTime = start.AddDays(1).AddHours(8), DepartureTime = start.AddDays(1).AddHours(8), CanBoard = false, CanAlight = true }] };
        await Conflict(await Send("PUT", $"/api/trips/{first}/stops", token, LegacyApproval(stopDto)), "Duraklardan yapılan saat değişikliği eski onayla da ekip engelini aşamaz");
        check(await Snapshot(first) == firstSnapshot, "Durak güncellemesi çakıştığında fiyat ve duraklar dahil hiçbir kısmi değişiklik kalmaz");
        baseline = await Counts();
        var batch = new[] { Body(5, start.AddDays(3), Crew([d1])), Body(6, start.AddDays(1), Crew([d1])) };
        var batchWarning = await Conflict(await Send("POST", "/api/trips/batch", token, new { trips = batch.Select(LegacyApproval).ToArray(), crewConfirmation = "old-token" }), "Toplu işlemde eski onay parametreleri çakışmayı aşamaz");
        check(batchWarning.GetProperty("batchIndex").GetInt32() == 1 && await Counts() == baseline,
            "Toplu işlemde tek çakışma bütün sefer, durak ve ekip kayıtlarını geri alır");
        var internalBatch = new[] { Body(5, start.AddDays(4), Crew([d1])), Body(6, start.AddDays(4), Crew([d1])) };
        var internalWarning = await Conflict(await Send("POST", "/api/trips/batch", token, new { trips = internalBatch }), "Aynı toplu işlem içindeki ekip çakışması kesin olarak engellenir");
        check(internalWarning.GetProperty("conflicts")[0].GetProperty("batchIndex").GetInt32() == 1 && await Counts() == baseline, "Toplu işlem içi çakışma yarım kayıt bırakmaz");
        var beforeValidBatch = await db.Trips.CountAsync();
        internalBatch[1].DepartureTime = internalBatch[0].ArrivalTime; internalBatch[1].ArrivalTime = internalBatch[1].DepartureTime.AddHours(6);
        await Json(await Send("POST", "/api/trips/batch", token, new { trips = internalBatch }));
        check(await db.Trips.CountAsync() == beforeValidBatch + 2, "Çakışma giderildiğinde toplu kayıt çalışır");
        foreach (var duty in new[] { "Driver", "Attendant" }) {
            var racer = await Staff("Eşzamanlı " + duty, duty);
            var raceCrew = duty == "Driver" ? Crew([racer]) : Crew([], [racer]);
            var raceTime = start.AddDays(duty == "Driver" ? 5 : 6);
            var countBeforeRace = await db.Trips.CountAsync();
            var races = await Task.WhenAll(Send("POST", "/api/trips", token, LegacyApproval(Body(5, raceTime, raceCrew))), Send("POST", "/api/trips", token, LegacyApproval(Body(6, raceTime, raceCrew))));
            check(races.Count(r => r.IsSuccessStatusCode) == 1 && races.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 1 &&
                await db.TripCrews.CountAsync(c => c.StaffId == racer) == 1 && await db.Trips.CountAsync() == countBeforeRace + 1,
                "Eşzamanlı " + duty + " atamasında tek kayıt oluşur; kaybeden istek hiçbir kısmi kayıt bırakmaz");
        }
        var publicTrip = await Json(await Send("GET", $"/api/trips/public/{first}", null));
        check(!publicTrip.GetRawText().Contains("crew", StringComparison.OrdinalIgnoreCase) && !publicTrip.GetRawText().Contains("Şoför") &&
            (await Send("GET", $"/api/trips/{first}/crew", passengerToken)).StatusCode == HttpStatusCode.Forbidden, "Ekip bilgileri yolcu ekranı ve genel sefer yanıtına sızmaz");
        check(await db.Trips.CountAsync(t => oldIds.Contains(t.Id)) == oldIds.Count, "Eski seferler ve geçmiş kayıtlar korunur");
    }
}
