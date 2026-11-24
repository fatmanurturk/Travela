using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Data;
using Travela.Api.Security;
using Travela.Api.Staff;

namespace Travela.Api.Controllers;

public record CompanyAttentionItem(string Id, string Title, string Description, string Href, DateTime? Date);
public record CompanyAttentionGroup(string Id, string Title, int Count, List<CompanyAttentionItem> Items);
public record CompanyAttentionResponse(DateTime GeneratedAt, List<CompanyAttentionGroup> Groups);

[ApiController, Route("api/company/attention"), Authorize(Roles = "Company")]
public sealed class CompanyAttentionController(TravelaDbContext db, TimeProvider clock, IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        var companyId = await db.Users.AsNoTracking().ForSession(User, db).Where(u => u.Id == userId && u.Role == "Company")
            .Select(u => u.CompanyId).SingleOrDefaultAsync(ct);
        if (companyId == null) return Forbid();
        var now = clock.GetUtcNow().UtcDateTime; var tomorrow = now.AddHours(24);
        var trips = db.Trips.AsNoTracking().Where(t => t.CompanyId == companyId);
        var scheduled = trips.Where(t => t.Status == "Scheduled" || t.Status == "Planlandı" || t.Status == "Planlandi");
        var groups = new List<CompanyAttentionGroup>();
        async Task AddTrips(string id, string title, IQueryable<Trip> query, string explanation)
        {
            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(t => t.DepartureTime).ThenBy(t => t.Id).Select(t => new
                { t.Id, t.Route.DepartureCity, t.Route.ArrivalCity, t.DepartureTime }).Take(5).ToListAsync(ct);
            groups.Add(new(id, title, total, items.Select(t => new CompanyAttentionItem(t.Id.ToString(),
                $"#{t.Id} · {t.DepartureCity} → {t.ArrivalCity}", explanation,
                $"/company/trips?detail={t.Id}", DateTime.SpecifyKind(t.DepartureTime, DateTimeKind.Utc))).ToList()));
        }
        await AddTrips("departures", "Önümüzdeki 24 saatte kalkacak seferler", scheduled.Where(t => t.DepartureTime > now && t.DepartureTime <= tomorrow), "Kalkış öncesi sefer bilgilerini kontrol edin.");
        // Every trip already requires a bus. Flag unusable assignments rather than inventing unassigned trips.
        await AddTrips("resources", "Otobüs veya güzergâh kontrolü gereken seferler", scheduled.Where(t => t.DepartureTime > now &&
            (!t.Bus.IsActive || t.Bus.CompanyId != t.CompanyId || !t.Route.IsActive || t.Bus.SeatCapacity <= 0)), "Otobüsün firmaya bağlı ve aktif, güzergâhın kullanıma açık olduğunu kontrol edin.");
        var refunds = db.PaymentOrders.AsNoTracking().Where(p => p.Reservation.Trip.CompanyId == companyId && p.PaymentStatus == "Paid" &&
            p.RefundAmount > 0 && (p.RefundStatus == "Pending" || p.RefundStatus == "Failed"));
        var refundCount = await refunds.CountAsync(ct);
        var refundItems = await refunds.OrderByDescending(p => p.NeedsAttention).ThenByDescending(p => p.RefundStatus == "Failed")
            .ThenBy(p => p.RefundRequestedAt).ThenBy(p => p.Id).Select(p => new
                { p.Id, p.Reservation.TripId, p.Reservation.Pnr, p.RefundStatus, p.RefundAmount, p.RefundRequestedAt }).Take(5).ToListAsync(ct);
        groups.Add(new("refunds", "Tamamlanmamış iadeler", refundCount, refundItems.Select(p => new CompanyAttentionItem(p.Id.ToString(),
            p.Pnr == null ? $"Sefer #{p.TripId} · Ödeme işlemi" : $"PNR {p.Pnr}",
            $"{(p.RefundStatus == "Failed" ? "İade tamamlanamadı" : "İade sonucu bekleniyor")} · {p.RefundAmount:N2} TL",
            $"/company/reservations?orderId={p.Id}&tripId={p.TripId}",
            p.RefundRequestedAt.HasValue ? DateTime.SpecifyKind(p.RefundRequestedAt.Value, DateTimeKind.Utc) : null)).ToList()));
        if ((await authorization.AuthorizeAsync(User, null, "trip.view")).Succeeded)
        {
            var today = StaffDates.Today(clock); var limit = today.AddDays(30);
            var documents = db.StaffDocuments.AsNoTracking().Where(d => d.CompanyId == companyId && !d.IsIndefinite && d.ExpiresOn <= limit);
            var total = await documents.CountAsync(ct);
            var items = await documents.OrderBy(d => d.ExpiresOn).ThenBy(d => d.Id)
                .Select(d => new { d.Id, d.StaffId, d.Staff.FullName, d.Name, d.ExpiresOn }).Take(10).ToListAsync(ct);
            groups.Add(new("staff-documents", "Personel belge uyarıları", total, items.Select(d => {
                var validity = StaffDates.Validity(d.ExpiresOn, false, today);
                return new CompanyAttentionItem(d.Id.ToString(), $"{d.FullName} · {d.Name}",
                    $"{validity.Label} · Son geçerlilik: {d.ExpiresOn:dd.MM.yyyy} · {validity.Message}", $"/company/staff?detail={d.StaffId}", null);
            }).ToList()));
        }
        return Ok(new CompanyAttentionResponse(now, groups));
    }
}
