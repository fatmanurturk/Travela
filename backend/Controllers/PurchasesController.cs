using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Travela.Api.Payments;
using Travela.Api.Security;
using Travela.Data;
using Travela.DTOs;

namespace Travela.Api.Controllers;

[ApiController, Route("api/purchases"), Authorize(Roles = "Passenger")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class PurchasesController(TravelaDbContext db, PaymentService payments, IPaymentProvider provider, LocalTestPaymentProvider local, IDataProtectionProvider protection, TimeProvider clock, IWebHostEnvironment env) : ControllerBase
{
    private Task<Travela.Models.User?> Current(CancellationToken ct) => db.Users.AsNoTracking().ForSession(User, db).SingleOrDefaultAsync(u => u.Role == "Passenger", ct);
    [HttpGet("options"), AllowAnonymous]
    public IActionResult Options() => Ok(new { provider = provider.Name, isSandbox = provider.IsTest, requiresBuyer = provider.Name == "IyzicoSandbox", maxPassengers = 10 });
    private async Task<IActionResult> Run(Func<Travela.Models.User, Task<IActionResult>> action, CancellationToken ct)
    {
        var user = await Current(ct); if (user == null) return Unauthorized();
        try { return await action(user); }
        catch (PaymentFlowException e) { return StatusCode(e.Status, new { message = e.Message }); }
        catch (Exception e) when (e is SqlException { Number: 1205 or 2601 or 2627 } || e is DbUpdateException { InnerException: SqlException { Number: 1205 or 2601 or 2627 } })
        { return Conflict(new { message = "Eş zamanlı koltuk işlemi var. Siparişlerinizi kontrol edip aynı işlem anahtarıyla tekrar deneyin." }); }
    }
    [HttpPost]
    public Task<IActionResult> Start(StartGroupPurchaseDto dto, CancellationToken ct) => Run(async user =>
    {
        var protectedBuyer = dto.Buyer == null ? null : protection.CreateProtector("Travela.IyzicoBuyer.v1").Protect(JsonSerializer.Serialize(new IyzicoBuyer(dto.Buyer, user.Email, HttpContext.Connection.RemoteIpAddress?.ToString() ?? throw new PaymentFlowException("İstemci adresi alınamadı.")), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var group = await payments.StartGroup(user.Id, dto, protectedBuyer, ct);
        return Ok(await Detail(group.Id, user.Id, ct));
    }, ct);
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Run(async user => !await Owns(id, user.Id, ct) ? NotFound() : Ok(await Detail(id, user.Id, ct)), ct);
    [HttpPost("{id:guid}/refresh")]
    public Task<IActionResult> Refresh(Guid id, CancellationToken ct) => Run(async user =>
    {
        if (!await Owns(id, user.Id, ct)) return NotFound();
        var group = await db.PurchaseGroups.AsNoTracking().SingleAsync(g => g.Id == id, ct);
        if (group.NextReconciliationAt == null || group.NextReconciliationAt <= clock.GetUtcNow().UtcDateTime) await payments.ReconcileGroup(id, ct);
        return Ok(await Detail(id, user.Id, ct));
    }, ct);
    [HttpPost("{id:guid}/local-complete")]
    public Task<IActionResult> Simulate(Guid id, TestPaymentDto dto, CancellationToken ct) => Run(async user =>
    {
        if (!(env.IsDevelopment() || env.IsEnvironment("Testing")) || !await db.PurchaseGroups.AnyAsync(g => g.Id == id && g.PassengerId == user.Id && g.Provider == "LocalTest", ct)) return NotFound();
        var notice = await local.Simulate(id, dto.Outcome, dto.RefundOutcome, ct); await payments.HandleNotice(notice, local.Sign(notice), ct);
        return Ok(await Detail(id, user.Id, ct));
    }, ct);
    private Task<bool> Owns(Guid id, int userId, CancellationToken ct) => db.PurchaseGroups.AnyAsync(g => g.Id == id && g.PassengerId == userId, ct);
    private async Task<object> Detail(Guid id, int userId, CancellationToken ct)
    {
        var group = await db.PurchaseGroups.AsNoTracking().Include(g => g.Items).SingleAsync(g => g.Id == id && g.PassengerId == userId, ct);
        var tickets = new List<PurchaseDto>(); foreach (var item in group.Items.OrderBy(p => p.ReservationId)) tickets.Add(await PurchasePresentation.Detail(db, userId, item.Id, clock, ct));
        return new { group.Id, group.Amount, group.Currency, group.Provider, group.IsTest, group.PaymentStatus, group.CheckoutState, group.LastError, group.NeedsAttention,
            holdExpiresAt = DateTime.SpecifyKind(group.HoldExpiresAt, DateTimeKind.Utc), checkoutUrl = group.PaymentStatus == "Pending" && group.HoldExpiresAt > clock.GetUtcNow().UtcDateTime && group.CheckoutState == "Ready" ? group.CheckoutUrl : null,
            refundRequested = group.Items.Sum(p => p.RefundAmount), refunded = group.Items.Where(p => p.RefundStatus == "Succeeded").Sum(p => p.RefundAmount), tickets };
    }
}

[ApiController, Route("api/payments/iyzico"), AllowAnonymous, RequestSizeLimit(16384)]
public class IyzicoNotificationsController(TravelaDbContext db, IyzicoSandboxProvider iyzico, PaymentService payments, IOptions<AccountEmailOptions> urls) : ControllerBase
{
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(IyzicoNotice notice, [FromHeader(Name = "X-IYZ-SIGNATURE-V3")] string? signature, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(signature) || !iyzico.VerifyWebhook(notice, signature) || !Guid.TryParse(notice.PaymentConversationId, out var id)) return BadRequest();
        var group = await db.PurchaseGroups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id && g.Provider == iyzico.Name && g.ProviderToken == notice.Token, ct);
        if (group == null) return NotFound();
        var eventId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(notice.IyziEventType + "|" + notice.IyziPaymentId + "|" + notice.Token + "|" + notice.Status)));
        try { await payments.ReconcileGroup(id, ct, eventId); return Ok(new { received = true }); }
        catch (Exception e) when (e is not OperationCanceledException) { return StatusCode(503, new { message = "Ödeme sonucu doğrulanamadı; sağlayıcı tekrar denemeli." }); }
    }
    [HttpPost("callback/{id:guid}")]
    public async Task<IActionResult> Callback(Guid id, [FromForm] string token, CancellationToken ct)
    {
        var group = await db.PurchaseGroups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id && g.Provider == iyzico.Name && g.ProviderToken == token, ct);
        if (group == null) return BadRequest();
        // A callback only triggers a signed server inquiry; its token/status cannot issue a ticket by itself.
        try { await payments.ReconcileGroup(id, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { /* Keep pending and let durable recovery retry. */ }
        return Redirect(urls.Value.FrontendBaseUrl.TrimEnd('/') + "/purchase/" + id);
    }
}
