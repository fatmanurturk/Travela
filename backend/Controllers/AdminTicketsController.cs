using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Payments;
using Travela.Api.Security;
using Travela.Data;

namespace Travela.Api.Controllers;

public sealed class AdminPaymentRetryDto
{
    [Required, MinLength(10), MaxLength(1000)] public string Reason { get; set; } = "";
    public Guid RequestKey { get; set; }
}

public sealed class AdminTicketRow
{
    [System.Text.Json.Serialization.JsonIgnore] public string? TicketJourneyJson { get; set; }
    public int Id { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? PurchaseGroupId { get; set; }
    public string ReservationNumber { get; set; } = "";
    public string? Pnr { get; set; }
    public string Kind { get; set; } = "";
    public int TripId { get; set; }
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public int PassengerId { get; set; }
    public string AccountName { get; set; } = "";
    public string PassengerName { get; set; } = "";
    public string? PassengerEmail { get; set; }
    public string? PassengerPhone { get; set; }
    public string? PassengerSnapshotSource { get; set; }
    public DateTime? PassengerCapturedAt { get; set; }
    public string DepartureCity { get; set; } = "";
    public string ArrivalCity { get; set; } = "";
    public DateTime DepartureTime { get; set; }
    public int SeatNumber { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TRY";
    public string TicketStatus { get; set; } = "";
    public string PaymentStatus { get; set; } = "Unpaid";
    public string RefundStatus { get; set; } = "None";
    public bool? IsTest { get; set; }
    public bool NeedsAttention { get; set; }
    public DateTime CreatedAt { get; set; }
}

[ApiController, Route("api/admin/tickets"), Authorize(Roles = "Admin")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AdminTicketsController(TravelaDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery, MaxLength(200)] string? search, [FromQuery] int? companyId,
        [FromQuery] int? passengerId, [FromQuery] string? paymentStatus, [FromQuery] string? refundStatus,
        [FromQuery] bool? needsAttention, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 100 || companyId <= 0 || passengerId <= 0 || from > to ||
            paymentStatus is not (null or "" or "Pending" or "Paid" or "Failed" or "Unpaid") ||
            refundStatus is not (null or "" or "None" or "Pending" or "Failed" or "Succeeded"))
            return BadRequest(new { message = "Arama, tarih, durum veya sayfalama filtresi geçersiz." });
        var query = db.Reservations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var orderId = Guid.TryParse(term, out var parsed) ? parsed : Guid.Empty;
            query = query.Where(r => r.ReservationNumber.Contains(term) || (r.Pnr != null && r.Pnr.Contains(term)) ||
                (r.PassengerNameSnapshot != null && r.PassengerNameSnapshot.Contains(term)) ||
                (r.PassengerEmailSnapshot != null && r.PassengerEmailSnapshot.Contains(term)) ||
                (r.PassengerPhoneSnapshot != null && r.PassengerPhoneSnapshot.Contains(term)) || r.Passenger.FullName.Contains(term) ||
                r.Passenger.Email.Contains(term) || r.Trip.Company.Name.Contains(term) || (r.Payment != null && (r.Payment.Id == orderId || r.Payment.PurchaseGroupId == orderId)));
        }
        if (companyId.HasValue) query = query.Where(r => r.Trip.CompanyId == companyId);
        if (passengerId.HasValue) query = query.Where(r => r.PassengerId == passengerId);
        if (from.HasValue)
        {
            var start = new DateTimeOffset(from.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(3)).UtcDateTime;
            query = query.Where(r => r.CreatedAt >= start);
        }
        if (to.HasValue)
        {
            var end = new DateTimeOffset(to.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.FromHours(3)).UtcDateTime;
            query = query.Where(r => r.CreatedAt <= end);
        }
        if (!string.IsNullOrEmpty(paymentStatus)) query = paymentStatus == "Unpaid" ? query.Where(r => r.Payment == null) : query.Where(r => r.Payment != null && r.Payment.PaymentStatus == paymentStatus);
        if (!string.IsNullOrEmpty(refundStatus)) query = refundStatus == "None" ? query.Where(r => r.Payment == null || r.Payment.RefundStatus == "None") : query.Where(r => r.Payment != null && r.Payment.RefundStatus == refundStatus);
        if (needsAttention.HasValue) query = needsAttention.Value
            ? query.Where(r => r.Payment != null && (r.Payment.NeedsAttention || r.Payment.RefundStatus == "Failed"))
            : query.Where(r => r.Payment == null || (!r.Payment.NeedsAttention && r.Payment.RefundStatus != "Failed"));
        var totalCount = await query.CountAsync(ct);
        var items = await Project(query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((int)Math.Min((long)(pageNumber - 1) * pageSize, int.MaxValue)).Take(pageSize)).ToListAsync(ct);
        items.ForEach(Utc);
        return Ok(new { items, totalCount, pageNumber, pageSize });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, [FromQuery] int historyPage = 1, CancellationToken ct = default)
    {
        if (historyPage < 1 || historyPage > 100000) return BadRequest(new { message = "Geçmiş sayfası geçersiz." });
        var ticket = await Project(db.Reservations.AsNoTracking().Where(r => r.Id == id)).SingleOrDefaultAsync(ct);
        if (ticket == null) return NotFound(new { message = "Bilet veya işlem bulunamadı." });
        Utc(ticket);
        var reservation = await db.Reservations.AsNoTracking().Where(r => r.Id == id).Select(r => new { r.CancelledAt, r.CancellationReason }).SingleAsync(ct);
        var order = ticket.OrderId.HasValue ? await db.PaymentOrders.AsNoTracking().SingleAsync(p => p.Id == ticket.OrderId, ct) : null;
        var history = db.PaymentEvents.AsNoTracking().Where(e => e.OrderId == ticket.OrderId);
        var totalCount = await history.CountAsync(ct);
        var events = await history.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Skip((historyPage - 1) * 20).Take(20)
            .Select(e => new { e.Id, e.Type, e.CreatedAt, e.PaymentStatus, e.RefundStatus, e.RefundAmount, e.ActorUserId, e.ActorName, e.Reason }).ToListAsync(ct);
        DateTime? Time(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
        return Ok(new { ticket, cancellation = new { cancelledAt = Time(reservation.CancelledAt), reservation.CancellationReason },
            payment = order == null ? null : new { order.Provider, order.ProviderReference, paidAt = Time(order.PaidAt), order.RefundAmount, order.RefundAttempts,
                order.RefundReference, refundRequestedAt = Time(order.RefundRequestedAt), refundedAt = Time(order.RefundedAt),
                nextRefundAttemptAt = Time(order.NextRefundAttemptAt), lastReconciledAt = Time(order.LastReconciledAt),
                nextReconciliationAt = Time(order.NextReconciliationAt), order.ReconciliationAttempts, order.LastError,
                canRetryRefund = order.PaymentStatus == "Paid" && (order.RefundStatus == "Pending" || order.RefundStatus == "Failed") &&
                    (order.RefundLeaseUntil == null || order.RefundLeaseUntil <= clock.GetUtcNow().UtcDateTime),
                canReconcile = order.PaymentStatus == "Pending" },
            history = new { items = events.Select(e => new { e.Id, e.Type, createdAt = Time(e.CreatedAt), e.PaymentStatus, e.RefundStatus, e.RefundAmount, e.ActorUserId, e.ActorName, e.Reason }), totalCount, pageNumber = historyPage, pageSize = 20 } });
    }

    [HttpPost("{id:int}/{operation}")]
    public async Task<IActionResult> Retry(int id, string operation, AdminPaymentRetryDto dto, CancellationToken ct)
    {
        if (operation is not ("retry-refund" or "reconcile")) return NotFound();
        var reason = dto.Reason.Trim();
        if (reason.Length < 10 || dto.RequestKey == Guid.Empty) return BadRequest(new { message = "En az 10 karakterlik gerekçe ve işlem anahtarı zorunludur." });
        var tripId = await db.Reservations.Where(r => r.Id == id).Select(r => (int?)r.TripId).SingleOrDefaultAsync(ct);
        if (tripId == null) return NotFound(new { message = "Bilet veya işlem bulunamadı." });
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await db.Trips.FromSqlInterpolated($"SELECT * FROM [Trips] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {tripId.Value}").SingleAsync(ct);
            var actor = await db.Users.AsNoTracking().ForSession(User, db).SingleOrDefaultAsync(u => u.Role == "Admin", ct);
            if (actor == null) return Unauthorized();
            var order = await db.PaymentOrders.SingleOrDefaultAsync(p => p.ReservationId == id, ct);
            if (order == null) return Conflict(new { message = "Ücretsiz kayıt için ödeme veya iade işlemi yapılamaz." });
            var type = operation == "retry-refund" ? "ManualRefundRetryQueued" : "ManualReconciliationQueued";
            var previous = await db.PaymentEvents.AsNoTracking().SingleOrDefaultAsync(e => e.ActorUserId == actor.Id && e.RequestKey == dto.RequestKey, ct);
            if (previous != null)
                return previous.OrderId == order.Id && previous.Type == type && previous.Reason == reason
                    ? Ok(new { message = "Bu istek daha önce kaydedildi.", eventId = previous.Id })
                    : Conflict(new { message = "İşlem anahtarı başka bir istek için kullanılmış." });
            var now = clock.GetUtcNow().UtcDateTime;
            if (operation == "retry-refund")
            {
                if (order.PaymentStatus != "Paid" || order.RefundStatus is not ("Pending" or "Failed") || order.RefundAmount <= 0)
                    return Conflict(new { message = "Yalnızca bekleyen veya başarısız gerçek bir iade talebi yeniden denenebilir." });
                if (order.RefundLeaseUntil > now) return Conflict(new { message = "İade şu anda işleniyor. Sonucu bekleyip yenileyin." });
                if (await db.PaymentEvents.AnyAsync(e => e.OrderId == order.Id && e.Type == type && e.CreatedAt > now.AddSeconds(-30), ct))
                    return Conflict(new { message = "Bu iade kısa süre önce yeniden denemeye alındı. Sonucu bekleyin." });
                order.RefundAttempts = 0; order.NextRefundAttemptAt = now; order.NeedsAttention = false;
            }
            else
            {
                if (order.PaymentStatus != "Pending") return Conflict(new { message = "Yalnızca sonucu beklenen ödemeler tekrar sorgulanabilir." });
                if (await db.PaymentEvents.AnyAsync(e => e.OrderId == order.Id && e.Type == type && e.CreatedAt > now.AddSeconds(-30), ct))
                    return Conflict(new { message = "Bu ödeme kısa süre önce sorgulama kuyruğuna alındı. Sonucu bekleyin." });
                order.NextReconciliationAt = now;
                if (order.PurchaseGroupId.HasValue)
                {
                    var group = await db.PurchaseGroups.SingleAsync(g => g.Id == order.PurchaseGroupId.Value, ct);
                    group.NextReconciliationAt = now;
                }
            }
            var entry = PaymentHistory.Record(db, order, type, now, actor.Id, actor.FullName, reason, dto.RequestKey);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Accepted(new { message = operation == "retry-refund" ? "İade yeniden denemeye alındı. Sonuç sağlayıcıdan doğrulanacak." : "Ödeme tekrar sorgulama kuyruğuna alındı.", eventId = entry.Id });
        }
        catch (Exception e) when (e is SqlException { Number: 1205 or 2601 or 2627 } || e is DbUpdateException { InnerException: SqlException { Number: 1205 or 2601 or 2627 } })
        { return Conflict(new { message = "Eş zamanlı işlem çakışması. İşlem geçmişini yenileyip aynı anahtarla tekrar deneyin." }); }
    }

    private static IQueryable<AdminTicketRow> Project(IQueryable<Reservation> query) => query.Select(r => new AdminTicketRow
    {
        Id = r.Id, OrderId = r.Payment == null ? null : r.Payment.Id, PurchaseGroupId = r.Payment == null ? null : r.Payment.PurchaseGroupId, ReservationNumber = r.ReservationNumber, Pnr = r.Pnr, Kind = r.Kind, TicketJourneyJson = r.TicketJourneyJson,
        TripId = r.TripId, CompanyId = r.Trip.CompanyId, CompanyName = r.Trip.Company.Name, PassengerId = r.PassengerId,
        AccountName = r.Passenger.FullName, PassengerName = r.PassengerNameSnapshot ?? "Yolcu bilgisi kaydedilmemiş",
        PassengerEmail = r.PassengerEmailSnapshot, PassengerPhone = r.PassengerPhoneSnapshot,
        PassengerSnapshotSource = r.PassengerSnapshotSource, PassengerCapturedAt = r.PassengerCapturedAt,
        DepartureCity = r.Trip.Route.DepartureCity, ArrivalCity = r.Trip.Route.ArrivalCity, DepartureTime = r.Trip.DepartureTime,
        SeatNumber = r.SeatNumber, Amount = r.Payment == null ? r.Price : r.Payment.Amount,
        Currency = r.Payment == null ? "TRY" : r.Payment.Currency, TicketStatus = r.Status,
        PaymentStatus = r.Payment == null ? "Unpaid" : r.Payment.PaymentStatus, RefundStatus = r.Payment == null ? "None" : r.Payment.RefundStatus,
        IsTest = r.Payment == null ? null : r.Payment.IsTest,
        NeedsAttention = r.Payment != null && (r.Payment.NeedsAttention || r.Payment.RefundStatus == "Failed"), CreatedAt = r.CreatedAt
    });

    private static void Utc(AdminTicketRow row)
    {
        if (row.TicketJourneyJson != null) { var journey = Travela.Api.Trips.TicketJourney.Read(row.TicketJourneyJson); row.CompanyName = journey.CompanyName; row.DepartureCity = journey.DepartureCity; row.ArrivalCity = journey.ArrivalCity; row.DepartureTime = journey.DepartureTime; }
        row.CreatedAt = DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc);
        row.DepartureTime = DateTime.SpecifyKind(row.DepartureTime, DateTimeKind.Utc);
        if (row.PassengerCapturedAt.HasValue) row.PassengerCapturedAt = DateTime.SpecifyKind(row.PassengerCapturedAt.Value, DateTimeKind.Utc);
    }
}
