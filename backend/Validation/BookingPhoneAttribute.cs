using System.ComponentModel.DataAnnotations;
using Travela.Api.Payments;

namespace Travela.Api.Validation;

public sealed class BookingPhoneAttribute : ValidationAttribute
{
    public BookingPhoneAttribute() : base("Geçerli bir cep telefonu giriniz (05… veya +905…).") { }
    public override bool IsValid(object? value) => value is string text && BookingContact.Phone(text) != null;
}
