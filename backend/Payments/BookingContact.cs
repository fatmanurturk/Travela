using System.Text.RegularExpressions;

namespace Travela.Api.Payments;

public static class BookingContact
{
    public static string Email(string value) => value.Trim().ToLowerInvariant();
    public static string Pnr(string value) => value.Trim().ToUpperInvariant();
    public static string? Phone(string? value)
    {
        var phone = Regex.Replace(value?.Trim() ?? "", @"[\s()\-]", "");
        if (Regex.IsMatch(phone, @"^05\d{9}$")) phone = "+9" + phone;
        else if (Regex.IsMatch(phone, @"^5\d{9}$")) phone = "+90" + phone;
        else if (Regex.IsMatch(phone, @"^905\d{9}$")) phone = "+" + phone;
        else if (phone.StartsWith("00")) phone = "+" + phone[2..];
        return Regex.IsMatch(phone, @"^\+[1-9]\d{7,14}$") ? phone : null;
    }
    public static bool Matches(string input, string? email, string? phone) =>
        input.Contains('@') ? email != null && Email(input) == Email(email)
        : Phone(input) is { } number && Phone(phone) == number;
    public static string? MaskEmail(string? email) => email?.Split('@') is [var name, var host] && name.Length > 0 ? name[..1] + "***@" + host : null;
    public static string? MaskPhone(string? phone) => Phone(phone) is { Length: >= 4 } number ? "*******" + number[^4..] : null;
    public static string? MaskIdentity(string? value) => value?.Length == 11 ? value[..1] + "********" + value[^2..] : null;
}
