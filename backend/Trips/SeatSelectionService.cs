using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Payments;
using Travela.Data;

namespace Travela.Api.Trips;

public sealed class SeatSelectionService(TravelaDbContext db, TimeProvider clock, TripSegmentService segments)
{
    public const string TakenMessage = "Bu koltuk başka bir kullanıcı tarafından ayrıldı. Lütfen farklı bir koltuk seçin.";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static PaymentFlowException Expired() => new("Koltuk ayırma süreniz doldu. Lütfen koltuklarınızı yeniden seçin.", 409, "selection_expired");
    public static bool TokenMatches(SeatSelectionSession session, string? token) => token is { Length: 64 } &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(session.TokenHash), Encoding.ASCII.GetBytes(Hash(token)));
    public async Task<SeatSelectionSession> Require(Guid id, string? token, CancellationToken ct, bool locked = false)
    {
        var session = locked
            ? await db.SeatSelectionSessions.FromSqlInterpolated($"SELECT * FROM SeatSelectionSessions WITH (UPDLOCK,HOLDLOCK) WHERE Id = {id}").SingleOrDefaultAsync(ct)
            : await db.SeatSelectionSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        if (session == null || !TokenMatches(session, token)) throw new PaymentFlowException("Koltuk ayırma oturumu bulunamadı.", 404, "selection_not_found");
        return session;
    }
    public async Task<object> Create(Guid id, string token, int tripId, int? from, int? to, string ip, int? userId, CancellationToken ct)
    {
        if (id == Guid.Empty || !Regex.IsMatch(token, "^[a-fA-F0-9]{64}$")) throw new PaymentFlowException("Geçerli bir satın alma oturumu gereklidir.", 400, "selection_required");
        if (!await db.Trips.AnyAsync(t => t.Id == tripId, ct)) throw new PaymentFlowException("Sefer bulunamadı.", 404);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var trip = await segments.LockTrip(tripId, ct);
        var segment = await segments.Resolve(trip, from, to, ct);
        if (!await segments.CanSell(trip, segment, ct)) throw new PaymentFlowException("Sefer satışa kapalı.");
        var existing = await db.SeatSelectionSessions.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (existing == null)
        {
            existing = new() { Id = id, TokenHash = Hash(token), IpHash = Hash(ip), UserId = userId, TripId = tripId,
                BoardingStopOrder = segment.From.StopOrder, AlightingStopOrder = segment.To.StopOrder, CreatedAt = Now };
            db.SeatSelectionSessions.Add(existing); await db.SaveChangesAsync(ct);
        }
        else if (!TokenMatches(existing, token) || existing.TripId != tripId || existing.BoardingStopOrder != segment.From.StopOrder || existing.AlightingStopOrder != segment.To.StopOrder)
            throw new PaymentFlowException("Koltuk ayırma oturumu bulunamadı.", 404, "selection_not_found");
        await tx.CommitAsync(ct);
        return await View(existing, ct);
    }
    public async Task<object> Get(Guid id, string? token, CancellationToken ct) => await View(await Require(id, token, ct), ct);
    private async Task<object> View(SeatSelectionSession session, CancellationToken ct)
    {
        var payment = session.PurchaseGroupId == null ? null : await db.PurchaseGroups.AsNoTracking().SingleAsync(g => g.Id == session.PurchaseGroupId, ct);
        var expires = payment?.HoldExpiresAt ?? session.ExpiresAt;
        var expired = expires <= Now || session.CreatedAt.AddHours(24) <= Now;
        var seats = await db.Reservations.AsNoTracking().Where(r => r.SelectionSessionId == session.Id && r.Status == "Active" && r.HoldExpiresAt > Now)
            .OrderBy(r => r.Id).Select(r => new { r.SeatNumber, gender = r.PassengerGender }).ToListAsync(ct);
        return new { session.Id, serverTime = DateTime.SpecifyKind(Now, DateTimeKind.Utc), expiresAt = expires == null ? (DateTime?)null : DateTime.SpecifyKind(expires.Value, DateTimeKind.Utc),
            phase = payment != null ? "Payment" : expired ? "Expired" : "Selection", session.PurchaseGroupId,
            paymentStatus = payment?.PaymentStatus, canStartNewSelection = payment?.PaymentStatus is "Failed" or "Paid", seats };
    }
    public async Task<object> Change(Guid id, string? token, int seat, string? gender, bool remove, CancellationToken ct)
    {
        var known = await Require(id, token, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var trip = await segments.LockTrip(known.TripId, ct);
        // Database application lock makes aggregate abuse limits work across instances/trips.
        var resource = "seat-selection:" + known.IpHash;
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000; IF @r<0 THROW 51031, 'Seat selection busy', 1;", ct);
        var session = await Require(id, token, ct, true);
        if (session.PurchaseGroupId != null) throw new PaymentFlowException("Koltuklar ödeme aşamasına geçti. Mevcut siparişinizden devam edin.", 409, "selection_in_payment");
        if (session.ExpiresAt <= Now || session.CreatedAt.AddHours(24) <= Now) throw Expired();
        await segments.ExpireLocked(trip, ct);
        var own = await db.Reservations.Where(r => r.SelectionSessionId == id && r.Status == "Active" && r.Kind == "Hold" && r.HoldExpiresAt > Now).ToListAsync(ct);
        var current = own.SingleOrDefault(r => r.SeatNumber == seat);
        if (remove)
        {
            if (current != null) segments.Release(current, "Koltuk seçimden çıkarıldı.");
        }
        else
        {
            if (!SeatCompatibility.IsGender(gender)) throw new PaymentFlowException("Yolcu cinsiyetini seçiniz.", 400, "gender_required");
            if (current == null && own.Count >= 10) throw new PaymentFlowException("En fazla 10 koltuk seçebilirsiniz.", 409, "selection_limit");
            var segment = await segments.Resolve(trip, session.BoardingStopOrder, session.AlightingStopOrder, ct);
            if (!await segments.CanSell(trip, segment, ct)) throw new PaymentFlowException("Sefer satışa kapalı.");
            if (await segments.OccupiedLegs(trip.Id, session.BoardingStopOrder, session.AlightingStopOrder).AnyAsync(l => l.SeatNumber == seat && l.Reservation.SelectionSessionId != id, ct))
                throw new PaymentFlowException(TakenMessage, 409, "seat_unavailable");
            var selected = own.Where(r => r.SeatNumber != seat).Select(r => new SeatPassenger(r.SeatNumber, r.PassengerGender)).Append(new(seat, gender)).ToList();
            var error = await segments.ValidateSelection(trip, session.BoardingStopOrder, session.AlightingStopOrder, selected, ct, own.Select(r => r.Id).ToList());
            if (error != null) throw new PaymentFlowException(error, 409, "seat_incompatible");
            if (current == null)
            {
                var activeSessionIds = db.SeatSelectionSessions.Where(s => s.IpHash == session.IpHash || session.UserId != null && s.UserId == session.UserId).Select(s => s.Id);
                if (await db.Reservations.CountAsync(r => r.SelectionSessionId != null && activeSessionIds.Contains(r.SelectionSessionId.Value) && r.Status == "Active" && r.Kind == "Hold" && r.HoldExpiresAt > Now, ct) >= 40)
                    throw new PaymentFlowException("Aktif koltuk ayırma sınırına ulaştınız. Mevcut seçiminizi tamamlayın veya süre dolmasını bekleyin.", 429, "active_hold_limit");
                session.ExpiresAt ??= new[] { Now.AddMinutes(5), segment.From.DepartureTime }.Min();
                current = new() { TripId = trip.Id, SelectionSessionId = id, SeatNumber = seat, PassengerGender = gender,
                    Kind = "Hold", Price = segment.Price, HoldExpiresAt = session.ExpiresAt };
                TripSegmentService.Capture(current, segment); db.Reservations.Add(current);
            }
            else current.PassengerGender = gender;
        }
        await segments.SaveInventory(trip, ct); await tx.CommitAsync(ct);
        return await View(session, ct);
    }
}
