using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Trips;
using Travela.DTOs;

namespace Travela.Api.Controllers;

public partial class TripsController
{
    private async Task<IActionResult?> PrepareCrew(Trip trip, CrewSelectionDto? crew,
        IReadOnlyList<Trip>? pending = null, int? batchIndex = null, CancellationToken ct = default)
    {
        try { await new TripCrewService(_context).Prepare(trip, crew, pending, ct); return null; }
        catch (CrewValidationException e) { return BadRequest(new { message = e.Message }); }
        catch (CrewConflictException e) { return Conflict(new { code = "crew_conflict", message = e.Message, conflicts = e.Conflicts, batchIndex }); }
        catch (SqlException e) when (e.Number == 1205) { return Conflict(new { code = "crew_concurrent_change", message = "Eşzamanlı ekip işlemi var. Bilgileri yenileyip tekrar deneyin." }); }
    }

    [HttpGet("{id:int}/crew"), Authorize(Roles = "Company", Policy = "trip.view")]
    public async Task<IActionResult> GetCrew(int id, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(); if (user == null) return Unauthorized();
        var trip = await _context.Trips.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        if (trip == null || !CanAccessTrip(user, trip)) return NotFound(new { message = "Sefer bulunamadı." });
        Response.Headers.CacheControl = "private, no-store";
        var crew = await _context.TripCrews.AsNoTracking().Where(c => c.TripId == id)
            .Select(c => new { id = c.StaffId, c.Staff.FullName, c.Duty, currentDuty = c.Staff.Duty, c.Staff.IsActive }).ToListAsync(ct);
        return Ok(new { crew, trip.ChangeVersion });
    }

    [HttpPut("{id:int}/crew"), Authorize(Roles = "Company", Policy = "trip.update")]
    public async Task<IActionResult> SaveCrew(int id, SaveCrewDto dto, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(); if (user == null) return Unauthorized();
        await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var trip = await _context.Trips.FromSqlInterpolated($"SELECT * FROM Trips WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync(ct);
        if (trip == null || !CanAccessTrip(user, trip)) return NotFound(new { message = "Sefer bulunamadı." });
        if (trip.ChangeVersion != dto.ExpectedVersion) return Conflict(new { message = "Sefer değişti. Detayı yenileyip tekrar deneyin." });
        if (NormalizeStatus(trip.Status) is not ("Scheduled" or "InProgress")) return Conflict(new { message = "Yalnızca aktif seferlerin ekibi düzenlenebilir." });
        var error = await PrepareCrew(trip, dto.Crew, ct: ct); if (error != null) return error;
        trip.ChangeVersion++; trip.UpdatedAt = DateTime.UtcNow;
        try { await _context.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (Exception e) when (IsConcurrencyConflict(e)) { return Conflict(new { message = "Eşzamanlı ekip işlemi var. Detayı yenileyin." }); }
        return Ok(new { message = "Görevli ekip kaydedildi." });
    }
}
