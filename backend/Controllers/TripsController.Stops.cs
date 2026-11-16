using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Trips;
using Travela.DTOs;

namespace Travela.Api.Controllers;

public partial class TripsController
{
    [HttpGet("{id:int}/stops"), Authorize(Policy = "trip.view")]
    public async Task<IActionResult> GetStops(int id, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(); if (user == null) return Unauthorized();
        var trip = await _context.Trips.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        if (trip == null || !CanAccessTrip(user, trip)) return NotFound();
        var stops = await _context.TripStops.AsNoTracking().Where(s => s.TripId == id).OrderBy(s => s.StopOrder).ToListAsync(ct);
        foreach (var stop in stops) { stop.ArrivalTime = AsUtc(stop.ArrivalTime); stop.DepartureTime = AsUtc(stop.DepartureTime); }
        var fares = await _context.TripSegmentFares.AsNoTracking().Where(f => f.TripId == id).ToListAsync(ct);
        var missing = (from a in stops where a.CanBoard from b in stops where b.CanAlight && b.StopOrder > a.StopOrder
            where !fares.Any(f => f.FromStopOrder == a.StopOrder && f.ToStopOrder == b.StopOrder && f.IsApproved)
            select new { fromStopOrder = a.StopOrder, toStopOrder = b.StopOrder }).ToList();
        return Ok(new { stops, fares, missingFares = missing, trip.ChangeVersion, topologyLocked = await _context.Reservations.AnyAsync(r => r.TripId == id, ct) });
    }

    [HttpPut("{id:int}/stops"), Authorize(Policy = "trip.update")]
    public async Task<IActionResult> PutStops(int id, SaveTripStopsDto dto, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(); if (user == null) return Unauthorized();
        if (!await _context.Trips.AnyAsync(t => t.Id == id, ct)) return NotFound();
        await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var trip = await Segments.LockTrip(id, ct);
        if (!CanAccessTrip(user, trip)) return NotFound();
        if (trip.ChangeVersion != dto.ExpectedVersion || NormalizeStatus(trip.Status) != "Scheduled" || trip.DepartureTime <= DateTime.UtcNow)
            return Conflict(new { message = "Sefer değişti veya kalkışı geldi. Güncel bilgileri yükleyiniz." });
        try {
            if (dto.Stops.Count < 2) return BadRequest(new { message = "En az iki durak gereklidir." });
            var departure = dto.Stops[0].DepartureTime.UtcDateTime; var arrival = dto.Stops[^1].ArrivalTime.UtcDateTime;
            if (departure <= DateTime.UtcNow || arrival <= departure) return BadRequest(new { message = "Seferin kalkış ve varış tarihlerini kontrol ediniz." });
            if (await HasBusOverlapAsync(trip.BusId, departure, arrival, id)) return Conflict(new { message = "Bu saatlerde otobüs başka bir sefere atanmış." });
            trip.DepartureTime = departure; trip.ArrivalTime = arrival;
            if (dto.FullPrice.HasValue) trip.Price = dto.FullPrice.Value;
            await TripJourneyManagement.SetJourney(_context, trip, dto.Stops, dto.Fares, ct);
            trip.ChangeVersion++; trip.UpdatedAt = DateTime.UtcNow;
            await Segments.SaveInventory(trip, ct); await tx.CommitAsync(ct);
            return Ok(new { message = "Durak saatleri, izinleri ve kesim fiyatları kaydedildi." });
        } catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    [HttpPut("{id:int}/fares"), Authorize(Policy = "trip.update")]
    public async Task<IActionResult> PutFares(int id, SaveTripStopsDto dto, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(); if (user == null) return Unauthorized();
        if (!await _context.Trips.AnyAsync(t => t.Id == id, ct)) return NotFound();
        await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var trip = await Segments.LockTrip(id, ct); if (!CanAccessTrip(user, trip)) return NotFound();
        if (trip.ChangeVersion != dto.ExpectedVersion) return Conflict(new { message = "Sefer değişti; güncel fiyatları yükleyiniz." });
        var count = await _context.TripStops.CountAsync(s => s.TripId == id, ct);
        if (dto.FullPrice.HasValue) {
            if (dto.FullPrice != decimal.Round(dto.FullPrice.Value,2)) return BadRequest(new { message = "Fiyat en fazla iki ondalık basamak içermelidir." });
            trip.Price = dto.FullPrice.Value;
            var full = await _context.TripSegmentFares.SingleAsync(f => f.TripId == id && f.FromStopOrder == 1 && f.ToStopOrder == count, ct);
            full.Amount = trip.Price;
        }
        if (dto.Fares.Any(f => f.FromStopOrder < 1 || f.ToStopOrder > count || f.FromStopOrder >= f.ToStopOrder || f.Amount != decimal.Round(f.Amount, 2)) ||
            dto.Fares.Select(f => (f.FromStopOrder, f.ToStopOrder)).Distinct().Count() != dto.Fares.Count)
            return BadRequest(new { message = "Kesim fiyatları geçersiz." });
        await _context.TripSegmentFares.Where(f => f.TripId == id && f.Source != "FullRoute").ExecuteDeleteAsync(ct);
        _context.TripSegmentFares.AddRange(dto.Fares.Where(f => f.FromStopOrder != 1 || f.ToStopOrder != count).Select(f => new Travela.Api.Models.TripSegmentFare {
            TripId = id, FromStopOrder = f.FromStopOrder, ToStopOrder = f.ToStopOrder, Amount = f.Amount, Source = "Manual", IsApproved = f.IsApproved, ApprovedAt = f.IsApproved ? DateTime.UtcNow : null }));
        trip.ChangeVersion++; await _context.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(new { message = "Kesim fiyatları kaydedildi. Mevcut biletlerin tutarları korundu." });
    }

    [HttpPost("{id:int}/fares/suggest"), Authorize(Policy = "trip.update")]
    public async Task<IActionResult> SuggestFares(int id, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(); if (user == null) return Unauthorized();
        if (!await _context.Trips.AnyAsync(t => t.Id == id, ct)) return NotFound();
        await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var trip = await Segments.LockTrip(id, ct); if (!CanAccessTrip(user, trip)) return NotFound();
        try { await Segments.RecalculateAutomaticFares(trip, ct); trip.ChangeVersion++; await _context.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(new { message = "Mesafe/süre oranına göre öneriler hazırlandı. Satış için fiyatları kontrol edip onaylayınız." }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
