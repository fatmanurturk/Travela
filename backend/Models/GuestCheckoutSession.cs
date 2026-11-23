namespace Travela.Api.Models;

// No user account. The capability is an authenticated, HttpOnly cookie, never this identifier alone.
public sealed class GuestCheckoutSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime ExpiresAt { get; set; }
}

public sealed class TicketCancellationChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentOrderId { get; set; }
    public string GrantHash { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public bool Delivered { get; set; }
    public DateTime? UsedAt { get; set; }
}
