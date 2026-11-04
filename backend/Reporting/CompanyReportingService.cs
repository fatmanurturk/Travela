using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Data;
using Travela.DTOs;

namespace Travela.Api.Reporting;

public class ReportQueryException(string message, int status = 400) : Exception(message) { public int Status { get; } = status; }

public class CompanyReportingService(TravelaDbContext db, TimeProvider clock)
{
    public const int CsvLimit = 10000;
    public ReportScope Scope(CompanyReportQuery q)
    {
        if (q.Environment is not ("all" or "real" or "test") || q.Basis is not ("transaction" or "departure") ||
            q.Period is not ("today" or "week" or "month" or "custom") || q.Page < 1 || q.PageSize is < 1 or > 100 ||
            q.Sort is not ("departure" or "sold" or "collected" or "refunded" or "occupancy" or "available") ||
            q.Direction is not ("asc" or "desc") || q.RouteSort is not ("sold" or "collected" or "refunded") || q.TripId <= 0 || q.RouteId <= 0)
            throw new ReportQueryException("Rapor filtresi, sıralama veya sayfa bilgisi geçersiz.");
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(3)).DateTime);
        var from = q.Period switch { "today" => today, "week" => today.AddDays(-6), "month" => new DateOnly(today.Year, today.Month, 1), _ => q.From ?? throw new ReportQueryException("Başlangıç tarihi gereklidir.") };
        var to = q.Period == "custom" ? q.To ?? throw new ReportQueryException("Bitiş tarihi gereklidir.") : today;
        if (from.Year < 2000 || to.Year > 2100 || to < from || to.DayNumber - from.DayNumber >= 366)
            throw new ReportQueryException("Tarih aralığı 2000–2100 yılları arasında ve en fazla 366 gün olmalıdır; bitiş başlangıçtan önce olamaz.");
        DateTime Utc(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(3)).UtcDateTime;
        return new(from, to, Utc(from), Utc(to.AddDays(1)), "all", q.Basis, now.UtcDateTime,
            q.Basis == "transaction"
                ? "İşlem tarihi: satış düzenlenme, iptal iptal zamanı, tahsilat doğrulanma, tamamlanan iade tamamlanma, bekleyen/başarısız iade güncel duruma geçiş tarihindedir. İade önceki dönemin satışına ait olabilir. Aktif bilet, dönemde düzenlenip halen aktif olan bilettir."
                : "Sefer kalkış tarihi: seçili tarihlerdeki seferlere ait kayıtların güncel durumları ve tüm ilgili doğrulanmış ödeme/iadeleridir; işlem dönemine ait nakit hareketi değildir.");
    }

    public async Task ValidateOwnership(int companyId, CompanyReportQuery q, CancellationToken ct)
    {
        if (q.TripId.HasValue && !await db.Trips.AnyAsync(t => t.Id == q.TripId && t.CompanyId == companyId, ct)) throw new ReportQueryException("Sefer bulunamadı.", 404);
        if (q.RouteId.HasValue && !await db.Trips.AnyAsync(t => t.RouteId == q.RouteId && t.CompanyId == companyId, ct)) throw new ReportQueryException("Güzergâh bulunamadı.", 404);
    }
    private IQueryable<Trip> CompanyTrips(int companyId, CompanyReportQuery q) => db.Trips.AsNoTracking()
        .Where(t => t.CompanyId == companyId && (!q.TripId.HasValue || t.Id == q.TripId) && (!q.RouteId.HasValue || t.RouteId == q.RouteId));
    private IQueryable<PaymentOrder> Orders(int companyId, CompanyReportQuery q, ReportScope s)
    {
        var query = db.PaymentOrders.AsNoTracking().Where(p => p.Reservation.Trip.CompanyId == companyId &&
            (!q.TripId.HasValue || p.Reservation.TripId == q.TripId) && (!q.RouteId.HasValue || p.Reservation.Trip.RouteId == q.RouteId));
        return q.Basis == "departure" ? query.Where(p => p.Reservation.Trip.DepartureTime >= s.StartUtc && p.Reservation.Trip.DepartureTime < s.EndExclusiveUtc) : query;
    }
    private IQueryable<ReportFact> Facts(int companyId, CompanyReportQuery q, ReportScope s)
    {
        var cohort = q.Basis == "departure"; var start = s.StartUtc; var end = s.EndExclusiveUtc;
        return Orders(companyId, q, s).Select(p => new ReportFact
        {
            OrderId = p.Id, TripId = p.Reservation.TripId, RouteId = p.Reservation.Trip.RouteId,
            DepartureCity = p.Reservation.Trip.Route.DepartureCity, ArrivalCity = p.Reservation.Trip.Route.ArrivalCity,
            Departure = p.Reservation.Trip.DepartureTime, IssuedAt = p.Reservation.IssuedAt,
            CancelledAt = p.Reservation.CancelledAt, PaidAt = p.PaidAt, RefundedAt = p.RefundedAt,
            Pnr = p.Reservation.Pnr, Status = p.Reservation.Status, RefundStatus = p.RefundStatus, Amount = p.Amount,
            Sale = p.PaymentStatus == "Paid" && p.Reservation.Kind == "Ticket" && p.Reservation.Pnr != null && (cohort || (p.Reservation.IssuedAt >= start && p.Reservation.IssuedAt < end)),
            Cancellation = p.PaymentStatus == "Paid" && p.Reservation.Kind == "Ticket" && p.Reservation.Pnr != null && p.Reservation.Status == "Cancelled" && (cohort || (p.Reservation.CancelledAt >= start && p.Reservation.CancelledAt < end)),
            Collected = p.PaymentStatus == "Paid" && (cohort || (p.PaidAt >= start && p.PaidAt < end)) ? p.Amount : 0,
            Refunded = p.PaymentStatus == "Paid" && p.RefundStatus == "Succeeded" && (cohort || (p.RefundedAt >= start && p.RefundedAt < end)) ? p.RefundAmount : 0,
            Pending = p.PaymentStatus == "Paid" && p.RefundStatus == "Pending" && (cohort || (p.RefundStatusChangedAt >= start && p.RefundStatusChangedAt < end)) ? p.RefundAmount : 0,
            Failed = p.PaymentStatus == "Paid" && p.RefundStatus == "Failed" && (cohort || (p.RefundStatusChangedAt >= start && p.RefundStatusChangedAt < end)),
            RefundAmount = p.RefundAmount,
            CreatedInRange = cohort || (p.CreatedAt >= start && p.CreatedAt < end)
        }).Where(f => f.Sale || f.Cancellation || f.Collected != 0 || f.Refunded != 0 || f.Pending != 0 || f.Failed || f.CreatedInRange);
    }
    private static IQueryable<TripMetric> ByTrip(IQueryable<ReportFact> facts) => facts.GroupBy(f => f.TripId).Select(g => new TripMetric
    {
        Id = g.Key, Sold = g.Sum(f => f.Sale ? 1 : 0), Active = g.Sum(f => f.Sale && f.Status == "Active" ? 1 : 0), Cancelled = g.Sum(f => f.Cancellation ? 1 : 0),
        Collected = g.Sum(f => f.Collected), Refunded = g.Sum(f => f.Refunded), PendingRefund = g.Sum(f => f.Pending),
        FailedRefundCount = g.Sum(f => f.Failed ? 1 : 0), FailedRefundAmount = g.Sum(f => f.Failed ? f.RefundAmount : 0)
    });

    private IQueryable<TripInventory> Inventories(int companyId, CompanyReportQuery q, ReportScope s, IQueryable<ReportFact> facts)
    {
        var now = s.GeneratedAt;
        var trips = CompanyTrips(companyId, q);
        trips = q.Basis == "departure" ? trips.Where(t => t.DepartureTime >= s.StartUtc && t.DepartureTime < s.EndExclusiveUtc) : trips.Where(t => facts.Any(f => f.TripId == t.Id));
        return trips.Select(t => new TripInventory
        {
            Id = t.Id, RouteId = t.RouteId, DepartureCity = t.Route.DepartureCity, ArrivalCity = t.Route.ArrivalCity,
            Departure = t.DepartureTime, Plate = t.Bus.PlateNumber, Capacity = t.SellableSeatCount ?? t.Bus.SeatCapacity, StoredAvailable = t.AvailableSeats, Status = t.Status,
            ActiveSold = db.Reservations.Count(r => r.TripId == t.Id && r.Status == "Active" && r.Kind == "Ticket" && r.Pnr != null && r.Payment != null && r.Payment.PaymentStatus == "Paid"),
            Held = db.Reservations.Count(r => r.TripId == t.Id && r.Status == "Active" && r.Kind == "Hold" && r.HoldExpiresAt > now && r.Payment != null),
            Legacy = db.Reservations.Count(r => r.TripId == t.Id && r.Status == "Active" && r.Kind == "Legacy"),
            OtherEnvironment = 0,
            Expired = db.Reservations.Count(r => r.TripId == t.Id && r.Status == "Active" && r.Kind == "Hold" && r.HoldExpiresAt <= now),
            AllActive = db.Reservations.Count(r => r.TripId == t.Id && r.Status == "Active"),
            Invalid = db.Reservations.Any(r => r.TripId == t.Id && r.Status == "Active" &&
                (r.SeatNumber < 1 || r.SeatNumber > (t.SeatCapacitySnapshot ?? t.Bus.SeatCapacity) || (r.Kind == "Ticket" && (r.Payment == null || r.Payment.PaymentStatus != "Paid" || r.Pnr == null)) || (r.Kind == "Hold" && r.Payment == null))),
            Open = t.DepartureTime > now && t.Company.IsActive && t.Bus.IsActive && t.Route.IsActive && t.Bus.CompanyId == t.CompanyId &&
                (t.DepartureTerminal == null || t.DepartureTerminal.IsActive) && (t.ArrivalTerminal == null || t.ArrivalTerminal.IsActive) &&
                (t.Status == "Scheduled" || t.Status == "Planlandı" || t.Status == "Planlandi")
        });
    }
    private IQueryable<TripReport> TripRows(int companyId, CompanyReportQuery q, ReportScope s, IQueryable<ReportFact> facts)
    {
        var metrics = ByTrip(facts);
        return from t in Inventories(companyId, q, s, facts)
               join m in metrics on t.Id equals m.Id into matches
               from m in matches.DefaultIfEmpty()
               let valid = t.Capacity > 0 && !t.Invalid && t.StoredAvailable == t.Capacity - t.AllActive && t.AllActive <= t.Capacity
               select new TripReport
               {
                   Id = t.Id, RouteId = t.RouteId, DepartureCity = t.DepartureCity, ArrivalCity = t.ArrivalCity,
                   DepartureTime = t.Departure, Plate = t.Plate, Capacity = t.Capacity, Status = t.Status,
                   ActiveSoldSeats = t.ActiveSold, HeldSeats = t.Held, LegacyBlockedSeats = t.Legacy, OtherEnvironmentBlocks = t.OtherEnvironment,
                   ExpiredHolds = t.Expired, AvailableForSale = !valid ? null : t.Open ? t.Capacity - t.AllActive + t.Expired : 0,
                   OccupancyPercent = valid ? 100m * t.ActiveSold / t.Capacity : null,
                   DataWarning = valid ? null : "Kapasite veya koltuk kayıtları tutarsız; satışa açık koltuk ve doluluk hesaplanmadı.",
                   Sold = (int?)m.Sold ?? 0, Active = (int?)m.Active ?? 0, Cancelled = (int?)m.Cancelled ?? 0,
                   Collected = (decimal?)m.Collected ?? 0, Refunded = (decimal?)m.Refunded ?? 0, PendingRefund = (decimal?)m.PendingRefund ?? 0,
                   FailedRefundCount = (int?)m.FailedRefundCount ?? 0, FailedRefundAmount = (decimal?)m.FailedRefundAmount ?? 0
               };
    }
    private static IOrderedQueryable<TripReport> Sort(IQueryable<TripReport> rows, CompanyReportQuery q) => q.Sort switch
    {
        "sold" => q.Direction == "asc" ? rows.OrderBy(t => t.Sold).ThenBy(t => t.Id) : rows.OrderByDescending(t => t.Sold).ThenBy(t => t.Id),
        "collected" => q.Direction == "asc" ? rows.OrderBy(t => t.Collected).ThenBy(t => t.Id) : rows.OrderByDescending(t => t.Collected).ThenBy(t => t.Id),
        "refunded" => q.Direction == "asc" ? rows.OrderBy(t => t.Refunded).ThenBy(t => t.Id) : rows.OrderByDescending(t => t.Refunded).ThenBy(t => t.Id),
        "occupancy" => q.Direction == "asc" ? rows.OrderBy(t => t.OccupancyPercent).ThenBy(t => t.Id) : rows.OrderByDescending(t => t.OccupancyPercent).ThenBy(t => t.Id),
        "available" => q.Direction == "asc" ? rows.OrderBy(t => t.AvailableForSale).ThenBy(t => t.Id) : rows.OrderByDescending(t => t.AvailableForSale).ThenBy(t => t.Id),
        _ => q.Direction == "asc" ? rows.OrderBy(t => t.DepartureTime).ThenBy(t => t.Id) : rows.OrderByDescending(t => t.DepartureTime).ThenBy(t => t.Id)
    };
    private static void Utc(TripReport row) => row.DepartureTime = DateTime.SpecifyKind(row.DepartureTime, DateTimeKind.Utc);

    public async Task<CompanyReportResponse> Get(int companyId, CompanyReportQuery q, CancellationToken ct)
    {
        var scope = Scope(q); await ValidateOwnership(companyId, q, ct);
        var facts = Facts(companyId, q, scope); var rows = TripRows(companyId, q, scope, facts);
        var result = new CompanyReportResponse { Scope = scope };
        result.Metrics = await facts.GroupBy(_ => 1).Select(g => new ReportMetrics
        {
            Sold = g.Sum(f => f.Sale ? 1 : 0), Active = g.Sum(f => f.Sale && f.Status == "Active" ? 1 : 0), Cancelled = g.Sum(f => f.Cancellation ? 1 : 0),
            Collected = g.Sum(f => f.Collected), Refunded = g.Sum(f => f.Refunded), PendingRefund = g.Sum(f => f.Pending),
            FailedRefundCount = g.Sum(f => f.Failed ? 1 : 0), FailedRefundAmount = g.Sum(f => f.Failed ? f.RefundAmount : 0)
        }).SingleOrDefaultAsync(ct) ?? new();
        result.Occupancy = await rows.GroupBy(_ => 1).Select(g => new ReportOccupancy
        {
            TripCount = g.Count(), Capacity = g.Sum(t => (long)t.Capacity), ActiveSoldSeats = g.Sum(t => t.ActiveSoldSeats), InvalidTrips = g.Sum(t => t.DataWarning != null ? 1 : 0)
        }).SingleOrDefaultAsync(ct) ?? new();
        if (result.Occupancy.Capacity > 0 && result.Occupancy.InvalidTrips == 0)
            result.Occupancy.Percent = decimal.Round(100m * result.Occupancy.ActiveSoldSeats / result.Occupancy.Capacity, 2);
        result.TotalTrips = result.Occupancy.TripCount;
        result.Trips = await Sort(rows, q).Skip((int)Math.Min((long)(q.Page - 1) * q.PageSize, int.MaxValue)).Take(q.PageSize).ToListAsync(ct);
        result.Trips.ForEach(Utc);
        result.Upcoming = await rows.Where(t => t.DepartureTime > scope.GeneratedAt && (t.Status == "Scheduled" || t.Status == "Planlandı" || t.Status == "Planlandi"))
            .OrderBy(t => t.DepartureTime).ThenBy(t => t.Id).Take(5).ToListAsync(ct);
        result.Upcoming.ForEach(Utc);
        var routes = facts.GroupBy(f => new { f.RouteId, f.DepartureCity, f.ArrivalCity }).Select(g => new RouteReport
        {
            RouteId = g.Key.RouteId, DepartureCity = g.Key.DepartureCity, ArrivalCity = g.Key.ArrivalCity,
            Sold = g.Sum(f => f.Sale ? 1 : 0), Active = g.Sum(f => f.Sale && f.Status == "Active" ? 1 : 0), Cancelled = g.Sum(f => f.Cancellation ? 1 : 0),
            Collected = g.Sum(f => f.Collected), Refunded = g.Sum(f => f.Refunded), PendingRefund = g.Sum(f => f.Pending),
            FailedRefundCount = g.Sum(f => f.Failed ? 1 : 0), FailedRefundAmount = g.Sum(f => f.Failed ? f.RefundAmount : 0)
        });
        result.RouteCount = await routes.CountAsync(ct);
        result.Routes = await (q.RouteSort == "collected" ? routes.OrderByDescending(r => r.Collected).ThenBy(r => r.RouteId) : q.RouteSort == "refunded" ? routes.OrderByDescending(r => r.Refunded).ThenBy(r => r.RouteId) : routes.OrderByDescending(r => r.Sold).ThenBy(r => r.RouteId)).Take(20).ToListAsync(ct);
        result.Daily = await Daily(facts, scope, ct);
        var sales = facts.Where(f => f.Sale).Select(f => new { f.OrderId, f.TripId, f.Pnr, Type = "Sale", Date = f.IssuedAt ?? f.PaidAt!.Value, f.Amount, f.RefundStatus });
        var cancellations = facts.Where(f => f.Cancellation).Select(f => new { f.OrderId, f.TripId, f.Pnr, Type = "Cancellation", Date = f.CancelledAt!.Value, f.Amount, f.RefundStatus });
        var recent = await sales.Concat(cancellations).OrderByDescending(f => f.Date).ThenBy(f => f.OrderId).Take(10).ToListAsync(ct);
        result.Recent = recent.Select(f => new ReportActivity(f.OrderId, f.TripId, f.Pnr, f.Type, DateTime.SpecifyKind(f.Date, DateTimeKind.Utc), f.Amount, f.RefundStatus)).ToList();
        var orders = Orders(companyId, q, scope);
        var unknown = await orders.Where(p => p.PaymentStatus == "Paid" && (p.RefundStatus == "Pending" || p.RefundStatus == "Failed") && p.RefundStatusChangedAt == null)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Amount = g.Sum(p => p.RefundAmount) }).SingleOrDefaultAsync(ct);
        result.UnknownRefundDateCount = unknown?.Count ?? 0; result.UnknownRefundDateAmount = unknown?.Amount ?? 0;
        var attention = await orders.Where(p => p.PaymentStatus == "Paid" && p.RefundStatus == "Failed" &&
            (q.Basis == "departure" || p.RefundStatusChangedAt == null || (p.RefundStatusChangedAt >= scope.StartUtc && p.RefundStatusChangedAt < scope.EndExclusiveUtc)))
            .OrderByDescending(p => p.NeedsAttention).ThenBy(p => p.RefundStatusChangedAt).Select(p => new { p.Id, p.Reservation.TripId, p.Reservation.Pnr, p.RefundAmount, p.LastError, p.RefundStatusChangedAt }).Take(10).ToListAsync(ct);
        result.FailedRefunds = attention.Select(p => new ReportRefundAttention(p.Id, p.TripId, p.Pnr, p.RefundAmount, p.LastError?.Replace("Yerel test adaptörü iade hatası.", "İade işlemi tamamlanamadı."), p.RefundStatusChangedAt.HasValue ? DateTime.SpecifyKind(p.RefundStatusChangedAt.Value, DateTimeKind.Utc) : null)).ToList();
        return result;
    }
    private static async Task<List<ReportDay>> Daily(IQueryable<ReportFact> facts, ReportScope s, CancellationToken ct)
    {
        var cohort = s.Basis == "departure";
        var sold = await facts.Where(f => f.Sale).GroupBy(f => (cohort ? f.Departure : f.IssuedAt!.Value).AddHours(3).Date).Select(g => new { Day = g.Key, Value = g.Count() }).ToDictionaryAsync(x => DateOnly.FromDateTime(x.Day), x => x.Value, ct);
        var cancelled = await facts.Where(f => f.Cancellation).GroupBy(f => (cohort ? f.Departure : f.CancelledAt!.Value).AddHours(3).Date).Select(g => new { Day = g.Key, Value = g.Count() }).ToDictionaryAsync(x => DateOnly.FromDateTime(x.Day), x => x.Value, ct);
        var collected = await facts.Where(f => f.Collected != 0).GroupBy(f => (cohort ? f.Departure : f.PaidAt!.Value).AddHours(3).Date).Select(g => new { Day = g.Key, Value = g.Sum(f => f.Collected) }).ToDictionaryAsync(x => DateOnly.FromDateTime(x.Day), x => x.Value, ct);
        var refunded = await facts.Where(f => f.Refunded != 0).GroupBy(f => (cohort ? f.Departure : f.RefundedAt!.Value).AddHours(3).Date).Select(g => new { Day = g.Key, Value = g.Sum(f => f.Refunded) }).ToDictionaryAsync(x => DateOnly.FromDateTime(x.Day), x => x.Value, ct);
        return Enumerable.Range(0, s.To.DayNumber - s.From.DayNumber + 1).Select(offset => s.From.AddDays(offset)).Select(day => new ReportDay
        { Date = day, Sold = sold.GetValueOrDefault(day), Cancelled = cancelled.GetValueOrDefault(day), Collected = collected.GetValueOrDefault(day), Refunded = refunded.GetValueOrDefault(day) }).ToList();
    }

    public async Task<byte[]> Csv(int companyId, CompanyReportQuery q, CancellationToken ct)
    {
        var scope = Scope(q); await ValidateOwnership(companyId, q, ct);
        var rows = TripRows(companyId, q, scope, Facts(companyId, q, scope));
        if (await rows.CountAsync(ct) > CsvLimit) throw new ReportQueryException($"CSV en fazla {CsvLimit} sefer içerebilir. Tarih veya sefer filtresini daraltınız; sonuçlar kesilmedi.", 422);
        var items = await Sort(rows, q).Take(CsvLimit + 1).ToListAsync(ct);
        if (items.Count > CsvLimit) throw new ReportQueryException($"CSV en fazla {CsvLimit} sefer içerebilir. Filtreyi daraltınız; sonuçlar kesilmedi.", 422);
        items.ForEach(Utc);
        const string label = "Firma işlemleri";
        var csv = new StringBuilder();
        void Line(params string[] cells) => csv.AppendLine(string.Join(";", cells.Select(Escape)));
        Line("Travela sefer raporu", label); Line("Tarih kapsamı", q.Basis == "transaction" ? "İşlem tarihi" : "Sefer kalkış tarihi", scope.From.ToString("yyyy-MM-dd"), scope.To.ToString("yyyy-MM-dd"));
        Line("Açıklama", scope.DateExplanation); Line("Doluluk", "Seçili seferlerin güncel aktif satılmış koltuk / kapasite oranı; geçici tutma ve ücretsiz kayıtlar doluluk değildir.");
        Line("Üretim zamanı (Türkiye)", new DateTimeOffset(scope.GeneratedAt).ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM.yyyy HH:mm:ss"));
        var unknown = await Orders(companyId, q, scope).Where(p => p.PaymentStatus == "Paid" && (p.RefundStatus == "Pending" || p.RefundStatus == "Failed") && p.RefundStatusChangedAt == null)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Amount = g.Sum(p => p.RefundAmount) }).SingleOrDefaultAsync(ct);
        if (unknown != null) Line("Eski iade tarihi uyarısı", $"{unknown.Count} iadenin ({unknown.Amount.ToString("F2", CultureInfo.GetCultureInfo("tr-TR"))} TRY) durum tarihi bilinmiyor; işlem tarihi toplamlarına dahil değildir, sefer tarihi kapsamına dahildir.");
        Line("Ortam", "Sefer", "Güzergâh", "Kalkış (Türkiye)", "Plaka", "Kapasite", "Aktif satılmış koltuk", "Geçici ödeme tutması", "Eski ücretsiz blokaj", "Satışa açık", "Doluluk (%)", "Satılan bilet", "Dönem aktif bilet", "İptal bilet", "Tahsilat (TRY)", "Tamamlanan iade (TRY)", "Bekleyen iade (TRY)", "Başarısız iade sayısı", "Başarısız iade (TRY)", "Tahsilat − tamamlanan iade (TRY)", "Sefer durumu", "Veri açıklaması");
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        foreach (var t in items)
        {
            Line(label, t.Id.ToString(), SafeText(t.DepartureCity + " → " + t.ArrivalCity), new DateTimeOffset(t.DepartureTime).ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM.yyyy HH:mm", tr), SafeText(t.Plate),
                t.Capacity.ToString(), t.ActiveSoldSeats.ToString(), t.HeldSeats.ToString(), t.LegacyBlockedSeats.ToString(), t.AvailableForSale?.ToString() ?? "Hesaplanamadı", t.OccupancyPercent?.ToString("F2", tr) ?? "Hesaplanamadı",
                t.Sold.ToString(), t.Active.ToString(), t.Cancelled.ToString(), t.Collected.ToString("F2", tr), t.Refunded.ToString("F2", tr), t.PendingRefund.ToString("F2", tr), t.FailedRefundCount.ToString(), t.FailedRefundAmount.ToString("F2", tr), t.CollectedMinusRefunded.ToString("F2", tr), SafeText(t.Status), t.DataWarning ?? "");
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }
    public static string SafeText(string text) => text.Length > 0 && ("=+-@".Contains(text.TrimStart().FirstOrDefault()) || text[0] is '\t' or '\r' or '\n') ? "'" + text : text;
    private static string Escape(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
}

