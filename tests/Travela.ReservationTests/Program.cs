using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Travela.Api.Models;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

// Real SQL Server + HTTP integration tests, without a mocked/in-memory database.
// Only this run's uniquely named database is created and removed.
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
Directory.SetCurrentDirectory(root);
using var source = new TravelaDbContextFactory().CreateDbContext([]);
if (args.Contains("--verify-existing"))
{
    var pending = (await source.Database.GetPendingMigrationsAsync()).ToArray();
    if (pending.Length > 0) throw new Exception("Uygulama veritabanında bekleyen migration: " + string.Join(", ", pending));
    using var api = new HttpClient { BaseAddress = new Uri("http://localhost:5062") };
    using var proxy = new HttpClient { BaseAddress = new Uri("http://localhost:5173") };
    var existing = await source.Trips.AsNoTracking().OrderByDescending(t => t.DepartureTime).Take(100).ToListAsync();
    if (existing.Count == 0) throw new Exception("Mevcut sefer bulunamadı; test kaydı oluşturulmadı.");
    foreach (var trip in existing)
    {
        var direct = await api.GetAsync($"/api/trips/public/{trip.Id}");
        var forwarded = await proxy.GetAsync($"/api/trips/public/{trip.Id}");
        direct.EnsureSuccessStatusCode(); forwarded.EnsureSuccessStatusCode();
        var detail = await forwarded.Content.ReadFromJsonAsync<JsonElement>();
        var capacity = detail.GetProperty("seatCapacity").GetInt32();
        var occupied = detail.GetProperty("occupiedSeats").EnumerateArray().Select(s => s.GetInt32()).ToArray();
        if (capacity < 1 || occupied.Any(s => s < 1 || s > capacity) || occupied.Length != occupied.Distinct().Count())
            throw new Exception("Koltuk planı geçersiz: " + trip.Id);
        if (trip.DepartureTime <= DateTime.UtcNow && detail.GetProperty("canReserve").GetBoolean())
            throw new Exception("Geçmiş sefer rezervasyona açık: " + trip.Id);
        Console.WriteLine($"Mevcut sefer #{trip.Id}: doğrudan/proxy HTTP 200, kapasite={capacity}, dolu={occupied.Length}, rezervasyona açık={detail.GetProperty("canReserve")}, kalkış={trip.DepartureTime:O}");
    }
    var missing = await proxy.GetAsync("/api/trips/public/2147483647");
    if (missing.StatusCode != HttpStatusCode.NotFound) throw new Exception("Olmayan sefer 404 dönmedi.");
    Console.WriteLine($"Mevcut veritabanı doğrulandı: bekleyen migration yok, {existing.Count} mevcut sefer yüklendi, olmayan sefer 404. Hiçbir kayıt değiştirilmedi.");
    return;
}
var connection = new SqlConnectionStringBuilder(source.Database.GetConnectionString())
{
    InitialCatalog = "TravelaReservationTests_" + Guid.NewGuid().ToString("N")
};
var testDatabaseName = connection.InitialCatalog;
var options = new DbContextOptionsBuilder<TravelaDbContext>().UseSqlServer(connection.ConnectionString).Options;
await using var db = new TravelaDbContext(options);
Process? server = null;
var output = new System.Text.StringBuilder();
var passed = 0;
try
{
    await db.GetService<IMigrator>().MigrateAsync("20261030074821_AddMustChangePasswordToUsers");
    Check((await db.Database.GetPendingMigrationsAsync()).Contains("20261102075307_AddSeatReservations"), "Regresyon için eski şema hazır: rezervasyon migration'ı eksik");
    var users = new[] { NewUser("one"), NewUser("two"), NewUser("admin", "Admin"), NewUser("company1", "Company"), NewUser("company2", "Company") };
    db.Users.AddRange(users);
    var adminRole = new Role { Name = "Admin" };
    db.RolePermissions.Add(new RolePermission { Role = adminRole, Permission = new Permission { Name = "trip.update" } });
    db.RolePermissions.Add(new RolePermission { Role = adminRole, Permission = new Permission { Name = "bus.update" } });
    var company = new Company { Name = "Test Firma" };
    var otherCompany = new Company { Name = "Diğer Firma" };
    var bus = new Bus { Company = company, PlateNumber = "34 TEST 001", NormalizedPlateNumber = "34TEST001", SeatCapacity = 5 };
    var route = new Travela.Api.Models.Route { DepartureCity = "İstanbul", ArrivalCity = "Ankara", EstimatedDurationMinutes = 360 };
    db.AddRange(company, otherCompany, bus, route);
    var trip = NewTrip();
    var cancelled = NewTrip(); cancelled.Status = "Cancelled";
    var past = NewTrip(); past.DepartureTime = DateTime.UtcNow.AddHours(-2);
    var single = NewTrip();
    var legacy = NewTrip(); legacy.AvailableSeats = 3;
    db.Trips.AddRange(trip, cancelled, past, single, legacy);
    await db.SaveChangesAsync();
    users[3].CompanyId = company.Id; users[4].CompanyId = otherCompany.Id;
    await db.SaveChangesAsync();

    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
    var dll = Path.Combine(root, "backend", "bin", configuration, "net10.0", "Travela.API.dll");
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(root, "backend"), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(dll);
    start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
    start.Environment["ConnectionStrings__DefaultConnection"] = connection.ConnectionString;
    start.Environment["Jwt__Key"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    start.Environment["Jwt__Issuer"] = "TravelaTests";
    start.Environment["Jwt__Audience"] = "TravelaTests";
    start.Environment["Logging__LogLevel__Default"] = "Warning";
    server = Process.Start(start)!;
    server.OutputDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
    server.ErrorDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
    server.BeginOutputReadLine(); server.BeginErrorReadLine();
    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(30) };
    var ready = false;
    for (var i = 0; i < 60; i++)
    {
        try { ready = (await http.GetAsync("/api/test")).IsSuccessStatusCode; if (ready) break; }
        catch (HttpRequestException) { }
        if (server.HasExited) throw new Exception("Test API başlatılamadı: " + output);
        await Task.Delay(250);
    }
    Check(ready, "İzole test API başlatıldı");
    var schemaFailure = await http.GetAsync($"/api/trips/public/{trip.Id}");
    Check(schemaFailure.StatusCode == HttpStatusCode.InternalServerError, "Eksik Reservations tablosuyla detay hatası HTTP 500 olarak yeniden üretildi");
    var loggedSchemaError = false;
    for (var i = 0; i < 20; i++)
    {
        lock (output) loggedSchemaError = output.ToString().Contains("Invalid object name 'Reservations'", StringComparison.Ordinal);
        if (loggedSchemaError) break;
        await Task.Delay(100);
    }
    Check(loggedSchemaError, "Backend logunda SQL Server 208: Invalid object name 'Reservations' doğrulandı");
    await db.GetService<IMigrator>().MigrateAsync("20261102075307_AddSeatReservations");
    var historicalNumber = Guid.NewGuid().ToString("N").ToUpperInvariant();
    var activeNumber = Guid.NewGuid().ToString("N").ToUpperInvariant();
    var fixtureTime = DateTime.UtcNow;
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Reservations (ReservationNumber, TripId, PassengerId, SeatNumber, Price, CreatedAt) VALUES ({historicalNumber}, {cancelled.Id}, {users[0].Id}, 1, 500, {fixtureTime}), ({activeNumber}, {legacy.Id}, {users[0].Id}, 2, 500, {fixtureTime})");
    await db.Trips.Where(t => t.Id == cancelled.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.AvailableSeats, 4));
    await db.Database.MigrateAsync();
    Check(!(await db.Database.GetPendingMigrationsAsync()).Any(), "Eksik migration aynı veritabanına uygulandı");
    Check(await db.Trips.CountAsync() == 5 && await db.Users.CountAsync() == 5 && await db.Trips.AnyAsync(t => t.Id == trip.Id), "Migration mevcut sefer ve kullanıcı kayıtlarını korudu");
    Check(await db.Reservations.CountAsync() == 2 && await db.Reservations.AnyAsync(r => r.ReservationNumber == activeNumber && r.Status == "Active" && r.CancelledAt == null), "Migration eski aktif rezervasyonu numarası ve durumuyla korudu");
    Check(await db.Reservations.AnyAsync(r => r.ReservationNumber == historicalNumber && r.Status == "Cancelled" && r.CancelledAt != null && r.CancellationReason != null) &&
        await db.Trips.AsNoTracking().Where(t => t.Id == cancelled.Id).Select(t => t.AvailableSeats).SingleAsync() == 5, "Migration önceden iptal edilmiş seferin rezervasyonunu ve kontenjanını uzlaştırdı");
    var token1 = await Login(users[0]); var token2 = await Login(users[1]); var adminToken = await Login(users[2]);
    var companyToken = await Login(users[3]); var otherCompanyToken = await Login(users[4]);
    var initial = await http.GetFromJsonAsync<JsonElement>($"/api/trips/public/{trip.Id}");
    Check(initial.GetProperty("seatCapacity").GetInt32() == 5 && initial.GetProperty("occupiedSeats").GetArrayLength() == 0, "Kapasiteye uygun boş koltuk planı");
    Check((await Reserve(null, trip.Id, 1)).StatusCode == HttpStatusCode.Unauthorized, "Giriş yapmadan rezervasyon: 401");
    Check((await Send(HttpMethod.Get, "/api/reservations/mine", null)).StatusCode == HttpStatusCode.Unauthorized, "Giriş yapmadan listeleme: 401");
    Check((await Reserve(adminToken, trip.Id, 1)).StatusCode == HttpStatusCode.Forbidden, "Yolcu olmayan hesapla rezervasyon: 403");
    Check((await Send(HttpMethod.Get, "/api/reservations/mine", adminToken)).StatusCode == HttpStatusCode.Forbidden, "Yolcu olmayan hesapla listeleme: 403");
    Check((await Reserve(token1, trip.Id, 0)).StatusCode == HttpStatusCode.BadRequest, "Sıfır numaralı koltuk reddedildi");
    Check((await Reserve(token1, trip.Id, 6)).StatusCode == HttpStatusCode.BadRequest, "Kapasite dışı koltuk reddedildi");
    Check((await Reserve(token1, int.MaxValue, 1)).StatusCode == HttpStatusCode.NotFound, "Olmayan sefer: 404");
    Check((await Reserve(token1, cancelled.Id, 1)).StatusCode == HttpStatusCode.Conflict, "İptal edilen sefer reddedildi");
    Check((await Reserve(token1, past.Id, 1)).StatusCode == HttpStatusCode.Conflict, "Kalkış zamanı geçmiş sefer reddedildi");
    var pastDetailResponse = await http.GetAsync($"/api/trips/public/{past.Id}");
    var pastDetail = await pastDetailResponse.Content.ReadFromJsonAsync<JsonElement>();
    Check(pastDetailResponse.StatusCode == HttpStatusCode.OK && !pastDetail.GetProperty("canReserve").GetBoolean() && pastDetail.GetProperty("departureHasPassed").GetBoolean(), "Geçmiş seferin detayı yükleniyor, rezervasyonu kapalı (404/500 değil)");
    Check((await http.GetAsync("/api/trips/public/2147483647")).StatusCode == HttpStatusCode.NotFound, "Olmayan seferin detayı 404 dönüyor");
    Check((await Reserve(token1, legacy.Id, 1)).StatusCode == HttpStatusCode.Conflict, "Eski ve tutarsız kontenjan bilgisiyle rezervasyon engellendi");
    var legacyDetail = await http.GetFromJsonAsync<JsonElement>($"/api/trips/public/{legacy.Id}");
    Check(!legacyDetail.GetProperty("seatPlanAvailable").GetBoolean() && !legacyDetail.GetProperty("canReserve").GetBoolean(), "Eski kayıtlarda bilinmeyen koltuklar boş gösterilmiyor");
    var response = await Reserve(token1, trip.Id, 1);
    Check(response.StatusCode == HttpStatusCode.Created, "Rezervasyon oluşturuldu: 201");
    var reservation = (await response.Content.ReadFromJsonAsync<ReservationDto>())!;
    Check(reservation.Price == 500 && reservation.ReservationNumber.Length == 32 && !reservation.IsPaid && reservation.Status == "Active", "Rezervasyon numarası ve sunucu fiyatı, aktif ve ödenmemiş durum");
    Check((await Reserve(token2, trip.Id, 1)).StatusCode == HttpStatusCode.Conflict, "Dolu koltuk tekrar rezerve edilemedi: 409");
    var mine1 = await (await Send(HttpMethod.Get, "/api/reservations/mine", token1)).Content.ReadFromJsonAsync<ReservationDto[]>();
    var mine2 = await (await Send(HttpMethod.Get, $"/api/reservations/mine?passengerId={users[0].Id}", token2)).Content.ReadFromJsonAsync<ReservationDto[]>();
    Check(mine1!.Count(r => r.TripId == trip.Id) == 1 && mine2!.Length == 0, "Yolcu yalnızca kendi rezervasyonlarını görebiliyor");
    var saved = await db.Reservations.AsNoTracking().SingleAsync(r => r.TripId == trip.Id);
    Check(saved.PassengerId == users[0].Id && saved.SeatNumber == 1, "Rezervasyon giriş yapan yolcuyla veritabanında ilişkili");
    var updated = await http.GetFromJsonAsync<JsonElement>($"/api/trips/public/{trip.Id}");
    Check(updated.GetProperty("availableSeats").GetInt32() == 4 && updated.GetProperty("occupiedSeats")[0].GetInt32() == 1, "Detayda dolu koltuk ve boş koltuk sayısı güncellendi");
    var edit = await Send(HttpMethod.Put, $"/api/trips/{trip.Id}", adminToken, new { companyId = company.Id, busId = bus.Id, routeId = route.Id, departureTime = trip.DepartureTime, arrivalTime = trip.ArrivalTime, price = 600 });
    Check(edit.StatusCode == HttpStatusCode.Conflict, "Rezervasyon bulunan seferin düzenlenmesi engellendi");

    // Simultaneous requests against distinct server contexts/connections.
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var requests = Enumerable.Range(0, 12).Select(async i => { await gate.Task; return await Reserve(i % 2 == 0 ? token1 : token2, trip.Id, 2); }).ToArray();
    gate.SetResult();
    var concurrent = await Task.WhenAll(requests);
    Check(concurrent.Count(r => r.StatusCode == HttpStatusCode.Created) == 1 && concurrent.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 11, "Aynı koltuğa 12 eş zamanlı istek: 1 başarı, 11 çakışma");
    Check(await db.Reservations.CountAsync(r => r.TripId == trip.Id && r.SeatNumber == 2) == 1 &&
        await db.Trips.AsNoTracking().Where(t => t.Id == trip.Id).Select(t => t.AvailableSeats).SingleAsync() == 3, "Eş zamanlı istekte tek kayıt ve doğru kontenjan");
    var differentSeats = await Task.WhenAll(Enumerable.Range(3, 3).Select(seat => Reserve(token1, trip.Id, seat)));
    Check(differentSeats.All(r => r.StatusCode == HttpStatusCode.Created), "Farklı koltuklara eş zamanlı rezervasyonlar başarılı");
    var full = await http.GetFromJsonAsync<JsonElement>($"/api/trips/public/{trip.Id}");
    Check(full.GetProperty("availableSeats").GetInt32() == 0 && full.GetProperty("occupiedSeats").GetArrayLength() == 5 && !full.GetProperty("canReserve").GetBoolean(), "Son koltuktan sonra detay açık, rezervasyon kapalı");
    Check((await Reserve(token2, trip.Id, 5)).StatusCode == HttpStatusCode.Conflict, "Tam dolu seferde ek rezervasyon reddedildi");

    await using (var direct = new TravelaDbContext(options))
    {
        direct.Reservations.Add(new Reservation { TripId = trip.Id, PassengerId = users[1].Id, SeatNumber = 1, Price = 500 });
        var blocked = false;
        try { await direct.SaveChangesAsync(); }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 }) { blocked = true; }
        Check(blocked, "API dışından mükerrer koltuk ekleme benzersiz indeks tarafından engellendi");
    }
    var toCancel = (await (await Reserve(token1, single.Id, 1)).Content.ReadFromJsonAsync<ReservationDto>())!;
    Task<HttpResponseMessage> Cancel(string? token, string number) => Send(HttpMethod.Post, $"/api/reservations/{number}/cancel", token);
    Check((await Cancel(null, toCancel.ReservationNumber)).StatusCode == HttpStatusCode.Unauthorized, "Girişsiz iptal 401");
    Check((await Cancel(token2, toCancel.ReservationNumber)).StatusCode == HttpStatusCode.NotFound, "Başka yolcunun rezervasyonunu iptal etme 404");
    Check((await Cancel(companyToken, toCancel.ReservationNumber)).StatusCode == HttpStatusCode.Forbidden, "Firma hesabı yolcu iptal endpoint'ine erişemiyor");
    Check(await db.Trips.AsNoTracking().Where(t => t.Id == single.Id).Select(t => t.AvailableSeats).SingleAsync() == 4, "Yetkisiz iptal koltuk sayısını değiştirmedi");
    var cancellations = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Cancel(token1, toCancel.ReservationNumber)));
    Check(cancellations.All(r => r.IsSuccessStatusCode), "Aynı rezervasyona 8 eş zamanlı iptal isteği idempotent");
    var cancelledBooking = (await cancellations[0].Content.ReadFromJsonAsync<ReservationDto>())!;
    Check(cancelledBooking.Status == "Cancelled" && cancelledBooking.CancelledAt != null && !cancelledBooking.CanCancel && cancelledBooking.CancellationReason != null, "İptal durumu, zamanı ve nedeni saklandı");
    var freed = await http.GetFromJsonAsync<JsonElement>($"/api/trips/public/{single.Id}");
    Check(freed.GetProperty("availableSeats").GetInt32() == 5 && freed.GetProperty("occupiedSeats").GetArrayLength() == 0, "İptal sonrası koltuk boş ve kontenjan yalnızca bir arttı");
    var rebookResponse = await Reserve(token2, single.Id, 1);
    Check(rebookResponse.StatusCode == HttpStatusCode.Created, "İptal edilen koltuk başka yolcu tarafından yeniden rezerve edildi");
    var rebook = (await rebookResponse.Content.ReadFromJsonAsync<ReservationDto>())!;
    await Cancel(token1, toCancel.ReservationNumber);
    Check(await db.Reservations.CountAsync(r => r.TripId == single.Id && r.SeatNumber == 1) == 2 &&
        await db.Trips.AsNoTracking().Where(t => t.Id == single.Id).Select(t => t.AvailableSeats).SingleAsync() == 4, "Eski iptal geçmişi korundu; tekrarı yeni rezervasyonun kontenjanını değiştirmedi");
    var raceGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var raceCancel = Task.Run(async () => { await raceGate.Task; return await Cancel(token2, rebook.ReservationNumber); });
    var raceCreates = Enumerable.Range(0, 10).Select(async _ => { await raceGate.Task; return await Reserve(token1, single.Id, 1); }).ToArray();
    raceGate.SetResult();
    Check((await raceCancel).IsSuccessStatusCode, "Yeniden rezervasyon istekleriyle eş zamanlı iptal başarılı");
    var raceResults = await Task.WhenAll(raceCreates);
    Check(raceResults.Count(r => r.StatusCode == HttpStatusCode.Created) <= 1 && raceResults.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict), "İptal ve yeniden rezervasyon yarışında çift aktif kayıt oluşmadı");
    if (raceResults.All(r => r.StatusCode != HttpStatusCode.Created)) Check((await Reserve(token1, single.Id, 1)).StatusCode == HttpStatusCode.Created, "İptal önceki isteklerden sonra tamamlandıysa sonraki yeniden rezervasyon başarılı");
    Check(await db.Reservations.CountAsync(r => r.TripId == single.Id && r.Status == "Active") == 1 &&
        await db.Trips.AsNoTracking().Where(t => t.Id == single.Id).Select(t => t.AvailableSeats).SingleAsync() == 4, "İptal/oluşturma yarışından sonra aktif kayıt ve kontenjan tutarlı");
    var pastReservation = new Reservation { TripId = past.Id, PassengerId = users[0].Id, SeatNumber = 1, Price = 500 };
    db.Reservations.Add(pastReservation); await db.SaveChangesAsync();
    await db.Trips.Where(t => t.Id == past.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.AvailableSeats, 4));
    Check((await Cancel(token1, pastReservation.ReservationNumber)).StatusCode == HttpStatusCode.Conflict, "Kalkış zamanı geçmiş rezervasyon iptal edilemiyor");
    Check(await db.Reservations.AsNoTracking().Where(r => r.Id == pastReservation.Id).Select(r => r.Status).SingleAsync() == "Active", "Geçmiş rezervasyon kaydı korunuyor");
    Check((await Send(HttpMethod.Get, "/api/reservations/company", token1)).StatusCode == HttpStatusCode.Forbidden, "Yolcu firma rezervasyon listesini göremiyor");
    var companyList = await (await Send(HttpMethod.Get, "/api/reservations/company", companyToken)).Content.ReadFromJsonAsync<JsonElement>();
    Check(companyList.GetProperty("totalCount").GetInt32() > 0 && companyList.GetProperty("items")[0].GetProperty("passengerName").GetString() != null, "Firma gerçek rezervasyon ve yolcu adlarını görebiliyor");
    var foreignList = await (await Send(HttpMethod.Get, $"/api/reservations/company?companyId={company.Id}", otherCompanyToken)).Content.ReadFromJsonAsync<JsonElement>();
    Check(foreignList.GetProperty("totalCount").GetInt32() == 0, "Başka firma kimliği gönderilerek veri kapsamı değiştirilemiyor");
    Check((await Send(HttpMethod.Get, $"/api/reservations/company?tripId={trip.Id}", otherCompanyToken)).StatusCode == HttpStatusCode.NotFound, "Başka firmanın sefer filtresi 404");
    var cancelledList = await (await Send(HttpMethod.Get, $"/api/reservations/company?tripId={single.Id}&status=Cancelled", companyToken)).Content.ReadFromJsonAsync<JsonElement>();
    Check(cancelledList.GetProperty("totalCount").GetInt32() == 2 && cancelledList.GetProperty("items").EnumerateArray().All(r => r.GetProperty("status").GetString() == "Cancelled"), "Firma sefer ve iptal durumu filtreleri doğru");
    var noDateMatches = await (await Send(HttpMethod.Get, "/api/reservations/company?dateFrom=2000-01-01&dateTo=2000-01-02", companyToken)).Content.ReadFromJsonAsync<JsonElement>();
    Check(noDateMatches.GetProperty("totalCount").GetInt32() == 0, "Firma kalkış tarihi aralığı filtresi doğru");
    var lateDeparture = DateTime.UtcNow.AddDays(2).Date.AddHours(22.5);
    await db.Trips.Where(t => t.Id == single.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.DepartureTime, lateDeparture));
    var turkeyDate = lateDeparture.AddHours(3).ToString("yyyy-MM-dd");
    var turkeyFiltered = await (await Send(HttpMethod.Get, $"/api/reservations/company?tripId={single.Id}&status=Active&dateFrom={turkeyDate}&dateTo={turkeyDate}", companyToken)).Content.ReadFromJsonAsync<JsonElement>();
    Check(turkeyFiltered.GetProperty("totalCount").GetInt32() == 1, "Tarih filtresi Türkiye saatindeki kalkış gününü kullanıyor");
    var utcDate = lateDeparture.ToString("yyyy-MM-dd");
    var previousDay = await (await Send(HttpMethod.Get, $"/api/reservations/company?tripId={single.Id}&dateFrom={utcDate}&dateTo={utcDate}", companyToken)).Content.ReadFromJsonAsync<JsonElement>();
    Check(previousDay.GetProperty("totalCount").GetInt32() == 0, "UTC günü ile Türkiye günü farklı olduğunda filtre sınırı doğru");
    Check((await Send(HttpMethod.Get, "/api/reservations/company?status=Invalid", companyToken)).StatusCode == HttpStatusCode.BadRequest, "Geçersiz durum filtresi reddedildi");
    var activeSingle = await db.Reservations.AsNoTracking().SingleAsync(r => r.TripId == single.Id && r.Status == "Active");
    var tripCancelRace = Send(HttpMethod.Post, $"/api/trips/{single.Id}/cancel", adminToken, new { reason = "Eş zamanlı sefer iptali" });
    var passengerCancelRace = Cancel(token1, activeSingle.ReservationNumber);
    var newBookingRace = Reserve(token2, single.Id, 2);
    Check((await tripCancelRace).IsSuccessStatusCode && (await passengerCancelRace).IsSuccessStatusCode, "Sefer ve yolcu iptali eş zamanlı tutarlı tamamlandı");
    Check((await newBookingRace).StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, "Sefer iptaliyle eş zamanlı rezervasyon sıralı işlendi");
    Check(!await db.Reservations.AnyAsync(r => r.TripId == single.Id && r.Status == "Active") &&
        await db.Trips.AsNoTracking().Where(t => t.Id == single.Id).Select(t => t.AvailableSeats).SingleAsync() == 5, "Sefer/yolcu/oluşturma yarışından sonra aktif kayıt yok ve kontenjan tam");
    Check((await Send(HttpMethod.Post, $"/api/trips/{trip.Id}/cancel", adminToken, new { reason = "Test iptali" })).IsSuccessStatusCode, "Rezervasyonu bulunan sefer iptal edilebiliyor");
    var cancelledMine = await (await Send(HttpMethod.Get, "/api/reservations/mine", token1)).Content.ReadFromJsonAsync<ReservationDto[]>();
    Check(cancelledMine!.Where(r => r.TripId == trip.Id).All(r => r.TripStatus == "Cancelled" && r.Status == "Cancelled" && r.CancelledAt != null && r.CancellationReason == "Sefer iptal edildi: Test iptali" && !r.IsPaid), "Sefer iptali aktif rezervasyonları iptal etti; zaman ve neden yolcuya gösteriliyor");
    Check(await db.Trips.AsNoTracking().Where(t => t.Id == trip.Id).Select(t => t.AvailableSeats).SingleAsync() == 5, "Sefer iptalinde kontenjan bir kez geri verildi");
    Check((await Reserve(token1, trip.Id, 1)).StatusCode == HttpStatusCode.Conflict, "Sonradan iptal edilen sefere rezervasyon kapalı");
    users[1].IsActive = false; await db.SaveChangesAsync();
    Check((await Reserve(token2, single.Id, 1)).StatusCode == HttpStatusCode.Unauthorized, "Pasif yolcunun mevcut token'ı reddedildi");
    Console.WriteLine($"SONUÇ: {passed} kontrol başarılı.");

    Trip NewTrip() => new() { Company = company, Bus = bus, Route = route, DepartureTime = DateTime.UtcNow.AddDays(2), ArrivalTime = DateTime.UtcNow.AddDays(2).AddHours(6), Price = 500, AvailableSeats = 5 };
    async Task<string> Login(User user)
    {
        var result = await http.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "TestPass123!" });
        result.EnsureSuccessStatusCode();
        return (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }
    Task<HttpResponseMessage> Reserve(string? token, int tripId, int seat) => Send(HttpMethod.Post, "/api/reservations", token, new { tripId, seatNumber = seat, passengerId = users[1].Id, price = 1, isPaid = true });
    async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token, object? data = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (data != null) request.Content = JsonContent.Create(data);
        return await http.SendAsync(request);
    }
}
finally
{
    if (server is { HasExited: false }) { server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); }
    server?.Dispose();
    SqlConnection.ClearAllPools();
    // Refuse cleanup unless the exact per-run test database is selected.
    if (connection.InitialCatalog == testDatabaseName && testDatabaseName.StartsWith("TravelaReservationTests_", StringComparison.Ordinal))
        await db.Database.EnsureDeletedAsync();
}

void Check(bool value, string description)
{
    if (!value) throw new Exception("BAŞARISIZ: " + description);
    passed++; Console.WriteLine("OK: " + description);
}
static User NewUser(string name, string role = "Passenger")
{
    var user = new User { FullName = name, Email = name + "@travela.test", Role = role };
    user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "TestPass123!");
    return user;
}
