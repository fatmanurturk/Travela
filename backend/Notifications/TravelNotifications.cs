using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Trips;
using Travela.Data;

namespace Travela.Api.Notifications;

public static class TravelNotifications
{
    // Queue in the same transaction as issuance/cancellation/change/refund. No network calls here.
    public static async Task Queue(TravelaDbContext db, Reservation reservation, string type, string key, DateTime now, string? details = null, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == reservation.PassengerId, ct);
        var trip = await db.Trips.Include(t => t.Route).Include(t => t.Company).Include(t => t.Bus).SingleAsync(t => t.Id == reservation.TripId, ct);
        var journey = await new TripSegmentService(db, TimeProvider.System).Journey(trip, reservation, ct);
        var subject = type switch { "Purchase" => "Biletiniz hazır", "Reminder" => "Yolculuğunuz yaklaşıyor", "Cancellation" => "Seferiniz iptal edildi", "RefundSucceeded" => "İadeniz tamamlandı", "RefundFailed" => "İadeniz henüz tamamlanamadı", _ => "Seferinizde değişiklik var" };
        var enabled = type switch { "Reminder" => (user?.TripRemindersEnabled ?? true), "Cancellation" => (user?.CancellationNotificationsEnabled ?? true), "RefundSucceeded" or "RefundFailed" => (user?.RefundNotificationsEnabled ?? true), _ => true };
        var body = $"Travela — {subject}. PNR: {reservation.Pnr ?? "Düzenlenmedi"}. {journey.DepartureCity} → {journey.ArrivalCity}. Kalkış: {journey.DepartureTime.AddHours(3):dd.MM.yyyy HH:mm} (Türkiye). Koltuk: {reservation.SeatNumber}. " +
            $"Biniş: {journey.DepartureTerminalName} {journey.DepartureTerminalAddress}. {journey.BoardingInstructions} {details}";
        var contact = await db.PaymentOrders.Where(p => p.ReservationId == reservation.Id && p.PurchaseGroupId != null)
            .Select(p => new { p.PurchaseGroup!.ContactEmail, p.PurchaseGroup.ContactPhone }).SingleOrDefaultAsync(ct);
        var channels = new[] { ("Email", contact?.ContactEmail ?? reservation.PassengerEmailSnapshot ?? user?.Email ?? "", (user?.EmailNotificationsEnabled ?? true)), ("Sms", contact?.ContactPhone ?? reservation.PassengerPhoneSnapshot ?? user?.PhoneNumber ?? "", (user?.SmsNotificationsEnabled ?? true)) };
        foreach (var (channel, recipient, channelEnabled) in channels)
        {
            if (db.TravelNotifications.Local.Any(n => n.ReservationId == reservation.Id && n.EventKey == key && n.Channel == channel) ||
                await db.TravelNotifications.AnyAsync(n => n.ReservationId == reservation.Id && n.EventKey == key && n.Channel == channel, ct)) continue;
            var send = enabled && channelEnabled && !string.IsNullOrWhiteSpace(recipient);
            db.TravelNotifications.Add(new() { ReservationId = reservation.Id, EventKey = key, Type = type, Channel = channel, Recipient = recipient,
                Subject = subject, Body = body, CreatedAt = now, NextAttemptAt = now, Status = send ? "Pending" : "Skipped",
                LastError = send ? null : "Bildirim tercihi kapalı veya iletişim bilgisi eksik.", CompletedAt = send ? null : now });
        }
    }

    public static async Task Reminders(TravelaDbContext db, DateTime now, CancellationToken ct)
    {
        var upper = now.AddHours(24);
        var items = await db.Reservations.Where(r => r.Kind == "Ticket" && r.Status == "Active" && r.Payment != null && r.Payment.PaymentStatus == "Paid" &&
            (r.ChangeCancellationUntil != null || r.Payment!.PurchaseGroupId == null ? r.Trip.DepartureTime : r.BoardingDepartureSnapshot ?? r.Trip.DepartureTime) > now &&
            (r.ChangeCancellationUntil != null || r.Payment!.PurchaseGroupId == null ? r.Trip.DepartureTime : r.BoardingDepartureSnapshot ?? r.Trip.DepartureTime) <= upper && (r.Trip.Status == "Scheduled" || r.Trip.Status == "InProgress") &&
            !db.TravelNotifications.Any(n => n.ReservationId == r.Id && n.Type == "Reminder" && n.EventKey == "reminder:" + r.Trip.ChangeVersion.ToString()))
            .OrderBy(r => r.Trip.DepartureTime).Take(100).Include(r => r.Trip).ToListAsync(ct);
        foreach (var r in items)
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            // Same lock order as payment and operational changes.
            var trip = await db.Trips.FromSqlInterpolated($"SELECT * FROM Trips WITH (UPDLOCK, HOLDLOCK) WHERE Id = {r.TripId}").SingleAsync(ct);
            await db.Entry(r).ReloadAsync(ct);
            await db.Entry(trip).ReloadAsync(ct);
            var journey = await new TripSegmentService(db, TimeProvider.System).Journey(trip, r, ct);
            if (r.Status == "Active" && (trip.Status == "Scheduled" || trip.Status == "InProgress") && journey.DepartureTime > now && journey.DepartureTime <= upper)
                await Queue(db, r, "Reminder", "reminder:" + trip.ChangeVersion, now, ct: ct);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
    }
}
