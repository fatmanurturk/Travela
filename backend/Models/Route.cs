namespace Travela.Api.Models
{
    public class Route
    {
        public int Id { get; set; }

        public string DepartureCity { get; set; } = string.Empty;

        public string ArrivalCity { get; set; } = string.Empty;

        public int? DistanceKm { get; set; }

        public int? EstimatedDurationMinutes { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}