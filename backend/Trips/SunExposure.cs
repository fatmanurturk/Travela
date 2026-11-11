using System.Text.Json;

namespace Travela.Api.Trips;

public record RoutePoint(double Latitude, double Longitude);
public record VerifiedRouteGeometry(List<RoutePoint> Points, string Source, DateTime VerifiedAt);
public record SunExposureResult(string Side, string Message, double LeftMinutes = 0, double RightMinutes = 0, double NightMinutes = 0);

public static class SunExposure
{
    public static SunExposureResult Unknown() => new("Unknown", "Güneş yönü bilgisi hesaplanamadı. Doğrulanmış yol geometrisi ve seyahat saatleri gereklidir.");
    private static double Rad(double d) => d * Math.PI / 180;
    private static double Deg(double r) => r * 180 / Math.PI;
    public static double Distance(RoutePoint a, RoutePoint b)
    {
        var h = Math.Pow(Math.Sin(Rad(b.Latitude - a.Latitude) / 2), 2) + Math.Cos(Rad(a.Latitude)) * Math.Cos(Rad(b.Latitude)) * Math.Pow(Math.Sin(Rad(b.Longitude - a.Longitude) / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
    }
    public static bool ValidGeometry(IReadOnlyList<RoutePoint>? points) => points is { Count: >= 3 and <= 20000 } &&
        points.All(p => p != null && double.IsFinite(p.Latitude) && double.IsFinite(p.Longitude) && p.Latitude is >= -72 and <= 72 && p.Longitude is >= -180 and <= 180) &&
        points.Zip(points.Skip(1)).All(pair => Distance(pair.First, pair.Second) is > 0.001 and <= 20);

    // NOAA General Solar Position Calculations (fractional-year approximation).
    // https://gml.noaa.gov/grad/solcalc/solareqns.PDF
    public static (double Azimuth, double Elevation) Position(DateTimeOffset instant, RoutePoint point)
    {
        var utc = instant.UtcDateTime;
        var hour = utc.TimeOfDay.TotalHours;
        var gamma = 2 * Math.PI / (DateTime.IsLeapYear(utc.Year) ? 366 : 365) * (utc.DayOfYear - 1 + (hour - 12) / 24);
        var equation = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma) - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
        var declination = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma) - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma) - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);
        var solarMinutes = ((utc.TimeOfDay.TotalMinutes + equation + 4 * point.Longitude) % 1440 + 1440) % 1440;
        var angle = Rad(solarMinutes / 4 - 180);
        var latitude = Rad(point.Latitude);
        var elevation = Math.Asin(Math.Clamp(Math.Sin(latitude) * Math.Sin(declination) + Math.Cos(latitude) * Math.Cos(declination) * Math.Cos(angle), -1, 1));
        var azimuth = (Deg(Math.Atan2(Math.Sin(angle), Math.Cos(angle) * Math.Sin(latitude) - Math.Tan(declination) * Math.Cos(latitude))) + 180 + 360) % 360;
        return (azimuth, Deg(elevation));
    }

    public static SunExposureResult Calculate(DateTimeOffset departure, DateTimeOffset arrival, string? geometryJson)
    {
        VerifiedRouteGeometry? geometry;
        try { geometry = JsonSerializer.Deserialize<VerifiedRouteGeometry>(geometryJson ?? "null", new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return Unknown(); }
        var duration = (arrival - departure).TotalMinutes;
        if (geometry == null || !ValidGeometry(geometry.Points) || string.IsNullOrWhiteSpace(geometry.Source) || geometry.VerifiedAt == default || duration is <= 0 or > 4320) return Unknown();
        var segments = geometry.Points.Zip(geometry.Points.Skip(1)).Select(pair => (A: pair.First, B: pair.Second, Length: Distance(pair.First, pair.Second))).ToList();
        var total = segments.Sum(s => s.Length);
        double left = 0, right = 0, night = 0, frontal = 0;
        // Midpoint samples no more than 5 minutes apart, weighted by actual interval length.
        // Travel time is distributed by road length: traffic, stops and weather are unknown.
        var samples = (int)Math.Ceiling(duration / 5);
        var step = duration / samples;
        var segmentIndex = 0; double distanceBefore = 0;
        for (var i = 0; i < samples; i++)
        {
            var target = total * (i + 0.5) / samples;
            while (segmentIndex < segments.Count - 1 && distanceBefore + segments[segmentIndex].Length < target) distanceBefore += segments[segmentIndex++].Length;
            var s = segments[segmentIndex]; var fraction = (target - distanceBefore) / s.Length;
            var point = new RoutePoint(s.A.Latitude + (s.B.Latitude - s.A.Latitude) * fraction, s.A.Longitude + (s.B.Longitude - s.A.Longitude) * fraction);
            var sun = Position(departure.AddMinutes((i + 0.5) * step), point);
            if (sun.Elevation <= 0) { night += step; continue; }
            var delta = Rad(s.B.Longitude - s.A.Longitude);
            var bearing = Deg(Math.Atan2(Math.Sin(delta) * Math.Cos(Rad(s.B.Latitude)), Math.Cos(Rad(s.A.Latitude)) * Math.Sin(Rad(s.B.Latitude)) - Math.Sin(Rad(s.A.Latitude)) * Math.Cos(Rad(s.B.Latitude)) * Math.Cos(delta)));
            var side = Math.Sin(Rad(sun.Azimuth - bearing));
            if (Math.Abs(side) < 0.15) frontal += step;
            else if (side > 0) right += step; else left += step;
        }
        var result = night >= duration - 0.01 ? "Night" : Math.Abs(left - right) <= duration * 0.1 || frontal > Math.Max(left, right) ? "Mixed" : left > right ? "Left" : "Right";
        var message = result switch
        {
            "Night" => "Gece seferi – Yolculuk sırasında doğrudan güneş ışığı beklenmiyor.",
            "Left" => "Bu seferde güneşin çoğunlukla otobüsün sol tarafından gelmesi bekleniyor.",
            "Right" => "Bu seferde güneşin çoğunlukla otobüsün sağ tarafından gelmesi bekleniyor.",
            _ => "Güneş yönü yolculuk boyunca değişiyor; belirgin bir taraf üstünlüğü beklenmiyor."
        };
        return new(result, message, Math.Round(left, 1), Math.Round(right, 1), Math.Round(night, 1));
    }
}
