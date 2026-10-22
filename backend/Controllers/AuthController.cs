using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

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
    // Aynı e-posta adresiyle kayıt var mı?
    string email = dto.Email.Trim().ToLowerInvariant();

    bool userExists = await _context.Users
        .AnyAsync(u => u.Email == email);

    if (userExists)
    {
        return Conflict(new
        {
            message = "Bu e-posta adresi zaten kayıtlı."
        });
    }

    // Yeni kullanıcı oluştur
    var user = new User
    {
        FullName = dto.FullName.Trim(),
        Email = email,
        Role = "Passenger",
        CreatedAt = DateTime.UtcNow
    };

    // Şifreyi güvenli şekilde hash'le
    var passwordHasher = new PasswordHasher<User>();

    user.PasswordHash = passwordHasher.HashPassword(
        user,
        dto.Password
    );

    // Kullanıcıyı veritabanına kaydet
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
        new Claim(ClaimTypes.Role, user.Role)
    };

    var securityKey = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(jwtKey)
    );

    var credentials = new SigningCredentials(
        securityKey,
        SecurityAlgorithms.HmacSha256
    );

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(2),
        signingCredentials: credentials
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
}

[HttpPost("login")]
public async Task<IActionResult> Login([FromBody] LoginDto dto)
{
    // E-posta adresini düzenle
    string email = dto.Email.Trim().ToLowerInvariant();

    // Kullanıcıyı veritabanında ara
    var user = await _context.Users
        .FirstOrDefaultAsync(u => u.Email == email);

    if (user == null)
    {
        return Unauthorized(new
        {
            message = "E-posta veya şifre hatalı."
        });
    }

    // Şifreyi doğrula
    var passwordHasher = new PasswordHasher<User>();

    var result = passwordHasher.VerifyHashedPassword(
        user,
        user.PasswordHash,
        dto.Password
    );

    if (result == PasswordVerificationResult.Failed)
    {
        return Unauthorized(new
        {
            message = "E-posta veya şifre hatalı."
        });
    }

    // Başarılı giriş
    string token = GenerateJwtToken(user);

return Ok(new
{
    message = "Giriş başarılı.",
    token = token,
    tokenType = "Bearer",
    expiresIn = 7200,
    user = new
{
    user.Id,
    user.FullName,
    user.Email,
    user.Role,
    user.CompanyId
}
});
}
    }
}