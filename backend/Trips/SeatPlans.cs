using System.Text.Json;

namespace Travela.Api.Trips;

public record SeatPosition(int Number, int Row, int Column, bool IsWindow, bool IsAisle, bool IsBlocked);

public static class SeatPlans
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly int[][] Coach41Rows = [
        [3, 6, 9, 12, 15, 18, 21, 0, 25, 28, 31, 34, 37, 41],
        [2, 5, 8, 11, 14, 17, 20, 0, 24, 27, 30, 33, 36, 40],
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39],
        [1, 4, 7, 10, 13, 16, 19, 22, 23, 26, 29, 32, 35, 38]
    ];

    private static bool IsCoach41Plan(List<SeatPosition> seats, string layout) =>
        layout == "2+1" && seats.Count == 41 && seats.All(s => s != null &&
            s.Row is >= 1 and <= 14 && s.Column is >= 1 and <= 4 &&
            Coach41Rows[s.Column - 1][s.Row - 1] == s.Number);
    public static List<SeatPosition> Create(string layout, int capacity, IEnumerable<int>? blocked = null)
    {
        if (layout is not ("2+1" or "2+2" or "Unspecified") || capacity is < 1 or > 1000)
            throw new ArgumentException("Koltuk düzeni 2+1 veya 2+2, kapasite 1–1000 arasında olmalıdır.");
        var closed = (blocked ?? []).ToList();
        if (closed.Any(n => n < 1 || n > capacity) || closed.Distinct().Count() != closed.Count)
            throw new ArgumentException("Satışa kapalı koltuklar kapasite içinde ve tekrarsız olmalıdır.");
        if (layout == "2+1" && capacity == 41)
            return Coach41Rows.SelectMany((numbers, column) => numbers.Select((number, row) =>
                new SeatPosition(number, row + 1, column + 1, column is 0 or 3,
                    column is 1 or 3, closed.Contains(number))))
                .Where(s => s.Number != 0).OrderBy(s => s.Number).ToList();
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
    public static List<SeatPosition> ReadVerified(string? json, string layout, int capacity)
    {
        if (layout == "Unspecified" || string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Koltuk planı bulunamıyor veya fiziksel yerleşimi doğrulanmamış. Firma ya da yönetici koltuk planını tanımlamalıdır.");
        return Read(json, layout, capacity);
    }
    public static List<SeatPosition> Read(string? json, string layout, int capacity)
    {
        if (string.IsNullOrEmpty(json)) return Create(layout, capacity);
        List<SeatPosition> seats;
        try { seats = JsonSerializer.Deserialize<List<SeatPosition>>(json, Options) ?? throw new ArgumentException("Koltuk planı okunamadı."); }
        catch (JsonException) { throw new ArgumentException("Koltuk planı okunamadı."); }
        var rearBench = IsCoach41Plan(seats, layout);
        if (layout is not ("2+1" or "2+2" or "Custom" or "Unspecified") || capacity is < 1 or > 1000 || seats.Count != capacity || seats.Any(s => s == null) ||
            seats.Select(s => s.Number).Distinct().Count() != capacity || seats.Any(s => s.Number < 1 || s.Number > capacity || s.Row < 1 || s.Row > 1000 || (s.Column is not (1 or 2 or 4 or 5) && !(rearBench && s.Number == 39 && s.Column == 3))) ||
            seats.Select(s => (s.Row, s.Column)).Distinct().Count() != capacity || (layout == "2+1" && seats.Any(s => s.Column == 5)))
            throw new ArgumentException("Koltuk planı kapasite ve düzenle eşleşmiyor; her fiziksel konum ve koltuk numarası benzersiz olmalıdır.");
        // Upgrade only the former generated template; seat IDs and booking/blocking data stay unchanged.
        if (layout == "2+1" && capacity == 41 && seats.All(s =>
            s.Row == (s.Number - 1) / 3 + 1 && s.Column == (((s.Number - 1) % 3) switch { 0 => 1, 1 => 2, _ => 4 })))
        {
            var positions = Create("2+1", 41);
            return seats.Select(s => positions[s.Number - 1] with { IsBlocked = s.IsBlocked }).ToList();
        }
        return seats;
    }
}
