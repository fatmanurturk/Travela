using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

namespace Travela.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly TravelaDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(
            TravelaDbContext context,
            IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("register")]
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
                MustChangePassword = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var passwordHasher = new PasswordHasher<User>();
            user.PasswordHash = passwordHasher.HashPassword(user, dto.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return StatusCode(201, new
            {
                message = "Kullanıcı başarıyla kaydedildi.",
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

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                return Unauthorized(new { message = "E-posta veya şifre hatalı." });
            }

            if (!user.IsActive)
            {
                return Unauthorized(new { message = "Hesabınız pasif olduğu için giriş yapamazsınız." });
            }

            if (user.Role == "Company")
            {
                if (!user.CompanyId.HasValue)
                {
                    return Unauthorized(new { message = "Firma hesabı için firma bilgisi eksik." });
                }

                var companyExists = await _context.Companies.AnyAsync(company => company.Id == user.CompanyId.Value && company.IsActive);
                if (!companyExists)
                {
                    return Unauthorized(new { message = "Firma hesabı pasif veya bulunamadı." });
                }
            }

            var passwordHasher = new PasswordHasher<User>();
            var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
            if (result == PasswordVerificationResult.Failed)
            {
                return Unauthorized(new { message = "E-posta veya şifre hatalı." });
            }

            user.LastLoginAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

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

            var email = dto.Email.Trim();
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

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
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

            user.PasswordHash = passwordHasher.HashPassword(user, dto.NewPassword);
            user.MustChangePassword = false;
            user.UpdatedAt = DateTime.UtcNow;
            user.SecurityVersion += 1;
            await _context.SaveChangesAsync();

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

            var users = await _context.Users
                .Where(u => u.PasswordResetTokenHash != null && u.PasswordResetTokenUsedAt == null)
                .ToListAsync();

            var user = users.FirstOrDefault(u =>
                u.PasswordResetTokenExpiresAt.HasValue &&
                u.PasswordResetTokenExpiresAt.Value > DateTime.UtcNow &&
                HashToken(dto.Token) == u.PasswordResetTokenHash);

            if (user == null)
            {
                return BadRequest(new { message = "Şifre sıfırlama tokenı geçersiz veya süresi dolmuş." });
            }

            var passwordHasher = new PasswordHasher<User>();
            user.PasswordHash = passwordHasher.HashPassword(user, dto.NewPassword);
            user.PasswordResetTokenHash = null;
            user.PasswordResetTokenExpiresAt = null;
            user.PasswordResetTokenUsedAt = DateTime.UtcNow;
            user.IsActive = true;
            user.SecurityVersion += 1;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Şifre başarıyla güncellendi." });
        }

        private string GenerateJwtToken(User user)
        {
            var jwtKey = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("JWT Key bulunamadı.");

            var jwtIssuer = _configuration["Jwt:Issuer"]
                ?? throw new InvalidOperationException("JWT Issuer bulunamadı.");

            var jwtAudience = _configuration["Jwt:Audience"]
                ?? throw new InvalidOperationException("JWT Audience bulunamadı.");

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("security_version", user.SecurityVersion.ToString()),
                new Claim("must_change_password", user.MustChangePassword.ToString())
            };

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

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