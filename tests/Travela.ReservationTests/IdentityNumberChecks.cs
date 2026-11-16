using System.ComponentModel.DataAnnotations;
using Travela.Api.Validation;
using Travela.DTOs;

internal static class IdentityNumberChecks
{
    public static void Run()
    {
        foreach (var value in new[] { "", "1000000014", "100000001460", "00000000000", "10000000136", "10000000147", "11111111111", "1000000014a", "１０００００００１４６", " 10000000146" })
        {
            var passenger = new TicketPassengerDto { SeatNumber = 1, FullName = "Test Yolcusu", Gender = "Female", IdentityNumber = value };
            if (Validator.TryValidateObject(passenger, new(passenger), [], true)) throw new Exception("Geçersiz kimlik kabul edildi: " + value);
        }
        var valid = new TicketPassengerDto { SeatNumber = 1, FullName = "Test Yolcusu", Gender = "Male", IdentityNumber = "10000000146" };
        if (!Validator.TryValidateObject(valid, new(valid), [], true) || !TurkishIdentityNumberAttribute.IsValidNumber(valid.IdentityNumber)) throw new Exception("Geçerli yolcu reddedildi.");
        Console.WriteLine("T.C. kimlik biçimi, kontrol basamakları ve zorunlu yolcu alanları doğrulandı.");
    }
}
