using System.Text.Json.Serialization;

namespace Travela.Api.Models;

public class RouteStop
{
    public int RouteId { get; set; }
    public int StopOrder { get; set; }
    public string City { get; set; } = "";
    public int? TerminalId { get; set; }
    public int ArrivalOffsetMinutes { get; set; }
    public int DepartureOffsetMinutes { get; set; }
    public decimal? DistanceFromStartKm { get; set; }
}

public class TripStop
{
    public int TripId { get; set; }
    public int StopOrder { get; set; }
    public string City { get; set; } = "";
    public int? TerminalId { get; set; }
    public string? TerminalName { get; set; }
    public string? TerminalAddress { get; set; }
    public string? BoardingInstructions { get; set; }
    public DateTime ArrivalTime { get; set; }
    public DateTime DepartureTime { get; set; }
    public decimal? DistanceFromStartKm { get; set; }
    [JsonIgnore] public Trip Trip { get; set; } = null!;
}

public class TripLeg
{
    public int TripId { get; set; }
    public int LegNumber { get; set; }
    public int FromStopOrder { get; set; }
    public int ToStopOrder { get; set; }
    [JsonIgnore] public Trip Trip { get; set; } = null!;
}

public class TripSegmentFare
{
    public int TripId { get; set; }
    public int FromStopOrder { get; set; }
    public int ToStopOrder { get; set; }
    public decimal Amount { get; set; }
    public string Source { get; set; } = "Manual";
    public bool IsApproved { get; set; }
    public DateTime? ApprovedAt { get; set; }
    [JsonIgnore] public Trip Trip { get; set; } = null!;
}

// Rows are produced and retained by the reservation inventory trigger, never by EF.
public class ReservationLeg
{
    public int ReservationId { get; set; }
    public int TripId { get; set; }
    public int SeatNumber { get; set; }
    public int LegNumber { get; set; }
    public bool IsActive { get; set; }
    [JsonIgnore] public Reservation Reservation { get; set; } = null!;
}
