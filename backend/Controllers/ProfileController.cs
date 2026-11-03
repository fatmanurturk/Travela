using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Security;
using Travela.Data;
using Travela.Models;

namespace Travela.API.Controllers;

public sealed class UpdateUserProfileDto
{
    [Required, MaxLength(120)] public string FullName { get; set; } = "";
    [MaxLength(32)] public string? PhoneNumber { get; set; }
}
public sealed class ChangePasswordDto
{
    [Required, MaxLength(256)] public string CurrentPassword { get; set; } = "";
    [Required, MinLength(8), MaxLength(128)] public string NewPassword { get; set; } = "";
    [Required, Compare(nameof(NewPassword))] public string ConfirmPassword { get; set; } = "";
}
public sealed class NotificationPreferencesDto
{
    [Required] public bool? TripReminders { get; set; }
    [Required] public bool? Cancellations { get; set; }
    [Required] public bool? RefundUpdates { get; set; }
}
public sealed class RevokeOtherSessionsDto
{
    [Required, MaxLength(256)] public string CurrentPassword { get; set; } = "";
}

[ApiController, Route("api/profile"), Authorize(Roles = "Passenger")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public partial class ProfileController(TravelaDbContext db, SessionTokenService tokens, TimeProvider clock) : ControllerBase
{
    private static readonly ConcurrentDictionary<int, Queue<DateTime>> SecurityAttempts = new();
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;
    private Task<User?> CurrentUser(CancellationToken ct) => db.Users.AsNoTracking()
        .SingleOrDefaultAsync(u => u.Id == UserId && u.IsActive && u.Role == "Passenger" && !u.MustChangePassword, ct);
    private static object Preferences(User user) => new { tripReminders = user.TripRemindersEnabled, cancellations = user.CancellationNotificationsEnabled, refundUpdates = user.RefundNotificationsEnabled };

    [HttpGet]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var user = await CurrentUser(ct); if (user == null) return Unauthorized();
        return Ok(new { user.Id, user.FullName, user.Email, user.PhoneNumber, user.Role,
            CreatedAt = Utc(user.CreatedAt), LastLoginAt = user.LastLoginAt.HasValue ? Utc(user.LastLoginAt.Value) : (DateTime?)null,
            notificationPreferences = Preferences(user) });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile(UpdateUserProfileDto dto, CancellationToken ct)
    {
        var name = dto.FullName.Trim();
        if (name.Length == 0) return BadRequest(new { message = "Ad soyad boş olamaz." });
        if (!TryPhone(dto.PhoneNumber, out var phone)) return BadRequest(new { message = "Geçerli bir telefon numarası girin. Örnek: 05xx xxx xx xx veya +ülke kodu ve numara." });
        var user = await CurrentUser(ct); if (user == null) return Unauthorized();
        var affected = await db.Users.Where(u => u.Id == user.Id && u.SecurityVersion == user.SecurityVersion && u.IsActive)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.FullName, name).SetProperty(u => u.PhoneNumber, phone).SetProperty(u => u.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);
        if (affected != 1) return Unauthorized();
        return Ok(new { message = "Kişisel bilgilerin güncellendi.", fullName = name, phoneNumber = phone });
    }

    [HttpPut("notification-preferences")]
    public async Task<IActionResult> UpdatePreferences(NotificationPreferencesDto dto, CancellationToken ct)
    {
        if (dto.TripReminders is not bool reminders || dto.Cancellations is not bool cancellations || dto.RefundUpdates is not bool refunds)
            return BadRequest(new { message = "Tüm bildirim tercihlerini belirtin." });
        var user = await CurrentUser(ct); if (user == null) return Unauthorized();
        var affected = await db.Users.Where(u => u.Id == user.Id && u.SecurityVersion == user.SecurityVersion && u.IsActive)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.TripRemindersEnabled, reminders)
                .SetProperty(u => u.CancellationNotificationsEnabled, cancellations)
                .SetProperty(u => u.RefundNotificationsEnabled, refunds)
                .SetProperty(u => u.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);
        if (affected != 1) return Unauthorized();
        return Ok(new { message = "Bildirim tercihlerin kaydedildi.", notificationPreferences = new { tripReminders = reminders, cancellations = cancellations, refundUpdates = refunds } });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto, CancellationToken ct)
    {
        var user = await CurrentUser(ct); if (user == null) return Unauthorized();
        if (RateLimited(user.Id)) return StatusCode(429, new { message = "Çok fazla güvenlik işlemi denendi. Lütfen 5 dakika sonra tekrar deneyin." });
        if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword == dto.CurrentPassword)
            return BadRequest(new { message = "Yeni şifre mevcut şifreden farklı olmalıdır." });
        var hasher = new PasswordHasher<User>();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, dto.CurrentPassword) == PasswordVerificationResult.Failed)
            return BadRequest(new { message = "Mevcut şifre yanlış." });
        var hash = hasher.HashPassword(user, dto.NewPassword);
        var affected = await db.Users.Where(u => u.Id == user.Id && u.SecurityVersion == user.SecurityVersion && u.PasswordHash == user.PasswordHash && u.IsActive)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.PasswordHash, hash).SetProperty(u => u.SecurityVersion, u => u.SecurityVersion + 1)
                .SetProperty(u => u.MustChangePassword, false).SetProperty(u => u.UpdatedAt, clock.GetUtcNow().UtcDateTime)
                .SetProperty(u => u.PasswordResetTokenHash, (string?)null).SetProperty(u => u.PasswordResetTokenExpiresAt, (DateTime?)null).SetProperty(u => u.PasswordResetTokenUsedAt, (DateTime?)null), ct);
        if (affected != 1) return Unauthorized();
        user.SecurityVersion++; user.MustChangePassword = false;
        return Ok(new { message = "Şifren değiştirildi ve diğer oturumlar kapatıldı.", token = tokens.Issue(user, CurrentExpiry()) });
    }

    [HttpPost("revoke-other-sessions")]
    public async Task<IActionResult> RevokeOtherSessions(RevokeOtherSessionsDto dto, CancellationToken ct)
    {
        var user = await CurrentUser(ct); if (user == null) return Unauthorized();
        if (RateLimited(user.Id)) return StatusCode(429, new { message = "Çok fazla güvenlik işlemi denendi. Lütfen 5 dakika sonra tekrar deneyin." });
        if (new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, dto.CurrentPassword) == PasswordVerificationResult.Failed)
            return BadRequest(new { message = "Mevcut şifre yanlış." });
        var affected = await db.Users.Where(u => u.Id == user.Id && u.SecurityVersion == user.SecurityVersion && u.IsActive)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.SecurityVersion, u => u.SecurityVersion + 1).SetProperty(u => u.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);
        if (affected != 1) return Unauthorized();
        user.SecurityVersion++;
        return Ok(new { message = "Diğer oturumlar kapatıldı. Bu oturum açık kalacak.", token = tokens.Issue(user, CurrentExpiry()) });
    }

    private DateTime? CurrentExpiry() => long.TryParse(User.FindFirstValue("exp"), out var exp) ? DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime : null;
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    public static bool TryPhone(string? value, out string? phone)
    {
        phone = null; if (string.IsNullOrWhiteSpace(value)) return true;
        if (value.Length > 32 || Regex.IsMatch(value, @"[^0-9+ ().-]")) return false;
        var digits = Regex.Replace(value, @"[ ().-]", "");
        if (Regex.IsMatch(digits, @"^0[2-5][0-9]{9}$")) digits = "+90" + digits[1..];
        else if (Regex.IsMatch(digits, @"^[2-5][0-9]{9}$")) digits = "+90" + digits;
        else if (Regex.IsMatch(digits, @"^90[2-5][0-9]{9}$")) digits = "+" + digits;
        if (!Regex.IsMatch(digits, @"^\+[1-9][0-9]{7,14}$")) return false;
        if (digits.StartsWith("+90") && !Regex.IsMatch(digits, @"^\+90[2-5][0-9]{9}$")) return false;
        phone = digits; return true;
    }
    private bool RateLimited(int userId)
    {
        var now = clock.GetUtcNow().UtcDateTime; var queue = SecurityAttempts.GetOrAdd(userId, _ => new());
        lock (queue) {
            while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromMinutes(5)) queue.Dequeue();
            if (queue.Count >= 5) return true;
            queue.Enqueue(now); return false;
        }
    }
}
