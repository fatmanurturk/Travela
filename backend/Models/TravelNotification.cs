namespace Travela.Api.Models;

public class TravelNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ReservationId { get; set; }
    public Reservation Reservation { get; set; } = null!;
    public string EventKey { get; set; } = "";
    public string Type { get; set; } = "";
    public string Channel { get; set; } = "Email";
    public string Recipient { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public int Polls { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public Guid? LeaseToken { get; set; }
    public string? ProviderReference { get; set; }
    public string? LastError { get; set; }
}

public class NotificationAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NotificationId { get; set; }
    public TravelNotification Notification { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "Started";
    public string? Error { get; set; }
    public int? ActorId { get; set; }
    public string? Reason { get; set; }
}
