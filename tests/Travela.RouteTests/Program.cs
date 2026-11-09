using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Travela.Api.Models;
using Travela.Data;
using Travela.Models;
using RouteModel = Travela.Api.Models.Route;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
Directory.SetCurrentDirectory(root);
using var source = new TravelaDbContextFactory().CreateDbContext([]);
if (args.Contains("--verify-live") || args.Contains("--diagnose-live"))
{
    await LiveRouteChecks.Run(root, source, args.Contains("--diagnose-live"));
    return;
}
var database = "TravelaRouteTests_" + Guid.NewGuid().ToString("N");
var connection = new SqlConnectionStringBuilder(source.Database.GetConnectionString()) { InitialCatalog = database };
await using var db = new TravelaDbContext(new DbContextOptionsBuilder<TravelaDbContext>().UseSqlServer(connection.ConnectionString).Options);
Process? server = null;
var log = new StringBuilder();
var passed = 0;
try
{
    // All writes are confined to a newly named disposable database, never the configured application DB.
    await db.Database.MigrateAsync();
    var adminRole = new Role { Name = "Admin" }; var companyRole = new Role { Name = "Company" };
    foreach (var name in new[] { "trip.view", "trip.create" })
    {
        var permission = new Permission { Name = name };
        db.AddRange(new RolePermission { Role = adminRole, Permission = permission }, new RolePermission { Role = companyRole, Permission = permission });
    }
    var company = new Company { Name = "Test Firma" }; var foreignCompany = new Company { Name = "Diğer Firma" };
    var bus = new Bus { Company = company, PlateNumber = "34 TEST 01", NormalizedPlateNumber = "34TEST01", SeatCapacity = 40 };
    var foreignBus = new Bus { Company = foreignCompany, PlateNumber = "06 TEST 02", NormalizedPlateNumber = "06TEST02", SeatCapacity = 40 };
    db.AddRange(company, foreignCompany, bus, foreignBus); await db.SaveChangesAsync();
    var admin = new User { FullName = "Admin", Email = "admin@routes.test", Role = "Admin" };
    var owner = new User { FullName = "Firma", Email = "company@routes.test", Role = "Company", CompanyId = company.Id };
    var foreign = new User { FullName = "Diğer", Email = "other@routes.test", Role = "Company", CompanyId = foreignCompany.Id };
    var passenger = new User { FullName = "Yolcu", Email = "passenger@routes.test" };
    db.AddRange(admin, owner, foreign, passenger); await db.SaveChangesAsync();
    var key = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    string Token(User user) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("RouteTests", "RouteTests",
        [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, user.Role), new Claim("security_version", user.SecurityVersion.ToString())],
        expires: DateTime.UtcNow.AddHours(1), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)));
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var config = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(root, "backend"), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(Path.Combine(root, "backend", "bin", config, "net10.0", "Travela.API.dll"));
    start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing"; start.Environment["ConnectionStrings__DefaultConnection"] = connection.ConnectionString;
    start.Environment["Jwt__Key"] = key; start.Environment["Jwt__Issuer"] = "RouteTests"; start.Environment["Jwt__Audience"] = "RouteTests";
    start.Environment["Payments__Provider"] = "LocalTest"; start.Environment["Payments__RecoveryEnabled"] = "false";
    start.Environment["Notifications__WorkerEnabled"] = "false"; start.Environment["AccountEmail__Worker__Enabled"] = "false";
    start.Environment["Logging__LogLevel__Default"] = "Warning";
    server = Process.Start(start)!;
    server.OutputDataReceived += (_, e) => { lock (log) log.AppendLine(e.Data); };
    server.ErrorDataReceived += (_, e) => { lock (log) log.AppendLine(e.Data); };
    server.BeginOutputReadLine(); server.BeginErrorReadLine();
    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(30) };
    for (var attempt = 0; attempt < 100; attempt++)
    {
        try { if ((await http.GetAsync("/api/test")).IsSuccessStatusCode) break; } catch (HttpRequestException) { }
        if (server.HasExited) throw new Exception(log.ToString()); await Task.Delay(100);
    }
    async Task<HttpResponseMessage> Send(string method, string path, User? user, object? body = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (user is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(user));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request);
    }
    async Task<JsonElement> Json(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw new Exception($"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}\n{log}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    object Body(string from = "İstanbul", string to = "Ankara", bool active = true, int? distance = 450, int? minutes = 360, bool confirm = false) =>
        new { departureCity = from, arrivalCity = to, isActive = active, distanceKm = distance, estimatedDurationMinutes = minutes, confirmDeactivation = confirm };
    Check((await Send("GET", "/api/routes", null)).StatusCode == HttpStatusCode.Unauthorized, "Liste JWT gerektirir");
    Check((await Send("GET", "/api/routes", passenger)).StatusCode == HttpStatusCode.Forbidden, "Passenger ortak yönetim listesine erişemez");
    var created = await Json(await Send("POST", "/api/routes", admin, Body()));
    var id = created.GetProperty("route").GetProperty("id").GetInt32();
    foreach (var (method, path, body) in new (string, string, object?)[] {
        ("POST", "/api/routes", Body("Ankara", "İzmir")), ("PUT", $"/api/routes/{id}", Body()), ("PATCH", $"/api/routes/{id}/status", new { isActive = false }) })
        Check((await Send(method, path, owner, body)).StatusCode == HttpStatusCode.Forbidden, $"Company {method} ile ortak güzergâhı değiştiremez");
    Check((await Send("POST", "/api/routes", admin, Body(" istanbul ", "ankara"))).StatusCode == HttpStatusCode.Conflict, "Türkçe harf ve boşluk normalizasyonu mükerrer aktif güzergâhı engeller");
    Check((await Send("POST", "/api/routes", admin, Body("Ankara", "İstanbul"))).StatusCode == HttpStatusCode.Created, "Ters yön ayrı güzergâhtır");
    foreach (var invalid in new[] { Body("Ankara", "Ankara"), Body("Geçersiz", "Ankara"), Body(distance: 0), Body(distance: -1), Body(minutes: 0) })
        Check((await Send("POST", "/api/routes", admin, invalid)).StatusCode == HttpStatusCode.BadRequest, "Geçersiz şehir, aynı şehir ve sıfır/negatif değer reddedilir");
    var cities = await Json(await Send("GET", "/api/routes/cities", owner));
    Check(cities.GetArrayLength() == 81, "81 il API üzerinden sağlanır");
    var shared = await db.Routes.SingleAsync(r => r.Id == id);
    Trip Trip(Company c, Bus b, DateTime at) => new() { Company = c, Bus = b, Route = shared, DepartureTime = at, ArrivalTime = at.AddHours(6), Price = 500, AvailableSeats = 40, Status = at < DateTime.UtcNow ? "Completed" : "Scheduled" };
    var ownTrip = Trip(company, bus, DateTime.UtcNow.AddDays(3)); var foreignTrip = Trip(foreignCompany, foreignBus, DateTime.UtcNow.AddDays(4));
    var past = Trip(company, bus, DateTime.UtcNow.AddDays(-3)); db.AddRange(ownTrip, foreignTrip, past); await db.SaveChangesAsync();
    var detail = await Json(await Send("GET", $"/api/routes/{id}?companyId={foreignCompany.Id}", owner));
    Check(detail.GetProperty("route").GetProperty("tripCount").GetInt32() == 2 && detail.GetProperty("trips").EnumerateArray().All(t => t.GetProperty("id").GetInt32() != foreignTrip.Id), "Firma parametreyle başka firmanın seferlerini ve sayılarını göremez");
    var foreignDetail = await Json(await Send("GET", $"/api/routes/{id}", foreign));
    Check(foreignDetail.GetProperty("trips").GetArrayLength() == 1 && foreignDetail.GetProperty("route").GetProperty("hasTrips").GetBoolean(), "Ortak güzergâh detayları her firmaya ayrı kapsamlanır");
    Check((await Json(await Send("GET", $"/api/routes/{id}", admin))).GetProperty("trips").GetArrayLength() == 3, "Admin tüm bağlı seferleri görebilir");
    Check((await Send("PUT", $"/api/routes/{id}", admin, Body("Bursa", "Ankara"))).StatusCode == HttpStatusCode.Conflict, "Geçmiş veya başka firma seferine bağlı şehir değiştirilemez");
    Check((await Send("PUT", $"/api/routes/{id}", admin, Body(distance: 460, minutes: 375))).IsSuccessStatusCode, "Bağlı güzergâhın mesafesi ve süresi güncellenebilir");
    Check((await Send("PATCH", $"/api/routes/{id}/status", admin, new { })).StatusCode == HttpStatusCode.BadRequest, "Eksik durum sessizce pasife almaz");
    var warningResponse = await Send("PATCH", $"/api/routes/{id}/status", admin, new { isActive = false });
    var warning = await warningResponse.Content.ReadFromJsonAsync<JsonElement>();
    Check(warningResponse.StatusCode == HttpStatusCode.Conflict && warning.GetProperty("plannedTripCount").GetInt32() == 2, "Pasife alma tüm firmaların planlanmış seferleri için onay gerektirir");
    Check((await db.Routes.AsNoTracking().SingleAsync(r => r.Id == id)).IsActive, "Onay olmadan durum değişmez");
    Check((await Send("PUT", $"/api/routes/{id}", admin, Body(active: false))).StatusCode == HttpStatusCode.Conflict, "PUT üzerinden de pasife alma onayı atlanamaz");
    await Json(await Send("PATCH", $"/api/routes/{id}/status", admin, new { isActive = false, confirmDeactivation = true }));
    Check((await Json(await Send("GET", "/api/routes", owner))).EnumerateArray().All(r => r.GetProperty("id").GetInt32() != id), "Yeni sefer seçim API'si pasif güzergâhı içermez");
    Check((await Json(await Send("GET", "/api/routes?includeInactive=true", owner))).EnumerateArray().Any(r => r.GetProperty("id").GetInt32() == id && !r.GetProperty("isActive").GetBoolean()), "Yönetim listesi pasif kayıtları içerir");
    Check(await db.Trips.CountAsync(t => t.RouteId == id) == 3 && (await Json(await Send("GET", $"/api/routes/{id}", owner))).GetProperty("trips").GetArrayLength() == 2, "Pasife alma mevcut seferleri ve geçmişi korur");
    var newTrip = new { busId = bus.Id, routeId = id, departureTime = DateTime.UtcNow.AddDays(10), arrivalTime = DateTime.UtcNow.AddDays(10).AddHours(6), price = 500 };
    Check((await Send("POST", "/api/trips", owner, newTrip)).StatusCode == HttpStatusCode.BadRequest, "Pasif güzergâhta API üzerinden yeni sefer de oluşturulamaz");
    Check((await Send("POST", "/api/routes", admin, Body())).StatusCode == HttpStatusCode.Created, "Pasif kayıt korunurken aynı yönde tek aktif güzergâh oluşturulabilir");
    Check((await Send("PATCH", $"/api/routes/{id}/status", admin, new { isActive = true })).StatusCode == HttpStatusCode.Conflict, "Yeniden aktifleştirme de mükerrer aktif kontrolü yapar");
    var activeId = await db.Routes.Where(r => r.Id != id && r.DepartureCity == "İstanbul" && r.IsActive).Select(r => r.Id).SingleAsync();
    await Json(await Send("PATCH", $"/api/routes/{activeId}/status", admin, new { isActive = false }));
    await Json(await Send("PATCH", $"/api/routes/{id}/status", admin, new { isActive = true }));
    Check((await db.Routes.AsNoTracking().SingleAsync(r => r.Id == id)).IsActive, "Çakışma kalkınca güzergâh yeniden aktif yapılabilir");
    var concurrent = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Send("POST", "/api/routes", admin, Body("Bursa", "İzmir"))));
    Check(concurrent.Count(r => r.StatusCode == HttpStatusCode.Created) == 1 && concurrent.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict) &&
        await db.Routes.CountAsync(r => r.DepartureCity == "Bursa" && r.ArrivalCity == "İzmir" && r.IsActive) == 1, "Eşzamanlı istekler tek aktif güzergâh oluşturur");
    Check((await Send("GET", "/api/routes/2147483647", owner)).StatusCode == HttpStatusCode.NotFound, "Olmayan güzergâh 404 döner");
    var legacy = new RouteModel { DepartureCity = "Istanbul", ArrivalCity = "Ankara", IsActive = false };
    db.Add(legacy); await db.SaveChangesAsync();
    db.Add(new Trip { Company = company, Bus = bus, Route = legacy, DepartureTime = DateTime.UtcNow.AddDays(-10), ArrivalTime = DateTime.UtcNow.AddDays(-10).AddHours(6), Price = 500, AvailableSeats = 40, Status = "Completed" });
    await db.SaveChangesAsync();
    Check((await Send("PUT", $"/api/routes/{legacy.Id}", admin, Body("Istanbul", active: false, distance: 470))).IsSuccessStatusCode &&
        (await db.Routes.AsNoTracking().SingleAsync(r => r.Id == legacy.Id)).DepartureCity == "Istanbul", "Eski şehir yazımı ve sefer geçmişi korunurken mesafe güncellenebilir");
    while (await db.Routes.MaxAsync(r => r.Id) < 9)
        await Json(await Send("POST", "/api/routes", admin, Body("Ankara", "İzmir", active: false)));
    var route9 = await db.Routes.SingleAsync(r => r.Id == 9);
    var noTrips = await Json(await Send("GET", "/api/routes/9", owner));
    Check(noTrips.GetProperty("route").GetProperty("tripCount").ValueKind == JsonValueKind.Number &&
        noTrips.GetProperty("route").GetProperty("tripCount").GetInt32() == 0 && noTrips.GetProperty("trips").GetArrayLength() == 0, "ID 9 sıfır seferde sayısal 0 ve boş liste döndürür");
    for (var index = 0; index < 12; index++) db.Add(new Trip { Company = company, Bus = bus, Route = route9,
        DepartureTime = DateTime.UtcNow.AddDays(20 + index), ArrivalTime = DateTime.UtcNow.AddDays(20 + index).AddHours(6), Price = 500, AvailableSeats = 40 });
    db.Add(new Trip { Company = foreignCompany, Bus = foreignBus, Route = route9, DepartureTime = DateTime.UtcNow.AddDays(20), ArrivalTime = DateTime.UtcNow.AddDays(20).AddHours(6), Price = 600, AvailableSeats = 40 });
    await db.SaveChangesAsync();
    var page1 = await Json(await Send("GET", "/api/routes/9?pageNumber=1&companyId=" + foreignCompany.Id, owner));
    var page2 = await Json(await Send("GET", "/api/routes/9?pageNumber=2", owner));
    var ids = page1.GetProperty("trips").EnumerateArray().Concat(page2.GetProperty("trips").EnumerateArray()).Select(t => t.GetProperty("id").GetInt32()).ToArray();
    Check(page1.GetProperty("route").GetProperty("id").GetInt32() == 9 && page1.GetProperty("route").GetProperty("tripCount").GetInt32() == 12 &&
        ids.Length == 12 && ids.Distinct().Count() == 12 && (await db.Trips.Where(t => t.RouteId == 9 && t.CompanyId == company.Id).Select(t => t.Id).ToListAsync()).All(ids.Contains),
        "GET /api/routes/9 tüm sayfalarda yalnızca firmanın seferlerini tek kez döndürür");
    Check(page1.GetProperty("trips")[0].GetProperty("arrivalTime").GetString()!.EndsWith('Z') &&
        page1.GetProperty("trips")[0].GetProperty("departureTime").GetString()!.EndsWith('Z'), "Detay kalkış ve varış tarihlerini UTC olarak içerir");
    var ownerList = await Json(await Send("GET", "/api/routes?includeInactive=true", owner));
    Check(ownerList.EnumerateArray().Sum(r => r.GetProperty("tripCount").GetInt32()) == await db.Trips.CountAsync(t => t.CompanyId == company.Id),
        "Kartın liste toplamı Trips tablosundaki gerçek firma sefer sayısıyla eşleşir");
    Console.WriteLine($"SONUÇ: {passed} güzergâh API/yetki/izolasyon/iş kuralı kontrolü başarılı.");
}
finally
{
    if (server is { HasExited: false }) { server.Kill(true); await server.WaitForExitAsync(); }
    server?.Dispose(); SqlConnection.ClearAllPools();
    if (connection.InitialCatalog == database && database.StartsWith("TravelaRouteTests_", StringComparison.Ordinal)) await db.Database.EnsureDeletedAsync();
}
void Check(bool ok, string message) { if (!ok) throw new Exception("BAŞARISIZ: " + message); passed++; Console.WriteLine("OK: " + message); }
