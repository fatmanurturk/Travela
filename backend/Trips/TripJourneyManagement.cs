using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Routes;
using Travela.Data;
using Travela.DTOs;

namespace Travela.Api.Trips;

public static class TripJourneyManagement
{
    public static async Task SetJourney(TravelaDbContext db, Trip trip, IReadOnlyList<TripStopInputDto> input,
        IReadOnlyList<SegmentFareInputDto> fares, CancellationToken ct = default)
    {
        if (input.Count is < 2 or > 81 || input.Any(s => s == null) || fares.Any(f => f == null)) throw new ArgumentException("En az iki geçerli durak gereklidir.");
        var terminalIds = input.Select(s => s.TerminalId).ToList();
        var terminals = await db.Terminals.Where(t => terminalIds.Contains(t.Id) && t.IsActive).ToDictionaryAsync(t => t.Id, ct);
        var stops = input.Select((s, i) => {
            if (!terminals.TryGetValue(s.TerminalId, out var terminal) || TurkishCities.Id(terminal.City) != s.CityId) throw new ArgumentException("Durak için şehriyle eşleşen aktif terminal seçiniz.");
            return new TripStop { Trip = trip, TripId = trip.Id, StopOrder = i + 1, CityId = s.CityId, City = terminal.City,
                TerminalId = terminal.Id, TerminalName = terminal.Name, TerminalAddress = terminal.Address + ", " + terminal.City,
                ArrivalTime = s.ArrivalTime.UtcDateTime, DepartureTime = s.DepartureTime.UtcDateTime,
                CanBoard = s.CanBoard, CanAlight = s.CanAlight, DistanceFromStartKm = s.DistanceFromStartKm, BoardingInstructions = s.BoardingInstructions };
        }).ToList();
        await new TripSegmentService(db, TimeProvider.System).ValidateStops(stops, ct);
        if (stops[0].CityId != TurkishCities.Id(trip.Route.DepartureCity) || stops[^1].CityId != TurkishCities.Id(trip.Route.ArrivalCity) ||
            stops[0].DepartureTime != trip.DepartureTime || stops[^1].ArrivalTime != trip.ArrivalTime)
            throw new ArgumentException("İlk/son durak şehir ve saatleri seferin ana başlangıç/bitişiyle eşleşmelidir.");
        if (fares.Select(f => (f.FromStopOrder, f.ToStopOrder)).Distinct().Count() != fares.Count ||
            fares.Any(f => f.FromStopOrder < 1 || f.ToStopOrder > stops.Count || f.FromStopOrder >= f.ToStopOrder || f.Amount is < .01m or > 1000000 || f.Amount != decimal.Round(f.Amount, 2)))
            throw new ArgumentException("Kesim fiyatları tekrarsız ve pozitif, durak sıraları ileri yönde olmalıdır.");
        if (trip.Id != 0) {
            if (await db.Reservations.AnyAsync(r => r.TripId == trip.Id, ct)) throw new ArgumentException("Rezervasyon geçmişi olan seferin durakları değiştirilemez. Yeni bir sefer oluşturunuz.");
            await db.TripSegmentFares.Where(f => f.TripId == trip.Id).ExecuteDeleteAsync(ct);
            await db.TripLegs.Where(l => l.TripId == trip.Id).ExecuteDeleteAsync(ct);
            await db.TripStops.Where(s => s.TripId == trip.Id).ExecuteDeleteAsync(ct);
        }
        trip.Stops = stops;
        trip.Legs = Enumerable.Range(1, stops.Count - 1).Select(i => new TripLeg { Trip = trip, TripId = trip.Id, LegNumber = i, FromStopOrder = i, ToStopOrder = i + 1 }).ToList();
        trip.SegmentFares = fares.Where(f => f.FromStopOrder != 1 || f.ToStopOrder != stops.Count).Select(f => new TripSegmentFare { Trip = trip, TripId = trip.Id,
            FromStopOrder = f.FromStopOrder, ToStopOrder = f.ToStopOrder, Amount = f.Amount, IsApproved = f.IsApproved, ApprovedAt = f.IsApproved ? DateTime.UtcNow : null, Source = "Manual" }).ToList();
        trip.SegmentFares.Add(new() { Trip = trip, TripId = trip.Id, FromStopOrder = 1, ToStopOrder = stops.Count, Amount = trip.Price, Source = "FullRoute", IsApproved = true, ApprovedAt = DateTime.UtcNow });
        trip.DepartureTerminalId = stops[0].TerminalId; trip.ArrivalTerminalId = stops[^1].TerminalId;
        trip.DepartureTerminalName = stops[0].TerminalName; trip.ArrivalTerminalName = stops[^1].TerminalName;
        trip.DepartureTerminalAddress = stops[0].TerminalAddress; trip.ArrivalTerminalAddress = stops[^1].TerminalAddress;
        trip.BoardingInstructions = stops[0].BoardingInstructions;
        if (trip.Id != 0) { db.TripStops.AddRange(trip.Stops); db.TripLegs.AddRange(trip.Legs); db.TripSegmentFares.AddRange(trip.SegmentFares); }
    }
}
