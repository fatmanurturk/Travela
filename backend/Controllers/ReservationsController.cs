using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Data;
using Travela.DTOs;

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
        var items = await Project(db.Reservations.AsNoTracking().Where(r => r.PassengerId == passengerId))
            .OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken);
        items.ForEach(SetUtc);
        return Ok(items);
    }

    [HttpPost]
    [Authorize(Roles = "Passenger")]
    public async Task<IActionResult> Create(CreateReservationDto dto, CancellationToken cancellationToken)
    {
        var passengerId = await GetPassengerId(cancellationToken);
        if (passengerId == null) return Unauthorized(new { message = "Aktif bir yolcu hesabıyla giriş yapınız." });
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            // Lock the trip before reading availability. All reservations on this trip serialize here.
            // The unique index remains the final database-level guarantee for seat ownership.
            var trip = await db.Trips.FromSqlInterpolated($"SELECT * FROM [Trips] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {dto.TripId}")
                .SingleOrDefaultAsync(cancellationToken);
            if (trip == null) return NotFound(new { message = "Sefer bulunamadı." });
            await db.Entry(trip).Reference(t => t.Bus).LoadAsync(cancellationToken);
            await db.Entry(trip).Reference(t => t.Company).LoadAsync(cancellationToken);
            await db.Entry(trip).Reference(t => t.Route).LoadAsync(cancellationToken);
            var status = trip.Status.ToLowerInvariant();
            if (status is not ("scheduled" or "planlandı" or "planlandi") || trip.DepartureTime <= DateTime.UtcNow ||
                !trip.Company.IsActive || !trip.Bus.IsActive || !trip.Route.IsActive || trip.Bus.CompanyId != trip.CompanyId)
                return Conflict(new { message = "Bu sefer rezervasyona açık değil. İptal edilmiş veya kalkış zamanı geçmiş olabilir." });
            if (dto.SeatNumber < 1 || dto.SeatNumber > trip.Bus.SeatCapacity)
                return BadRequest(new { message = "Koltuk numarası otobüs kapasitesi içinde olmalıdır." });
            var reservedCount = await db.Reservations.CountAsync(r => r.TripId == trip.Id && r.Status == "Active", cancellationToken);
            if (trip.AvailableSeats != trip.Bus.SeatCapacity - reservedCount)
                return Conflict(new { message = "Seferin eski kontenjan bilgileri koltuk kayıtlarıyla eşleşmiyor. Firma tarafından doğrulanana kadar rezervasyon yapılamaz." });
            if (await db.Reservations.AnyAsync(r => r.TripId == trip.Id && r.SeatNumber == dto.SeatNumber && r.Status == "Active", cancellationToken))
                return Conflict(new { message = "Bu koltuk az önce rezerve edildi. Başka bir koltuk seçiniz." });
            if (trip.AvailableSeats <= 0)
                return Conflict(new { message = "Bu seferde boş koltuk kalmadı." });

            var reservation = new Reservation { TripId = trip.Id, PassengerId = passengerId.Value, SeatNumber = dto.SeatNumber, Price = trip.Price };
            db.Reservations.Add(reservation);
            trip.AvailableSeats--;
            trip.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var result = await Project(db.Reservations.AsNoTracking().Where(r => r.Id == reservation.Id)).SingleAsync(cancellationToken);
            SetUtc(result);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (Exception exception) when (exception is SqlException { Number: 1205 or 2601 or 2627 } ||
            exception is DbUpdateException { InnerException: SqlException { Number: 1205 or 2601 or 2627 } })
        {
            return Conflict(new { message = "Koltuk başka bir işlemde rezerve edilmiş olabilir. Koltuk planını yenileyip tekrar deneyiniz." });
        }
    }

    [HttpPost("{number}/cancel")]
    [Authorize(Roles = "Passenger")]
    public async Task<IActionResult> Cancel(string number, CancellationToken cancellationToken)
    {
        var passengerId = await GetPassengerId(cancellationToken);
        if (passengerId == null) return Unauthorized(new { message = "Aktif bir yolcu hesabıyla giriş yapınız." });
        var tripId = await db.Reservations.AsNoTracking()
            .Where(r => r.ReservationNumber == number && r.PassengerId == passengerId)
            .Select(r => (int?)r.TripId).SingleOrDefaultAsync(cancellationToken);
        if (tripId == null) return NotFound(new { message = "Rezervasyon bulunamadı." });
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            // Use the same lock order as Create and trip cancellation: trip first, reservations second.
            var trip = await db.Trips.FromSqlInterpolated($"SELECT * FROM [Trips] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {tripId.Value}")
                .SingleAsync(cancellationToken);
            var reservation = await db.Reservations.SingleAsync(r => r.ReservationNumber == number && r.PassengerId == passengerId, cancellationToken);
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
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive && u.Role == "Company" && !u.MustChangePassword, cancellationToken);
        if (user?.CompanyId == null || !await db.Companies.AnyAsync(c => c.Id == user.CompanyId && c.IsActive, cancellationToken)) return Forbid();
        if (pageNumber < 1 || pageSize < 1 || pageSize > 100 || tripId <= 0 || dateFrom > dateTo || status is not (null or "" or "Active" or "Cancelled"))
            return BadRequest(new { message = "Sefer, tarih, durum veya sayfa filtresi geçersiz." });
        // Never accept a company id from the caller. Scope every query to the authenticated company.
        var query = db.Reservations.AsNoTracking().Where(r => r.Trip.CompanyId == user.CompanyId);
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
                PassengerName = r.Passenger.FullName, CompanyName = r.Trip.Company.Name,
                DepartureCity = r.Trip.Route.DepartureCity, ArrivalCity = r.Trip.Route.ArrivalCity,
                DepartureTime = r.Trip.DepartureTime, ArrivalTime = r.Trip.ArrivalTime, CreatedAt = r.CreatedAt,
                TripStatus = r.Trip.Status, Status = r.Status, CancelledAt = r.CancelledAt, CancellationReason = r.CancellationReason
            }).ToListAsync(cancellationToken);
        items.ForEach(SetUtc);
        return Ok(new { items, totalCount, pageNumber, pageSize });
    }

    private async Task<int?> GetPassengerId(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await db.Users.AnyAsync(u => u.Id == id && u.IsActive && u.Role == "Passenger" && !u.MustChangePassword, cancellationToken) ? id : null;
    }

    private static IQueryable<ReservationDto> Project(IQueryable<Reservation> query) => query.Select(r => new ReservationDto
    {
        ReservationNumber = r.ReservationNumber, TripId = r.TripId, SeatNumber = r.SeatNumber, Price = r.Price,
        CreatedAt = r.CreatedAt, CompanyName = r.Trip.Company.Name,
        DepartureCity = r.Trip.Route.DepartureCity, ArrivalCity = r.Trip.Route.ArrivalCity,
        DepartureTime = r.Trip.DepartureTime, ArrivalTime = r.Trip.ArrivalTime, TripStatus = r.Trip.Status,
        Status = r.Status, CancelledAt = r.CancelledAt, CancellationReason = r.CancellationReason,
        CanCancel = r.Status == "Active" && r.Trip.DepartureTime > DateTime.UtcNow
    });

    private static void SetUtc(ReservationDto item)
    {
        item.CreatedAt = DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc);
        if (item.CancelledAt.HasValue) item.CancelledAt = DateTime.SpecifyKind(item.CancelledAt.Value, DateTimeKind.Utc);
        item.DepartureTime = DateTime.SpecifyKind(item.DepartureTime, DateTimeKind.Utc);
        item.ArrivalTime = DateTime.SpecifyKind(item.ArrivalTime, DateTimeKind.Utc);
    }
}
