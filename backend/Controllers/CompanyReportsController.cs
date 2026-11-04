using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Reporting;
using Travela.Data;
using Travela.DTOs;
using Travela.Api.Security;

namespace Travela.Api.Controllers;

[ApiController, Route("api/company/reports"), Authorize(Roles = "Company")]
public class CompanyReportsController(TravelaDbContext db, CompanyReportingService reporting) : ControllerBase
{
    private async Task<int?> Company(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await db.Users.AsNoTracking().ForSession(User, db).Where(u => u.Id == id && u.Role == "Company")
            .Select(u => u.CompanyId).SingleOrDefaultAsync(ct);
    }
    [HttpGet]
    [HttpGet("overview")]
    public async Task<IActionResult> Get([FromQuery] CompanyReportQuery q, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var companyId = await Company(ct); if (companyId == null) return Forbid();
        try { return Ok(await reporting.Get(companyId.Value, q, ct)); }
        catch (ReportQueryException e) { return StatusCode(e.Status, new { message = e.Message }); }
    }
    [HttpGet("csv")]
    public async Task<IActionResult> Csv([FromQuery] CompanyReportQuery q, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var companyId = await Company(ct); if (companyId == null) return Forbid();
        try
        {
            var scope = reporting.Scope(q);
            return File(await reporting.Csv(companyId.Value, q, ct), "text/csv; charset=utf-8", $"Travela-sefer-raporu-{scope.From:yyyyMMdd}-{scope.To:yyyyMMdd}.csv");
        }
        catch (ReportQueryException e) { return StatusCode(e.Status, new { message = e.Message }); }
    }
    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var companyId = await Company(ct); if (companyId == null) return Forbid();
        return Ok(await db.Trips.AsNoTracking().Where(t => t.CompanyId == companyId).Select(t => new { id = t.RouteId, t.Route.DepartureCity, t.Route.ArrivalCity })
            .Distinct().OrderBy(t => t.DepartureCity).ThenBy(t => t.ArrivalCity).Take(500).ToListAsync(ct));
    }
}
