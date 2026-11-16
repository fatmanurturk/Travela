using System.ComponentModel.DataAnnotations;

namespace Travela.Api.Validation;

// Format/checksum validation, not a civil registry identity lookup.
public sealed class TurkishIdentityNumberAttribute : ValidationAttribute
{
    public TurkishIdentityNumberAttribute() : base("Geçerli bir T.C. kimlik numarası giriniz.") { }

    public static bool IsValidNumber(string? value)
    {
        if (value is not { Length: 11 } || value[0] == '0' || value.Any(c => c < '0' || c > '9')) return false;
        var digits = value.Select(c => c - '0').ToArray();
        var odd = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        var even = digits[1] + digits[3] + digits[5] + digits[7];
        return ((odd * 7 - even) % 10 + 10) % 10 == digits[9] && digits.Take(10).Sum() % 10 == digits[10];
    }

    public override bool IsValid(object? value) => value == null || value is string s && (s.Length == 0 || IsValidNumber(s));
}
