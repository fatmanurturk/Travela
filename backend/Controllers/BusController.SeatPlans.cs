using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Trips;

namespace Travela.Api.Controllers;

public partial class BusController
{
    private static string PlanVersion(Bus bus) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{bus.SeatCapacity}|{bus.SeatLayout}|{bus.SeatPlanJson}")));
    private static List<SeatPosition>? VerifiedSeats(string? json, string layout, int capacity)
    {
        try { return SeatPlans.ReadVerified(json, layout, capacity); }
        catch (ArgumentException) { return null; }
    }
    private static string? CannotApply(Trip trip, int active, int capacity, DateTime now)
    {
        if (trip.DepartureTime <= now || trip.Status.ToLowerInvariant() is not ("scheduled" or "planlandı" or "planlandi")) return "Seferin kalkışı geçmiş veya durumu planlanmış değil.";
        if (active > 0) return "Aktif rezervasyon veya bilet var; fiziksel plan değiştirilemez.";
        if ((trip.SeatCapacitySnapshot ?? capacity) != capacity) return "Seferin kayıtlı kapasitesi otobüs kapasitesiyle eşleşmiyor.";
        if (VerifiedSeats(trip.SeatPlanJson, trip.SeatLayout, trip.SeatCapacitySnapshot ?? capacity) != null) return "Seferin fiziksel planı zaten doğrulanmış; mevcut plan korunur.";
        return null;
    }

    [HttpGet("{id:int}/seat-plan"), Authorize(Policy = "bus.view")]
    public async Task<IActionResult> GetPhysicalPlan(int id, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(); if (user == null) return Unauthorized();
        var bus = await _context.Buses.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        if (bus == null || (user.Role != "Admin" && user.CompanyId != bus.CompanyId)) return NotFound(new { message = "Otobüs bulunamadı." });
        var trips = await _context.Trips.AsNoTracking().Where(t => t.BusId == id && t.CompanyId == bus.CompanyId).OrderBy(t => t.DepartureTime)
            .Select(t => new { Trip = t, Active = _context.Reservations.Count(r => r.TripId == t.Id && r.Status == "Active"), t.Route.DepartureCity, t.Route.ArrivalCity }).ToListAsync(ct);
        var seats = VerifiedSeats(bus.SeatPlanJson, bus.SeatLayout, bus.SeatCapacity);
        return Ok(new { bus.Id, bus.PlateNumber, capacity = bus.SeatCapacity, layout = bus.SeatLayout, seats = seats ?? [], verified = seats != null, version = PlanVersion(bus),
            trips = trips.Select(item => new { item.Trip.Id, item.DepartureCity, item.ArrivalCity, departureTime = DateTime.SpecifyKind(item.Trip.DepartureTime, DateTimeKind.Utc),
                seatLayout = item.Trip.SeatLayout, activeReservations = item.Active, canApply = CannotApply(item.Trip, item.Active, bus.SeatCapacity, DateTime.UtcNow) == null,
                reason = CannotApply(item.Trip, item.Active, bus.SeatCapacity, DateTime.UtcNow) }) });
    }

    private async Task<IActionResult> SavePhysicalPlan(int id, PhysicalSeatPlanDto dto, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(); if (user == null) return Unauthorized();
        if (!dto.Confirmed || dto.Layout is not ("2+1" or "2+2" or "Custom") || dto.ApplyToTripIds == null || dto.ApplyToTripIds.Any(t => t < 1) || dto.ApplyToTripIds.Distinct().Count() != dto.ApplyToTripIds.Count)
            return BadRequest(new { message = "Fiziksel yerleşimi doğrulayınız ve geçerli, tekrarsız seferler seçiniz." });
        try
        {
            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            // Use the sales lock order: selected trips first, then their bus.
            var targets = new List<Trip>();
            foreach (var tripId in dto.ApplyToTripIds.Order())
            {
                var trip = await _context.Trips.FromSqlInterpolated($"SELECT * FROM [Trips] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {tripId}").SingleOrDefaultAsync(ct);
                if (trip == null || trip.BusId != id || (user.Role != "Admin" && trip.CompanyId != user.CompanyId)) return NotFound(new { message = "Seçilen sefer bulunamadı veya bu otobüse ait değil." });
                targets.Add(trip);
            }
            var bus = await _context.Buses.FromSqlInterpolated($"SELECT * FROM [Buses] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}").SingleOrDefaultAsync(ct);
            if (bus == null || (user.Role != "Admin" && user.CompanyId != bus.CompanyId)) return NotFound(new { message = "Otobüs bulunamadı." });
            if (dto.ExpectedPlanVersion != null && dto.ExpectedPlanVersion != PlanVersion(bus)) return Conflict(new { message = "Otobüs planı başka bir işlemde değişmiş. Güncel planı yükleyip yeniden inceleyiniz." });
            var seats = SeatPlans.ReadVerified(SeatPlans.Write(dto.Seats), dto.Layout, bus.SeatCapacity);
            if (seats.All(s => s.IsBlocked)) return BadRequest(new { message = "En az bir koltuk satışa açık olmalıdır." });
            foreach (var trip in targets)
            {
                if (trip.CompanyId != bus.CompanyId) return Conflict(new { message = "Sefer ve otobüsün firması eşleşmiyor." });
                var active = await _context.Reservations.CountAsync(r => r.TripId == trip.Id && r.Status == "Active", ct);
                var reason = CannotApply(trip, active, bus.SeatCapacity, DateTime.UtcNow);
                if (reason != null) return Conflict(new { message = $"Sefer #{trip.Id}: {reason} Hiçbir plan değiştirilmedi." });
            }
            bus.SeatLayout = dto.Layout; bus.SeatPlanJson = SeatPlans.Write(seats); bus.UpdatedAt = DateTime.UtcNow;
            foreach (var trip in targets)
            {
                // Retain trip-specific closed seats from legacy drafts when they can be read safely.
                List<SeatPosition> old;
                try { old = SeatPlans.Read(trip.SeatPlanJson, trip.SeatLayout, bus.SeatCapacity); }
                catch (ArgumentException) { old = []; }
                var closed = old.Where(s => s.IsBlocked).Select(s => s.Number).ToHashSet();
                var snapshot = seats.Select(s => s with { IsBlocked = s.IsBlocked || closed.Contains(s.Number) }).ToList();
                trip.SeatLayout = dto.Layout; trip.SeatPlanJson = SeatPlans.Write(snapshot); trip.SeatCapacitySnapshot = bus.SeatCapacity;
                trip.SellableSeatCount = snapshot.Count(s => !s.IsBlocked); trip.AvailableSeats = trip.SellableSeatCount.Value; trip.UpdatedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Ok(new { message = targets.Count == 0 ? "Doğrulanmış otobüs planı kaydedildi. Mevcut seferler değiştirilmedi." : $"Doğrulanmış otobüs planı kaydedildi ve seçilen {targets.Count} sefere aktarıldı.", appliedTripIds = targets.Select(t => t.Id) });
        }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (Exception e) when (e is SqlException { Number: 1205 } || e is DbUpdateException { InnerException: SqlException { Number: 1205 } })
        { return Conflict(new { message = "Eş zamanlı bir koltuk işlemi nedeniyle plan kaydedilemedi. Güncel durumu yükleyip tekrar deneyiniz." }); }
    }
}
