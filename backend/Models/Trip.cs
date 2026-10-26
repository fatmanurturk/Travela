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