using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Payments;
using Travela.Api.Routes;
using Travela.Api.Trips;
using Travela.DTOs;

namespace Travela.Api.Controllers;

public partial class TripsController
{
    private static readonly TimeZoneInfo TurkeyZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    [HttpGet("search"), AllowAnonymous]
    public async Task<IActionResult> SearchPublicTrips([FromQuery] string? departureCity, [FromQuery] string? arrivalCity,
        [FromQuery] DateOnly? date, [FromQuery] bool all = false, [FromQuery] int passengers = 1,
        [FromQuery] int? companyId = null, [FromQuery] int? departureHourFrom = null, [FromQuery] int? departureHourTo = null,
        [FromQuery] decimal? minPrice = null, [FromQuery] decimal? maxPrice = null, [FromQuery] string sortBy = "departure",
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 100,
        [FromQuery] int? departureCityId = null, [FromQuery] int? arrivalCityId = null)
    {
        var ct = HttpContext.RequestAborted;
        var from = departureCityId ?? TurkishCities.Id(departureCity);
        var to = arrivalCityId ?? TurkishCities.Id(arrivalCity);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TurkeyZone));
        if (!all && (!from.HasValue || !to.HasValue || !date.HasValue) || from.HasValue && TurkishCities.Name(from) == null ||
            to.HasValue && TurkishCities.Name(to) == null || from.HasValue && from == to || date < today || passengers is < 1 or > 10 ||
            pageNumber < 1 || pageSize is < 1 or > 100 || departureHourFrom is < 0 or > 23 || departureHourTo is < 0 or > 23 ||
            departureHourFrom > departureHourTo || minPrice < 0 || maxPrice < 0 || minPrice > maxPrice || sortBy is not ("departure" or "price" or "duration"))
            return BadRequest(new { message = "Şehir, tarih, yolcu sayısı veya arama filtreleri geçersiz." });
        var query = TripSalesQuery.Segments(_context, DateTime.UtcNow);
        if (from.HasValue) query = query.Where(s => _context.TripStops.Any(t => t.TripId == s.Id && t.StopOrder == s.BoardingStopOrder && t.CityId == from));
        if (to.HasValue) query = query.Where(s => _context.TripStops.Any(t => t.TripId == s.Id && t.StopOrder == s.AlightingStopOrder && t.CityId == to));
        if (all && !from.HasValue && !to.HasValue) query = query.Where(s => s.BoardingStopOrder == 1 && s.AlightingStopOrder == _context.TripStops.Count(t => t.TripId == s.Id));
        if (date.HasValue) {
            var start = TimeZoneInfo.ConvertTimeToUtc(date.Value.ToDateTime(TimeOnly.MinValue), TurkeyZone);
            var end = TimeZoneInfo.ConvertTimeToUtc(date.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TurkeyZone);
            query = query.Where(s => s.DepartureTime >= start && s.DepartureTime < end);
        }
        query = query.Where(s => s.AvailableSeats >= passengers);
        var companies = await query.Select(s => new PublicTripCompanyDto { Id = s.CompanyId, Name = s.CompanyName }).Distinct().OrderBy(s => s.Name).ToListAsync(ct);
        if (companyId.HasValue) query = query.Where(s => s.CompanyId == companyId);
        if (departureHourFrom.HasValue) query = query.Where(s => (s.DepartureTime.Hour + 3) % 24 >= departureHourFrom);
        if (departureHourTo.HasValue) query = query.Where(s => (s.DepartureTime.Hour + 3) % 24 <= departureHourTo);
        if (minPrice.HasValue) query = query.Where(s => s.Price >= minPrice);
        if (maxPrice.HasValue) query = query.Where(s => s.Price <= maxPrice);
        var grouped = query.GroupBy(s => s.Id).Select(g => new { Id = g.Key, Departure = g.Min(s => s.DepartureTime), Price = g.Min(s => s.Price), Duration = g.Min(s => s.DurationMinutes) });
        var total = await grouped.CountAsync(ct);
        var ordered = sortBy == "price" ? grouped.OrderBy(s => s.Price).ThenBy(s => s.Id) : sortBy == "duration" ? grouped.OrderBy(s => s.Duration).ThenBy(s => s.Id) : grouped.OrderBy(s => s.Departure).ThenBy(s => s.Id);
        var ids = await ordered.Skip((int)Math.Min((long)(pageNumber - 1) * pageSize, int.MaxValue)).Take(pageSize).Select(s => s.Id).ToListAsync(ct);
        var rows = await query.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        foreach (var row in rows) { row.DepartureTime = AsUtc(row.DepartureTime); row.ArrivalTime = AsUtc(row.ArrivalTime); }
        var items = ids.Select(id => {
            var options = rows.Where(s => s.Id == id).OrderBy(s => sortBy == "price" ? s.Price : sortBy == "duration" ? s.DurationMinutes : 0).ThenBy(s => s.DepartureTime).ThenBy(s => s.BoardingStopOrder).ToList();
            var item = options[0]; item.JourneyOptions = options.Skip(1).ToList(); return item;
        }).ToList();
        return Ok(new PublicTripSearchResponseDto { Items = items, Companies = companies, TotalCount = total, PageNumber = pageNumber, PageSize = pageSize });
    }

    [HttpGet("public/{id:int}"), AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetPublicTripById(int id, [FromServices] PaymentService payments,
        [FromQuery] int? boardingStopOrder = null, [FromQuery] int? alightingStopOrder = null)
    {
        var ct = HttpContext.RequestAborted;
        await payments.ExpireHolds(ct);
        var trip = await _context.Trips.AsNoTracking().Include(t => t.Company).Include(t => t.Bus).Include(t => t.Route).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (trip == null) return NotFound(new { message = "Sefer bulunamadı." });
        try {
            var segment = await Segments.Resolve(trip, boardingStopOrder, alightingStopOrder, ct);
            var j = TicketJourney.Capture(trip, segment);
            var available = await Segments.AvailableSeatNumbers(trip, segment.From.StopOrder, segment.To.StopOrder, ct);
            var occupied = await Segments.OccupiedLegs(id, segment.From.StopOrder, segment.To.StopOrder)
                .Select(l => new SeatOccupancy(l.SeatNumber, l.Reservation.PassengerGender, l.Reservation.Kind, l.Reservation.HoldExpiresAt)).Distinct().ToListAsync(ct);
            List<SeatPosition> seats;
            try { seats = SeatPlans.ReadVerified(trip.SeatPlanJson, trip.SeatLayout, trip.SeatCapacitySnapshot ?? trip.Bus.SeatCapacity); }
            catch (ArgumentException) { seats = []; }
            var valid = seats.Count == (trip.SeatCapacitySnapshot ?? trip.Bus.SeatCapacity) && seats.Count(s => !s.IsBlocked) == (trip.SellableSeatCount ?? trip.Bus.SeatCapacity);
            return Ok(new PublicTripDetailDto {
                Id = id, CompanyId = trip.CompanyId, CompanyName = j.CompanyName, BoardingStopOrder = segment.From.StopOrder, AlightingStopOrder = segment.To.StopOrder,
                MainDepartureCity = trip.Route.DepartureCity, MainArrivalCity = trip.Route.ArrivalCity,
                DepartureCity = j.DepartureCity, ArrivalCity = j.ArrivalCity, DepartureTime = j.DepartureTime, ArrivalTime = j.ArrivalTime,
                DepartureTerminalId = j.DepartureTerminalId, ArrivalTerminalId = j.ArrivalTerminalId, DepartureTerminalName = j.DepartureTerminalName, ArrivalTerminalName = j.ArrivalTerminalName,
                DepartureTerminalAddress = j.DepartureTerminalAddress, ArrivalTerminalAddress = j.ArrivalTerminalAddress, BoardingInstructions = j.BoardingInstructions,
                DurationMinutes = (int)(j.ArrivalTime - j.DepartureTime).TotalMinutes, Price = segment.Price, AvailableSeats = available.Count,
                SeatCapacity = trip.SeatCapacitySnapshot ?? trip.Bus.SeatCapacity, SellableSeatCount = trip.SellableSeatCount ?? trip.Bus.SeatCapacity,
                SeatLayout = trip.SeatLayout, SeatPlanJson = trip.SeatPlanJson, Seats = seats, OccupiedSeats = occupied.Select(s => s.SeatNumber).Distinct().ToList(), SeatOccupancies = occupied,
                Status = trip.Status, SeatPlanAvailable = valid, DepartureHasPassed = j.DepartureTime <= DateTime.UtcNow,
                CanReserve = valid && available.Count > 0 && await Segments.CanSell(trip, segment, ct),
                SunExposure = segment.From.StopOrder == 1 && segment.To.StopOrder == await _context.TripStops.CountAsync(s => s.TripId == id, ct)
                    ? SunExposure.Calculate(new(j.DepartureTime), new(j.ArrivalTime), trip.Route.GeometryJson) : SunExposure.Unknown()
            });
        } catch (PaymentFlowException error) { return StatusCode(error.Status, new { message = error.Message }); }
    }
}
