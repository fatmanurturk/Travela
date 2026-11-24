using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Payments;
using Travela.Api.Security;
using Travela.Api.Trips;
using Travela.Data;

namespace Travela.Api.Controllers;

public record CreateSeatSession(Guid Id, string Token, int TripId, int? BoardingStopOrder, int? AlightingStopOrder);
public record SelectSeat(int SeatNumber, string Gender);
[ApiController, Route("api/seat-selections"), AllowAnonymous, EnableRateLimiting("seat-selection-read")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[RequestSizeLimit(4096)]
public sealed class SeatSelectionsController(SeatSelectionService selections, TravelaDbContext db) : ControllerBase
{
    private string? Token => Request.Headers["X-Seat-Token"].FirstOrDefault();
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        Response.Headers.CacheControl = "private, no-store";
        try { return Ok(await action()); }
        catch (PaymentFlowException e) { return StatusCode(e.Status, new { e.Code, message = e.Message }); }
        catch (Exception e) when (e is SqlException { Number: 1205 or 2601 or 2627 or 51011 or 51031 } || e is DbUpdateException { InnerException: SqlException { Number: 1205 or 2601 or 2627 or 51011 or 51031 } })
        { return Conflict(new { code = "seat_unavailable", message = SeatSelectionService.TakenMessage }); }
    }
    [HttpPost, EnableRateLimiting("seat-selection-create")]
    public Task<IActionResult> Create(CreateSeatSession dto, CancellationToken ct) => Run(async () => {
        var userId = await db.Users.AsNoTracking().ForSession(User, db).Where(u => u.Role == "Passenger").Select(u => (int?)u.Id).SingleOrDefaultAsync(ct);
        return await selections.Create(dto.Id, dto.Token ?? "", dto.TripId, dto.BoardingStopOrder, dto.AlightingStopOrder, HttpContext.Connection.RemoteIpAddress!.ToString(), userId, ct);
    });
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Run(() => selections.Get(id, Token, ct));
    [HttpPut("{id:guid}/seats"), EnableRateLimiting("seat-selection-write")]
    public Task<IActionResult> Select(Guid id, SelectSeat dto, CancellationToken ct) => Run(() => selections.Change(id, Token, dto.SeatNumber, dto.Gender, false, ct));
    [HttpDelete("{id:guid}/seats/{seat:int}"), EnableRateLimiting("seat-selection-write")]
    public Task<IActionResult> Remove(Guid id, int seat, CancellationToken ct) => Run(() => selections.Change(id, Token, seat, null, true, ct));
}
