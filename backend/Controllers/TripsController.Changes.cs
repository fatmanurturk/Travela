using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Notifications;
using Travela.Api.Payments;
using Travela.Api.Trips;

namespace Travela.Api.Controllers;

public sealed class OperationalChangeDto
{
    public Guid RequestKey { get; set; }
    public int ExpectedVersion { get; set; }
    public int? ExpectedAffectedTickets { get; set; }
    public bool Preview { get; set; }
    [Range(1, int.MaxValue)] public int BusId { get; set; }
    public DateTime DepartureTime { get; set; }
    public DateTime ArrivalTime { get; set; }
    [Required, MinLength(10), MaxLength(1000)] public string Reason { get; set; } = "";
}

public partial class TripsController
{
    [HttpGet("{id:int}/changes"), Authorize(Policy = "trip.view")]
    public async Task<IActionResult> Changes(int id, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(); if (user == null) return Unauthorized();
        var trip = await _context.Trips.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        if (trip == null || !CanAccessTrip(user, trip)) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        return Ok(new { trip.ChangeVersion, changes = await _context.TripChanges.AsNoTracking().Where(c => c.TripId == id).OrderByDescending(c => c.Version).ToListAsync(ct) });
    }

    [HttpPost("{id:int}/changes"), Authorize(Policy = "trip.update")]
    public async Task<IActionResult> Change(int id, OperationalChangeDto dto, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(); if (user == null) return Unauthorized();
        if (dto.RequestKey == Guid.Empty || string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Trim().Length < 10) return BadRequest(new { message = "İşlem anahtarı ve en az 10 karakterlik gerekçe gereklidir." });
        var departure = AsUtc(dto.DepartureTime); var arrival = AsUtc(dto.ArrivalTime); var now = DateTime.UtcNow;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { dto.BusId, departure, arrival, reason = dto.Reason.Trim(), dto.ExpectedVersion }))));
        await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var trip = await _context.Trips.FromSqlInterpolated($"SELECT * FROM Trips WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
            .Include(t => t.Bus).Include(t => t.Route).Include(t => t.Company).SingleOrDefaultAsync(ct);
        if (trip == null || !CanAccessTrip(user, trip)) return NotFound();
        var existing = await _context.TripChanges.AsNoTracking().SingleOrDefaultAsync(c => c.RequestKey == dto.RequestKey, ct);
        if (existing != null) return existing.TripId == id && existing.RequestHash == hash ? Ok(new { message = "Değişiklik daha önce uygulandı.", change = existing }) : Conflict(new { message = "İşlem anahtarı farklı bir değişiklikte kullanılmış." });
        if (trip.ChangeVersion != dto.ExpectedVersion) return Conflict(new { message = "Sefer değişti. Detayı yenileyip tekrar önizleyin." });
        if (NormalizeStatus(trip.Status) != "Scheduled" || trip.DepartureTime <= now || departure <= now || arrival <= departure) return Conflict(new { message = "Yalnızca kalkışı gelmemiş sefer değiştirilebilir; yeni saatler gelecekte ve sıralı olmalıdır." });
        if (trip.BusId == dto.BusId && trip.DepartureTime == departure && trip.ArrivalTime == arrival) return BadRequest(new { message = "Saat veya araç değişikliği seçin." });
        var bus = await _context.Buses.SingleOrDefaultAsync(b => b.Id == dto.BusId && b.CompanyId == trip.CompanyId && b.IsActive, ct);
        if (bus == null || !trip.Company.IsActive || !trip.Route.IsActive) return BadRequest(new { message = "Aktif ve firmaya ait bir otobüs gereklidir." });
        if (await HasBusOverlapAsync(dto.BusId, departure, arrival, trip.Id)) return Conflict(new { message = "Yeni araç/saat başka bir seferle çakışıyor." });
        var reservations = await _context.Reservations.Include(r => r.Payment).Where(r => r.TripId == id && r.Status == "Active").ToListAsync(ct);
        var stable = reservations.Where(r => r.Kind != "Hold").ToList();
        var tickets = stable.Where(r => r.Kind == "Ticket" && r.Payment?.PaymentStatus == "Paid").ToList();
        if (stable.Any(r => r.Kind == "Ticket" && r.Payment?.PaymentStatus != "Paid")) return Conflict(new { message = "Ödeme/bilet tutarsızlığı giderilmeden sefer değiştirilemez." });
        var occupied = reservations.Count;
        if (trip.AvailableSeats != (trip.SellableSeatCount ?? trip.Bus.SeatCapacity) - occupied) return Conflict(new { message = "Kontenjan tutarsızlığı giderilmeden sefer değiştirilemez." });
        List<SeatPosition> plan;
        var capacity = bus.Id == trip.BusId ? trip.SeatCapacitySnapshot ?? bus.SeatCapacity : bus.SeatCapacity;
        var layout = bus.Id == trip.BusId ? trip.SeatLayout : bus.SeatLayout;
        try { plan = SeatPlans.Read(bus.Id == trip.BusId ? trip.SeatPlanJson : bus.SeatPlanJson, layout, capacity); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        if (stable.Count > 0 && (layout != trip.SeatLayout || stable.Any(r => !plan.Any(s => s.Number == r.SeatNumber && !s.IsBlocked))))
            return Conflict(new { message = "Yeni araç mevcut yolcuların koltuk numaralarını ve yerleşimini korumalıdır. Uyumlu bir araç seçin." });
        if (stable.Count > 0)
        {
            List<SeatPosition> previous;
            try { previous = SeatPlans.ReadVerified(trip.SeatPlanJson, trip.SeatLayout, trip.SeatCapacitySnapshot ?? trip.Bus.SeatCapacity); }
            catch (ArgumentException) { return Conflict(new { message = "Mevcut yolcuların fiziksel koltuk planı doğrulanmadan araç değiştirilemez." }); }
            if (stable.Any(r =>
            {
                var beforeSeat = previous.Single(s => s.Number == r.SeatNumber); var afterSeat = plan.Single(s => s.Number == r.SeatNumber);
                return beforeSeat.Row != afterSeat.Row || beforeSeat.Column != afterSeat.Column || SeatCompatibility.Adjacent(previous, beforeSeat)?.Number != SeatCompatibility.Adjacent(plan, afterSeat)?.Number;
            })) return Conflict(new { message = "Yeni araç yolcuların fiziksel koltuk konumunu ve eş koltuk komşuluğunu korumalıdır." });
        }
        var before = TicketJourney.Capture(trip);
        var after = TicketJourney.Capture(trip); after.BusId = bus.Id; after.BusPlateNumber = bus.PlateNumber; after.DepartureTime = departure; after.ArrivalTime = arrival;
        if (dto.Preview) return Ok(new { before, after, affectedTickets = tickets.Count, releasedHolds = reservations.Count(r => r.Kind == "Hold"), changeVersion = trip.ChangeVersion, cancellationUntil = departure,
            message = "Aktif biletler aynı koltukta korunur; ödeme tutmaları sonlandırılır. Etkilenen yolcular yeni kalkışa kadar tam iadeyle iptal edebilir." });
        if (dto.ExpectedAffectedTickets != tickets.Count) return Conflict(new { message = "Etkilenen bilet sayısı değişti. Önizlemeyi yenileyin." });
        var change = new TripChange { TripId = id, RequestKey = dto.RequestKey, RequestHash = hash, ActorId = user.Id, Reason = dto.Reason.Trim(), Version = trip.ChangeVersion + 1,
            AffectedTickets = tickets.Count, ReleasedHolds = reservations.Count(r => r.Kind == "Hold"), BeforeJson = before.Write(), AfterJson = after.Write(), CreatedAt = now };
        _context.TripChanges.Add(change);
        foreach (var r in await _context.Reservations.Where(r => r.TripId == id && r.Kind == "Ticket" && r.Status != "Active" && r.TicketJourneyJson == null).ToListAsync(ct)) r.TicketJourneyJson = before.Write();
        trip.Bus = bus; trip.BusId = bus.Id; trip.DepartureTime = departure; trip.ArrivalTime = arrival; trip.ChangeVersion++; trip.UpdatedAt = now;
        trip.SeatLayout = layout; trip.SeatPlanJson = SeatPlans.Write(plan); trip.SeatCapacitySnapshot = capacity; trip.SellableSeatCount = plan.Count(s => !s.IsBlocked); trip.AvailableSeats = trip.SellableSeatCount.Value - stable.Count;
        foreach (var r in reservations.Where(r => r.Kind == "Hold"))
        {
            r.Status = "Cancelled"; r.CancelledAt = now; r.CancellationReason = "Sefer değişikliği nedeniyle ödeme tutması sonlandırıldı. Yeni sefer bilgileriyle tekrar seçim yapın.";
            if (r.Payment != null) { PaymentService.RequestRefund(r.Payment, now, _context); PaymentHistory.Record(_context, r.Payment, "HoldReleasedForTripChange", now, user.Id, user.FullName, change.Reason); }
        }
        foreach (var r in tickets)
        {
            r.TicketJourneyJson = after.Write(); r.ChangeCancellationUntil = departure;
            _context.TripChangeTickets.Add(new() { TripChange = change, ReservationId = r.Id });
            await TravelNotifications.Queue(_context, r, "TripChanged", "trip-change:" + change.Id, now,
                $"Önceki kalkış: {before.DepartureTime.AddHours(3):dd.MM.yyyy HH:mm}. Önceki araç: {before.BusPlateNumber}. Yeni araç: {after.BusPlateNumber}. Gerekçe: {change.Reason}. Yeni kalkışa kadar Biletlerim üzerinden tam iadeyle iptal edebilirsiniz.", ct);
        }
        try { await _context.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (Exception e) when (IsConcurrencyConflict(e)) { return Conflict(new { message = "Eş zamanlı sefer işlemi var. Detayı yenileyin." }); }
        return Ok(new { message = "Sefer değiştirildi; bildirimler kuyruğa alındı ve tam iade hakkı tanımlandı.", change });
    }
}
