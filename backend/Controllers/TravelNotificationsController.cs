using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Security;
using Travela.Data;

namespace Travela.Api.Controllers;

public sealed class RetryNotificationDto
{
    [Required, MinLength(10), MaxLength(1000)] public string Reason { get; set; } = "";
    public bool ConfirmPossibleDuplicate { get; set; }
}

[ApiController, Route("api/travel-notifications"), Authorize(Roles = "Admin,Company")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class TravelNotificationsController(TravelaDbContext db, TimeProvider clock) : ControllerBase
{
    private Task<Travela.Models.User?> Current(CancellationToken ct) => db.Users.AsNoTracking().ForSession(User, db).SingleOrDefaultAsync(ct);
    private IQueryable<TravelNotification> Scoped(Travela.Models.User user) => db.TravelNotifications.Where(n => user.Role == "Admin" || n.Reservation.Trip.CompanyId == user.CompanyId);
    [HttpGet]
    public async Task<IActionResult> List(string? q, string? status, string? channel, int page = 1, CancellationToken ct = default)
    {
        var user = await Current(ct); if (user == null) return Unauthorized();
        if (page < 1 || page > 100000) return BadRequest();
        var query = Scoped(user).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim(); query = query.Where(n => n.Recipient.Contains(term) || n.Reservation.Pnr == term || n.Reservation.Trip.Company.Name.Contains(term)); }
        if (!string.IsNullOrEmpty(status)) query = query.Where(n => n.Status == status);
        if (!string.IsNullOrEmpty(channel)) query = query.Where(n => n.Channel == channel);
        return Ok(new { totalCount = await query.CountAsync(ct), items = await query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id).Skip((page - 1) * 30).Take(30)
            .Select(n => new { n.Id, n.Type, n.Channel, n.Recipient, n.Subject, n.Status, n.Attempts, n.Polls, n.CreatedAt, n.NextAttemptAt, n.CompletedAt, n.LastError, n.ProviderReference, n.Reservation.Pnr, n.Reservation.TripId }).ToListAsync(ct) });
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var user = await Current(ct); if (user == null) return Unauthorized();
        var message = await Scoped(user).AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, ct); if (message == null) return NotFound();
        return Ok(new { message, attempts = await db.NotificationAttempts.AsNoTracking().Where(a => a.NotificationId == id).OrderBy(a => a.CreatedAt).ToListAsync(ct) });
    }
    [HttpPost("{id:guid}/retry"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Retry(Guid id, RetryNotificationDto dto, CancellationToken ct)
    {
        var user = await Current(ct); if (user == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Trim().Length < 10) return BadRequest(new { message = "Açıklayıcı gerekçe gereklidir." });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await db.TravelNotifications.FromSqlInterpolated($"SELECT * FROM TravelNotifications WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}").SingleOrDefaultAsync(ct);
        if (n == null) return NotFound();
        if (n.Status is not ("Failed" or "Unknown") || n.LeaseUntil > clock.GetUtcNow().UtcDateTime) return Conflict(new { message = "Yalnızca başarısız veya sonucu belirsiz bildirim yeniden gönderilebilir." });
        if (n.Status == "Unknown" && !dto.ConfirmPossibleDuplicate) return BadRequest(new { message = "Sağlayıcıda gönderim kontrolünü ve olası çift gönderimi onaylayın." });
        n.Status = "Pending"; n.Attempts = 0; n.Polls = 0; n.ProviderReference = null; n.CompletedAt = null; n.LastError = null; n.NextAttemptAt = clock.GetUtcNow().UtcDateTime;
        db.NotificationAttempts.Add(new() { NotificationId = id, CreatedAt = clock.GetUtcNow().UtcDateTime, Status = "ManualRetry", ActorId = user.Id, Reason = dto.Reason.Trim() });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Ok(new { message = "Bildirim gerekçeyle yeniden kuyruğa alındı." });
    }
}

[ApiController, Route("api/tickets"), Authorize(Roles = "Passenger")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class TicketCommunicationsController(TravelaDbContext db) : ControllerBase
{
    [HttpGet("{pnr}/communications")]
    public async Task<IActionResult> Get(string pnr, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().ForSession(User, db).SingleOrDefaultAsync(ct); if (user == null) return Unauthorized();
        var id = await db.Reservations.Where(r => r.Pnr == pnr && r.PassengerId == user.Id && r.Kind == "Ticket").Select(r => (int?)r.Id).SingleOrDefaultAsync(ct);
        if (id == null) return NotFound();
        var changes = await db.TripChangeTickets.AsNoTracking().Where(c => c.ReservationId == id).OrderByDescending(c => c.TripChange.Version).Select(c => c.TripChange).ToListAsync(ct);
        return Ok(new { changes, notifications = await db.TravelNotifications.AsNoTracking().Where(n => n.ReservationId == id).OrderByDescending(n => n.CreatedAt).Take(30)
            .Select(n => new { n.Id, n.Type, n.Channel, n.Status, n.CreatedAt, n.CompletedAt, n.LastError }).ToListAsync(ct) });
    }
}
