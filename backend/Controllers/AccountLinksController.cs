using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Security;
using Travela.Data;
using Travela.Models;

namespace Travela.API.Controllers;

public sealed class EmailLinkRequestDto
{
    [Required, EmailAddress, MaxLength(254)] public string Email { get; set; } = "";
}
public class AccountLinkDto
{
    [Required, MaxLength(256)] public string Token { get; set; } = "";
}
public sealed class ActivateAccountDto : AccountLinkDto
{
    [MinLength(8), MaxLength(128)] public string? NewPassword { get; set; }
    [Compare(nameof(NewPassword))] public string? ConfirmPassword { get; set; }
}

[ApiController, Route("api/auth"), AllowAnonymous, EnableRateLimiting("account-links")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AccountLinksController(TravelaDbContext db, AccountEmailService mail, TimeProvider clock) : ControllerBase
{
    [HttpPost("forgot-password")]
    public async Task<IActionResult> Forgot(EmailLinkRequestDto dto, CancellationToken ct)
    {
        await mail.Request(dto.Email, false, ct);
        return Ok(new { message = AccountEmailService.RequestMessage });
    }
    [HttpPost("resend-activation")]
    public async Task<IActionResult> Resend(EmailLinkRequestDto dto, CancellationToken ct)
    {
        await mail.Request(dto.Email, true, ct);
        return Ok(new { message = AccountEmailService.RequestMessage });
    }

    private IQueryable<User> ValidLink(string token)
    {
        var hash = AccountEmailService.HashToken(token);
        var now = clock.GetUtcNow().UtcDateTime;
        return db.Users.EnabledAccounts().WithActiveCompany(db).Where(u => u.PasswordResetTokenHash == hash &&
            u.PasswordResetTokenUsedAt == null && u.PasswordResetTokenExpiresAt > now);
    }

    [HttpPost("account-link")]
    public async Task<IActionResult> Inspect(AccountLinkDto dto, CancellationToken ct)
    {
        var user = await ValidLink(dto.Token).AsNoTracking().Select(u => new { u.ActivationPending, u.PasswordHash }).SingleOrDefaultAsync(ct);
        return user == null ? BadRequest(new { message = "Bağlantı geçersiz, kullanılmış veya süresi dolmuş. Yeni bir bağlantı isteyin." }) :
            Ok(new { purpose = user.ActivationPending ? "activation" : "reset", requiresPassword = !user.ActivationPending || user.PasswordHash == "" });
    }

    [HttpPost("activate-account")]
    public async Task<IActionResult> Activate(ActivateAccountDto dto, CancellationToken ct)
    {
        var query = ValidLink(dto.Token).Where(u => u.ActivationPending);
        var user = await query.AsNoTracking().SingleOrDefaultAsync(ct);
        if (user == null) return BadRequest(new { message = "Aktivasyon bağlantısı geçersiz, kullanılmış veya süresi dolmuş." });
        if (user.PasswordHash == "" && string.IsNullOrWhiteSpace(dto.NewPassword))
            return BadRequest(new { message = "Hesabınız için yeni bir şifre belirleyin." });
        if (dto.NewPassword != null && (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.ConfirmPassword == null))
            return BadRequest(new { message = "Yeni şifreyi ve tekrarını girin." });
        var hash = dto.NewPassword == null ? user.PasswordHash : new PasswordHasher<User>().HashPassword(user, dto.NewPassword);
        var affected = await query.Where(u => u.Id == user.Id && u.SecurityVersion == user.SecurityVersion && u.PasswordHash == user.PasswordHash &&
                u.PasswordResetTokenExpiresAt > clock.GetUtcNow().UtcDateTime)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.PasswordHash, hash).SetProperty(u => u.ActivationPending, false)
                .SetProperty(u => u.PasswordResetRequired, false).SetProperty(u => u.MustChangePassword, false)
                .SetProperty(u => u.PasswordResetTokenHash, (string?)null).SetProperty(u => u.PasswordResetTokenExpiresAt, (DateTime?)null)
                .SetProperty(u => u.PasswordResetTokenUsedAt, clock.GetUtcNow().UtcDateTime)
                .SetProperty(u => u.SecurityVersion, u => u.SecurityVersion + 1).SetProperty(u => u.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);
        return affected == 1 ? Ok(new { message = "Hesabınız aktifleştirildi. Giriş yapabilirsiniz." }) :
            BadRequest(new { message = "Bağlantı artık geçerli değil. Yeni bir bağlantı isteyin." });
    }
}
