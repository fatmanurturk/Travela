namespace Travela.Api.Models;

public class PaymentOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ReservationId { get; set; }
    public Reservation Reservation { get; set; } = null!;
    public int PassengerId { get; set; }
    public Guid RequestKey { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TRY";
    public string Provider { get; set; } = "LocalTest";
    public bool IsTest { get; set; } = true;
    public string PaymentStatus { get; set; } = "Pending";
    public string? CheckoutUrl { get; set; }
    public string? ProviderReference { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public string RefundStatus { get; set; } = "None";
    public decimal RefundAmount { get; set; }
    public string RefundKey { get; set; } = Guid.NewGuid().ToString("N");
    public string? RefundReference { get; set; }
    public int RefundAttempts { get; set; }
    public DateTime? NextRefundAttemptAt { get; set; }
    public DateTime? RefundLeaseUntil { get; set; }
    public Guid? RefundLeaseToken { get; set; }
    public DateTime? RefundedAt { get; set; }
    public string? LastError { get; set; }
    public bool NeedsAttention { get; set; }
}

// Local provider's durable test ledger. It contains no card data or credentials.
public class LocalPaymentRecord
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TRY";
    public string Status { get; set; } = "Pending";
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public string RefundOutcome { get; set; } = "Succeeded";
    public string? RefundKey { get; set; }
    public int RefundTransfers { get; set; }
}

public class PaymentNotification
{
    public int Id { get; set; }
    public string Provider { get; set; } = "";
    public string EventId { get; set; } = "";
    public Guid OrderId { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
