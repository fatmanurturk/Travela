using System.ComponentModel.DataAnnotations;

namespace Travela.DTOs;

public class StartPurchaseDto : CreateReservationDto { public Guid RequestKey { get; set; } }
public record TestPaymentDto(string Outcome, string RefundOutcome = "Succeeded");
public class PurchaseDto
{
    public Guid Id { get; set; }
    public string? Pnr { get; set; }
    public string Kind { get; set; } = "";
    public int TripId { get; set; }
    public int SeatNumber { get; set; }
    public string CompanyName { get; set; } = "";
    public string PassengerName { get; set; } = "";
    public string DepartureCity { get; set; } = "";
    public string ArrivalCity { get; set; } = "";
    public DateTime DepartureTime { get; set; }
    public DateTime ArrivalTime { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime RetrievedAt { get; set; }
    public DateTime CancelDeadline { get; set; }
    public DateTime? HoldExpiresAt { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TRY";
    public bool IsTest { get; set; }
    public string PaymentStatus { get; set; } = "";
    public string TicketStatus { get; set; } = "";
    public string RefundStatus { get; set; } = "";
    public string RefundStatusLabel => Travela.Api.Payments.RefundPresentation.Label(RefundStatus);
    public string RefundDescription => Travela.Api.Payments.RefundPresentation.Describe(RefundStatus, IsTest, LastError, NeedsAttention);
    public decimal RefundAmount { get; set; }
    public bool CanCancel { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? LastError { get; set; }
    public bool NeedsAttention { get; set; }
    public string? CheckoutUrl { get; set; }
}
