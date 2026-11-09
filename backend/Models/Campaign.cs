namespace Travela.Api.Models;

public sealed class Campaign
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int? RouteId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    // Null code means an automatic campaign. Discounts never stack.
    public string? Code { get; set; }
    public string DiscountType { get; set; } = "Percentage";
    public decimal Value { get; set; }
    public decimal MinimumSubtotal { get; set; }
    public decimal? MaximumDiscount { get; set; }
    public int? UsageLimit { get; set; }
    public int PerUserLimit { get; set; } = 1;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public DateTime? DepartureFrom { get; set; }
    public DateTime? DepartureUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPublic { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public byte[] Version { get; set; } = [];
}
