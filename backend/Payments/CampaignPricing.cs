using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Data;

namespace Travela.Api.Payments;

public sealed record CampaignPrice(decimal OriginalAmount, decimal DiscountAmount, decimal Amount,
    int? CampaignId = null, string? CampaignName = null, string? CouponCode = null);

public static class CampaignPricing
{
    public static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var value = code.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(value, "^[A-Z0-9][A-Z0-9_-]{2,31}$"))
            throw new PaymentFlowException("Kupon kodu 3–32 harf, rakam, tire veya alt çizgiden oluşmalıdır.", 400);
        return value;
    }

    public static decimal Discount(Campaign c, decimal subtotal, int passengers)
    {
        if (subtotal < c.MinimumSubtotal) return 0;
        var value = c.DiscountType == "Percentage" ? subtotal * c.Value / 100m : c.Value;
        if (c.MaximumDiscount.HasValue) value = Math.Min(value, c.MaximumDiscount.Value);
        // The existing payment providers require a positive amount for every ticket.
        return Math.Max(0, Math.Min(decimal.Round(value, 2, MidpointRounding.AwayFromZero), subtotal - passengers * 0.01m));
    }

    public static IQueryable<PurchaseGroup> Uses(TravelaDbContext db, int campaignId, DateTime now) =>
        db.PurchaseGroups.Where(g => g.CampaignId == campaignId &&
            (g.PaymentStatus == "Pending" && g.HoldExpiresAt > now ||
             g.PaymentStatus == "Paid" && g.Items.Any(p => p.Reservation.Kind == "Ticket")));

    // Reserve capacity in the same serializable transaction as the seat holds.
    // Campaign locks are always taken by ID, after the trip lock, including automatic selection.
    public static async Task<CampaignPrice> Quote(TravelaDbContext db, Trip trip, int userId, int passengers,
        string? couponCode, DateTime now, bool reserve, CancellationToken ct)
    {
        if (passengers is < 1 or > 10) throw new PaymentFlowException("1–10 yolcu seçilmelidir.", 400);
        var subtotal = decimal.Round(trip.Price, 2) * passengers;
        if (subtotal <= 0) throw new PaymentFlowException("Sefer fiyatı geçersiz.");
        var code = NormalizeCode(couponCode);
        var ids = await db.Campaigns.AsNoTracking().Where(c => c.CompanyId == trip.CompanyId && c.IsActive &&
            c.StartsAt <= now && c.EndsAt > now && (c.RouteId == null || c.RouteId == trip.RouteId) &&
            (c.DepartureFrom == null || c.DepartureFrom <= trip.DepartureTime) &&
            (c.DepartureUntil == null || c.DepartureUntil > trip.DepartureTime) &&
            c.MinimumSubtotal <= subtotal && c.Code == code).OrderBy(c => c.Id).Select(c => c.Id).ToListAsync(ct);
        var best = new CampaignPrice(subtotal, 0, subtotal);
        foreach (var id in ids)
        {
            var c = reserve
                ? await db.Campaigns.FromSqlInterpolated($"SELECT * FROM Campaigns WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}").SingleAsync(ct)
                : await db.Campaigns.AsNoTracking().SingleAsync(c => c.Id == id, ct);
            // Recheck after locking: management may have changed the campaign meanwhile.
            if (!c.IsActive || c.StartsAt > now || c.EndsAt <= now || c.Code != code || c.CompanyId != trip.CompanyId ||
                c.RouteId.HasValue && c.RouteId != trip.RouteId || c.DepartureFrom > trip.DepartureTime || c.DepartureUntil <= trip.DepartureTime) continue;
            var uses = Uses(db, id, now);
            if (c.UsageLimit.HasValue && await uses.CountAsync(ct) >= c.UsageLimit.Value ||
                await uses.CountAsync(g => g.PassengerId == userId, ct) >= c.PerUserLimit) continue;
            var discount = Discount(c, subtotal, passengers);
            if (discount > best.DiscountAmount) best = new(subtotal, discount, subtotal - discount, c.Id, c.Name, c.Code);
        }
        if (code != null && best.CampaignId == null)
            throw new PaymentFlowException("Kupon bu sefer, tarih veya sepet için kullanılamıyor; kullanım limiti dolmuş olabilir.", 400);
        return best;
    }
}
