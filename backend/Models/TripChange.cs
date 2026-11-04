namespace Travela.Api.Models;

public class TripChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestKey { get; set; }
    public string RequestHash { get; set; } = "";
    public int TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public int ActorId { get; set; }
    public string Reason { get; set; } = "";
    public int Version { get; set; }
    public int AffectedTickets { get; set; }
    public int ReleasedHolds { get; set; }
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
public class TripChangeTicket
{
    public Guid TripChangeId { get; set; }
    public TripChange TripChange { get; set; } = null!;
    public int ReservationId { get; set; }
    public Reservation Reservation { get; set; } = null!;
}
