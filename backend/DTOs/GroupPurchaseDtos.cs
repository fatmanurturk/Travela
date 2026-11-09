using System.ComponentModel.DataAnnotations;

namespace Travela.DTOs;

public class TicketPassengerDto
{
    [Range(1,1000)] public int SeatNumber { get; set; }
    [Required, MinLength(2), MaxLength(120)] public string FullName { get; set; } = "";
    [Required, EmailAddress, MaxLength(320)] public string Email { get; set; } = "";
    [Required, RegularExpression(@"^\+[1-9]\d{7,14}$")] public string Phone { get; set; } = "";
}
public class CheckoutBuyerDto
{
    [Required, MaxLength(60)] public string Name { get; set; } = "";
    [Required, MaxLength(60)] public string Surname { get; set; } = "";
    [Required, RegularExpression(@"^\d{11}$")] public string IdentityNumber { get; set; } = "";
    [Required, MaxLength(500)] public string Address { get; set; } = "";
    [Required, MaxLength(100)] public string City { get; set; } = "";
    [Required, MaxLength(100)] public string Country { get; set; } = "Türkiye";
    [Required, MaxLength(20)] public string ZipCode { get; set; } = "";
    [Required, RegularExpression(@"^\+[1-9]\d{7,14}$")] public string Phone { get; set; } = "";
}
public class StartGroupPurchaseDto
{
    [Range(1,int.MaxValue)] public int TripId { get; set; }
    public Guid RequestKey { get; set; }
    [Required, MinLength(1), MaxLength(10)] public List<TicketPassengerDto> Passengers { get; set; } = [];
    public CheckoutBuyerDto? Buyer { get; set; }
    [MaxLength(32)] public string? CouponCode { get; set; }
    [Range(typeof(decimal), "0.01", "999999999")] public decimal? ExpectedAmount { get; set; }
}
