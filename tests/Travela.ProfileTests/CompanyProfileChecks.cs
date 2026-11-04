using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Data;
using Travela.Models;

internal static class CompanyProfileChecks
{
    public static async Task Run(TravelaDbContext db, HttpClient http, Action<bool, string> check)
    {
        const string endpoint = "/api/company/user";
        const string originalPassword = "TestPass123!";
        const string newPassword = "CompanyPass456!";
        var company = new Company { Name = "Profil Test Firması", Email = "company@company-profile.example", Phone = "02121234567", Address = "Firma adresi" };
        var foreignCompany = new Company { Name = "Diğer Firma" };
        db.AddRange(company, foreignCompany);
        await db.SaveChangesAsync();
        User Account(string name, string role = "Company", int? companyId = null)
        {
            var user = new User { FullName = name, Email = name + "@company-profile.example", Role = role, CompanyId = companyId };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, originalPassword);
            return user;
        }
        var owner = Account("company-owner", companyId: company.Id);
        var foreign = Account("company-foreign", companyId: foreignCompany.Id);
        var limited = Account("company-limited", companyId: company.Id);
        var restricted = Account("company-restricted", companyId: company.Id);
        var passenger = Account("company-passenger", "Passenger");
        var admin = Account("company-admin", "Admin");
        db.AddRange(owner, foreign, limited, restricted, passenger, admin);
        await db.SaveChangesAsync();

        async Task<HttpResponseMessage> Send(string method, string path, string? token, object? body = null)
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
        async Task<string> Login(User user, string password = originalPassword) =>
            (await Json(await http.PostAsJsonAsync("/api/auth/login", new { user.Email, password }))).GetProperty("token").GetString()!;
        object Password(string current = originalPassword, string next = newPassword, string? confirm = null) =>
            new { currentPassword = current, newPassword = next, confirmPassword = confirm ?? next };

        var token = await Login(owner);
        var secondSession = await Login(owner);
        var foreignToken = await Login(foreign);
        var restrictedToken = await Login(restricted);
        foreach (var denied in new[] { (Token: (string?)null, Status: HttpStatusCode.Unauthorized),
                     (Token: (string?)await Login(passenger), Status: HttpStatusCode.Forbidden),
                     (Token: (string?)await Login(admin), Status: HttpStatusCode.Forbidden) })
        {
            foreach (var (method, path, body) in new (string, string, object?)[]
                     { ("GET", endpoint, null), ("PUT", endpoint, new { fullName = "İzinsiz" }), ("POST", endpoint + "/change-password", Password()) })
                check((await Send(method, path, denied.Token, body)).StatusCode == denied.Status, $"Firma kullanıcı profili {method}: giriş/rol sınırı korunur ({denied.Status})");
        }

        var initialResponse = await Send("GET", endpoint + "?userId=" + foreign.Id + "&companyId=" + foreignCompany.Id, token);
        var initial = await Json(initialResponse);
        check(initial.GetProperty("id").GetInt32() == owner.Id && initial.GetProperty("companyId").GetInt32() == company.Id &&
            initial.GetProperty("companyName").GetString() == company.Name, "Firma profili istemcinin kimlik parametreleri yerine oturum sahibini getirir");
        check(initialResponse.Headers.CacheControl?.NoStore == true && !initial.TryGetProperty("passwordHash", out _), "Firma yetkilisinin bilgileri önbelleğe alınmaz ve şifre hash'i gönderilmez");
        check((await Send("GET", "/api/company/me", token)).IsSuccessStatusCode &&
            (await Send("GET", "/api/company/me/summary", token)).IsSuccessStatusCode, "Firma ekranının firma, yetkili ve özet istekleri birlikte çalışır");
        check((await Send("GET", "/api/profile", token)).StatusCode == HttpStatusCode.Forbidden, "Yolcu profilinin rol kısıtı korunur");

        var updated = await Json(await Send("PUT", endpoint, token, new { fullName = "  Güncel Firma Yetkilisi  ", userId = foreign.Id, companyId = foreignCompany.Id, role = "Admin", email = "changed@example.com" }));
        var saved = await db.Users.AsNoTracking().SingleAsync(u => u.Id == owner.Id);
        check(updated.GetProperty("fullName").GetString() == "Güncel Firma Yetkilisi" && saved.FullName == "Güncel Firma Yetkilisi", "Firma yetkilisinin adı kırpılarak kalıcı kaydedilir");
        check(saved.CompanyId == company.Id && saved.Role == "Company" && saved.Email == owner.Email &&
            (await db.Users.AsNoTracking().SingleAsync(u => u.Id == foreign.Id)).FullName == foreign.FullName, "Profil güncellemesi başka kullanıcıyı, firmayı, rolü veya giriş e-postasını değiştirmez");
        check((await db.Companies.AsNoTracking().SingleAsync(c => c.Id == company.Id)).Name == company.Name &&
            (await Send("GET", endpoint, token)).IsSuccessStatusCode, "Yetkili adı güncellemesi firma bilgilerini ve mevcut oturumu korur");
        foreach (var name in new[] { "  ", new string('A', 121) })
            check((await Send("PUT", endpoint, token, new { fullName = name })).StatusCode == HttpStatusCode.BadRequest, "Boş veya aşırı uzun yetkili adı reddedilir");

