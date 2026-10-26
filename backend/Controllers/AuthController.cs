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
                IsActive = true,
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

            var passwordHasher = new PasswordHasher<User>();
            var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
            if (result == PasswordVerificationResult.Failed)
            {
                return Unauthorized(new { message = "E-posta veya şifre hatalı." });
            }

            user.LastLoginAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            string token = GenerateJwtToken(user);

            return Ok(new
            {
                message = "Giriş başarılı.",
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
                new Claim("security_version", user.SecurityVersion.ToString())
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

        private static string HashToken(string value)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes);
        }
    }
}