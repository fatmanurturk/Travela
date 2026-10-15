using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travela.Data;
using Travela.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Travela.DTOs;

namespace Travela.Api.Controllers
{
    [ApiController]
    [Route("api/buses")]
    [Authorize(Roles = "Company,Admin")]
    public class BusController : ControllerBase
    {
        private readonly TravelaDbContext _context;

        public BusController(TravelaDbContext context)
        {
            _context = context;
        }

        [HttpGet]
public async Task<IActionResult> GetBuses()
{
    var buses = await _context.Buses
        .AsNoTracking()
        .Select(b => new
        {
            b.Id,
            b.CompanyId,
            b.PlateNumber,
            b.Brand,
            b.Model,
            b.SeatCapacity,
            b.IsActive
        })
        .ToListAsync();

    return Ok(buses);
}

[HttpPost]
public async Task<IActionResult> CreateBus(
    [FromBody] CreateBusDto bus)
{

    var userIdClaim = User.FindFirstValue(
    ClaimTypes.NameIdentifier
);

if (!int.TryParse(userIdClaim, out int userId))
{
    return Unauthorized("Kullanıcı kimliği doğrulanamadı.");
}

var currentUser = await _context.Users
    .AsNoTracking()
    .FirstOrDefaultAsync(u => u.Id == userId);

if (currentUser == null)
{
    return Unauthorized("Kullanıcı bulunamadı.");
}

if (currentUser.Role == "Company")
{
    if (currentUser.CompanyId == null)
    {
        return Forbid();
    }

    if (currentUser.CompanyId != bus.CompanyId)
    {
        return Forbid();
    }
}
else if (currentUser.Role != "Admin")
{
    return Forbid();
}

    if (bus.SeatCapacity <= 0)
    {
        return BadRequest(
            "Koltuk kapasitesi 0'dan büyük olmalıdır.");
    }

    var companyExists = await _context.Companies
        .AnyAsync(c => c.Id == bus.CompanyId);

    if (!companyExists)
    {
        return BadRequest("Firma bulunamadı.");
    }

    var newBus = new Bus
{
    CompanyId = bus.CompanyId,
    PlateNumber = bus.PlateNumber,
    Brand = bus.Brand,
    Model = bus.Model,
    SeatCapacity = bus.SeatCapacity,
    IsActive = bus.IsActive
};

_context.Buses.Add(newBus);

await _context.SaveChangesAsync();

return Ok(new
{
    message = "Otobüs başarıyla eklendi.",
    busId = newBus.Id
});
}
    }
}