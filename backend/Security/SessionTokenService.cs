using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Travela.Models;

namespace Travela.Api.Security;

public sealed class SessionTokenService(IConfiguration configuration, TimeProvider clock)
{
    public string Issue(User user, DateTime? expiresAt = null)
    {
        var key = configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key bulunamadı.");
        var issuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("JWT Issuer bulunamadı.");
        var audience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("JWT Audience bulunamadı.");
        var claims = new[] {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email), new Claim(ClaimTypes.Role, user.Role),
            new Claim("security_version", user.SecurityVersion.ToString()),
            new Claim("must_change_password", user.MustChangePassword.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer, audience, claims,
            expires: expiresAt ?? clock.GetUtcNow().UtcDateTime.AddHours(2), signingCredentials: credentials));
    }
}
