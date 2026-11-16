using System.ComponentModel.DataAnnotations;

namespace Travela.DTOs;

public sealed class TripStopInputDto
{
    [Range(1,81)] public int CityId { get; set; }
    [Range(1,int.MaxValue)] public int TerminalId { get; set; }
    public DateTimeOffset ArrivalTime { get; set; }
    public DateTimeOffset DepartureTime { get; set; }
    public bool CanBoard { get; set; } = true;
    public bool CanAlight { get; set; } = true;
    [Range(typeof(decimal),"0","100000", ParseLimitsInInvariantCulture = true)] public decimal? DistanceFromStartKm { get; set; }
    [MaxLength(1000)] public string? BoardingInstructions { get; set; }
}
public sealed class SegmentFareInputDto
{
    [Range(1,81)] public int FromStopOrder { get; set; }
    [Range(2,81)] public int ToStopOrder { get; set; }
    [Range(typeof(decimal),"0.01","1000000", ParseLimitsInInvariantCulture = true)] public decimal Amount { get; set; }
    public bool IsApproved { get; set; }
}
public sealed class SaveTripStopsDto
{
    [Range(typeof(decimal),"0.01","1000000", ParseLimitsInInvariantCulture = true)] public decimal? FullPrice { get; set; }
    [Required, MinLength(2), MaxLength(81)] public List<TripStopInputDto> Stops { get; set; } = [];
    [Required, MaxLength(3240)] public List<SegmentFareInputDto> Fares { get; set; } = [];
    public int ExpectedVersion { get; set; }
}
public sealed class RouteStopInputDto
{
    [Range(1,81)] public int CityId { get; set; }
    [Range(1,int.MaxValue)] public int TerminalId { get; set; }
    [Range(0,100000)] public int ArrivalOffsetMinutes { get; set; }
    [Range(0,100000)] public int DepartureOffsetMinutes { get; set; }
    public bool CanBoard { get; set; } = true;
    public bool CanAlight { get; set; } = true;
    public decimal? DistanceFromStartKm { get; set; }
}
public sealed class SaveRouteStopsDto
{
    [Required, MinLength(2), MaxLength(81)] public List<RouteStopInputDto> Stops { get; set; } = [];
}
