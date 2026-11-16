using Microsoft.EntityFrameworkCore;
using Travela.Data;
using Travela.DTOs;

namespace Travela.Api.Trips;

public static class TripSalesQuery
{
    public static IQueryable<PublicTripDto> Segments(TravelaDbContext db, DateTime now) =>
        from fromStop in db.TripStops.AsNoTracking()
        join toStop in db.TripStops on fromStop.TripId equals toStop.TripId
        join fare in db.TripSegmentFares on new { fromStop.TripId, FromStopOrder = fromStop.StopOrder, ToStopOrder = toStop.StopOrder }
            equals new { fare.TripId, fare.FromStopOrder, fare.ToStopOrder }
        let trip = fromStop.Trip
        where fromStop.StopOrder < toStop.StopOrder && fromStop.CanBoard && toStop.CanAlight && fare.IsApproved &&
            fromStop.DepartureTime > now && trip.Company.IsActive && trip.Bus.IsActive && trip.Bus.CompanyId == trip.CompanyId && trip.Route.IsActive &&
            (fromStop.TerminalId == null || db.Terminals.Any(t => t.Id == fromStop.TerminalId && t.IsActive)) &&
            (toStop.TerminalId == null || db.Terminals.Any(t => t.Id == toStop.TerminalId && t.IsActive)) &&
            (trip.Status == "Scheduled" || trip.Status == "Planlandı" || trip.Status == "Planlandi" || trip.Status == "InProgress" || trip.Status == "In Progress" || trip.Status == "Devam ediyor")
        select new PublicTripDto {
            Id = trip.Id, CompanyId = trip.CompanyId, CompanyName = trip.Company.Name,
            BoardingStopOrder = fromStop.StopOrder, AlightingStopOrder = toStop.StopOrder,
            DepartureCity = fromStop.City, ArrivalCity = toStop.City,
            MainDepartureCity = trip.Route.DepartureCity, MainArrivalCity = trip.Route.ArrivalCity,
            DepartureTerminalId = fromStop.TerminalId, ArrivalTerminalId = toStop.TerminalId,
            DepartureTerminalName = fromStop.TerminalName, ArrivalTerminalName = toStop.TerminalName,
            DepartureTerminalAddress = fromStop.TerminalAddress, ArrivalTerminalAddress = toStop.TerminalAddress,
            BoardingInstructions = fromStop.BoardingInstructions,
            DepartureTime = fromStop.DepartureTime, ArrivalTime = toStop.ArrivalTime,
            DurationMinutes = EF.Functions.DateDiffMinute(fromStop.DepartureTime, toStop.ArrivalTime),
            Price = fare.Source == "FullRoute" ? trip.Price : fare.Amount,
            AvailableSeats = (trip.SellableSeatCount ?? trip.Bus.SeatCapacity) - db.ReservationLegs.Where(l =>
                l.TripId == trip.Id && l.LegNumber >= fromStop.StopOrder && l.LegNumber < toStop.StopOrder && l.IsActive &&
                l.Reservation.Status == "Active" && (l.Reservation.Kind != "Hold" || l.Reservation.HoldExpiresAt > now))
                .Select(l => l.SeatNumber).Distinct().Count()
        };
}