        check((await Send("POST", endpoint + "/change-password", token, Password(next: "short"))).StatusCode == HttpStatusCode.BadRequest, "Kısa firma şifresi reddedilir");
        check((await Send("POST", endpoint + "/change-password", token, Password(confirm: "Mismatch456!"))).StatusCode == HttpStatusCode.BadRequest, "Firma şifre tekrarı doğrulanır");
        check((await Send("POST", endpoint + "/change-password", token, Password(next: originalPassword))).StatusCode == HttpStatusCode.BadRequest, "Firma yetkilisi aynı şifreyi yeniden kullanamaz");
        check((await Send("POST", endpoint + "/change-password", token, Password(current: "WrongPassword!"))).StatusCode == HttpStatusCode.BadRequest &&
            (await Send("GET", endpoint, token)).IsSuccessStatusCode, "Yanlış mevcut şifre reddedilir; firma oturumu açık kalır");
        await db.Users.Where(u => u.Id == owner.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.PasswordResetTokenHash, "old-reset")
            .SetProperty(u => u.PasswordResetTokenExpiresAt, DateTime.UtcNow.AddHours(1)));
        var changed = await Json(await Send("POST", endpoint + "/change-password", token, Password()));
        var renewed = changed.GetProperty("token").GetString()!;
        check((await Send("GET", endpoint, token)).StatusCode == HttpStatusCode.Unauthorized &&
            (await Send("GET", endpoint, secondSession)).StatusCode == HttpStatusCode.Unauthorized &&
            (await Send("GET", endpoint, renewed)).IsSuccessStatusCode, "Firma şifresi değişince eski oturumlar kapanır ve yeni token çalışır");
        check(new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo == new JwtSecurityTokenHandler().ReadJwtToken(renewed).ValidTo,
            "Firma şifresi değişimi mevcut oturum süresini uzatmaz");
        check((await http.PostAsJsonAsync("/api/auth/login", new { owner.Email, password = originalPassword })).StatusCode == HttpStatusCode.Unauthorized &&
            !string.IsNullOrEmpty(await Login(owner, newPassword)), "Firma hesabı yalnızca yeni şifreyle giriş yapar");
        saved = await db.Users.AsNoTracking().SingleAsync(u => u.Id == owner.Id);
        check(saved.PasswordResetTokenHash == null && saved.PasswordResetTokenExpiresAt == null && saved.SecurityVersion == owner.SecurityVersion + 1 &&
            new PasswordHasher<User>().VerifyHashedPassword(saved, saved.PasswordHash, newPassword) != PasswordVerificationResult.Failed,
            "Firma şifresi hashlenir, güvenlik sürümü artar ve eski sıfırlama bağlantıları iptal edilir");
        check((await Send("GET", endpoint, foreignToken)).IsSuccessStatusCode &&
            !string.IsNullOrEmpty(await Login(foreign)), "Firma şifresi değişimi diğer firmanın hesabını ve oturumunu etkilemez");

        var limitedToken = await Login(limited);
        for (var i = 0; i < 5; i++) await Send("POST", endpoint + "/change-password", limitedToken, Password(current: "WrongPassword!"));
        check((await Send("POST", endpoint + "/change-password", limitedToken, Password())).StatusCode == HttpStatusCode.TooManyRequests,
            "Firma şifre denemeleri beş denemeden sonra sınırlandırılır");

        async Task DeniedProfile(string reason)
        {
            check((await Send("GET", endpoint, restrictedToken)).StatusCode == HttpStatusCode.Unauthorized &&
                (await Send("PUT", endpoint, restrictedToken, new { fullName = "İzinsiz" })).StatusCode == HttpStatusCode.Unauthorized &&
                (await Send("POST", endpoint + "/change-password", restrictedToken, Password())).StatusCode == HttpStatusCode.Unauthorized, reason);
        }
        await db.Companies.Where(c => c.Id == company.Id).ExecuteUpdateAsync(update => update.SetProperty(c => c.IsActive, false));
        await DeniedProfile("Pasif firma için yetkili profilini okuma ve değiştirme engellenir");
        await db.Companies.Where(c => c.Id == company.Id).ExecuteUpdateAsync(update => update.SetProperty(c => c.IsActive, true));
        await db.Users.Where(u => u.Id == restricted.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.CompanyId, (int?)null));
        await DeniedProfile("Firmaya bağlı olmayan hesap profil işlemi yapamaz");
        await db.Users.Where(u => u.Id == restricted.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.CompanyId, company.Id).SetProperty(u => u.MustChangePassword, true));
        await DeniedProfile("İlk şifre değişimi bekleyen hesap profil işlemi yapamaz");
        await db.Users.Where(u => u.Id == restricted.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.MustChangePassword, false).SetProperty(u => u.IsActive, false));
        await DeniedProfile("Pasif kullanıcı profil işlemi yapamaz");
    }
}
