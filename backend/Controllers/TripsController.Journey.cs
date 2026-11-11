using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Trips;
using Travela.DTOs;

namespace Travela.Api.Controllers;

public sealed class SeatBlocksDto { [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(1000)] public List<int> BlockedSeats { get; set; } = []; }
public sealed class PhysicalSeatPlanDto
{
    public string Layout { get; set; } = "Custom";
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(1000)] public List<SeatPosition> Seats { get; set; } = [];
    public bool Confirmed { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(1000)] public List<int> ApplyToTripIds { get; set; } = [];
    public string? ExpectedPlanVersion { get; set; }
}

public partial class TripsController
{
    [HttpPut("{id:int}/seat-plan"), Authorize(Policy = "trip.update")]
    public async Task<IActionResult> DefineSeatPlan(int id, PhysicalSeatPlanDto dto, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(); if (user == null) return Unauthorized();
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var trip = await _context.Trips.FromSqlInterpolated($"SELECT * FROM [Trips] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}").Include(t => t.Bus).SingleOrDefaultAsync(ct);
        if (trip == null || !CanAccessTrip(user, trip)) return NotFound(new { message = "Sefer bulunamadı." });
        if (!dto.Confirmed || dto.Layout is not ("2+1" or "2+2" or "Custom")) return BadRequest(new { message = "Fiziksel koltuk yerleşimini doğrulayınız." });
        if (NormalizeStatus(trip.Status) != "Scheduled" || trip.DepartureTime <= DateTime.UtcNow) return Conflict(new { message = "Seferin koltuk planı artık değiştirilemez." });
        List<SeatPosition> seats;
        try { seats = SeatPlans.ReadVerified(SeatPlans.Write(dto.Seats), dto.Layout, trip.SeatCapacitySnapshot ?? trip.Bus.SeatCapacity); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        var occupied = await _context.Reservations.Where(r => r.TripId == id && r.Status == "Active").Select(r => r.SeatNumber).ToListAsync(ct);
        if (seats.Any(s => s.IsBlocked && occupied.Contains(s.Number))) return Conflict(new { message = "Satılmış veya tutulan koltuk kapatılamaz." });
        if (occupied.Count > 0 && trip.SeatLayout != "Unspecified") return Conflict(new { message = "Aktif rezervasyonu olan doğrulanmış seferin fiziksel planı değiştirilemez; yalnızca koltuk blokajlarını güncelleyebilirsiniz." });
        trip.SeatPlanJson = SeatPlans.Write(seats); trip.SeatLayout = dto.Layout;
        trip.SeatCapacitySnapshot ??= trip.Bus.SeatCapacity; trip.SellableSeatCount = seats.Count(s => !s.IsBlocked);
        trip.AvailableSeats = trip.SellableSeatCount.Value - occupied.Count; trip.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Ok(new { message = "Seferin fiziksel koltuk planı doğrulandı ve kaydedildi." });
    }

    private async Task<IActionResult?> PrepareJourney(Trip trip, Bus bus, Travela.Api.Models.Route route, CreateTripDto dto)
    {
        List<SeatPosition> seats;
        try { seats = bus.SeatLayout == "Unspecified" ? SeatPlans.Read(bus.SeatPlanJson, bus.SeatLayout, bus.SeatCapacity) : SeatPlans.ReadVerified(bus.SeatPlanJson, bus.SeatLayout, bus.SeatCapacity); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        if (seats.All(s => s.IsBlocked)) return BadRequest(new { message = "Otobüste satışa açık koltuk bulunmuyor." });
        if (dto.DepartureTerminalId.HasValue != dto.ArrivalTerminalId.HasValue)
            return BadRequest(new { message = "Kalkış ve varış terminallerini birlikte seçin." });
        var departure = dto.DepartureTerminalId.HasValue ? await _context.Terminals.SingleOrDefaultAsync(t => t.Id == dto.DepartureTerminalId) : null;
        var arrival = dto.ArrivalTerminalId.HasValue ? await _context.Terminals.SingleOrDefaultAsync(t => t.Id == dto.ArrivalTerminalId) : null;
        if (dto.DepartureTerminalId.HasValue && (departure == null || arrival == null || !departure.IsActive || !arrival.IsActive ||
            !string.Equals(departure.City, route.DepartureCity, StringComparison.OrdinalIgnoreCase) || !string.Equals(arrival.City, route.ArrivalCity, StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { message = "Terminaller aktif olmalı ve güzergâhın kalkış/varış şehirleriyle eşleşmelidir." });
        trip.SeatLayout = bus.SeatLayout; trip.SeatPlanJson = SeatPlans.Write(seats); trip.SeatCapacitySnapshot = bus.SeatCapacity;
        trip.SellableSeatCount = seats.Count(s => !s.IsBlocked); trip.AvailableSeats = trip.SellableSeatCount.Value;
        trip.DepartureTerminalId = departure?.Id; trip.ArrivalTerminalId = arrival?.Id;
        trip.DepartureTerminalName = departure?.Name; trip.ArrivalTerminalName = arrival?.Name;
        trip.DepartureTerminalAddress = departure == null ? null : departure.Address + ", " + departure.City;
        trip.ArrivalTerminalAddress = arrival == null ? null : arrival.Address + ", " + arrival.City;
        trip.BoardingInstructions = string.IsNullOrWhiteSpace(dto.BoardingInstructions) ? null : dto.BoardingInstructions.Trim();
        return null;
    }

    [HttpPut("{id:int}/seat-blocks"), Authorize(Policy = "trip.update")]
    public async Task<IActionResult> UpdateSeatBlocks(int id, SeatBlocksDto dto, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(); if (user == null) return Unauthorized();
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var trip = await _context.Trips.FromSqlInterpolated($"SELECT * FROM [Trips] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}").Include(t => t.Bus).SingleOrDefaultAsync(ct);
        if (trip == null || !CanAccessTrip(user, trip)) return NotFound(new { message = "Sefer bulunamadı." });
        if (NormalizeStatus(trip.Status) != "Scheduled" || trip.DepartureTime <= DateTime.UtcNow)
            return Conflict(new { message = "Yalnızca kalkışı gelmemiş planlanan seferin koltukları değiştirilebilir." });
        List<SeatPosition> seats;
        try
        {
            seats = SeatPlans.ReadVerified(trip.SeatPlanJson, trip.SeatLayout, trip.SeatCapacitySnapshot ?? trip.Bus.SeatCapacity);
            if (dto.BlockedSeats.Distinct().Count() != dto.BlockedSeats.Count || dto.BlockedSeats.Any(n => !seats.Any(s => s.Number == n))) throw new ArgumentException("Satışa kapalı koltuk numaraları geçersiz.");
            seats = seats.Select(s => s with { IsBlocked = dto.BlockedSeats.Contains(s.Number) }).ToList();
        }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        var occupied = await _context.Reservations.Where(r => r.TripId == id && r.Status == "Active").Select(r => r.SeatNumber).ToListAsync(ct);
        if (seats.Any(s => s.IsBlocked && occupied.Contains(s.Number))) return Conflict(new { message = "Satılmış veya ödeme için tutulmuş koltuk satışa kapatılamaz." });
        if (trip.AvailableSeats != (trip.SellableSeatCount ?? trip.Bus.SeatCapacity) - occupied.Count)
            return Conflict(new { message = "Mevcut kontenjan doğrulanmadan koltuk blokajı değiştirilemez." });
        trip.SeatPlanJson = SeatPlans.Write(seats); trip.SellableSeatCount = seats.Count(s => !s.IsBlocked);
        trip.AvailableSeats = trip.SellableSeatCount.Value - occupied.Count; trip.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Ok(new { message = "Seferin satışa kapalı koltukları güncellendi." });
    }
}
