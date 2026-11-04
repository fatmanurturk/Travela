using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Security;
using Travela.Data;
using Travela.Models;

namespace Travela.API.Controllers;

public sealed class UpdateCompanyUserDto
{
    [Required, MaxLength(120)] public string FullName { get; set; } = "";
}

[ApiController, Route("api/company/user"), Authorize(Roles = "Company")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class CompanyUserController(TravelaDbContext db, SessionTokenService tokens, TimeProvider clock) : ControllerBase
{
    private static readonly ConcurrentDictionary<int, Queue<DateTime>> PasswordAttempts = new();

    // Both the user and their current company must be active. Caller-supplied ids are never used.
    private IQueryable<User> ActiveCompanyUsers() => db.Users.ForSession(User, db).Where(u => u.Role == "Company");

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = await ActiveCompanyUsers().AsNoTracking().Select(u => new
        {
            u.Id, u.FullName, u.Email, u.Role, u.CompanyId,
            CompanyName = db.Companies.Where(c => c.Id == u.CompanyId).Select(c => c.Name).FirstOrDefault()
        }).SingleOrDefaultAsync(ct);
        return user == null ? Unauthorized() : Ok(user);
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateCompanyUserDto dto, CancellationToken ct)
    {
        var name = dto.FullName.Trim();
        if (name.Length == 0) return BadRequest(new { message = "Ad soyad boş olamaz." });
        var user = await ActiveCompanyUsers().AsNoTracking().SingleOrDefaultAsync(ct);
        if (user == null) return Unauthorized();
        var affected = await ActiveCompanyUsers().Where(u => u.SecurityVersion == user.SecurityVersion && u.CompanyId == user.CompanyId)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.FullName, name)
                .SetProperty(u => u.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);
        if (affected != 1) return Unauthorized();
        return Ok(new { message = "Ad soyad güncellendi.", fullName = name });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto, CancellationToken ct)
    {
        var user = await ActiveCompanyUsers().AsNoTracking().SingleOrDefaultAsync(ct);
        if (user == null) return Unauthorized();
        if (RateLimited(user.Id)) return StatusCode(429, new { message = "Çok fazla güvenlik işlemi denendi. Lütfen 5 dakika sonra tekrar deneyin." });
        if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword == dto.CurrentPassword)
            return BadRequest(new { message = "Yeni şifre mevcut şifreden farklı olmalıdır." });
        var hasher = new PasswordHasher<User>();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, dto.CurrentPassword) == PasswordVerificationResult.Failed)
            return BadRequest(new { message = "Mevcut şifre yanlış." });
        var hash = hasher.HashPassword(user, dto.NewPassword);
        var affected = await ActiveCompanyUsers().Where(u => u.SecurityVersion == user.SecurityVersion &&
                u.PasswordHash == user.PasswordHash && u.CompanyId == user.CompanyId)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.PasswordHash, hash)
                .SetProperty(u => u.SecurityVersion, u => u.SecurityVersion + 1)
                .SetProperty(u => u.UpdatedAt, clock.GetUtcNow().UtcDateTime)
                .SetProperty(u => u.PasswordResetTokenHash, (string?)null)
                .SetProperty(u => u.PasswordResetTokenExpiresAt, (DateTime?)null)
                .SetProperty(u => u.PasswordResetTokenUsedAt, (DateTime?)null), ct);
        if (affected != 1) return Unauthorized();
        user.SecurityVersion++;
        DateTime? expiry = long.TryParse(User.FindFirstValue("exp"), out var exp)
            ? DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime : null;
        return Ok(new { message = "Şifreniz değiştirildi ve diğer oturumlar kapatıldı.", token = tokens.Issue(user, expiry) });
    }

    private bool RateLimited(int userId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var queue = PasswordAttempts.GetOrAdd(userId, _ => new());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromMinutes(5)) queue.Dequeue();
            if (queue.Count >= 5) return true;
            queue.Enqueue(now);
            return false;
        }
    }
}
