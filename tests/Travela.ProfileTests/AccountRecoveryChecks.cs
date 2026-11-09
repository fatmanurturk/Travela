using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Data;
using Travela.Models;

internal static class AccountRecoveryChecks
{
    public static async Task Run(TravelaDbContext db, HttpClient http, Action<bool, string> check)
    {
        const string password = "RecoveryPass123!";
        const string replacement = "RecoveryPass456!";
        async Task<HttpResponseMessage> Send(string path, object body, string? token = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/" + path) { Content = JsonContent.Create(body) };
            if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await http.SendAsync(request);
        }
        async Task<JsonElement> Json(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode) throw new Exception($"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        Task<HttpResponseMessage> Inspect(string token) => Send("account-link", new { token });
        var email = "new@recovery.example";
        var legalAcceptances = new[] { "PrivacyNotice", "TermsOfUse" }.Select(documentType => new
        {
            documentType,
            version = Travela.Api.Legal.LegalDocuments.Version,
            accepted = true
        });
        var registered = await Json(await Send("register", new { fullName = "Ayşe <b>Öztürk</b>", email, password, confirmPassword = password, legalAcceptances }));
        var id = registered.GetProperty("userId").GetInt32();
        check(registered.GetProperty("requiresActivation").GetBoolean() && !registered.TryGetProperty("token", out _) && !registered.TryGetProperty("activationToken", out _),
            "Kayıt aktivasyon bekler; API oturum veya aktivasyon token'ı sızdırmaz");
        var token = await TestMailbox.Token(db, id);
        var saved = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
        check(saved.IsActive && saved.ActivationPending && saved.PasswordResetTokenHash != token && saved.PasswordHash != password,
            "Aktivasyon token'ı ve şifre veritabanında hash olarak tutulur");
        check((await Send("login", new { email, password })).StatusCode == HttpStatusCode.Unauthorized, "E-posta doğrulanmadan yeni hesap giriş yapamaz");
        var infoResponse = await Inspect(token);
        var info = await Json(infoResponse);
        check(info.GetProperty("purpose").GetString() == "activation" && !info.GetProperty("requiresPassword").GetBoolean() &&
            infoResponse.Headers.CacheControl?.NoStore == true && !info.TryGetProperty("email", out _), "Bağlantı kontrolü kayıt şifresinin korunacağını söyler; kişisel veri döndürmez ve cache edilmez");
        check((await Inspect(token)).IsSuccessStatusCode && await db.Users.Where(u => u.Id == id).Select(u => u.ActivationPending).SingleAsync(), "Bağlantının önizlenmesi aktivasyon token'ını tüketmez");
        var message = await db.AccountEmailMessages.AsNoTracking().Where(m => m.UserId == id).SingleAsync();
        var html = await File.ReadAllTextAsync(Path.Combine(TestMailbox.DirectoryPath, message.Id + ".html"));
        check(html.Contains("/activate-account#token=") && html.Contains("&lt;b&gt;") && !html.Contains("<b>Öztürk</b>") &&
            Directory.GetFiles(TestMailbox.DirectoryPath, "*.eml").Length > 0, "Yerel HTML ve EML e-postası doğru bağlantıyı taşır; kullanıcı metni HTML olarak çalışmaz");
        await Json(await Send("activate-account", new { token }));
        check((await Send("login", new { email, password })).IsSuccessStatusCode &&
            (await Send("activate-account", new { token })).StatusCode == HttpStatusCode.BadRequest, "Kayıt aktivasyonu mevcut şifreyi korur; aynı bağlantı ikinci kez kullanılamaz");

        var login = await Json(await Send("login", new { email, password }));
        var session = login.GetProperty("token").GetString()!;
        await db.AccountEmailMessages.Where(m => m.UserId == id).ExecuteUpdateAsync(update => update.SetProperty(m => m.CreatedAt, DateTime.UtcNow.AddMinutes(-2)));
        var request = await Send("forgot-password", new { email = " NEW@RECOVERY.EXAMPLE " });
        var unknown = await Send("forgot-password", new { email = "missing@recovery.example" });
        check(request.StatusCode == unknown.StatusCode && await request.Content.ReadAsStringAsync() == await unknown.Content.ReadAsStringAsync(), "Şifremi unuttum kayıtlı ve bilinmeyen e-posta için aynı yanıtı verir");
        var resetToken = await TestMailbox.Token(db, id);
        saved = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
        using var profile = new HttpRequestMessage(HttpMethod.Get, "/api/profile"); profile.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session);
        check(!saved.PasswordResetRequired && (await http.SendAsync(profile)).IsSuccessStatusCode && (await Send("login", new { email, password })).IsSuccessStatusCode,
            "Herkese açık sıfırlama talebi mevcut oturumu ve eski şifreyi kilitlemez");
        var count = await db.AccountEmailMessages.CountAsync(m => m.UserId == id);
        await Send("forgot-password", new { email });
        check(await db.AccountEmailMessages.CountAsync(m => m.UserId == id) == count, "Tekrarlanan e-posta isteklerinde bir dakikalık hesap bazlı bekleme uygulanır");
        check((await Json(await Inspect(resetToken))).GetProperty("purpose").GetString() == "reset", "Sıfırlama bağlantısı aktivasyon bağlantısından ayrılır");
        check((await Send("activate-account", new { token = resetToken })).StatusCode == HttpStatusCode.BadRequest, "Şifre sıfırlama bağlantısı hesap aktivasyonu yerine kullanılamaz");
        check((await Send("reset-password", new { token = resetToken, newPassword = replacement, confirmPassword = "Mismatch456!" })).StatusCode == HttpStatusCode.BadRequest,
            "Şifre sıfırlamada tekrar alanı sunucuda doğrulanır");
        await Json(await Send("reset-password", new { token = resetToken, newPassword = replacement, confirmPassword = replacement }));
        check((await Send("login", new { email, password })).StatusCode == HttpStatusCode.Unauthorized &&
            (await Send("login", new { email, password = replacement })).IsSuccessStatusCode && (await Inspect(resetToken)).StatusCode == HttpStatusCode.BadRequest,
            "E-postadaki bağlantıyla şifre yenilenir; eski şifre ve tüketilmiş bağlantı çalışmaz");

        var pending = new User { FullName = "Davetli", Email = "pending@recovery.example", ActivationPending = true };
        var blocked = new User { FullName = "Kapalı", Email = "blocked@recovery.example", IsActive = false, PasswordHash = saved.PasswordHash };
        db.AddRange(pending, blocked); await db.SaveChangesAsync();
        var denied = await Send("forgot-password", new { email = blocked.Email });
        check(await denied.Content.ReadAsStringAsync() == await unknown.Content.ReadAsStringAsync() && !await db.AccountEmailMessages.AnyAsync(m => m.UserId == blocked.Id),
            "Kapalı hesap için genel yanıt korunur; sıfırlama e-postası veya erişim açılmaz");
        await Send("resend-activation", new { email = pending.Email });
        var expiredToken = await TestMailbox.Token(db, pending.Id);
        check((await Json(await Inspect(expiredToken))).GetProperty("requiresPassword").GetBoolean() &&
            (await Send("activate-account", new { token = expiredToken })).StatusCode == HttpStatusCode.BadRequest, "Şifresiz davet aktivasyonda yeni şifre seçilmesini zorunlu kılar");
        await db.Users.Where(u => u.Id == pending.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.PasswordResetTokenExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        check((await Inspect(expiredToken)).StatusCode == HttpStatusCode.BadRequest, "Süresi dolmuş bağlantı doğrulama aşamasında reddedilir");
        await db.AccountEmailMessages.Where(m => m.UserId == pending.Id).ExecuteUpdateAsync(update => update.SetProperty(m => m.CreatedAt, DateTime.UtcNow.AddMinutes(-2)));
        await Send("resend-activation", new { email = pending.Email });
        var fresh = await TestMailbox.Token(db, pending.Id);
        check(fresh != expiredToken && (await Inspect(expiredToken)).StatusCode == HttpStatusCode.BadRequest, "Yeniden gönderim eski bağlantıyı geçersizleştirip yeni token üretir");
        check((await Send("activate-account", new { token = fresh, newPassword = replacement, confirmPassword = "Mismatch456!" })).StatusCode == HttpStatusCode.BadRequest,
            "Aktivasyonda şifre tekrarı sunucuda doğrulanır");
        await Json(await Send("activate-account", new { token = fresh, newPassword = replacement, confirmPassword = replacement }));
        check((await Send("login", new { pending.Email, password = replacement })).IsSuccessStatusCode, "Davetli yeni şifre belirleyerek hesabını aktifleştirip giriş yapar");

        // Test real delivery retry handling without contacting an external mail server.
        await db.AccountEmailMessages.Where(m => m.UserId == id).ExecuteUpdateAsync(update => update.SetProperty(m => m.CreatedAt, DateTime.UtcNow.AddMinutes(-2)));
        await Send("forgot-password", new { email });
        await TestMailbox.Token(db, id);
        saved = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
        var broken = new AccountEmailMessage { UserId = id, Recipient = email, Subject = "Test", ProtectedBody = "invalid-protected-body", TokenHash = saved.PasswordResetTokenHash!,
            CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow };
        db.Add(broken); await db.SaveChangesAsync();
        for (var i = 0; i < 100 && !await db.AccountEmailMessages.AsNoTracking().AnyAsync(m => m.Id == broken.Id && m.Attempts > 0 && m.LeaseToken == null); i++) await Task.Delay(100);
        var failed = await db.AccountEmailMessages.AsNoTracking().SingleAsync(m => m.Id == broken.Id);
        check(failed.Status == "Pending" && failed.Attempts == 1 && failed.NextAttemptAt > DateTime.UtcNow && failed.LastError != null,
            "E-posta gönderim hatası kalıcı kuyrukta artan beklemeyle yeniden denenir");
        await db.AccountEmailMessages.Where(m => m.Id == broken.Id).ExecuteUpdateAsync(update => update.SetProperty(m => m.Attempts, 7).SetProperty(m => m.NextAttemptAt, DateTime.UtcNow));
        for (var i = 0; i < 100 && !await db.AccountEmailMessages.AsNoTracking().AnyAsync(m => m.Id == broken.Id && m.Status == "Failed"); i++) await Task.Delay(100);
        check(await db.AccountEmailMessages.AsNoTracking().AnyAsync(m => m.Id == broken.Id && m.Attempts == 8 && m.Status == "Failed"), "Sekiz hatadan sonra e-posta kaydı kaybolmadan başarısız olarak tutulur");
        var mixed = new User { FullName = "Eski Karma Harfli Hesap", Email = "Mixed@Recovery.Example", PasswordHash = saved.PasswordHash };
        db.Add(mixed); await db.SaveChangesAsync();
        await Send("forgot-password", new { email = "mixed@recovery.example" });
        check(!string.IsNullOrEmpty(await TestMailbox.Token(db, mixed.Id)), "Eski kayıtlardaki büyük/küçük harfli e-postalara da kurtarma bağlantısı gönderilir");
        HttpStatusCode lastStatus = HttpStatusCode.OK;
        for (var i = 0; i < 1001 && lastStatus != HttpStatusCode.TooManyRequests; i++)
        {
            using var response = await Send("account-link", new { token = "" });
            lastStatus = response.StatusCode;
            if (lastStatus == HttpStatusCode.TooManyRequests)
                check(response.Headers.RetryAfter != null, "Bağlantı uçlarında gerçek IP deneme sınırı ve Retry-After başlığı uygulanır");
        }
        check(lastStatus == HttpStatusCode.TooManyRequests, "Hatalı bağlantı sorguları sınırsız denenemez");
    }
}
