namespace Travela.Api.Trips;

public record SeatOccupancy(int SeatNumber, string? Gender, string Kind, DateTime? ExpiresAt);
public record SeatPassenger(int SeatNumber, string? Gender);

public static class SeatCompatibility
{
    public static bool IsGender(string? gender) => gender is "Female" or "Male";

    // Column 3 is the aisle. Pair only actual positions on the same side and row.
    // Numbering order is deliberately irrelevant.
    public static SeatPosition? Adjacent(IReadOnlyList<SeatPosition> plan, SeatPosition seat) =>
        plan.SingleOrDefault(s => s.Row == seat.Row && s.Column == (seat.Column switch
        { 1 => 2, 2 => 1, 4 => 5, 5 => 4, _ => -1 }));

    public static string? Validate(IReadOnlyList<SeatPosition> plan, IReadOnlyList<SeatPassenger> passengers,
        IReadOnlyList<SeatOccupancy> occupied, DateTime now)
    {
        var active = occupied.Where(s => s.Kind != "Hold" || s.ExpiresAt > now).ToList();
        foreach (var passenger in passengers)
        {
            if (!IsGender(passenger.Gender)) return "Her yolcu için cinsiyet seçiniz.";
            var seat = plan.SingleOrDefault(s => s.Number == passenger.SeatNumber);
            if (seat == null || seat.IsBlocked) return "Seçilen koltuk geçersiz veya satışa kapalı.";
            var taken = active.FirstOrDefault(s => s.SeatNumber == seat.Number);
            if (taken != null) return taken.Kind == "Hold" ? "Koltuk geçici olarak rezerve edilmiş. Lütfen başka bir koltuk seçiniz." : "Koltuk başka biri tarafından satın alınmış. Lütfen planı yenileyiniz.";
            var adjacent = Adjacent(plan, seat);
            if (adjacent == null) continue;
            var neighbor = active.FirstOrDefault(s => s.SeatNumber == adjacent.Number);
            if (neighbor != null && !IsGender(neighbor.Gender))
                return "Yan koltuktaki yolcunun cinsiyet bilgisi doğrulanamadığı için bu koltuk satışa uygun değil.";
            var gender = neighbor?.Gender ?? passengers.FirstOrDefault(p => p.SeatNumber == adjacent.Number)?.Gender;
            if (IsGender(gender) && gender != passenger.Gender)
                return $"Bu koltuğun yanındaki yolcu {(gender == "Female" ? "kadın" : "erkek")} olduğu için yalnızca {(gender == "Female" ? "kadın" : "erkek")} yolcular rezervasyon yapabilir.";
        }
        return null;
    }
}
