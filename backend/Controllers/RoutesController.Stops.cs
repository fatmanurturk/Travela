using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Routes;
using Travela.DTOs;

namespace Travela.Api.Controllers;

public partial class RoutesController
{
    [HttpGet("{id:int}/stops")]
    public async Task<IActionResult> Stops(int id, CancellationToken ct)
    {
        if (await GetCurrentUserAsync() == null) return Forbidden();
        if (!await context.Routes.AnyAsync(r => r.Id == id, ct)) return NotFound();
        return Ok(await context.RouteStops.AsNoTracking().Where(s => s.RouteId == id).OrderBy(s => s.StopOrder).ToListAsync(ct));
    }

    [HttpPut("{id:int}/stops"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> SaveStops(int id, SaveRouteStopsDto dto, CancellationToken ct)
    {
        if (await GetCurrentUserAsync() is not { Role: "Admin" }) return Forbidden();
        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var route = await context.Routes.FromSqlInterpolated($"SELECT * FROM Routes WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync(ct);
        if (route == null) return NotFound();
        var ids = dto.Stops.Select(s => s.TerminalId).ToList();
        var terminals = await context.Terminals.Where(t => ids.Contains(t.Id) && t.IsActive).ToDictionaryAsync(t => t.Id, ct);
        if (dto.Stops.Count < 2 || ids.Distinct().Count() != ids.Count || dto.Stops.Any(s =>
            !terminals.TryGetValue(s.TerminalId, out var terminal) || TurkishCities.Id(terminal.City) != s.CityId || s.ArrivalOffsetMinutes > s.DepartureOffsetMinutes) ||
            dto.Stops[0].DepartureOffsetMinutes != 0 || dto.Stops[0].CityId != TurkishCities.Id(route.DepartureCity) || dto.Stops[^1].CityId != TurkishCities.Id(route.ArrivalCity) ||
            dto.Stops.Skip(1).Select((s,i) => dto.Stops[i].DepartureOffsetMinutes >= s.ArrivalOffsetMinutes).Any(b => b))
            return BadRequest(new { message = "Durak şehirleri, aktif terminaller ve başlangıçtan itibaren dakika değerleri ileri sırada olmalıdır. İlk kalkış 0. dakikadır." });
        await context.RouteStops.Where(s => s.RouteId == id).ExecuteDeleteAsync(ct);
        context.RouteStops.AddRange(dto.Stops.Select((s,i) => new RouteStop { RouteId = id, StopOrder = i + 1, CityId = s.CityId, City = TurkishCities.Name(s.CityId)!, TerminalId = s.TerminalId,
            ArrivalOffsetMinutes = s.ArrivalOffsetMinutes, DepartureOffsetMinutes = s.DepartureOffsetMinutes, CanBoard = s.CanBoard, CanAlight = s.CanAlight, DistanceFromStartKm = s.DistanceFromStartKm }));
        await context.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Ok(new { message = "Güzergâh durak şablonu kaydedildi. Mevcut seferler değiştirilmedi." });
    }
}
