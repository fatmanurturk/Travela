using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Data;
using Travela.DTOs;

namespace Travela.Api.Controllers;

[ApiController, Route("api/terminals")]
public sealed class TerminalsController(TravelaDbContext db) : ControllerBase
{
    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> List([FromQuery] string? city, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        if (includeInactive && !User.IsInRole("Admin")) return Forbid();
        var query = db.Terminals.AsNoTracking().Where(t => includeInactive || t.IsActive);
        if (!string.IsNullOrWhiteSpace(city)) query = query.Where(t => t.City == city.Trim());
        return Ok(await query.OrderBy(t => t.City).ThenBy(t => t.Name).ToListAsync(ct));
    }
    [HttpPost, Authorize(Roles = "Admin")]
    public Task<IActionResult> Create(TerminalDto dto, CancellationToken ct) => Save(null, dto, ct);
    [HttpPut("{id:int}"), Authorize(Roles = "Admin")]
    public Task<IActionResult> Update(int id, TerminalDto dto, CancellationToken ct) => Save(id, dto, ct);

    private async Task<IActionResult> Save(int? id, TerminalDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.City) || string.IsNullOrWhiteSpace(dto.Address))
            return BadRequest(new { message = "Terminal adı, şehir ve açık adres zorunludur." });
        var terminal = id.HasValue ? await db.Terminals.SingleOrDefaultAsync(t => t.Id == id, ct) : new Terminal();
        if (terminal == null) return NotFound(new { message = "Terminal bulunamadı." });
        if (id.HasValue && terminal.City != dto.City.Trim() && await db.Trips.AnyAsync(t => t.DepartureTerminalId == id || t.ArrivalTerminalId == id, ct))
            return Conflict(new { message = "Seferlerde kullanılmış terminalin şehri değiştirilemez." });
        terminal.Name = dto.Name.Trim(); terminal.City = dto.City.Trim(); terminal.Address = dto.Address.Trim(); terminal.IsActive = dto.IsActive;
        if (!id.HasValue) db.Terminals.Add(terminal);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 })
        { return Conflict(new { message = "Bu şehirde aynı isimli terminal zaten var." }); }
        return StatusCode(id.HasValue ? 200 : 201, new { message = "Terminal kaydedildi.", terminal });
    }
}
