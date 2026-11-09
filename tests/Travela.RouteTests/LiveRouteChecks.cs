using System.IdentityModel.Tokens.Jwt;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Travela.Api.Security;
using Travela.Data;
using Travela.Models;

internal static class LiveRouteChecks
{
    // This mode never creates accounts, changes IDs, migrates or writes application records.
    public static async Task Run(string root, TravelaDbContext db, bool diagnose)
    {
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root, "backend"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<TravelaDbContextFactory>(optional: true).AddEnvironmentVariables().Build();
        var jwtKey = config["Jwt:Key"] ?? throw new Exception("JWT yapılandırması bulunamadı.");
        string Token(User user) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"],
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, user.Role), new Claim("security_version", user.SecurityVersion.ToString())],
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), SecurityAlgorithms.HmacSha256)));
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5062"), Timeout = TimeSpan.FromSeconds(30) };
        var users = await db.Users.AsNoTracking().SessionAccounts(db).Where(u => u.Role == "Company").ToListAsync();
        if (users.Count == 0) throw new Exception("Doğrulanabilir aktif firma hesabı bulunamadı.");
        var route9Exists = await db.Routes.AnyAsync(r => r.Id == 9);
        if (!route9Exists) throw new Exception("Mevcut veritabanında güzergâh ID 9 bulunmuyor.");
        var foreignKeys = await db.Database.SqlQueryRaw<string>("SELECT [name] AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID(N'Trips'), OBJECT_ID(N'Buses')) AND is_disabled = 0").ToListAsync();
        foreach (var keyName in new[] { "FK_Trips_Routes_RouteId", "FK_Trips_Companies_CompanyId", "FK_Trips_Buses_BusId", "FK_Buses_Companies_CompanyId" })
            Check(foreignKeys.Contains(keyName), "Etkin SQL ilişkisi: " + keyName);
        foreach (var user in users.DistinctBy(u => u.CompanyId))
        {
            using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/routes?includeInactive=true");
            listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(user));
            using var listResponse = await http.SendAsync(listRequest);
            Check(listResponse.IsSuccessStatusCode, $"Firma #{user.CompanyId} liste HTTP {(int)listResponse.StatusCode}");
            using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
            Console.WriteLine("Liste JSON alanları: " + string.Join(", ", list.RootElement[0].EnumerateObject().Select(p => p.Name)));
            using var detailRequest = new HttpRequestMessage(HttpMethod.Get, "/api/routes/9?pageNumber=1&companyId=2147483647");
            detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(user));
            using var detailResponse = await http.SendAsync(detailRequest);
            Console.WriteLine($"GET http://localhost:5062/api/routes/9?pageNumber=1: HTTP {(int)detailResponse.StatusCode}");
            if (diagnose) continue;
            Check(detailResponse.IsSuccessStatusCode, "ID 9 detay endpointi çalışır");
            var expectedByRoute = await db.Trips.AsNoTracking().Where(t => t.CompanyId == user.CompanyId)
                .GroupBy(t => t.RouteId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count);
            foreach (var route in list.RootElement.EnumerateArray())
                Check(route.GetProperty("tripCount").ValueKind == JsonValueKind.Number && route.GetProperty("tripCount").GetInt32() == expectedByRoute.GetValueOrDefault(route.GetProperty("id").GetInt32()), "Liste sefer sayısı SQL ile eşleşir: güzergâh #" + route.GetProperty("id").GetInt32());
            var total = list.RootElement.EnumerateArray().Sum(r => r.GetProperty("tripCount").GetInt32());
            Check(total == expectedByRoute.Values.Sum(), $"Firma #{user.CompanyId} kart toplamı SQL ile eşleşir: {total}");
            using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
            Check(detail.RootElement.GetProperty("route").GetProperty("tripCount").GetInt32() == expectedByRoute.GetValueOrDefault(9), "ID 9 firma sefer sayısı SQL ile eşleşir");
            var seen = new HashSet<int>();
            var pages = detail.RootElement.GetProperty("totalPages").GetInt32();
            for (var page = 1; page <= pages; page++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/routes/9?pageNumber={page}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(user));
                using var response = await http.SendAsync(request); Check(response.IsSuccessStatusCode, "ID 9 sefer sayfası yüklenir: " + page);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                foreach (var trip in body.RootElement.GetProperty("trips").EnumerateArray())
                {
                    var id = trip.GetProperty("id").GetInt32();
                    Check(seen.Add(id) && await db.Trips.AnyAsync(t => t.Id == id && t.RouteId == 9 && t.CompanyId == user.CompanyId), "ID 9 seferi tekil ve oturum firmasına aittir");
                    Check(trip.GetProperty("arrivalTime").GetDateTime().ToUniversalTime() == DateTime.SpecifyKind(await db.Trips.Where(t => t.Id == id).Select(t => t.ArrivalTime).SingleAsync(), DateTimeKind.Utc), "Seferin varış tarihi SQL ile eşleşir");
                }
            }
            Check(seen.Count == expectedByRoute.GetValueOrDefault(9), $"Firma #{user.CompanyId} ID 9 seferlerinin tümü listelenir: {seen.Count}");
            var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(Path.Combine(root, "frontend", "tests", "verify-live-routes.mjs"));
            start.Environment["TRAVELA_VERIFY_TOKEN"] = Token(user);
            start.Environment["TRAVELA_VERIFY_TOTAL"] = total.ToString();
            start.Environment["TRAVELA_VERIFY_ROUTE9_COUNT"] = seen.Count.ToString();
            using var browserCheck = Process.Start(start)!;
            await browserCheck.WaitForExitAsync();
            Check(browserCheck.ExitCode == 0, "Mevcut frontend üzerinden canlı güzergâh, otobüs ve sefer akışları çalışır");
        }
        Console.WriteLine(diagnose ? "Canlı API eski/yeni sözleşme teşhisi tamamlandı." : "SONUÇ: Mevcut ID 9, firma izolasyonu, sayfalama, SQL ilişkileri ve tüm liste toplamları doğrulandı.");
    }
    private static void Check(bool valid, string message)
    {
        if (!valid) throw new Exception("BAŞARISIZ: " + message);
        Console.WriteLine("OK: " + message);
    }
}
