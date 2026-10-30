using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Data;
using Travela.Models;

namespace Travela.API.Controllers
{
    public class UpdateUserProfileDto
    {
        public string FullName { get; set; } = string.Empty;
    }

    public class ChangePasswordDto
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("api/profile")]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private static readonly ConcurrentDictionary<int, Queue<DateTime>> PasswordChangeAttempts = new();
        private readonly TravelaDbContext _context;

        public ProfileController(TravelaDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            string? companyName = null;
            if (user.CompanyId.HasValue)
            {
                companyName = await _context.Companies
                    .AsNoTracking()
                    .Where(company => company.Id == user.CompanyId.Value)
                    .Select(company => company.Name)
                    .FirstOrDefaultAsync();
            }

            return Ok(new
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                CompanyId = user.CompanyId,
                CompanyName = companyName
            });
        }

        [HttpPut]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserProfileDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Profil verisi gerekli." });
            }

            var fullName = dto.FullName?.Trim();
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return BadRequest(new { message = "Ad soyad boş olamaz." });
            }

            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            user.FullName = fullName;
            user.UpdatedAt = DateTime.UtcNow;
            user.SecurityVersion += 1;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Profil bilgileri güncellendi.",
                fullName = user.FullName
            });
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Şifre bilgileri gerekli." });
            }

            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            if (IsRateLimited(user.Id))
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    message = "Çok fazla şifre değiştirme denemesi yapıldı. Lütfen 5 dakika sonra tekrar deneyin."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.CurrentPassword) ||
                string.IsNullOrWhiteSpace(dto.NewPassword) ||
                string.IsNullOrWhiteSpace(dto.ConfirmPassword))
            {
                return BadRequest(new { message = "Mevcut şifre, yeni şifre ve onay alanları zorunludur." });
            }

            if (dto.NewPassword.Length < 8)
            {
                return BadRequest(new { message = "Yeni şifre en az 8 karakter olmalıdır." });
            }

            if (!string.Equals(dto.NewPassword, dto.ConfirmPassword, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Yeni şifre ile tekrar alanı eşleşmiyor." });
            }

            var passwordHasher = new PasswordHasher<User>();
            var verificationResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.CurrentPassword);
            if (verificationResult == PasswordVerificationResult.Failed)
            {
                return Unauthorized(new { message = "Mevcut şifre yanlış." });
            }

            if (string.Equals(dto.CurrentPassword, dto.NewPassword, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Yeni şifre mevcut şifre ile aynı olamaz." });
            }

            user.PasswordHash = passwordHasher.HashPassword(user, dto.NewPassword);
            user.MustChangePassword = false;
            user.UpdatedAt = DateTime.UtcNow;
            user.SecurityVersion += 1;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Şifre başarıyla güncellendi." });
        }

        private async Task<User?> GetCurrentUserAsync()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return null;
            }

            return await _context.Users.FirstOrDefaultAsync(user => user.Id == userId);
        }

        private static bool IsRateLimited(int userId)
        {
            var now = DateTime.UtcNow;
            var queue = PasswordChangeAttempts.GetOrAdd(userId, _ => new Queue<DateTime>());

            lock (queue)
            {
                while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromMinutes(5))
                {
                    queue.Dequeue();
                }

                if (queue.Count >= 5)
                {
                    return true;
                }

                queue.Enqueue(now);
                return false;
            }
        }
    }
}