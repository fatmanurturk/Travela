using System.ComponentModel.DataAnnotations;

namespace Travela.DTOs
{
    public class TripListDto
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public int BusId { get; set; }
        public string BusPlateNumber { get; set; } = string.Empty;
        public string? BusBrand { get; set; }
        public string? BusModel { get; set; }
        public int BusCapacity { get; set; }
        public int RouteId { get; set; }
        public string DepartureCity { get; set; } = string.Empty;
        public string ArrivalCity { get; set; } = string.Empty;
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public decimal Price { get; set; }
        public int AvailableSeats { get; set; }
        public string Status { get; set; } = "Scheduled";
    }

    public class TripListResponseDto
    {
        public List<TripListDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0
            ? (int)Math.Ceiling((double)TotalCount / PageSize)
            : 0;
    }

    public class TripSummaryDto
    {
        public int TotalTrips { get; set; }
        public int ScheduledTrips { get; set; }
        public int InProgressTrips { get; set; }
        public int CompletedTrips { get; set; }
        public int CancelledTrips { get; set; }
    }

    public class TripDetailDto : TripListDto
    {
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool BusIsActive { get; set; }
        public bool CompanyIsActive { get; set; }
        public int? DistanceKm { get; set; }
        public int? EstimatedDurationMinutes { get; set; }
        public string? CancellationReason { get; set; }
        public DateTime? CancelledAt { get; set; }
    }

    public class CreateTripDto
    {
        public int? CompanyId { get; set; }

        [Range(1, int.MaxValue)]
        public int BusId { get; set; }

        [Range(1, int.MaxValue)]
        public int RouteId { get; set; }

        public DateTime DepartureTime { get; set; }

        public DateTime ArrivalTime { get; set; }

        [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true)]
        public decimal Price { get; set; }
    }

    public class UpdateTripDto : CreateTripDto
    {
    }

    public class UpdateTripStatusDto
    {
        [Required]
        public string Status { get; set; } = string.Empty;
    }

    public class CancelTripDto
    {
        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = string.Empty;
    }

    public class CreateRouteDto
    {
        [Required]
        [MaxLength(100)]
        public string DepartureCity { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string ArrivalCity { get; set; } = string.Empty;

        [Range(0, 100000)]
        public int? DistanceKm { get; set; }

        [Required]
        [Range(1, 100000)]
        public int EstimatedDurationMinutes { get; set; }
    }

    public class RouteDto
    {
        public int Id { get; set; }
        public string DepartureCity { get; set; } = string.Empty;
        public string ArrivalCity { get; set; } = string.Empty;
        public int? DistanceKm { get; set; }
        public int? EstimatedDurationMinutes { get; set; }
    }
}
