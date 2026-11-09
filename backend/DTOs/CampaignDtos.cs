using System.ComponentModel.DataAnnotations;

namespace Travela.DTOs;

public sealed class SaveCampaignDto
{
    [Range(1, int.MaxValue)] public int CompanyId { get; set; }
    [Range(1, int.MaxValue)] public int? RouteId { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(1000)] public string Description { get; set; } = "";
    [MaxLength(32)] public string? Code { get; set; }
    [Required] public string DiscountType { get; set; } = "Percentage";
    [Range(typeof(decimal), "0.01", "9999999")] public decimal Value { get; set; }
    [Range(typeof(decimal), "0", "99999999")] public decimal MinimumSubtotal { get; set; }
    [Range(typeof(decimal), "0.01", "99999999")] public decimal? MaximumDiscount { get; set; }
    [Range(1, 1000000)] public int? UsageLimit { get; set; }
    [Range(1, 1000000)] public int PerUserLimit { get; set; } = 1;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset? DepartureFrom { get; set; }
    public DateTimeOffset? DepartureUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPublic { get; set; } = true;
    [MaxLength(24)] public string? Version { get; set; }
}

public sealed class CampaignQuoteDto
{
    [Range(1, int.MaxValue)] public int TripId { get; set; }
    [Range(1, 10)] public int PassengerCount { get; set; }
    [MaxLength(32)] public string? CouponCode { get; set; }
}
