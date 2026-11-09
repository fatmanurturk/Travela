namespace Travela.DTOs;

public class RouteManagementDto : RouteDto
{
    public bool IsActive { get; set; }
    // Counts are scoped to the current company; admins see all companies.
    public int TripCount { get; set; }
    public int PlannedTripCount { get; set; }
    // City changes are prohibited if ANY company has used this shared route.
    public bool HasTrips { get; set; }
}

public class RouteStatusDto
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Güzergâh durumu zorunludur.")]
    public bool? IsActive { get; set; }
    public bool ConfirmDeactivation { get; set; }
}

public class RouteTripDto
{
    public int Id { get; set; }
    public DateTime DepartureTime { get; set; }
    public DateTime ArrivalTime { get; set; }
    public string BusPlateNumber { get; set; } = "";
    public decimal Price { get; set; }
    public string Status { get; set; } = "";
}

public class RouteDetailDto
{
    public RouteManagementDto Route { get; set; } = null!;
    public List<RouteTripDto> Trips { get; set; } = new();
    public int PageNumber { get; set; }
    public int TotalPages { get; set; }
}
