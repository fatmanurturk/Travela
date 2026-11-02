using System.ComponentModel.DataAnnotations;

namespace Travela.DTOs;

public class CreateReservationDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir sefer seçiniz.")]
    public int TripId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir koltuk seçiniz.")]
    public int SeatNumber { get; set; }
}

public class ReservationDto
{
    public string ReservationNumber { get; set; } = "";
    public int TripId { get; set; }
    public int SeatNumber { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CompanyName { get; set; } = "";
    public string DepartureCity { get; set; } = "";
    public string ArrivalCity { get; set; } = "";
    public DateTime DepartureTime { get; set; }
    public DateTime ArrivalTime { get; set; }
    public string TripStatus { get; set; } = "";
    // A reservation is never a paid ticket. Payment is outside this workflow.
    public string Status { get; set; } = "Active";
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public bool CanCancel { get; set; }
    public bool IsPaid => false;
}

public class CompanyReservationDto : ReservationDto
{
    public string PassengerName { get; set; } = "";
}
