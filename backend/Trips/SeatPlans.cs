using System.Text.Json;

namespace Travela.Api.Trips;

public record SeatPosition(int Number, int Row, int Column, bool IsWindow, bool IsAisle, bool IsBlocked);

public static class SeatPlans
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static List<SeatPosition> Create(string layout, int capacity, IEnumerable<int>? blocked = null)
    {
        if (layout is not ("2+1" or "2+2" or "Unspecified") || capacity is < 1 or > 1000)
            throw new ArgumentException("Koltuk düzeni 2+1 veya 2+2, kapasite 1–1000 arasında olmalıdır.");
        var closed = (blocked ?? []).ToList();
        if (closed.Any(n => n < 1 || n > capacity) || closed.Distinct().Count() != closed.Count)
            throw new ArgumentException("Satışa kapalı koltuklar kapasite içinde ve tekrarsız olmalıdır.");
        var perRow = layout == "2+1" ? 3 : 4;
        return Enumerable.Range(1, capacity).Select(number =>
        {
            var index = (number - 1) % perRow;
            var column = index < 2 ? index + 1 : index + 2;
            return new SeatPosition(number, (number - 1) / perRow + 1, column,
                layout != "Unspecified" && (index == 0 || index == perRow - 1),
                layout != "Unspecified" && (index == 1 || index == 2), closed.Contains(number));
        }).ToList();
    }
    public static string Write(IEnumerable<SeatPosition> seats) => JsonSerializer.Serialize(seats, Options);
    public static List<SeatPosition> Read(string? json, string layout, int capacity)
    {
        if (string.IsNullOrEmpty(json)) return Create(layout, capacity);
        List<SeatPosition> seats;
        try { seats = JsonSerializer.Deserialize<List<SeatPosition>>(json, Options) ?? throw new ArgumentException("Koltuk planı okunamadı."); }
        catch (JsonException) { throw new ArgumentException("Koltuk planı okunamadı."); }
        var expected = Create(layout, capacity, seats.Where(s => s.IsBlocked).Select(s => s.Number));
        if (!seats.SequenceEqual(expected)) throw new ArgumentException("Koltuk planı kapasite ve düzenle eşleşmiyor.");
        return seats;
    }
}
