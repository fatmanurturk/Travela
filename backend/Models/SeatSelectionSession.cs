namespace Travela.Api.Models;

public sealed class SeatSelectionSession
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = "";
    public string IpHash { get; set; } = "";
    public int? UserId { get; set; }
    public int TripId { get; set; }
    public int BoardingStopOrder { get; set; }
    public int AlightingStopOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    // Set once, on the first accepted seat; never extended by selection or reads.
    public DateTime? ExpiresAt { get; set; }
    public Guid? PurchaseGroupId { get; set; }
}
