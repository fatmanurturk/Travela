namespace Travela.DTOs
{
    public class PublicTripDto
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
