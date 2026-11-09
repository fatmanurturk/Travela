namespace Travela.Api.Models;

public class PurchaseGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int PassengerId { get; set; }
    public int TripId { get; set; }
    public Guid RequestKey { get; set; }
    public string RequestHash { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public int? CampaignId { get; set; }
    public string? CampaignName { get; set; }
    public string? CouponCode { get; set; }
    public string Currency { get; set; } = "TRY";
    public string Provider { get; set; } = "LocalTest";
    public bool IsTest { get; set; } = true;
    public string PaymentStatus { get; set; } = "Pending";
    public string CheckoutState { get; set; } = "New";
    public string? CheckoutUrl { get; set; }
    public string? ProviderToken { get; set; }
    public string? ProviderPaymentId { get; set; }
    public string? ProtectedBuyerJson { get; set; }
    public string? LastError { get; set; }
    public bool NeedsAttention { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime HoldExpiresAt { get; set; }
    public DateTime? CheckoutLeaseUntil { get; set; }
    public DateTime? NextReconciliationAt { get; set; }
    public int ReconciliationAttempts { get; set; }
    public List<PaymentOrder> Items { get; set; } = [];
}

public class ProviderRefundAttempt
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public string RefundKey { get; set; } = "";
    public string Status { get; set; } = "Submitting";
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
