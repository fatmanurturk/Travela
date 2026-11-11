using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Routes;
using Travela.Api.Security;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;
using RouteModel = Travela.Api.Models.Route;

namespace Travela.Api.Controllers;

[ApiController]
[Route("api/routes")]
[Authorize(Policy = "trip.view", Roles = "Admin,Company")]
public class RoutesController(TravelaDbContext context) : ControllerBase
{
    [HttpGet("public")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicRoutes() => Ok(await context.Routes.AsNoTracking()
        .Where(r => r.IsActive).OrderBy(r => r.DepartureCity).ThenBy(r => r.ArrivalCity)
        .Select(r => new RouteDto { Id = r.Id, DepartureCity = r.DepartureCity, ArrivalCity = r.ArrivalCity,
            DistanceKm = r.DistanceKm, EstimatedDurationMinutes = r.EstimatedDurationMinutes }).ToListAsync());

    [HttpGet("cities")]
    public async Task<IActionResult> GetCities() => await GetCurrentUserAsync() is null
        ? Forbidden() : Ok(TurkishCities.All);

    [HttpGet("{id:int}/geometry")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetGeometry(int id)
    {
        if (await GetCurrentUserAsync() is not { Role: "Admin" }) return Forbidden();
        var route = await context.Routes.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id);
        return route == null ? NotFound(new { message = "Güzergâh bulunamadı." }) : Ok(new { route.GeometryJson });
    }

    [HttpPut("{id:int}/geometry")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PutGeometry(int id, RouteGeometryDto dto)
    {
        if (await GetCurrentUserAsync() is not { Role: "Admin" }) return Forbidden();
        if (!dto.Confirmed || !Travela.Api.Trips.SunExposure.ValidGeometry(dto.Points) ||
            !Uri.TryCreate(dto.Source, UriKind.Absolute, out var source) || source.Scheme != "https")
            return BadRequest(new { message = "Doğrulanmış yol geometrisi, HTTPS kaynak bağlantısı ve doğrulama onayı gereklidir. En az 3 nokta olmalı; ardışık noktalar en fazla 20 km aralıklı olmalıdır." });
        var route = await context.Routes.SingleOrDefaultAsync(r => r.Id == id);
        if (route == null) return NotFound(new { message = "Güzergâh bulunamadı." });
        route.GeometryJson = System.Text.Json.JsonSerializer.Serialize(new Travela.Api.Trips.VerifiedRouteGeometry(dto.Points!, dto.Source, DateTime.UtcNow), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        await context.SaveChangesAsync();
        return Ok(new { message = "Doğrulanmış yol geometrisi kaydedildi. Güneş yönü bu güzergâhın sefer saatlerine göre hesaplanacak." });
    }

    // Preserve the active-only array contract used by trip creation and other existing screens.
    [HttpGet]
    public async Task<IActionResult> GetRoutes([FromQuery] bool includeInactive = false)
    {
        var user = await GetCurrentUserAsync();
        if (user is null) return Forbidden();
        return Ok(await Summaries(user).Where(r => includeInactive || r.IsActive)
            .OrderBy(r => r.DepartureCity).ThenBy(r => r.ArrivalCity).ToListAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetRoute(int id, [FromQuery] int pageNumber = 1)
    {
        var user = await GetCurrentUserAsync();
        if (user is null) return Forbidden();
        var route = await Summaries(user).SingleOrDefaultAsync(r => r.Id == id);
        if (route is null) return NotFound(new { code = "route_not_found", message = "Güzergâh bulunamadı." });
        const int pageSize = 10;
        var totalPages = Math.Max(1, (int)Math.Ceiling(route.TripCount / (double)pageSize));
        pageNumber = Math.Clamp(pageNumber, 1, totalPages);
        var trips = await context.Trips.AsNoTracking()
            .Where(t => t.RouteId == id && (user.Role == "Admin" || t.CompanyId == user.CompanyId))
            .OrderByDescending(t => t.DepartureTime).ThenByDescending(t => t.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(t => new RouteTripDto { Id = t.Id, DepartureTime = t.DepartureTime, ArrivalTime = t.ArrivalTime,
                BusPlateNumber = t.Bus.PlateNumber, Price = t.Price, Status = t.Status }).ToListAsync();
        foreach (var trip in trips)
        {
            trip.DepartureTime = DateTime.SpecifyKind(trip.DepartureTime, DateTimeKind.Utc);
            trip.ArrivalTime = DateTime.SpecifyKind(trip.ArrivalTime, DateTimeKind.Utc);
        }
        return Ok(new RouteDetailDto { Route = route, Trips = trips, PageNumber = pageNumber, TotalPages = totalPages });
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> CreateRoute(CreateRouteDto dto) => SaveRoute(null, dto);

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> UpdateRoute(int id, CreateRouteDto dto) => SaveRoute(id, dto);

    private async Task<IActionResult> SaveRoute(int? id, CreateRouteDto dto)
    {
        if (await GetCurrentUserAsync() is not { Role: "Admin" }) return Forbidden();
        if (dto.DistanceKm is <= 0 or > 100000 || dto.EstimatedDurationMinutes is <= 0 or > 100000)
            return BadRequest(new { message = "Mesafe ve tahmini süre girildiğinde sıfırdan büyük ve en fazla 100000 olmalıdır." });

        // Serializable protects duplicate checks, trip bindings and updates together without a schema change.
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var route = id.HasValue ? await context.Routes.SingleOrDefaultAsync(r => r.Id == id) : new RouteModel();
            if (route is null) return NotFound(new { message = "Güzergâh bulunamadı." });
            // Keep unchanged legacy spellings intact so linked historical routes remain editable.
            var sameCities = id.HasValue && dto.DepartureCity.Trim() == route.DepartureCity.Trim() &&
                dto.ArrivalCity.Trim() == route.ArrivalCity.Trim();
            var departure = sameCities ? route.DepartureCity : TurkishCities.Normalize(dto.DepartureCity);
            var arrival = sameCities ? route.ArrivalCity : TurkishCities.Normalize(dto.ArrivalCity);
            if (departure is null || arrival is null)
                return BadRequest(new { message = "Kalkış ve varış için Türkiye'deki illerden birini seçin." });
            if (string.Equals(departure, arrival, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Kalkış ve varış şehirleri aynı olamaz." });
            if (id.HasValue && !sameCities &&
                await context.Trips.AnyAsync(t => t.RouteId == id))
                return Conflict(new { message = "Bu güzergâha bağlı seferler bulunduğu için kalkış ve varış şehirleri değiştirilemez. Yeni bir güzergâh oluşturun." });
            if (dto.IsActive && await DuplicateActive(route.Id, departure, arrival)) return Duplicate();
            if (route.IsActive && !dto.IsActive && id.HasValue && !dto.ConfirmDeactivation)
            {
                var warning = await DeactivationWarning(route.Id);
                if (warning is not null) return warning;
            }
            route.DepartureCity = departure; route.ArrivalCity = arrival;
            route.DistanceKm = dto.DistanceKm; route.EstimatedDurationMinutes = dto.EstimatedDurationMinutes;
            route.IsActive = dto.IsActive;
            if (!id.HasValue) context.Routes.Add(route);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            var result = new RouteDto { Id = route.Id, DepartureCity = departure, ArrivalCity = arrival,
                DistanceKm = route.DistanceKm, EstimatedDurationMinutes = route.EstimatedDurationMinutes };
            return id.HasValue ? Ok(new { message = "Güzergâh güncellendi.", route = result })
                : CreatedAtAction(nameof(GetRoute), new { id = route.Id }, new { message = "Güzergâh oluşturuldu.", route = result });
        }
        catch (Exception ex) when (IsConflict(ex)) { return ConcurrentConflict(); }
    }

    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateStatus(int id, RouteStatusDto dto)
    {
        if (await GetCurrentUserAsync() is not { Role: "Admin" }) return Forbidden();
        if (!dto.IsActive.HasValue) return BadRequest(new { message = "Güzergâh durumu zorunludur." });
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var route = await context.Routes.SingleOrDefaultAsync(r => r.Id == id);
            if (route is null) return NotFound(new { message = "Güzergâh bulunamadı." });
            if (dto.IsActive.Value && await DuplicateActive(id, route.DepartureCity, route.ArrivalCity)) return Duplicate();
            if (route.IsActive && !dto.IsActive.Value && !dto.ConfirmDeactivation)
            {
                var warning = await DeactivationWarning(id);
                if (warning is not null) return warning;
            }
            route.IsActive = dto.IsActive.Value;
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return Ok(new { message = route.IsActive ? "Güzergâh aktif hale getirildi." : "Güzergâh pasife alındı." });
        }
        catch (Exception ex) when (IsConflict(ex)) { return ConcurrentConflict(); }
    }

    private IQueryable<RouteManagementDto> Summaries(User user)
    {
        var now = DateTime.UtcNow;
        return context.Routes.AsNoTracking().Select(r => new RouteManagementDto
        {
            Id = r.Id, DepartureCity = r.DepartureCity, ArrivalCity = r.ArrivalCity,
            DistanceKm = r.DistanceKm, EstimatedDurationMinutes = r.EstimatedDurationMinutes, IsActive = r.IsActive,
            HasTrips = context.Trips.Any(t => t.RouteId == r.Id),
            TripCount = context.Trips.Count(t => t.RouteId == r.Id && (user.Role == "Admin" || t.CompanyId == user.CompanyId)),
            PlannedTripCount = context.Trips.Count(t => t.RouteId == r.Id && t.DepartureTime > now &&
                (t.Status == "Scheduled" || t.Status == "Planlandı" || t.Status == "Planlandi") &&
                (user.Role == "Admin" || t.CompanyId == user.CompanyId))
        });
    }

    private Task<bool> DuplicateActive(int id, string departure, string arrival) => context.Routes.AnyAsync(r =>
        r.Id != id && r.IsActive &&
        EF.Functions.Collate(r.DepartureCity.Trim(), "Turkish_100_CI_AS") == departure &&
        EF.Functions.Collate(r.ArrivalCity.Trim(), "Turkish_100_CI_AS") == arrival);

    private async Task<IActionResult?> DeactivationWarning(int id)
    {
        var now = DateTime.UtcNow;
        var count = await context.Trips.CountAsync(t => t.RouteId == id && t.DepartureTime > now &&
            (t.Status == "Scheduled" || t.Status == "Planlandı" || t.Status == "Planlandi"));
        return count == 0 ? null : Conflict(new { code = "route_deactivation_confirmation_required", plannedTripCount = count,
            message = $"Bu güzergâhta {count} planlanmış sefer var. Pasife alma bu seferlerin yeni bilet satışını da kapatır. Seferler, mevcut biletler ve geçmiş kayıtları korunur. Devam etmek için işlemi onaylayın." });
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await context.Users.AsNoTracking().ForSession(User, context).FirstOrDefaultAsync(u => u.Id == id &&
            (u.Role == "Admin" || (u.Role == "Company" && u.CompanyId != null)));
    }

    private ObjectResult Forbidden() => StatusCode(403, new { message = "Bu işlem için yetkiniz veya geçerli bir firma hesabınız bulunmuyor." });
    private ConflictObjectResult Duplicate() => Conflict(new { message = "Aynı kalkış ve varış şehirleri için aktif bir güzergâh zaten var. Mevcut güzergâhı kullanın." });
    private ConflictObjectResult ConcurrentConflict() => Conflict(new { message = "Eşzamanlı bir güzergâh işlemi nedeniyle kayıt tamamlanamadı. Lütfen tekrar deneyin." });
    private static bool IsConflict(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 or 2601 or 2627 }) return true;
        return false;
    }
}
