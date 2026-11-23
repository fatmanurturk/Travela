using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Payments;
using Travela.Data;

namespace Travela.Api.Security;

public sealed record TicketGrant(Guid OrderId, string Nonce, DateTime ExpiresAt);

public sealed class GuestTicketAccess(TravelaDbContext db, IDataProtectionProvider protection, TimeProvider clock, IWebHostEnvironment env, IConfiguration config)
{
    public const string SessionCookie = "travela_guest_checkout";
    public const string TicketCookie = "travela_ticket_access";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private CookieOptions Cookie(DateTime expires) => new() { HttpOnly = true, Secure = !env.IsDevelopment() && !env.IsEnvironment("Testing"),
        SameSite = SameSiteMode.Lax, Path = "/api", IsEssential = true, Expires = new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc)) };
    private T? Read<T>(HttpContext http, string name, string purpose) where T : class
    {
        try { return http.Request.Cookies.TryGetValue(name, out var raw) ? JsonSerializer.Deserialize<T>(protection.CreateProtector(purpose).Unprotect(raw)) : null; }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { return null; }
    }
    public async Task<Guid?> Session(HttpContext http, CancellationToken ct)
    {
        var session = Read<GuestCheckoutSession>(http, SessionCookie, "Travela.GuestCheckout.v1");
        return session != null && session.ExpiresAt > Now && await db.GuestCheckoutSessions.AnyAsync(s => s.Id == session.Id && s.ExpiresAt > Now, ct) ? session.Id : null;
    }
    public async Task<Guid> EnsureSession(HttpContext http, CancellationToken ct)
    {
        if (await Session(http, ct) is { } existing) return existing;
        var session = new GuestCheckoutSession { ExpiresAt = Now.AddHours(24) };
        db.GuestCheckoutSessions.Add(session); await db.SaveChangesAsync(ct);
        http.Response.Cookies.Append(SessionCookie, protection.CreateProtector("Travela.GuestCheckout.v1").Protect(JsonSerializer.Serialize(session)), Cookie(session.ExpiresAt));
        return session.Id;
    }
    public TicketGrant? Grant(HttpContext http)
    {
        var grant = Read<TicketGrant>(http, TicketCookie, "Travela.TicketLookup.v1");
        return grant?.ExpiresAt > Now ? grant : null;
    }
    public TicketGrant IssueGrant(HttpContext http, Guid orderId)
    {
        var grant = new TicketGrant(orderId, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), Now.AddMinutes(15));
        http.Response.Cookies.Append(TicketCookie, protection.CreateProtector("Travela.TicketLookup.v1").Protect(JsonSerializer.Serialize(grant)), Cookie(grant.ExpiresAt));
        return grant;
    }
    public string CodeHash(Guid id, string code) => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(config["Jwt:Key"] ?? throw new InvalidOperationException()), Encoding.UTF8.GetBytes("ticket-cancel:" + id + ":" + code)));
    public static string GrantHash(TicketGrant grant) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(grant.Nonce)));
}
