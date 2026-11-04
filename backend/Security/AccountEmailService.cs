using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Travela.Api.Models;
using Travela.Data;
using Travela.Models;

namespace Travela.Api.Security;

public sealed class AccountEmailOptions
{
    public string DeliveryMode { get; set; } = "Pickup";
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
    public string PickupDirectory { get; set; } = "../tmp/account-mail";
    public string FromAddress { get; set; } = "noreply@travela.local";
    public string FromName { get; set; } = "Travela";
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUserName { get; set; }
    public string? SmtpPassword { get; set; }
    public int RequestLimitPerMinute { get; set; } = 20;

    public bool Valid(IHostEnvironment env) => Uri.TryCreate(FrontendBaseUrl, UriKind.Absolute, out var uri) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
        (uri.Scheme == "https" || ((env.IsDevelopment() || env.IsEnvironment("Testing")) && uri.Scheme == "http" && uri.IsLoopback)) &&
        System.Net.Mail.MailAddress.TryCreate(FromAddress, out _) && RequestLimitPerMinute is >= 1 and <= 1000 &&
        ((DeliveryMode == "Pickup" && (env.IsDevelopment() || env.IsEnvironment("Testing")) && !string.IsNullOrWhiteSpace(PickupDirectory)) ||
         (DeliveryMode == "Smtp" && !string.IsNullOrWhiteSpace(SmtpHost) && SmtpPort is >= 1 and <= 65535));
}

public sealed class AccountEmailService(TravelaDbContext db, IDataProtectionProvider protection, IOptions<AccountEmailOptions> options, TimeProvider clock)
{
    public const string RequestMessage = "Bilgileriniz uygun bir hesapla eşleşiyorsa e-posta bağlantısı gönderilecektir. Gelen kutunuzu ve gereksiz e-posta klasörünü kontrol edin.";
    public static string CreateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public IDataProtector Protector => protection.CreateProtector("Travela.AccountEmail.Body.v1");

    // Add to the caller's transaction alongside the account/token change.
    public void Queue(User user, string token, DateTime expiresAt)
    {
        var activation = user.ActivationPending;
        var title = activation ? "Travela hesabınızı aktifleştirin" : "Travela şifrenizi yenileyin";
        var path = activation ? "/activate-account" : "/reset-password";
        var url = options.Value.FrontendBaseUrl.TrimEnd('/') + path + "#token=" + Uri.EscapeDataString(token);
        var html = $"<!doctype html><html lang=\"tr\"><head><meta charset=\"utf-8\"><meta name=\"referrer\" content=\"no-referrer\"><title>{title}</title></head>" +
            $"<body><h1>{title}</h1><p>Merhaba {WebUtility.HtmlEncode(user.FullName)},</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(url)}\">{(activation ? "Hesabımı aktifleştir" : "Yeni şifre belirle")}</a></p>" +
            $"<p>Bağlantı {expiresAt.AddHours(3):dd.MM.yyyy HH:mm} Türkiye saatine kadar geçerlidir ve bir kez kullanılabilir.</p>" +
            "<p>Bu işlemi siz istemediyseniz bu e-postayı yok sayabilirsiniz. Şifreniz bu bağlantı kullanılmadan değiştirilmez.</p></body></html>";
        var now = clock.GetUtcNow().UtcDateTime;
        db.AccountEmailMessages.Add(new AccountEmailMessage { User = user, Recipient = user.Email, Subject = title,
            ProtectedBody = Protector.Protect(html), TokenHash = HashToken(token), CreatedAt = now, NextAttemptAt = now });
    }

    public async Task Request(string email, bool activation, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var id = await db.Users.AsNoTracking().EnabledAccounts().WithActiveCompany(db)
            .Where(u => u.Email == normalized && u.ActivationPending == activation).Select(u => (int?)u.Id).SingleOrDefaultAsync(ct);
        if (id == null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id.Value}")
            .EnabledAccounts().WithActiveCompany(db).SingleOrDefaultAsync(ct);
        if (user == null || !string.Equals(user.Email.Trim(), normalized, StringComparison.OrdinalIgnoreCase) || user.ActivationPending != activation) return;
        var now = clock.GetUtcNow().UtcDateTime;
        if (await db.AccountEmailMessages.AnyAsync(m => m.UserId == user.Id && m.CreatedAt >= now.AddMinutes(-1), ct)) return;
        var token = CreateToken();
        user.PasswordResetTokenHash = HashToken(token);
        user.PasswordResetTokenExpiresAt = now.AddHours(activation ? 48 : 1);
        user.PasswordResetTokenUsedAt = null;
        user.UpdatedAt = now;
        // A public request never locks out a user or revokes their sessions.
        Queue(user, token, user.PasswordResetTokenExpiresAt.Value);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
