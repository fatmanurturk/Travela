using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Payments;
using Travela.Data;
using Travela.DTOs;
using Travela.Api.Documents;

namespace Travela.Api.Controllers;

[ApiController, Route("api/tickets"), Authorize(Roles = "Passenger")]
public class TicketsController(TravelaDbContext db, PaymentService payments, LocalTestPaymentProvider local, IWebHostEnvironment env, TimeProvider clock, TicketPdfRenderer pdf) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : -1;
    private Task<bool> ActiveUser(CancellationToken ct) => db.Users.AnyAsync(u => u.Id == UserId && u.Role == "Passenger" && u.IsActive && !u.MustChangePassword, ct);
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        if (!await ActiveUser(ct)) return Unauthorized();
        try { return await action(); }
        catch (PaymentFlowException e) { return StatusCode(e.Status, new { message = e.Message }); }
        catch (Exception e) when (e is SqlException { Number: 1205 or 2601 or 2627 } || e is DbUpdateException { InnerException: SqlException { Number: 1205 or 2601 or 2627 } })
        { return Conflict(new { message = "Eş zamanlı işlem çakışması. Biletlerim sayfasını kontrol edip aynı işlem anahtarıyla tekrar deneyiniz." }); }
    }
    [HttpPost("checkout")]
    public Task<IActionResult> Start(StartPurchaseDto dto, CancellationToken ct) => Run(async () =>
    {
        var order = await payments.Start(UserId, dto.TripId, dto.SeatNumber, dto.RequestKey, ct);
        return Ok(await Detail(order.Id, ct));
    }, ct);
    [HttpGet("mine")]
    public Task<IActionResult> Mine(CancellationToken ct) => Run(async () =>
    {
        var ids = await db.PaymentOrders.AsNoTracking().Where(p => p.PassengerId == UserId).OrderByDescending(p => p.CreatedAt).Select(p => p.Id).ToListAsync(ct);
        var items = new List<PurchaseDto>();
        foreach (var id in ids) items.Add(await Detail(id, ct));
        return Ok(items);
    }, ct);
    [HttpGet("orders/{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Run(async () =>
    {
        if (!await Owns(id, ct)) return NotFound(new { message = "Sipariş bulunamadı." });
        return Ok(await Detail(id, ct));
    }, ct);
    [HttpPost("{pnr}/cancel")]
    public Task<IActionResult> Cancel(string pnr, CancellationToken ct) => Run(async () =>
    {
        await payments.CancelTicket(UserId, pnr, ct);
        var id = await db.PaymentOrders.Where(p => p.Reservation.Pnr == pnr && p.PassengerId == UserId).Select(p => p.Id).SingleAsync(ct);
        return Ok(await Detail(id, ct));
    }, ct);
    [HttpGet("{pnr}")]
    public Task<IActionResult> Ticket(string pnr, CancellationToken ct) => Run(async () =>
    {
        var ticket = await IssuedTicket(pnr, ct);
        return ticket == null ? NotFound(new { message = "Bilet bulunamadı veya bu bilete erişim izniniz yok." }) : Ok(ticket);
    }, ct);
    [HttpGet("{pnr}/pdf")]
    public Task<IActionResult> Pdf(string pnr, CancellationToken ct) => Run(async () =>
    {
        var ticket = await IssuedTicket(pnr, ct);
        if (ticket == null) return NotFound(new { message = "Bilet belgesi bulunamadı veya erişim izniniz yok." });
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(pdf.Render(ticket), "application/pdf", $"Travela-{ticket.Pnr}.pdf");
    }, ct);
    private async Task<PurchaseDto?> IssuedTicket(string pnr, CancellationToken ct)
    {
        // PNR alone is never an access token. Free records and unpaid holds cannot produce a document.
        var id = await db.PaymentOrders.AsNoTracking()
            .Where(p => p.PassengerId == UserId && p.PaymentStatus == "Paid" && p.Reservation.Kind == "Ticket" && p.Reservation.Pnr == pnr)
            .Select(p => (Guid?)p.Id).SingleOrDefaultAsync(ct);
        return id.HasValue ? await Detail(id.Value, ct) : null;
    }
    [HttpPost("local/{id:guid}/complete")]
    public Task<IActionResult> Simulate(Guid id, TestPaymentDto dto, CancellationToken ct) => Run(async () =>
    {
        if (!TestEnvironment()) return NotFound();
        if (!await Owns(id, ct)) return NotFound();
        var notice = await local.Simulate(id, dto.Outcome, dto.RefundOutcome, ct);
        await payments.HandleNotice(notice, local.Sign(notice), ct);
        return Ok(await Detail(id, ct));
    }, ct);
    [HttpPost("local/{id:guid}/refund-result")]
    public Task<IActionResult> SimulateRefund(Guid id, TestPaymentDto dto, CancellationToken ct) => Run(async () =>
    {
        if (!TestEnvironment()) return NotFound();
        if (!await Owns(id, ct)) return NotFound();
        if (dto.Outcome is not ("Succeeded" or "Failed" or "Pending")) return BadRequest();
        await db.LocalPaymentRecords.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.RefundOutcome, dto.Outcome), ct);
        // Test-only control advances the provider result; it never marks the application refund complete.
        await db.PaymentOrders.Where(p => p.Id == id && p.RefundStatus != "Succeeded").ExecuteUpdateAsync(s => s.SetProperty(p => p.NextRefundAttemptAt, clock.GetUtcNow().UtcDateTime).SetProperty(p => p.RefundAttempts, 0), ct);
        await payments.ProcessRefund(id, ct);
        return Ok(await Detail(id, ct));
    }, ct);
    private bool TestEnvironment() => env.IsDevelopment() || env.IsEnvironment("Testing");
    private Task<bool> Owns(Guid id, CancellationToken ct) => db.PaymentOrders.AnyAsync(p => p.Id == id && p.PassengerId == UserId, ct);
    private async Task<PurchaseDto> Detail(Guid id, CancellationToken ct)
    {
        var p = await db.PaymentOrders.AsNoTracking().Include(p => p.Reservation).ThenInclude(r => r.Trip).ThenInclude(t => t.Route)
            .Include(p => p.Reservation).ThenInclude(r => r.Trip).ThenInclude(t => t.Company)
            .Include(p => p.Reservation).ThenInclude(r => r.Passenger).SingleAsync(p => p.Id == id && p.PassengerId == UserId, ct);
        var r = p.Reservation; var t = r.Trip;
        DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
        return new() { Id = id, Pnr = r.Pnr, Kind = r.Kind, TripId = t.Id, SeatNumber = r.SeatNumber,
            CompanyName = t.Company.Name, PassengerName = r.Passenger.FullName, DepartureCity = t.Route.DepartureCity, ArrivalCity = t.Route.ArrivalCity,
            ArrivalTime = Utc(t.ArrivalTime), PaidAt = p.PaidAt.HasValue ? Utc(p.PaidAt.Value) : null, RetrievedAt = clock.GetUtcNow().UtcDateTime,
            DepartureTime = Utc(t.DepartureTime), CancelDeadline = Utc(t.DepartureTime.AddHours(-2)),
            HoldExpiresAt = r.HoldExpiresAt.HasValue ? Utc(r.HoldExpiresAt.Value) : null,
            Amount = p.Amount, Currency = p.Currency, IsTest = p.IsTest, PaymentStatus = p.PaymentStatus,
            TicketStatus = r.Status, RefundStatus = p.RefundStatus, RefundAmount = p.RefundAmount,
            CanCancel = r.Kind == "Ticket" && r.Status == "Active" && p.PaymentStatus == "Paid" && PaymentService.CanCancel(t.DepartureTime, clock.GetUtcNow().UtcDateTime),
            CancellationReason = r.CancellationReason, CancelledAt = r.CancelledAt.HasValue ? Utc(r.CancelledAt.Value) : null,
            LastError = p.LastError, NeedsAttention = p.NeedsAttention,
            CheckoutUrl = r.Kind == "Hold" && r.Status == "Active" && r.HoldExpiresAt > clock.GetUtcNow().UtcDateTime ? p.CheckoutUrl : null };
    }
}

[ApiController, Route("api/payments/notifications")]
public class PaymentNotificationsController(PaymentService payments) : ControllerBase
{
    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Notify(ProviderNotice notice, [FromHeader(Name = "X-Provider-Signature")] string? signature, CancellationToken ct)
    {
        try { await payments.HandleNotice(notice, signature ?? "", ct); return Ok(new { received = true }); }
        catch (PaymentFlowException e) { return StatusCode(e.Status, new { message = e.Message }); }
    }
}
