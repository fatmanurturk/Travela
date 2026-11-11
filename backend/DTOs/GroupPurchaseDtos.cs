using System.ComponentModel.DataAnnotations;

namespace Travela.DTOs;

public class TicketPassengerDto
{
    [Required(ErrorMessage = "Cinsiyet seçiniz."), RegularExpression("^(Female|Male)$", ErrorMessage = "Cinsiyet Kadın veya Erkek olmalıdır.")]
    public string Gender { get; set; } = "";
    [Range(1,1000, ErrorMessage = "Geçerli bir koltuk seçiniz.")] public int SeatNumber { get; set; }
    [Required(ErrorMessage = "Ad soyad zorunludur."), MinLength(2, ErrorMessage = "Ad soyad en az 2 karakter olmalıdır."), MaxLength(120, ErrorMessage = "Ad soyad en fazla 120 karakter olmalıdır.")] public string FullName { get; set; } = "";
    [Required(ErrorMessage = "E-posta zorunludur."), EmailAddress(ErrorMessage = "Geçerli bir e-posta giriniz."), MaxLength(320, ErrorMessage = "E-posta en fazla 320 karakter olmalıdır.")] public string Email { get; set; } = "";
    [Required(ErrorMessage = "Telefon zorunludur."), RegularExpression(@"^\+[1-9]\d{7,14}$", ErrorMessage = "Telefonu +905xxxxxxxxx biçiminde giriniz.")] public string Phone { get; set; } = "";
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
    [MaxLength(8)]
    public List<LegalAcceptanceDto>? LegalAcceptances { get; set; }

    [Range(1,int.MaxValue)] public int TripId { get; set; }
    public Guid RequestKey { get; set; }
    [Required, MinLength(1), MaxLength(10)] public List<TicketPassengerDto> Passengers { get; set; } = [];
    public CheckoutBuyerDto? Buyer { get; set; }
    [MaxLength(32)] public string? CouponCode { get; set; }
    [Range(typeof(decimal), "0.01", "999999999", ParseLimitsInInvariantCulture = true)]
    public decimal? ExpectedAmount { get; set; }
}
