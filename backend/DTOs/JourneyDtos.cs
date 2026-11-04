using System.ComponentModel.DataAnnotations;

namespace Travela.DTOs;

public class JourneyDto
{
    public int? DepartureTerminalId { get; set; }
    public int? ArrivalTerminalId { get; set; }
    public string? DepartureTerminalName { get; set; }
    public string? DepartureTerminalAddress { get; set; }
    public string? ArrivalTerminalName { get; set; }
    public string? ArrivalTerminalAddress { get; set; }
    public string? BoardingInstructions { get; set; }
    public string? DepartureMapUrl => Map(DepartureTerminalAddress);
    public string? ArrivalMapUrl => Map(ArrivalTerminalAddress);
    private static string? Map(string? address) => string.IsNullOrWhiteSpace(address) ? null : "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(address);
}

public class TerminalDto
{
    [Required, MaxLength(160)] public string Name { get; set; } = "";
    [Required, MaxLength(100)] public string City { get; set; } = "";
    [Required, MaxLength(500)] public string Address { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
