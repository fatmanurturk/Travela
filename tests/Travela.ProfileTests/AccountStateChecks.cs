using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using Travela.Api.Models;
using Travela.Data;
using Travela.Models;

internal static class AccountStateChecks
{
    public static async Task Run(TravelaDbContext db, HttpClient http, Action<bool, string> check, string jwtKey)
    {
        const string oldPassword = "AccountPass123!";
        const string newPassword = "AccountPass456!";
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
        Task<HttpResponseMessage> Login(User user, string password = oldPassword) => Send("POST", "/api/auth/login", body: new { user.Email, password });
        async Task<string> Token(User user) => (await Json(await Login(user))).GetProperty("token").GetString()!;
        Task<HttpResponseMessage> FirstChange(User user, string password = newPassword) => Send("POST", "/api/auth/change-password", body:
            new { email = " " + user.Email.ToUpperInvariant() + " ", currentPassword = oldPassword, newPassword = password, confirmPassword = password });
        Task<HttpResponseMessage> Reset(string token, string password = newPassword) => Send("POST", "/api/auth/reset-password", body: new { token, newPassword = password, confirmPassword = password });

        var company = new Company { Name = "Hesap Durum Test Firması" };
        db.Add(company);
        await db.SaveChangesAsync();
        User Account(string name, string role = "Passenger")
        {
            var account = new User { FullName = name, Email = name + "@account-state.example", Role = role, CompanyId = role == "Company" ? company.Id : null };
            account.PasswordHash = new PasswordHasher<User>().HashPassword(account, oldPassword);
            return account;
        }
        var admin = Account("state-admin", "Admin");
        var passenger = Account("state-passenger");
        var companyUser = Account("state-company", "Company");
        var first = Account("state-first", "Company");
        first.MustChangePassword = true;
        db.AddRange(admin, passenger, companyUser, first);
        await db.SaveChangesAsync();
        var adminToken = await Token(admin);
        var passengerToken = await Token(passenger);
        var companyToken = await Token(companyUser);

        var legacy = await db.Users.AsNoTracking().SingleAsync(u => u.Email == "legacy-disabled@profile.example");
        check(!legacy.IsActive && legacy.MustChangePassword && legacy.SecurityVersion == 4 && legacy.PasswordHash == "old-hash" &&
            !legacy.ActivationPending && !legacy.PasswordResetRequired, "Yeni migration eski pasif hesabın engelini, şifre gereksinimini ve güvenlik sürümünü korur");
        check((await Reset("legacy-disabled-token")).StatusCode == HttpStatusCode.BadRequest &&
            !await db.Users.AsNoTracking().Where(u => u.Id == legacy.Id).Select(u => u.IsActive).SingleAsync(), "Eski geçerli sıfırlama bağlantısı pasif hesabı aktifleştiremez");

        var challenge = await Json(await Login(first));
        check(challenge.GetProperty("requiresPasswordChange").GetBoolean() && !challenge.TryGetProperty("token", out _), "İlk şifre değişimi gereken hesap normal oturum token'ı alamaz");
        await db.Companies.Where(c => c.Id == company.Id).ExecuteUpdateAsync(update => update.SetProperty(c => c.IsActive, false));
        check((await Login(first)).StatusCode == HttpStatusCode.Unauthorized && (await FirstChange(first)).StatusCode == HttpStatusCode.Unauthorized,
            "Pasif firma hem normal girişte hem ilk şifre değişiminde engellenir");
        foreach (var endpoint in new[] { "/api/company/dashboard", "/api/company/me", "/api/company/user", "/api/company/me/summary", "/api/company/reports", "/api/company/attention", "/api/reservations/company", "/api/buses", "/api/trips", "/api/routes" })
            check((await Send("GET", endpoint, companyToken)).StatusCode == HttpStatusCode.Unauthorized, "Pasif firma mevcut oturumla ortak kontrolden geçemez: " + endpoint);
        await db.Companies.Where(c => c.Id == company.Id).ExecuteUpdateAsync(update => update.SetProperty(c => c.IsActive, true));
        await db.Users.Where(u => u.Id == first.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.IsActive, false));
        check((await FirstChange(first)).StatusCode == HttpStatusCode.Unauthorized &&
            (await db.Users.AsNoTracking().SingleAsync(u => u.Id == first.Id)).MustChangePassword, "Yönetici tarafından kapalı hesap ilk şifre değişimiyle engeli aşamaz");
        await db.Users.Where(u => u.Id == first.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.IsActive, true).SetProperty(u => u.CompanyId, (int?)null));
        check((await FirstChange(first)).StatusCode == HttpStatusCode.Unauthorized, "Firmasız Company hesabı ilk şifre değişimi yapamaz");
        await db.Users.Where(u => u.Id == first.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.CompanyId, company.Id));
        var firstResult = await Json(await FirstChange(first));
        check((await Send("GET", "/api/company/user", firstResult.GetProperty("token").GetString())).IsSuccessStatusCode &&
            !(await db.Users.AsNoTracking().SingleAsync(u => u.Id == first.Id)).MustChangePassword, "Aktif firmanın ilk şifre değişimi e-posta normalizasyonuyla tamamlanır");

        var invitation = await Json(await Send("POST", "/api/admin/users", adminToken, new { fullName = "Davetli Hesap", email = "invited@account-state.example", role = "Passenger", isActive = true }));
        var invitedId = invitation.GetProperty("user").GetProperty("id").GetInt32();
        var activationToken = await TestMailbox.Token(db, invitedId);
        check(!invitation.TryGetProperty("activationToken", out _), "Yönetici API'si ham aktivasyon token'ını döndürmez; bağlantı e-posta ile teslim edilir");
        var invited = await db.Users.AsNoTracking().SingleAsync(u => u.Id == invitedId);
        check(invited.IsActive && invited.ActivationPending && !invited.PasswordResetRequired && (await Login(invited)).StatusCode == HttpStatusCode.Unauthorized,
            "Aktivasyon beklemesi yönetici engelinden ayrıdır ve şifreyle girişi engeller");
        var detail = await Json(await Send("GET", "/api/admin/users/" + invitedId, adminToken));
        check(detail.GetProperty("activationPending").GetBoolean() && detail.GetProperty("isActive").GetBoolean(), "Yönetici API'si aktivasyon beklemesi ile hesap iznini ayrı gösterir");
        check((await Reset(activationToken)).IsSuccessStatusCode && (await Login(invited, newPassword)).IsSuccessStatusCode &&
            !(await db.Users.AsNoTracking().SingleAsync(u => u.Id == invitedId)).ActivationPending, "Tek kullanımlık aktivasyon yalnızca aktivasyon beklemesini kaldırır ve yeni şifreyle giriş açılır");
        check((await Reset(activationToken)).StatusCode == HttpStatusCode.BadRequest, "Aktivasyon token'ı tekrar kullanılamaz");

        var disabledCreate = await Json(await Send("POST", "/api/admin/users", adminToken, new { fullName = "Kapalı Hesap", email = "disabled@account-state.example", role = "Passenger", password = oldPassword, isActive = false }));
        var disabledId = disabledCreate.GetProperty("user").GetProperty("id").GetInt32();
        check(!await db.Users.Where(u => u.Id == disabledId).Select(u => u.IsActive).SingleAsync(), "Şifre verilerek hesap oluşturulması yönetici tarafından verilen kapalı durumu değiştirmez");
        check((await Send("POST", $"/api/admin/users/{disabledId}/password-reset", adminToken)).StatusCode == HttpStatusCode.Conflict,
            "Yönetici engeli kaldırılmadan kapalı hesaba sıfırlama başlatılamaz");

        var reset = await Json(await Send("POST", $"/api/admin/users/{passenger.Id}/password-reset", adminToken));
        var resetToken = await TestMailbox.Token(db, passenger.Id);
        check(!reset.TryGetProperty("resetToken", out _), "Yönetici API'si ham sıfırlama token'ını döndürmez");
        var waiting = await db.Users.AsNoTracking().SingleAsync(u => u.Id == passenger.Id);
        check(waiting.IsActive && waiting.PasswordResetRequired && !waiting.ActivationPending, "Şifre sıfırlama hesabı kapatmaz; ayrı sıfırlama gereksinimini işaretler");
        check((await Login(passenger)).StatusCode == HttpStatusCode.Unauthorized && (await Send("GET", "/api/profile", passengerToken)).StatusCode == HttpStatusCode.Unauthorized,
            "Şifre sıfırlama beklerken eski şifre ve mevcut oturum erişim sağlayamaz");
        var completions = await Task.WhenAll(Reset(resetToken), Reset(resetToken));
        check(completions.Count(response => response.IsSuccessStatusCode) == 1 && completions.Count(response => response.StatusCode == HttpStatusCode.BadRequest) == 1,
            "Aynı sıfırlama token'ıyla eş zamanlı iki istekten yalnızca biri tamamlanır");
        var completed = await db.Users.AsNoTracking().SingleAsync(u => u.Id == passenger.Id);
        check(completed.IsActive && !completed.PasswordResetRequired && !completed.MustChangePassword && (await Login(passenger, newPassword)).IsSuccessStatusCode,
            "Başarılı sıfırlama yalnızca şifre gereksinimlerini temizler ve yeni şifreyle giriş sağlar");

        var companyReset = await Json(await Send("POST", $"/api/admin/users/{companyUser.Id}/password-reset", adminToken));
        var companyResetToken = await TestMailbox.Token(db, companyUser.Id);
        await Send("PATCH", $"/api/admin/companies/{company.Id}/status", adminToken);
        check((await Reset(companyResetToken)).StatusCode == HttpStatusCode.BadRequest, "Pasif firmanın geçerli sıfırlama token'ı şifreyi değiştiremez");
        await Send("PATCH", $"/api/admin/companies/{company.Id}/status", adminToken);
        check((await Send("GET", "/api/company/dashboard", companyToken)).StatusCode == HttpStatusCode.Unauthorized,
            "Firma yönetici tarafından tekrar açılsa da eski oturum token'ları geçersiz kalır");
        check((await Reset(companyResetToken)).IsSuccessStatusCode && (await Login(companyUser, newPassword)).IsSuccessStatusCode, "Firma yeniden açıldıktan sonra geçerli sıfırlama tamamlanabilir");

        var normalReset = await Json(await Send("POST", $"/api/admin/users/{passenger.Id}/password-reset", adminToken));
        var revokedResetToken = await TestMailbox.Token(db, passenger.Id);
        await Json(await Send("PATCH", $"/api/admin/users/{passenger.Id}/status", adminToken, new { isActive = false }));
        check((await Reset(revokedResetToken)).StatusCode == HttpStatusCode.BadRequest && !await db.Users.Where(u => u.Id == passenger.Id).Select(u => u.IsActive).SingleAsync(),
            "Sıfırlama sonrası yönetici engeli eski bağlantıyla kaldırılamaz");
        await Json(await Send("PATCH", $"/api/admin/users/{passenger.Id}/status", adminToken, new { isActive = true }));
        check((await Login(passenger, newPassword)).StatusCode == HttpStatusCode.Unauthorized &&
            await db.Users.Where(u => u.Id == passenger.Id).Select(u => u.PasswordResetRequired).SingleAsync(), "Yönetici iznini açmak bekleyen şifre yenileme gereksinimini kaldırmaz");

        var firstReset = await Json(await Send("POST", $"/api/admin/users/{first.Id}/password-reset", adminToken));
        var firstResetToken = await TestMailbox.Token(db, first.Id);
        await db.Users.Where(u => u.Id == first.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.MustChangePassword, true));
        check((await FirstChange(first)).StatusCode == HttpStatusCode.Unauthorized &&
            (await Reset(firstResetToken)).IsSuccessStatusCode,
            "Sıfırlama gereksinimi ilk şifre değişimiyle aşılamaz; token'la sıfırlama iki şifre gereksinimini de tamamlar");

        var claims = new[] { new Claim(ClaimTypes.Role, "Admin"), new Claim("security_version", "1") };
        var malformed = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("ProfileTests", "ProfileTests", claims,
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), SecurityAlgorithms.HmacSha256)));
        check((await Send("GET", "/api/admin/dashboard", malformed)).StatusCode == HttpStatusCode.Unauthorized, "İmzalı olsa bile kullanıcı kimliği eksik token ortak kontrolde reddedilir");

        // A credential flag change must deny even a token whose version was not incremented.
        var resumed = (await Json(await Login(companyUser, newPassword))).GetProperty("token").GetString()!;
        await db.Users.Where(u => u.Id == companyUser.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.ActivationPending, true));
        check((await Send("GET", "/api/company/dashboard", resumed)).StatusCode == HttpStatusCode.Unauthorized, "Aktivasyon beklemesi güvenlik sürümü aynı kalsa bile mevcut oturumu engeller");
        await db.Users.Where(u => u.Id == companyUser.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.ActivationPending, false).SetProperty(u => u.MustChangePassword, true));
        check((await Send("GET", "/api/company/dashboard", resumed)).StatusCode == HttpStatusCode.Unauthorized, "İlk şifre değişimi gereksinimi güvenlik sürümü aynı kalsa bile mevcut oturumu engeller");
        await db.Users.Where(u => u.Id == companyUser.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.MustChangePassword, false).SetProperty(u => u.PasswordResetRequired, true));
        check((await Send("GET", "/api/company/dashboard", resumed)).StatusCode == HttpStatusCode.Unauthorized, "Şifre sıfırlama gereksinimi güvenlik sürümü aynı kalsa bile mevcut oturumu engeller");

        var invitedSession = (await Json(await Login(invited, newPassword))).GetProperty("token").GetString()!;
        await Json(await Send("PUT", $"/api/admin/users/{invitedId}", adminToken,
            new { fullName = invited.FullName, invited.Email, role = "Passenger", isActive = false }));
        check(!await db.Users.Where(u => u.Id == invitedId).Select(u => u.IsActive).SingleAsync() &&
            (await Send("GET", "/api/profile", invitedSession)).StatusCode == HttpStatusCode.Unauthorized, "Kullanıcı düzenleme endpoint'i de yönetici engelini uygular ve mevcut oturumu kapatır");
        check((await Send("PUT", $"/api/admin/users/{admin.Id}", adminToken,
            new { admin.FullName, admin.Email, role = "Admin", isActive = false })).StatusCode == HttpStatusCode.BadRequest,
            "Yönetici kendi hesabını düzenleme yoluyla kapatamaz");
        var list = await Json(await Send("GET", "/api/admin/users?status=PasswordResetRequired", adminToken));
        check(list.GetProperty("items").EnumerateArray().All(u => u.GetProperty("passwordResetRequired").GetBoolean() && u.GetProperty("isActive").GetBoolean()),
            "Şifre sıfırlama beklemesi filtresi kapalı hesap filtresinden ayrıdır");
        var rollbackBlocked = false;
        try { await db.GetService<IMigrator>().MigrateAsync("20261103065228_AddPassengerProfilePreferences"); }
        catch (SqlException exception) when (exception.Number is 51000 or 51013) { rollbackBlocked = true; }
        check(rollbackBlocked && await db.Users.AnyAsync(u => u.PasswordResetRequired) &&
            (await db.Database.GetAppliedMigrationsAsync()).Contains("20261104064425_SeparateAccountAccessStates"),
            "Bekleyen hesap durumları varken geri migration engellenir ve güvenlik alanları korunur");
        // Each successful down migration commits separately before an older guard can reject.
        // Restore the current schema before continuing against the running API.
        await db.Database.MigrateAsync();
    }
}
