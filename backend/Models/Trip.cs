using System.Text.Json.Serialization;

namespace Travela.Api.Models
{
    public class Trip
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }

        public int BusId { get; set; }

        public int RouteId { get; set; }

        public DateTime DepartureTime { get; set; }

        public DateTime ArrivalTime { get; set; }

        public decimal Price { get; set; }

        public int AvailableSeats { get; set; }
        public int ChangeVersion { get; set; }
        [JsonIgnore] public List<TripStop> Stops { get; set; } = [];
        [JsonIgnore] public List<TripLeg> Legs { get; set; } = [];
        [JsonIgnore] public List<TripSegmentFare> SegmentFares { get; set; } = [];
        public string SeatLayout { get; set; } = "Unspecified";
        public string? SeatPlanJson { get; set; }
        public int? SeatCapacitySnapshot { get; set; }
        public int? SellableSeatCount { get; set; }
        public int? DepartureTerminalId { get; set; }
        public int? ArrivalTerminalId { get; set; }
        public Terminal? DepartureTerminal { get; set; }
        public Terminal? ArrivalTerminal { get; set; }
        public string? DepartureTerminalName { get; set; }
        public string? DepartureTerminalAddress { get; set; }
        public string? ArrivalTerminalName { get; set; }
        public string? ArrivalTerminalAddress { get; set; }
        public string? BoardingInstructions { get; set; }

        public string Status { get; set; } = "Scheduled";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public string? CancellationReason { get; set; }

        public DateTime? CancelledAt { get; set; }

        public int? CancelledByUserId { get; set; }

        public Company Company { get; set; } = null!;

        public Bus Bus { get; set; } = null!;

        public Route Route { get; set; } = null!;
    }
}
