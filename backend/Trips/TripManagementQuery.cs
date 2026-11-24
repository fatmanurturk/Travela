using Travela.DTOs;
using Travela.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Travela.Api.Trips;

public class TripManagementFilters
{
    public string? Search { get; set; }
    public int? CompanyId { get; set; }
    public string? DepartureCity { get; set; }
    public string? ArrivalCity { get; set; }
    public DateTime? DepartureFrom { get; set; }
    public DateTime? DepartureTo { get; set; }
    public string? Status { get; set; }
    public string? BusPlate { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string CreatedPeriod { get; set; } = "all";
    public DateOnly? CreatedFrom { get; set; }
    public DateOnly? CreatedTo { get; set; }
    public string SortBy { get; set; } = "date";
    public string SortDirection { get; set; } = "asc";
}

public record TripCreationWindow(DateTime? StartUtc, DateTime? EndUtc, string Label);

public static class TripManagementQuery
{
    public static readonly TimeZoneInfo Turkey = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    public static DateTime LocalTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Turkey);
    public static TripCreationWindow CreationWindow(TripManagementFilters filters, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Turkey).DateTime);
        DateOnly? from = null, until = null; string label;
        switch (filters.CreatedPeriod.ToLowerInvariant()) {
            case "all": return new(null, null, "Tüm tarihler");
            case "today": from = today; until = today.AddDays(1); label = "Bugün"; break;
            case "yesterday": from = today.AddDays(-1); until = today; label = "Dün"; break;
            case "last7days": from = today.AddDays(-6); until = today.AddDays(1); label = "Son 7 gün"; break;
            case "thismonth": from = new DateOnly(today.Year, today.Month, 1); until = from.Value.AddMonths(1); label = "Bu ay"; break;
            case "custom":
                if (!filters.CreatedFrom.HasValue || !filters.CreatedTo.HasValue)
                    throw new ArgumentException("Özel oluşturma tarihi aralığı için başlangıç ve bitiş tarihlerini girin.");
                if (filters.CreatedFrom > filters.CreatedTo || filters.CreatedTo == DateOnly.MaxValue)
                    throw new ArgumentException("Oluşturma tarihi aralığı geçersiz. Başlangıç bitişten sonra olamaz; bitiş 31.12.9999'dan önce olmalıdır.");
                from = filters.CreatedFrom; until = filters.CreatedTo.Value.AddDays(1); label = "Özel tarih aralığı"; break;
            default: throw new ArgumentException("Geçerli bir oluşturma tarihi seçeneği seçin.");
        }
        DateTime Utc(DateOnly date) => TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), Turkey);
        return new(Utc(from!.Value), Utc(until!.Value), $"{label}: {from:dd.MM.yyyy} – {until.Value.AddDays(-1):dd.MM.yyyy} (günler dahil)");
    }

    public static IQueryable<Trip> Filter(IQueryable<Trip> query, TripManagementFilters f, TripCreationWindow window)
    {
        if (f.MinPrice < 0 || f.MaxPrice < 0 || f.MinPrice > f.MaxPrice) throw new ArgumentException("Bilet fiyatı aralığı geçersiz.");
        if (f.DepartureFrom?.Date > f.DepartureTo?.Date || f.DepartureTo?.Date == DateTime.MaxValue.Date) throw new ArgumentException("Kalkış tarihi aralığı geçersiz.");
        if (!string.IsNullOrWhiteSpace(f.Search)) {
            var term = f.Search.Trim(); var numeric = int.TryParse(term, out var id);
            query = query.Where(t => (numeric && t.Id == id) || t.Company.Name.Contains(term) || t.Bus.PlateNumber.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(f.DepartureCity)) { var city = f.DepartureCity.Trim(); query = query.Where(t => t.Route.DepartureCity.Contains(city)); }
        if (!string.IsNullOrWhiteSpace(f.ArrivalCity)) { var city = f.ArrivalCity.Trim(); query = query.Where(t => t.Route.ArrivalCity.Contains(city)); }
        if (f.DepartureFrom.HasValue) { var start = Utc(f.DepartureFrom.Value).Date; query = query.Where(t => t.DepartureTime >= start); }
        if (f.DepartureTo.HasValue) { var end = Utc(f.DepartureTo.Value).Date.AddDays(1); query = query.Where(t => t.DepartureTime < end); }
        if (!string.IsNullOrWhiteSpace(f.Status)) {
            var status = NormalizeStatus(f.Status) ?? throw new ArgumentException("Geçersiz sefer durumu.");
            query = query.Where(t => t.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(f.BusPlate)) { var plate = f.BusPlate.Trim(); query = query.Where(t => t.Bus.PlateNumber.Contains(plate)); }
        if (f.MinPrice.HasValue) query = query.Where(t => t.Price >= f.MinPrice);
        if (f.MaxPrice.HasValue) query = query.Where(t => t.Price <= f.MaxPrice);
        if (window.StartUtc.HasValue) query = query.Where(t => t.CreatedAt >= window.StartUtc && t.CreatedAt < window.EndUtc);
        return query;
    }
    public static IOrderedQueryable<Trip> Order(IQueryable<Trip> query, TripManagementFilters f)
    {
        var desc = string.Equals(f.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return f.SortBy.ToLowerInvariant() switch {
            "createdat" => desc ? query.OrderBy(t => t.CreatedAt == null).ThenByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
                : query.OrderBy(t => t.CreatedAt == null).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id),
            "price" => desc ? query.OrderByDescending(t => t.Price).ThenBy(t => t.Id) : query.OrderBy(t => t.Price).ThenBy(t => t.Id),
            _ => desc ? query.OrderByDescending(t => t.DepartureTime).ThenBy(t => t.Id) : query.OrderBy(t => t.DepartureTime).ThenBy(t => t.Id)
        };
    }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    public static string SortLabel(TripManagementFilters f) => $"{(f.SortBy.ToLowerInvariant() switch { "createdat" => "Oluşturulma", "price" => "Fiyat", _ => "Kalkış" })}: {(string.Equals(f.SortDirection, "desc", StringComparison.OrdinalIgnoreCase) ? "Azalan" : "Artan")}";
    public static string? NormalizeStatus(string? status) => status?.Trim().ToLowerInvariant() switch {
        "scheduled" or "planlandı" or "planlandi" => "Scheduled", "inprogress" or "in progress" or "devam ediyor" => "InProgress",
        "completed" or "tamamlandı" or "tamamlandi" => "Completed", "cancelled" or "canceled" or "iptal" or "iptal edildi" => "Cancelled", _ => null
    };
    // One projection keeps the list and workbook's resource/capacity definitions identical.
    public static IQueryable<TripListDto> Rows(IQueryable<Trip> query) => query.Select(t => new TripListDto {
        Id = t.Id, CreatedAt = t.CreatedAt, ChangeVersion = t.ChangeVersion, CompanyId = t.CompanyId, CompanyName = t.Company.Name,
        BusId = t.BusId, BusPlateNumber = t.Bus.PlateNumber, BusBrand = t.Bus.Brand, BusModel = t.Bus.Model,
        BusCapacity = t.SeatCapacitySnapshot ?? t.Bus.SeatCapacity, SellableSeatCount = t.SellableSeatCount ?? t.Bus.SeatCapacity, SeatLayout = t.SeatLayout, SeatPlanJson = t.SeatPlanJson,
        RouteId = t.RouteId, DepartureCity = t.Route.DepartureCity, ArrivalCity = t.Route.ArrivalCity,
        DepartureTerminalId = t.DepartureTerminalId, ArrivalTerminalId = t.ArrivalTerminalId, DepartureTerminalName = t.DepartureTerminalName, DepartureTerminalAddress = t.DepartureTerminalAddress,
        ArrivalTerminalName = t.ArrivalTerminalName, ArrivalTerminalAddress = t.ArrivalTerminalAddress, BoardingInstructions = t.BoardingInstructions,
        DepartureTime = t.DepartureTime, ArrivalTime = t.ArrivalTime, Price = t.Price, AvailableSeats = t.AvailableSeats, Status = t.Status
    });
}
