using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Data;
using Travela.DTOs;
using Travela.Api.Security;

namespace Travela.Api.Controllers;

[ApiController]
[Route("api/reservations")]
[Authorize]
public class ReservationsController(TravelaDbContext db) : ControllerBase
{
    [HttpGet("mine")]
    [Authorize(Roles = "Passenger")]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken)
    {
        var passengerId = await GetPassengerId(cancellationToken);
        if (passengerId == null) return Unauthorized(new { message = "Aktif bir yolcu hesabıyla giriş yapınız." });
        var items = await Project(db.Reservations.AsNoTracking().Where(r => r.PassengerId == passengerId && r.Kind == "Legacy"))
            .OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken);
        items.ForEach(SetUtc);
        return Ok(items);
    }

    [HttpPost]
    [Authorize(Roles = "Passenger")]
    public IActionResult Create() => StatusCode(410, new { message = "Bağımsız rezervasyon kapatıldı. Bilet almak için ödeme adımını kullanınız." });

    [HttpPost("{number}/cancel")]
    [Authorize(Roles = "Passenger")]
    public async Task<IActionResult> Cancel(string number, CancellationToken cancellationToken)
    {
        var passengerId = await GetPassengerId(cancellationToken);
        if (passengerId == null) return Unauthorized(new { message = "Aktif bir yolcu hesabıyla giriş yapınız." });
        var tripId = await db.Reservations.AsNoTracking()
            .Where(r => r.ReservationNumber == number && r.PassengerId == passengerId && r.Kind == "Legacy")
            .Select(r => (int?)r.TripId).SingleOrDefaultAsync(cancellationToken);
        if (tripId == null) return NotFound(new { message = "Rezervasyon bulunamadı." });
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            // Use the same lock order as Create and trip cancellation: trip first, reservations second.
            var trip = await db.Trips.FromSqlInterpolated($"SELECT * FROM [Trips] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {tripId.Value}")
                .SingleAsync(cancellationToken);
            var reservation = await db.Reservations.SingleAsync(r => r.ReservationNumber == number && r.PassengerId == passengerId && r.Kind == "Legacy", cancellationToken);
            if (reservation.Status == "Active")
            {
                if (trip.DepartureTime <= DateTime.UtcNow)
                    return Conflict(new { message = "Kalkış zamanı gelmiş rezervasyon iptal edilemez." });
                reservation.Status = "Cancelled";
                reservation.CancelledAt = DateTime.UtcNow;
                reservation.CancellationReason = "Yolcu tarafından iptal edildi.";
                trip.AvailableSeats++;
                trip.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            var result = await Project(db.Reservations.AsNoTracking().Where(r => r.Id == reservation.Id)).SingleAsync(cancellationToken);
            SetUtc(result);
            return Ok(result);
        }
        catch (Exception exception) when (exception is SqlException { Number: 1205 } || exception is DbUpdateException { InnerException: SqlException { Number: 1205 } })
        {
            return Conflict(new { message = "Eş zamanlı bir işlem nedeniyle iptal tamamlanamadı. Rezervasyonlarınızı yenileyip tekrar deneyiniz." });
        }
    }

    [HttpGet("company")]
    [Authorize(Roles = "Company")]
    public async Task<IActionResult> CompanyReservations([FromQuery] int? tripId, [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo, [FromQuery] string? status, [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20, [FromQuery] Guid? orderId = null, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        var user = await db.Users.AsNoTracking().ForSession(User, db).SingleOrDefaultAsync(u => u.Id == userId && u.Role == "Company", cancellationToken);
        if (user?.CompanyId == null) return Forbid();
        if (pageNumber < 1 || pageSize < 1 || pageSize > 100 || tripId <= 0 || dateFrom > dateTo || status is not (null or "" or "Active" or "Cancelled"))
            return BadRequest(new { message = "Sefer, tarih, durum veya sayfa filtresi geçersiz." });
        // Never accept a company id from the caller. Scope every query to the authenticated company.
        var query = db.Reservations.AsNoTracking().Where(r => r.Trip.CompanyId == user.CompanyId);
        if (orderId.HasValue)
        {
            if (!await db.PaymentOrders.AnyAsync(p => p.Id == orderId && p.Reservation.Trip.CompanyId == user.CompanyId, cancellationToken)) return NotFound(new { message = "İşlem bulunamadı." });
            query = query.Where(r => r.Payment != null && r.Payment.Id == orderId);
        }
        if (tripId.HasValue)
        {
            if (!await db.Trips.AnyAsync(t => t.Id == tripId && t.CompanyId == user.CompanyId, cancellationToken))
                return NotFound(new { message = "Sefer bulunamadı." });
            query = query.Where(r => r.TripId == tripId);
        }
        if (dateFrom.HasValue)
        {
            var start = new DateTimeOffset(dateFrom.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(3)).UtcDateTime;
            query = query.Where(r => r.Trip.DepartureTime >= start);
        }
        if (dateTo.HasValue)
        {
            var end = new DateTimeOffset(dateTo.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.FromHours(3)).UtcDateTime;
            query = query.Where(r => r.Trip.DepartureTime <= end);
        }
        if (!string.IsNullOrEmpty(status)) query = query.Where(r => r.Status == status);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((int)Math.Min((long)(pageNumber - 1) * pageSize, int.MaxValue)).Take(pageSize)
            .Select(r => new CompanyReservationDto
            {
                ReservationNumber = r.ReservationNumber, TripId = r.TripId, SeatNumber = r.SeatNumber, Price = r.Price,
                PassengerName = r.PassengerNameSnapshot ?? "Yolcu bilgisi kaydedilmemiş", CompanyName = r.Trip.Company.Name, TicketJourneyJson = r.TicketJourneyJson,
                LastError = r.Payment == null || r.Payment.LastError == null ? null : r.Payment.LastError.Replace("Yerel test adaptörü iade hatası.", "İade işlemi tamamlanamadı."),
                NeedsAttention = r.Payment != null && r.Payment.NeedsAttention,
                RefundAttempts = r.Payment == null ? 0 : r.Payment.RefundAttempts,
                DepartureCity = r.Trip.Route.DepartureCity, ArrivalCity = r.Trip.Route.ArrivalCity,
                DepartureTime = r.Trip.DepartureTime, ArrivalTime = r.Trip.ArrivalTime, CreatedAt = r.CreatedAt,
                TripStatus = r.Trip.Status, Status = r.Status, CancelledAt = r.CancelledAt, CancellationReason = r.CancellationReason,
                Kind = r.Kind, Pnr = r.Pnr, PaymentStatus = r.Payment == null ? "Unpaid" : r.Payment.PaymentStatus,
                RefundStatus = r.Payment == null ? "None" : r.Payment.RefundStatus,
                RefundAmount = r.Payment == null ? 0 : r.Payment.RefundAmount, IsTest = r.Payment != null && r.Payment.IsTest
            }).ToListAsync(cancellationToken);
        items.ForEach(SetUtc);
        return Ok(new { items, totalCount, pageNumber, pageSize });
    }

    private async Task<int?> GetPassengerId(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await db.Users.ForSession(User, db).AnyAsync(u => u.Id == id && u.Role == "Passenger", cancellationToken) ? id : null;
    }

    private static IQueryable<ReservationDto> Project(IQueryable<Reservation> query) => query.Select(r => new ReservationDto
    {
        ReservationNumber = r.ReservationNumber, TripId = r.TripId, SeatNumber = r.SeatNumber, Price = r.Price,
        CreatedAt = r.CreatedAt, CompanyName = r.Trip.Company.Name,
        DepartureCity = r.Trip.Route.DepartureCity, ArrivalCity = r.Trip.Route.ArrivalCity,
        DepartureTime = r.Trip.DepartureTime, ArrivalTime = r.Trip.ArrivalTime, TripStatus = r.Trip.Status,
        Status = r.Status, CancelledAt = r.CancelledAt, CancellationReason = r.CancellationReason,
        CanCancel = r.Kind == "Legacy" && r.Status == "Active" && r.Trip.DepartureTime > DateTime.UtcNow
    });

    private static void SetUtc(ReservationDto item)
    {
        if (item.TicketJourneyJson != null) { var journey = Travela.Api.Trips.TicketJourney.Read(item.TicketJourneyJson); item.CompanyName = journey.CompanyName; item.DepartureCity = journey.DepartureCity; item.ArrivalCity = journey.ArrivalCity; item.DepartureTime = journey.DepartureTime; item.ArrivalTime = journey.ArrivalTime; }
        item.CreatedAt = DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc);
        if (item.CancelledAt.HasValue) item.CancelledAt = DateTime.SpecifyKind(item.CancelledAt.Value, DateTimeKind.Utc);
        item.DepartureTime = DateTime.SpecifyKind(item.DepartureTime, DateTimeKind.Utc);
        item.ArrivalTime = DateTime.SpecifyKind(item.ArrivalTime, DateTimeKind.Utc);
    }
}
