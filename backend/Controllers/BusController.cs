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
    [Authorize]
    public class BusController : ControllerBase
    {
        private readonly TravelaDbContext _context;

        public BusController(TravelaDbContext context)
        {
            _context = context;
        }

     
[HttpGet]
[Authorize(Policy = "bus.view")]
public async Task<IActionResult> GetBuses()
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

    var query = _context.Buses
        .AsNoTracking()
        .AsQueryable();

    if (currentUser.Role == "Company")
    {
        if (currentUser.CompanyId == null)
        {
            return Forbid();
        }

        query = query.Where(
            b => b.CompanyId == currentUser.CompanyId
        );
    }
    else if (currentUser.Role != "Admin")
    {
        return Forbid();
    }

    var buses = await query
        .Join(
            _context.Companies.AsNoTracking(),
            bus => bus.CompanyId,
            company => company.Id,
            (bus, company) => new
            {
                bus.Id,
                bus.CompanyId,
                CompanyName = company.Name,
                bus.PlateNumber,
                bus.Brand,
                bus.Model,
                bus.SeatCapacity,
                bus.IsActive
            }
        )
        .OrderBy(b => b.CompanyName)
        .ThenBy(b => b.PlateNumber)
        .ToListAsync();

    return Ok(buses);
}



[HttpGet("{id:int}")]
[Authorize(Policy = "bus.view")]
public async Task<IActionResult> GetBusById(int id)
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

    if (currentUser.Role != "Admin" &&
        currentUser.Role != "Company")
    {
        return Forbid();
    }

    if (currentUser.Role == "Company" &&
        currentUser.CompanyId == null)
    {
        return Forbid();
    }

    var query = _context.Buses
        .AsNoTracking()
        .Where(b => b.Id == id);

    if (currentUser.Role == "Company")
    {
        query = query.Where(
            b => b.CompanyId == currentUser.CompanyId
        );
    }

    var bus = await query
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
        .FirstOrDefaultAsync();

    if (bus == null)
    {
        return NotFound("Otobüs bulunamadı.");
    }

    return Ok(bus);
}
[HttpPost]
[Authorize(Policy = "bus.create")]
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