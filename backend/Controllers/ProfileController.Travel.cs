using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Travela.API.Controllers;

public record ProfileJourney(int TripId, string Pnr, string CompanyName, string DepartureCity, string ArrivalCity,
    DateTime DepartureTime, DateTime ArrivalTime, int SeatNumber, decimal Amount, string Currency);
public record ProfileNotice(string Id, string Title, string Description, string Href, DateTime Date);

public partial class ProfileController
{
    [HttpGet("travel-summary")]
    public async Task<IActionResult> TravelSummary(CancellationToken ct)
    {
        var user = await CurrentUser(ct); if (user == null) return Unauthorized();
        var now = clock.GetUtcNow().UtcDateTime; var cutoff = now.AddDays(-30); var tomorrow = now.AddHours(24);
        var orders = db.PaymentOrders.AsNoTracking().Where(p => p.PassengerId == user.Id && p.PaymentStatus == "Paid" && p.Reservation.Kind == "Ticket" && p.Reservation.Pnr != null);
        var upcoming = orders.Where(p => p.Reservation.Status == "Active" && p.Reservation.Trip.DepartureTime > now && p.Reservation.Trip.Status == "Scheduled");
        var count = await upcoming.CountAsync(ct);
        var next = await upcoming.OrderBy(p => p.Reservation.Trip.DepartureTime).ThenBy(p => p.Id).Select(p => new ProfileJourney(
            p.Reservation.TripId, p.Reservation.Pnr!, p.Reservation.Trip.Company.Name, p.Reservation.Trip.Route.DepartureCity, p.Reservation.Trip.Route.ArrivalCity,
            p.Reservation.Trip.DepartureTime, p.Reservation.Trip.ArrivalTime, p.Reservation.SeatNumber, p.Amount, p.Currency)).FirstOrDefaultAsync(ct);
        if (next != null) next = next with { DepartureTime = Utc(next.DepartureTime), ArrivalTime = Utc(next.ArrivalTime) };
        var notices = new List<ProfileNotice>();
        if (user.TripRemindersEnabled)
        {
            var reminders = await upcoming.Where(p => p.Reservation.Trip.DepartureTime <= tomorrow).OrderBy(p => p.Reservation.Trip.DepartureTime).ThenBy(p => p.Id)
                .Select(p => new { p.Reservation.Pnr, p.Reservation.Trip.Route.DepartureCity, p.Reservation.Trip.Route.ArrivalCity, p.Reservation.Trip.DepartureTime }).Take(5).ToListAsync(ct);
            notices.AddRange(reminders.Select(r => new ProfileNotice("trip-" + r.Pnr, "Yolculuğun yaklaşıyor", $"{r.DepartureCity} → {r.ArrivalCity} seferin önümüzdeki 24 saat içinde kalkacak.",
                "/tickets/" + Uri.EscapeDataString(r.Pnr!), Utc(r.DepartureTime))));
        }
        if (user.CancellationNotificationsEnabled)
        {
            var cancellations = await orders.Where(p => p.Reservation.Status == "Cancelled" && p.Reservation.CancelledAt >= cutoff).OrderByDescending(p => p.Reservation.CancelledAt).ThenBy(p => p.Id)
                .Select(p => new { p.Reservation.Pnr, p.Reservation.Trip.Route.DepartureCity, p.Reservation.Trip.Route.ArrivalCity, p.Reservation.CancelledAt }).Take(5).ToListAsync(ct);
            notices.AddRange(cancellations.Select(r => new ProfileNotice("cancel-" + r.Pnr, "Biletin iptal edildi", $"{r.DepartureCity} → {r.ArrivalCity} biletinin iptal ve iade durumunu inceleyebilirsin.",
                "/tickets/" + Uri.EscapeDataString(r.Pnr!), Utc(r.CancelledAt!.Value))));
        }
        if (user.RefundNotificationsEnabled)
        {
            var refunds = await orders.Where(p => (p.RefundStatus == "Pending" || p.RefundStatus == "Failed" || p.RefundStatus == "Succeeded") && p.RefundStatusChangedAt >= cutoff)
                .OrderByDescending(p => p.RefundStatusChangedAt).ThenBy(p => p.Id).Select(p => new { p.Reservation.Pnr, p.RefundStatus, p.RefundStatusChangedAt }).Take(5).ToListAsync(ct);
            notices.AddRange(refunds.Select(r => new ProfileNotice("refund-" + r.Pnr,
                r.RefundStatus == "Succeeded" ? "İaden tamamlandı" : r.RefundStatus == "Failed" ? "İaden henüz tamamlanamadı" : "İade talebin işleniyor",
                "Güncel iade durumunu bilet detayından takip edebilirsin.", "/tickets/" + Uri.EscapeDataString(r.Pnr!), Utc(r.RefundStatusChangedAt!.Value))));
        }
        return Ok(new { upcomingCount = count, upcomingTrip = next, notifications = notices.OrderByDescending(n => n.Date).Take(10), generatedAt = now });
    }
}