internal class ReportFact
{
    public Guid OrderId { get; set; }
    public int TripId { get; set; }
    public int RouteId { get; set; }
    public string DepartureCity { get; set; } = "";
    public string ArrivalCity { get; set; } = "";
    public DateTime Departure { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? RefundedAt { get; set; }
    public string? Pnr { get; set; }
    public string Status { get; set; } = "";
    public string RefundStatus { get; set; } = "";
    public decimal Amount { get; set; }
    public bool Sale { get; set; }
    public bool Cancellation { get; set; }
    public decimal Collected { get; set; }
    public decimal Refunded { get; set; }
    public decimal Pending { get; set; }
    public bool Failed { get; set; }
    public decimal RefundAmount { get; set; }
    public bool CreatedInRange { get; set; }
}
internal class TripMetric : ReportMetrics { public int Id { get; set; } }
internal class TripInventory
{
    public int Id { get; set; } public int RouteId { get; set; }
    public string DepartureCity { get; set; } = ""; public string ArrivalCity { get; set; } = "";
    public DateTime Departure { get; set; } public string Plate { get; set; } = ""; public string Status { get; set; } = "";
    public int Capacity { get; set; } public int StoredAvailable { get; set; } public int ActiveSold { get; set; }
    public int Held { get; set; } public int Legacy { get; set; } public int OtherEnvironment { get; set; }
    public int Expired { get; set; } public int AllActive { get; set; } public bool Invalid { get; set; } public bool Open { get; set; }
}
