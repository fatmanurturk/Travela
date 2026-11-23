using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Travela.Api.Documents;
using Travela.Api.Models;
using Travela.Api.Notifications;
using Travela.Api.Payments;
using Travela.Api.Security;
using Travela.Data;
using Travela.DTOs;

namespace Travela.Api.Controllers;

public sealed record TicketLookupDto([Required(ErrorMessage = "PNR numarasını giriniz."), MaxLength(128)] string Pnr,
    [Required(ErrorMessage = "Cep telefonu veya e-posta giriniz."), MaxLength(320)] string Contact);
public sealed record TicketCancelCodeDto(Guid ChallengeId, [Required(ErrorMessage = "Doğrulama kodunu giriniz."), RegularExpression("^[0-9]{6}$", ErrorMessage = "6 haneli doğrulama kodunu giriniz.")] string Code);

[ApiController, Route("api/ticket-lookup"), AllowAnonymous, EnableRateLimiting("ticket-lookup")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class TicketLookupController(TravelaDbContext db, GuestTicketAccess access, TimeProvider clock, PaymentService payments,
    TicketPdfRenderer pdf, EmailTransport email, SmsTransport sms, IOptions<AccountEmailOptions> emailOptions, IOptions<SmsOptions> smsOptions,
    IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private const string NotMatched = "Girdiğiniz bilgilerle eşleşen bilet bulunamadı.";
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers.XContentTypeOptions = "nosniff";
        try { return await action(); }
        catch (PaymentFlowException e) { return StatusCode(e.Status, new { message = e.Message }); }
    }
    [HttpPost]
    public Task<IActionResult> Lookup(TicketLookupDto dto, CancellationToken ct) => Run(async () =>
    {
        var pnr = BookingContact.Pnr(dto.Pnr);
        if (pnr.Length is < 1 or > 32) return NotFound(new { message = NotMatched });
        var order = await db.PaymentOrders.AsNoTracking().Include(p => p.PurchaseGroup).Include(p => p.Reservation)
            .SingleOrDefaultAsync(p => p.Reservation.Pnr == pnr && p.Reservation.Kind == "Ticket" && p.PaymentStatus == "Paid", ct);
        // Historical tickets use purchase-time reservation snapshots only. Never consult today's profile.
        if (order == null || !BookingContact.Matches(dto.Contact, order.PurchaseGroup?.ContactEmail ?? order.Reservation.PassengerEmailSnapshot,
            order.PurchaseGroup?.ContactPhone ?? order.Reservation.PassengerPhoneSnapshot))
            return NotFound(new { message = NotMatched });
        var grant = access.IssueGrant(HttpContext, order.Id);
        return Ok(await Detail(order.Id, ct, grant.ExpiresAt));
    });
    private async Task<TicketGrant> Require(CancellationToken ct)
    {
        var grant = access.Grant(HttpContext);
        if (grant == null || !await db.PaymentOrders.AnyAsync(p => p.Id == grant.OrderId && p.PaymentStatus == "Paid" && p.Reservation.Kind == "Ticket", ct))
            throw new PaymentFlowException("Bilet erişiminin süresi doldu. PNR ve iletişim bilginizle yeniden sorgulayın.", 401);
        return grant;
    }
    [HttpGet("ticket")]
    public Task<IActionResult> Get(CancellationToken ct) => Run(async () => Ok(await Detail((await Require(ct)).OrderId, ct)));
    [HttpGet("pdf")]
    public Task<IActionResult> Pdf(CancellationToken ct) => Run(async () =>
        File(pdf.Render(await Masked((await Require(ct)).OrderId, ct)), "application/pdf", "Travela-bilet.pdf"));

    private async Task<PurchaseDto> Masked(Guid id, CancellationToken ct)
    {
        var order = await db.PaymentOrders.AsNoTracking().SingleAsync(p => p.Id == id, ct);
        var ticket = await PurchasePresentation.Detail(db, order.PassengerId, id, clock, ct);
        ticket.PassengerEmail = BookingContact.MaskEmail(ticket.PassengerEmail);
        ticket.PassengerPhone = BookingContact.MaskPhone(ticket.PassengerPhone);
        ticket.PassengerIdentityNumber = BookingContact.MaskIdentity(await db.Reservations.Where(r => r.Id == order.ReservationId).Select(r => r.PassengerIdentityNumberSnapshot).SingleAsync(ct));
        ticket.PurchaseGroupId = null; ticket.CheckoutUrl = null;
        return ticket;
    }
    private async Task<object> Detail(Guid id, CancellationToken ct, DateTime? expiresAt = null) => new {
        ticket = await Masked(id, ct), accessExpiresAt = DateTime.SpecifyKind(expiresAt ?? access.Grant(HttpContext)!.ExpiresAt, DateTimeKind.Utc) };

    [HttpPost("cancellation-code"), EnableRateLimiting("ticket-code")]
    public Task<IActionResult> SendCode(CancellationToken ct) => Run(async () =>
    {
        var grant = await Require(ct);
        var ticket = await Masked(grant.OrderId, ct);
        if (!ticket.CanCancel) return BadRequest(new { message = "Bu bilet için iptal süresi doldu veya bilet zaten iptal edildi." });
        var order = await db.PaymentOrders.AsNoTracking().Include(p => p.PurchaseGroup).Include(p => p.Reservation).SingleAsync(p => p.Id == grant.OrderId, ct);
        var recipientEmail = order.PurchaseGroup?.ContactEmail ?? order.Reservation.PassengerEmailSnapshot;
        var recipientPhone = BookingContact.Phone(order.PurchaseGroup?.ContactPhone ?? order.Reservation.PassengerPhoneSnapshot);
        var mode = config["GuestTickets:CancellationDelivery"] ?? "Disabled";
        var useEmail = mode == "Email" && !string.IsNullOrEmpty(recipientEmail) && emailOptions.Value.Valid(env);
        var useSms = mode == "Sms" && recipientPhone != null && smsOptions.Value.Valid(env);
        if (!useEmail && !useSms) return StatusCode(503, new { message = "İptal doğrulama kodu gönderimi şu anda kullanılamıyor. Lütfen destek ekibiyle iletişime geçin." });
        var code = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");
        TicketCancellationChallenge challenge;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            // Serialise code creation per ticket, including grants issued by different browsers.
            await db.PaymentOrders.FromSqlInterpolated($"SELECT * FROM PaymentOrders WITH (UPDLOCK, HOLDLOCK) WHERE Id = {grant.OrderId}").SingleAsync(ct);
            if (await db.TicketCancellationChallenges.AnyAsync(c => c.PaymentOrderId == grant.OrderId && c.CreatedAt > Now.AddMinutes(-1), ct) ||
                await db.TicketCancellationChallenges.CountAsync(c => c.PaymentOrderId == grant.OrderId && c.CreatedAt > Now.AddHours(-1), ct) >= 5)
                return StatusCode(429, new { message = "Yeni kod istemeden önce bekleyin. Saatlik kod sınırı uygulanır." });
            await db.TicketCancellationChallenges.Where(c => c.PaymentOrderId == grant.OrderId && c.UsedAt == null).ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, Now), ct);
            challenge = new() { PaymentOrderId = grant.OrderId, GrantHash = GuestTicketAccess.GrantHash(grant), CreatedAt = Now, ExpiresAt = Now.AddMinutes(5) };
            challenge.CodeHash = access.CodeHash(challenge.Id, code);
            db.TicketCancellationChallenges.Add(challenge); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        try
        {
            var body = $"Travela bilet iptal kodunuz: {code}. PNR: {ticket.Pnr}. Kod 5 dakika geçerlidir. Bu kod yalnızca bu bileti iptal eder. Kodu kimseyle paylaşmayın.";
            if (useEmail) await email.Send(challenge.Id, recipientEmail!, "Travela bilet iptal doğrulama kodu", "<p>" + System.Net.WebUtility.HtmlEncode(body) + "</p>", ct);
            else await sms.Send(new TravelNotification { Id = challenge.Id, Recipient = recipientPhone!, Body = body }, ct);
            challenge.Delivered = true; await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        { return StatusCode(503, new { message = "Doğrulama kodu gönderilemedi. Bir süre sonra yeniden deneyin." }); }
        var preview = useEmail ? emailOptions.Value.DeliveryMode == "Pickup" : smsOptions.Value.Mode == "Pickup";
        return Ok(new { challengeId = challenge.Id, expiresInSeconds = 300, destination = useEmail ? ticket.PassengerEmail : ticket.PassengerPhone,
            message = preview ? "Test kodu yerel test posta/SMS klasörüne yazıldı; gerçek gönderim yapılmadı." : "Doğrulama kodu kayıtlı iletişim adresinize iletilmek üzere kabul edildi." });
    });
    [HttpPost("cancel"), EnableRateLimiting("ticket-verify")]
    public Task<IActionResult> Cancel(TicketCancelCodeDto dto, CancellationToken ct) => Run(async () =>
    {
        var grant = await Require(ct);
        await payments.CancelTicketWithCode(grant.OrderId, dto.ChallengeId, GuestTicketAccess.GrantHash(grant), access.CodeHash(dto.ChallengeId, dto.Code), ct);
        return Ok(await Detail(grant.OrderId, ct));
    });
}
