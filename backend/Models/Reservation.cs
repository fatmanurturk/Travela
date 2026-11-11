using Travela.Models;

namespace Travela.Api.Models;

public class Reservation
{
    public int Id { get; set; }
    public string ReservationNumber { get; set; } = Guid.NewGuid().ToString("N").ToUpperInvariant();
    public int TripId { get; set; }
    public int PassengerId { get; set; }
    public string? PassengerNameSnapshot { get; set; }
    public string? PassengerEmailSnapshot { get; set; }
    public string? PassengerPhoneSnapshot { get; set; }
    public string? PassengerGender { get; set; }
    public DateTime? PassengerCapturedAt { get; set; }
    public string? PassengerSnapshotSource { get; set; }
    public int SeatNumber { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Active";
    public string Kind { get; set; } = "Legacy";
    public DateTime? HoldExpiresAt { get; set; }
    public string? Pnr { get; set; }
    public DateTime? IssuedAt { get; set; }
    public string? TicketJourneyJson { get; set; }
    public DateTime? ChangeCancellationUntil { get; set; }
    public PaymentOrder? Payment { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public Trip Trip { get; set; } = null!;
    public User Passenger { get; set; } = null!;
}
