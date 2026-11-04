using System.Text.Json;
using Travela.Api.Models;
using Travela.DTOs;

namespace Travela.Api.Trips;

public class TicketJourney : JourneyDto
{
    public string CompanyName { get; set; } = "";
    public string DepartureCity { get; set; } = "";
    public string ArrivalCity { get; set; } = "";
    public string BusPlateNumber { get; set; } = "";
    public int BusId { get; set; }
    public DateTime DepartureTime { get; set; }
    public DateTime ArrivalTime { get; set; }
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static TicketJourney Capture(Trip t) => new() {
        CompanyName = t.Company.Name, DepartureCity = t.Route.DepartureCity, ArrivalCity = t.Route.ArrivalCity,
        BusId = t.BusId, BusPlateNumber = t.Bus.PlateNumber, DepartureTime = DateTime.SpecifyKind(t.DepartureTime, DateTimeKind.Utc), ArrivalTime = DateTime.SpecifyKind(t.ArrivalTime, DateTimeKind.Utc),
        DepartureTerminalId = t.DepartureTerminalId, ArrivalTerminalId = t.ArrivalTerminalId,
        DepartureTerminalName = t.DepartureTerminalName, ArrivalTerminalName = t.ArrivalTerminalName,
        DepartureTerminalAddress = t.DepartureTerminalAddress, ArrivalTerminalAddress = t.ArrivalTerminalAddress, BoardingInstructions = t.BoardingInstructions };
    public string Write() => JsonSerializer.Serialize(this, Options);
    public static TicketJourney Read(string json) => JsonSerializer.Deserialize<TicketJourney>(json, Options)!;
}
