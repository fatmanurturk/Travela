using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.DTOs;

namespace Travela.Api.Controllers;

public sealed class CreateTripBatchDto
{
    [Required, MinLength(1), MaxLength(100)]
    public List<CreateTripDto?> Trips { get; set; } = [];
}

public partial class TripsController
{
    [HttpPost("batch")]
    [Authorize(Policy = "trip.create")]
    public async Task<IActionResult> CreateBatch([FromBody] CreateTripBatchDto request, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();
        if (request.Trips == null || request.Trips.Count is < 1 or > 100 || request.Trips.Any(t => t == null))
            return BadRequest(new { message = "Tek işlemde 1 ile 100 arasında sefer oluşturulabilir." });

        var now = DateTime.UtcNow;
        var planned = new List<Trip>();
        // One transaction covers validation and insertion, including concurrent single-trip creation.
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            foreach (var (item, index) in request.Trips.Select((t, i) => (t!, i)))
            {
                var companyId = await ResolveCompanyIdAsync(user, item.CompanyId);
                if (companyId == null) return user.Role == "Admin"
                    ? BadRequest(new { message = "Her sefer için firma seçimi zorunludur." }) : Forbid();
                var departure = AsUtc(item.DepartureTime); var arrival = AsUtc(item.ArrivalTime);
                if (departure <= now || arrival <= departure || item.Price is <= 0 or > 1000000)
                    return BadRequest(new { message = $"{index + 1}. seferin tarih veya fiyat bilgisi geçersiz. Kalkış gelecekte, varış kalkıştan sonra olmalıdır." });
                var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == companyId.Value, ct);
                var bus = await _context.Buses.FirstOrDefaultAsync(b => b.Id == item.BusId, ct);
                var route = await _context.Routes.FirstOrDefaultAsync(r => r.Id == item.RouteId, ct);
                var error = ValidateTripResources(company, bus, route, companyId.Value);
                if (error != null) return error;
                if (bus!.SeatCapacity <= 0) return BadRequest(new { message = "Otobüsün koltuk kapasitesi sıfırdan büyük olmalıdır." });
                if (planned.Any(t => t.BusId == item.BusId && t.DepartureTime < arrival && t.ArrivalTime > departure) ||
                    await HasBusOverlapAsync(item.BusId, departure, arrival, null))
                    return Conflict(new { message = $"{index + 1}. sefer ({departure.AddHours(3):dd.MM.yyyy HH:mm}, {bus.PlateNumber}) başka bir seferle çakışıyor. Hiçbir sefer oluşturulmadı." });
                planned.Add(new Trip
                {
                    CompanyId = companyId.Value, BusId = bus.Id, RouteId = route!.Id,
                    DepartureTime = departure, ArrivalTime = arrival, Price = decimal.Round(item.Price, 2, MidpointRounding.ToEven),
                    AvailableSeats = bus.SeatCapacity, Status = "Scheduled", CreatedAt = now
                });
            }
            _context.Trips.AddRange(planned);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Conflict(new { message = "Eşzamanlı sefer işlemi nedeniyle kayıt tamamlanamadı. Hiçbir sefer oluşturulmadı; listeyi yenileyip tekrar deneyin." });
        }
        return StatusCode(StatusCodes.Status201Created, new
        {
            message = $"{planned.Count} sefer oluşturuldu.", count = planned.Count, tripIds = planned.Select(t => t.Id)
        });
    }
}
