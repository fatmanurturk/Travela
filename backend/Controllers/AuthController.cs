using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;
using Travela.Api.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace Travela.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public class AuthController : ControllerBase
    {
        private readonly TravelaDbContext _context;
        private readonly Travela.Api.Security.SessionTokenService _tokens;
        private readonly AccountEmailService _mail;

        public AuthController(
            TravelaDbContext context,
            Travela.Api.Security.SessionTokenService tokens, AccountEmailService mail)
        {
            _context = context;
            _tokens = tokens;
            _mail = mail;
        }

        [HttpPost("register")]
        [EnableRateLimiting("account-links")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Kayıt bilgileri gerekli." });
            }

            if (!string.Equals(dto.Password, dto.ConfirmPassword, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Şifre ile tekrarı eşleşmiyor." });
            }

            string email = dto.Email.Trim().ToLowerInvariant();

            bool userExists = await _context.Users.AnyAsync(u => u.Email == email);
            if (userExists)
            {
                return Conflict(new { message = "Bu e-posta adresi zaten kayıtlı." });
            }

            var user = new User
            {
                FullName = dto.FullName.Trim(),
                Email = email,
                Role = "Passenger",
                CompanyId = null,
                IsActive = true,
                ActivationPending = true,
                MustChangePassword = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var passwordHasher = new PasswordHasher<User>();
            user.PasswordHash = passwordHasher.HashPassword(user, dto.Password);
            var activationToken = AccountEmailService.CreateToken();
            user.PasswordResetTokenHash = AccountEmailService.HashToken(activationToken);
            user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddDays(2);

            _context.Users.Add(user);
            _mail.Queue(user, activationToken, user.PasswordResetTokenExpiresAt.Value);
            await _context.SaveChangesAsync();

            return StatusCode(201, new
            {
                message = "Hesabınız oluşturuldu. Giriş yapmadan önce e-posta adresinize gönderilen aktivasyon bağlantısını açın.",
                requiresActivation = true,
                userId = user.Id,
                fullName = user.FullName,
                email = user.Email,
                role = user.Role
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            string email = dto.Email.Trim().ToLowerInvariant();

            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                return Unauthorized(new { message = "E-posta veya şifre hatalı." });
            }

            if (!await _context.Users.PasswordLoginAccounts(_context).AnyAsync(u => u.Id == user.Id && u.SecurityVersion == user.SecurityVersion))
            {
                return Unauthorized(new { message = "Hesap veya firma erişime kapalı ya da hesap aktivasyonu/şifre sıfırlama bekleniyor." });
            }

            var passwordHasher = new PasswordHasher<User>();
            var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
            if (result == PasswordVerificationResult.Failed)
            {
                return Unauthorized(new { message = "E-posta veya şifre hatalı." });
            }

            var affected = await _context.Users.PasswordLoginAccounts(_context)
                .Where(u => u.Id == user.Id && u.SecurityVersion == user.SecurityVersion && u.PasswordHash == user.PasswordHash &&
                    u.Role == user.Role && u.CompanyId == user.CompanyId && u.MustChangePassword == user.MustChangePassword)
                .ExecuteUpdateAsync(update => update.SetProperty(u => u.LastLoginAt, DateTime.UtcNow).SetProperty(u => u.UpdatedAt, DateTime.UtcNow));
            if (affected != 1) return Unauthorized(new { message = "Hesap durumu değişti. Lütfen yeniden giriş yapınız." });

            if (user.MustChangePassword)
            {
                return Ok(new
                {
                    message = "İlk girişte şifrenizi değiştirmelisiniz.",
                    requiresPasswordChange = true,
                    user = new
                    {
                        user.Id,
                        user.FullName,
                        user.Email,
                        user.Role,
                        user.CompanyId,
                        user.IsActive,
                        user.MustChangePassword
                    }
                });
            }

            string token = GenerateJwtToken(user);

            return Ok(new
            {
                message = "Giriş başarılı.",
                token,
                tokenType = "Bearer",
                expiresIn = 7200,
                requiresPasswordChange = false,
                user = new
                {
                    user.Id,
                    user.FullName,
                    user.Email,
                    user.Role,
                    user.CompanyId,
                    user.IsActive
                }
            });
        }

        [HttpPost("change-password")]
        [AllowAnonymous]
        public async Task<IActionResult> FirstLoginPasswordChange([FromBody] FirstLoginPasswordChangeDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Şifre bilgileri gerekli." });
            }

            var email = dto.Email.Trim().ToLowerInvariant();
            if (!IsValidEmail(email))
            {
                return BadRequest(new { message = "Geçerli bir e-posta adresi giriniz." });
            }

            if (string.IsNullOrWhiteSpace(dto.CurrentPassword) ||
                string.IsNullOrWhiteSpace(dto.NewPassword) ||
                string.IsNullOrWhiteSpace(dto.ConfirmPassword))
            {
                return BadRequest(new { message = "Mevcut şifre, yeni şifre ve tekrarı zorunludur." });
            }

            if (dto.NewPassword.Length < 8)
            {
                return BadRequest(new { message = "Yeni şifre en az 8 karakter olmalıdır." });
            }

            if (!string.Equals(dto.NewPassword, dto.ConfirmPassword, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Yeni şifre ile tekrarı eşleşmiyor." });
            }

            var user = await _context.Users.AsNoTracking().PasswordLoginAccounts(_context).FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                return Unauthorized(new { message = "Kullanıcı bulunamadı." });
            }

            if (!user.MustChangePassword)
            {
                return BadRequest(new { message = "Bu hesaba şifre değişimi uygulanamaz." });
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

            var hash = passwordHasher.HashPassword(user, dto.NewPassword);
            var affected = await _context.Users.PasswordLoginAccounts(_context)
                .Where(u => u.Id == user.Id && u.SecurityVersion == user.SecurityVersion && u.PasswordHash == user.PasswordHash &&
                    u.MustChangePassword && u.Role == user.Role && u.CompanyId == user.CompanyId)
                .ExecuteUpdateAsync(update => update.SetProperty(u => u.PasswordHash, hash).SetProperty(u => u.MustChangePassword, false)
                    .SetProperty(u => u.SecurityVersion, u => u.SecurityVersion + 1).SetProperty(u => u.UpdatedAt, DateTime.UtcNow)
                    .SetProperty(u => u.PasswordResetTokenHash, (string?)null).SetProperty(u => u.PasswordResetTokenExpiresAt, (DateTime?)null)
                    .SetProperty(u => u.PasswordResetTokenUsedAt, (DateTime?)null));
            if (affected != 1) return Unauthorized(new { message = "Hesap durumu değişti. Lütfen yeniden giriş yapınız." });
            user.MustChangePassword = false;
            user.SecurityVersion += 1;

            var token = GenerateJwtToken(user);

            return Ok(new
            {
                message = "Şifre başarıyla değiştirildi. Giriş yapılıyor.",
                token,
                tokenType = "Bearer",
                expiresIn = 7200,
                user = new
                {
                    user.Id,
                    user.FullName,
                    user.Email,
                    user.Role,
                    user.CompanyId,
                    user.IsActive
                }
            });
        }

        [HttpPost("reset-password")]
        [EnableRateLimiting("account-links")]
        public async Task<IActionResult> ResetPassword([FromBody] PasswordResetRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.NewPassword))
            {
                return BadRequest(new { message = "Token ve yeni şifre zorunludur." });
            }

            if (dto.NewPassword.Length < 8)
            {
                return BadRequest(new { message = "Yeni şifre en az 8 karakter olmalıdır." });
            }

            var tokenHash = HashToken(dto.Token);
            var now = DateTime.UtcNow;
            var eligible = _context.Users.EnabledAccounts().WithActiveCompany(_context)
                .Where(u => u.PasswordResetTokenHash == tokenHash && u.PasswordResetTokenUsedAt == null && u.PasswordResetTokenExpiresAt > now);
            var user = await eligible.AsNoTracking().SingleOrDefaultAsync();

            if (user == null)
            {
                return BadRequest(new { message = "Şifre sıfırlama tokenı geçersiz veya süresi dolmuş." });
            }

            var passwordHasher = new PasswordHasher<User>();
            var hash = passwordHasher.HashPassword(user, dto.NewPassword);
            var affected = await eligible.Where(u => u.Id == user.Id && u.SecurityVersion == user.SecurityVersion &&
                    u.PasswordHash == user.PasswordHash && u.Role == user.Role && u.CompanyId == user.CompanyId && u.PasswordResetTokenExpiresAt > DateTime.UtcNow)
                .ExecuteUpdateAsync(update => update.SetProperty(u => u.PasswordHash, hash)
                    .SetProperty(u => u.PasswordResetTokenHash, (string?)null).SetProperty(u => u.PasswordResetTokenExpiresAt, (DateTime?)null)
                    .SetProperty(u => u.PasswordResetTokenUsedAt, DateTime.UtcNow).SetProperty(u => u.ActivationPending, false)
                    .SetProperty(u => u.PasswordResetRequired, false).SetProperty(u => u.MustChangePassword, false)
                    .SetProperty(u => u.SecurityVersion, u => u.SecurityVersion + 1).SetProperty(u => u.UpdatedAt, DateTime.UtcNow));
            if (affected != 1) return BadRequest(new { message = "Şifre sıfırlama tokenı geçersiz, kullanılmış veya hesap durumu değişmiş." });

            return Ok(new { message = "Şifre başarıyla güncellendi." });
        }

        private string GenerateJwtToken(User user) => _tokens.Issue(user);

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            try
            {
                var mailAddress = new System.Net.Mail.MailAddress(email);
                return mailAddress.Address == email.Trim();
            }
            catch
            {
                return false;
            }
        }

        private static string HashToken(string value)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes);
        }
    }
}
