namespace Travela.DTOs;

public class CompanyReportQuery
{
    public string Period { get; set; } = "month";
    public string Environment { get; set; } = "all";
    public string Basis { get; set; } = "transaction";
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int? TripId { get; set; }
    public int? RouteId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string Sort { get; set; } = "departure";
    public string Direction { get; set; } = "asc";
    public string RouteSort { get; set; } = "sold";
}
public record ReportScope(DateOnly From, DateOnly To, DateTime StartUtc, DateTime EndExclusiveUtc,
    string Environment, string Basis, DateTime GeneratedAt, string DateExplanation);
public class ReportMetrics
{
    public int Sold { get; set; }
    public int Active { get; set; }
    public int Cancelled { get; set; }
    public decimal Collected { get; set; }
    public decimal Refunded { get; set; }
    public decimal PendingRefund { get; set; }
    public int FailedRefundCount { get; set; }
    public decimal FailedRefundAmount { get; set; }
    public decimal CollectedMinusRefunded => Collected - Refunded;
}
public class ReportOccupancy
{
    public int ActiveSoldSeats { get; set; }
    public long Capacity { get; set; }
    public int TripCount { get; set; }
    public int InvalidTrips { get; set; }
    public decimal? Percent { get; set; }
}
public class ReportDay
{
    public DateOnly Date { get; set; }
    public int Sold { get; set; }
    public int Cancelled { get; set; }
    public decimal Collected { get; set; }
    public decimal Refunded { get; set; }
}
public class RouteReport : ReportMetrics
{
    public int RouteId { get; set; }
    public string DepartureCity { get; set; } = "";
    public string ArrivalCity { get; set; } = "";
}
public class TripReport : ReportMetrics
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public string DepartureCity { get; set; } = "";
    public string ArrivalCity { get; set; } = "";
    public DateTime DepartureTime { get; set; }
    public string Plate { get; set; } = "";
    public int Capacity { get; set; }
    public int ActiveSoldSeats { get; set; }
    public int HeldSeats { get; set; }
    public int LegacyBlockedSeats { get; set; }
    public int OtherEnvironmentBlocks { get; set; }
    public int ExpiredHolds { get; set; }
    public int? AvailableForSale { get; set; }
    public decimal? OccupancyPercent { get; set; }
    public string? DataWarning { get; set; }
    public string Status { get; set; } = "";
}
public record ReportActivity(Guid OrderId, int TripId, string? Pnr, string Type, DateTime Date, decimal Amount, string RefundStatus);
public record ReportRefundAttention(Guid OrderId, int TripId, string? Pnr, decimal Amount, string? Error, DateTime? StatusDate);
public class CompanyReportResponse
{
    public ReportScope Scope { get; set; } = null!;
    public ReportMetrics Metrics { get; set; } = new();
    public ReportOccupancy Occupancy { get; set; } = new();
    public List<ReportDay> Daily { get; set; } = [];
    public List<RouteReport> Routes { get; set; } = [];
    public int RouteCount { get; set; }
    public List<TripReport> Trips { get; set; } = [];
    public int TotalTrips { get; set; }
    public List<TripReport> Upcoming { get; set; } = [];
    public List<ReportActivity> Recent { get; set; } = [];
    public List<ReportRefundAttention> FailedRefunds { get; set; } = [];
    public int UnknownRefundDateCount { get; set; }
    public decimal UnknownRefundDateAmount { get; set; }
}
