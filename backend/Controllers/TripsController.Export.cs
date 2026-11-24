using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Trips;

namespace Travela.Api.Controllers;

public partial class TripsController
{
    // Match list authorization. ManagementQuery enforces the current database role and tenant.
    [HttpGet("export"), Authorize(Policy = "trip.view")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ExportTrips([FromQuery] TripManagementFilters filters, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(); if (user == null) return Unauthorized();
        var now = _clock.GetUtcNow(); var prepared = ManagementQuery(user, filters, now);
        if (prepared.Error != null) return prepared.Error;
        // Report the effective tenant scope even when the company client omits companyId.
        if (user.Role == "Company") filters.CompanyId = user.CompanyId;
        // DeleteOnClose cleans the private temporary file on errors, cancellation and completed responses.
        var stream = new FileStream(Path.Combine(Path.GetTempPath(), $"travela-trips-{Guid.NewGuid():N}.xlsx"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        try {
            var rows = TripManagementQuery.Rows(TripManagementQuery.Order(prepared.Query!, filters));
            await TripWorkbook.Write(stream, rows.AsAsyncEnumerable(), filters, now, ct);
            stream.Position = 0;
            Response.Headers.CacheControl = "private, no-store";
            return File(stream, TripWorkbook.ContentType, TripWorkbook.FileName(now));
        } catch (ArgumentException error) { await stream.DisposeAsync(); return BadRequest(new { message = error.Message }); }
        catch { await stream.DisposeAsync(); throw; }
    }
}
