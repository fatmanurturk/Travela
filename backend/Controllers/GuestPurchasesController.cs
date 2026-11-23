using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Documents;
using Travela.Api.Payments;
using Travela.Api.Security;
using Travela.Api.Trips;
using Travela.Data;
using Travela.DTOs;

namespace Travela.Api.Controllers;

[ApiController, Route("api/guest/purchases"), AllowAnonymous, EnableRateLimiting("guest-checkout")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class GuestPurchasesController(TravelaDbContext db, PaymentService payments, GuestTicketAccess access,
    IDataProtectionProvider protection, TimeProvider clock, LocalTestPaymentProvider local, IWebHostEnvironment env, TicketPdfRenderer pdf) : ControllerBase
{
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        try { return await action(); }
        catch (PaymentFlowException e) { return StatusCode(e.Status, new { message = e.Message }); }
        catch (Exception e) when (e is SqlException { Number: 1205 or 2601 or 2627 or 51011 or 51012 } || e is DbUpdateException { InnerException: SqlException { Number: 1205 or 2601 or 2627 or 51011 or 51012 } })
        { return Conflict(new { message = "Eş zamanlı işlem var. Yeni sipariş başlatmadan mevcut siparişinizi kontrol edin." }); }
    }
    [HttpPost("session"), EnableRateLimiting("guest-create")]
    public Task<IActionResult> Session(CancellationToken ct) => Run(async () => { await access.EnsureSession(HttpContext, ct); return Ok(new { ready = true }); });

    [HttpGet("resume")]
    public Task<IActionResult> Resume(CancellationToken ct) => Run(async () =>
    {
        var session = await access.Session(HttpContext, ct);
        if (session == null) return Unauthorized(new { message = "Misafir oturumu bulunamadı." });
        var id = await db.PurchaseGroups.Where(g => g.GuestSessionId == session && g.PassengerId == null &&
            (g.PaymentStatus == "Paid" || g.PaymentStatus == "Pending"))
            .OrderByDescending(g => g.CreatedAt).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct);
        return Ok(new { id });
    });
    [HttpPost("quote")]
    public Task<IActionResult> Quote(CampaignQuoteDto dto, CancellationToken ct) => Run(async () =>
    {
        var trip = await db.Trips.AsNoTracking().Include(t => t.Company).Include(t => t.Bus).Include(t => t.Route).SingleOrDefaultAsync(t => t.Id == dto.TripId, ct);
        if (trip == null) return NotFound();
        var segments = new TripSegmentService(db, clock);
        var segment = await segments.Resolve(trip, dto.BoardingStopOrder, dto.AlightingStopOrder, ct);
        if (!await segments.CanSell(trip, segment, ct)) return BadRequest(new { message = "Bu yolculuk kesimi satışa açık değil." });
        return Ok(await CampaignPricing.Quote(db, trip, null, dto.PassengerCount, dto.CouponCode, clock.GetUtcNow().UtcDateTime, false, ct, segment.Price, segment.From.DepartureTime));
    });
    [HttpPost, EnableRateLimiting("guest-create")]
    public Task<IActionResult> Start(StartGroupPurchaseDto dto, CancellationToken ct) => Run(async () =>
    {
        // Session cookie must already be in the browser before a purchase can create any holds/payment.
        var session = await access.Session(HttpContext, ct);
        if (session == null) return Unauthorized(new { message = "Misafir oturumunu başlatıp tekrar deneyin." });
        var buyer = dto.Buyer == null ? null : protection.CreateProtector("Travela.IyzicoBuyer.v1").Protect(JsonSerializer.Serialize(
            new IyzicoBuyer(dto.Buyer, BookingContact.Email(dto.ContactEmail), HttpContext.Connection.RemoteIpAddress!.ToString()), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var group = await payments.StartGroup(null, dto, buyer, ct, HttpContext.Connection.RemoteIpAddress?.ToString(), session);
        return Ok(await Detail(group.Id, session.Value, ct));
    });
    private Task<bool> Owns(Guid id, Guid? session, CancellationToken ct) => db.PurchaseGroups.AnyAsync(g => session != null && g.Id == id && g.GuestSessionId == session && g.PassengerId == null, ct);
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Run(async () =>
    {
        var session = await access.Session(HttpContext, ct);
        return !await Owns(id, session, ct) ? NotFound() : Ok(await Detail(id, session!.Value, ct));
    });
    [HttpPost("{id:guid}/refresh")]
    public Task<IActionResult> Refresh(Guid id, CancellationToken ct) => Run(async () =>
    {
        var session = await access.Session(HttpContext, ct);
        if (!await Owns(id, session, ct)) return NotFound();
        var group = await db.PurchaseGroups.AsNoTracking().SingleAsync(g => g.Id == id, ct);
        if (group.NextReconciliationAt == null || group.NextReconciliationAt <= clock.GetUtcNow().UtcDateTime) await payments.ReconcileGroup(id, ct);
        return Ok(await Detail(id, session!.Value, ct));
    });
    [HttpPost("{id:guid}/local-complete")]
    public Task<IActionResult> Simulate(Guid id, TestPaymentDto dto, CancellationToken ct) => Run(async () =>
    {
        var session = await access.Session(HttpContext, ct);
        if (!(env.IsDevelopment() || env.IsEnvironment("Testing")) || !await Owns(id, session, ct) || !await db.PurchaseGroups.AnyAsync(g => g.Id == id && g.Provider == "LocalTest", ct)) return NotFound();
        if (!await payments.HasLegalAcceptance(null, id, true, ct)) return BadRequest();
        var notice = await local.Simulate(id, dto.Outcome, dto.RefundOutcome, ct);
        await payments.HandleNotice(notice, local.Sign(notice), ct);
        return Ok(await Detail(id, session!.Value, ct));
    });
    [HttpGet("{id:guid}/tickets/{pnr}/pdf")]
    public Task<IActionResult> Pdf(Guid id, string pnr, CancellationToken ct) => Run(async () =>
    {
        if (!await Owns(id, await access.Session(HttpContext, ct), ct)) return NotFound();
        var item = await db.PaymentOrders.AsNoTracking().SingleOrDefaultAsync(p => p.PurchaseGroupId == id && p.Reservation.Pnr == pnr && p.PaymentStatus == "Paid" && p.Reservation.Kind == "Ticket", ct);
        if (item == null) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(pdf.Render(await PurchasePresentation.Detail(db, null, item.Id, clock, ct)), "application/pdf", "Travela-bilet.pdf");
    });
    private async Task<object> Detail(Guid id, Guid session, CancellationToken ct)
    {
        var group = await db.PurchaseGroups.AsNoTracking().Include(g => g.Items).SingleAsync(g => g.Id == id && g.GuestSessionId == session && g.PassengerId == null, ct);
        var tickets = new List<PurchaseDto>();
        foreach (var item in group.Items.OrderBy(p => p.ReservationId)) tickets.Add(await PurchasePresentation.Detail(db, null, item.Id, clock, ct));
        var legalAccepted = await payments.HasLegalAcceptance(null, id, true, ct);
        return new { group.Id, guest = true, group.Amount, group.OriginalAmount, group.DiscountAmount, group.CampaignName, group.CouponCode, group.Currency,
            group.Provider, group.IsTest, group.PaymentStatus, group.CheckoutState, group.LastError, group.NeedsAttention, legalAccepted,
            holdExpiresAt = DateTime.SpecifyKind(group.HoldExpiresAt, DateTimeKind.Utc),
            checkoutUrl = legalAccepted && group.PaymentStatus == "Pending" && group.HoldExpiresAt > clock.GetUtcNow().UtcDateTime && group.CheckoutState == "Ready" ? group.CheckoutUrl : null,
            refundRequested = group.Items.Sum(p => p.RefundAmount), refunded = group.Items.Where(p => p.RefundStatus == "Succeeded").Sum(p => p.RefundAmount), tickets };
    }
}
