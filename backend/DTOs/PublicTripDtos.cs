namespace Travela.DTOs
{
    public class PublicTripDetailDto : PublicTripDto
    {
        public int SeatCapacity { get; set; }
        public int SellableSeatCount { get; set; }
        public string SeatLayout { get; set; } = "Unspecified";
        public string? SeatPlanJson { get; set; }
        public List<Travela.Api.Trips.SeatPosition> Seats { get; set; } = [];
        public List<int> OccupiedSeats { get; set; } = new();
        public bool CanReserve { get; set; }
        public bool SeatPlanAvailable { get; set; }
        public bool DepartureHasPassed { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class PublicTripDto : JourneyDto
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string DepartureCity { get; set; } = string.Empty;
        public string ArrivalCity { get; set; } = string.Empty;
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public int DurationMinutes { get; set; }
        public decimal Price { get; set; }
        public int AvailableSeats { get; set; }
    }

    public class PublicTripCompanyDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class PublicTripSearchResponseDto
    {
        public List<PublicTripDto> Items { get; set; } = new();
        public List<PublicTripCompanyDto> Companies { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0
            ? (int)Math.Ceiling((double)TotalCount / PageSize)
            : 0;
    }

    public class FeaturedRouteDto
    {
        public int RouteId { get; set; }
        public string DepartureCity { get; set; } = string.Empty;
        public string ArrivalCity { get; set; } = string.Empty;
        public int UpcomingTripCount { get; set; }
        public decimal StartingPrice { get; set; }
    }
}
