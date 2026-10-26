using System.ComponentModel.DataAnnotations;

namespace Travela.DTOs
{
    public class BusListDto
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string PlateNumber { get; set; } = string.Empty;
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public int SeatCapacity { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class BusListResponseDto
    {
        public List<BusListDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0
            ? (int)Math.Ceiling((double)TotalCount / PageSize)
            : 0;
    }

    public class BusSummaryDto
    {
        public int TotalBuses { get; set; }
        public int ActiveBuses { get; set; }
        public int InactiveBuses { get; set; }
        public int CompaniesWithBuses { get; set; }
    }

    public class BusDetailDto : BusListDto
    {
        public bool CompanyIsActive { get; set; }
        public int TotalTrips { get; set; }
        public int UpcomingTripsCount { get; set; }
        public int CompletedTripsCount { get; set; }
        public int CancelledTripsCount { get; set; }
        public List<BusTripDto> UpcomingTrips { get; set; } = new();
    }

    public class BusTripDto
    {
        public int Id { get; set; }
        public string DepartureCity { get; set; } = string.Empty;
        public string ArrivalCity { get; set; } = string.Empty;
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public int AvailableSeats { get; set; }
    }

    public class CreateBusDto
    {
        public int? CompanyId { get; set; }

        [Required]
        [MaxLength(40)]
        public string PlateNumber { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Brand { get; set; }

        [MaxLength(100)]
        public string? Model { get; set; }

        [Range(1, 1000)]
        public int SeatCapacity { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class UpdateBusDto
    {
        [Required]
        [MaxLength(40)]
        public string PlateNumber { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Brand { get; set; }

        [MaxLength(100)]
        public string? Model { get; set; }

        [Range(1, 1000)]
        public int SeatCapacity { get; set; }

        public int? CompanyId { get; set; }

        [Required]
        public bool? IsActive { get; set; }
    }

    public class UpdateBusStatusDto
    {
        [Required]
        public bool? IsActive { get; set; }
    }
}
