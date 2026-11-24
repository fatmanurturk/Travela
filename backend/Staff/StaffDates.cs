namespace Travela.Api.Staff;

public record DocumentValidity(string Status, string Label, int? Days, string Message);

public static class StaffDates
{
    private static readonly TimeZoneInfo Turkey = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Turkey).DateTime);
    public static DocumentValidity Validity(DateOnly? expiresOn, bool indefinite, DateOnly today)
    {
        if (indefinite) return new("Indefinite", "Süresiz", null, "Süresiz belge");
        if (expiresOn == null) throw new ArgumentException("Son geçerlilik tarihi gereklidir.");
        var days = expiresOn.Value.DayNumber - today.DayNumber;
        return days < 0 ? new("Expired", "Süresi dolmuş", days, $"{Math.Abs(days)} gün önce süresi doldu")
            : days <= 30 ? new("Expiring", "Süresi yaklaşıyor", days, days == 0 ? "Bugün sona eriyor" : $"{days} gün kaldı")
            : new("Valid", "Geçerli", days, $"{days} gün kaldı");
    }
}
