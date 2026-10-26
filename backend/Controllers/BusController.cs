using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

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
        public async Task<IActionResult> GetBuses(
            [FromQuery] string? search,
            [FromQuery] int? companyId,
            [FromQuery] bool? isActive,
            [FromQuery] string? brand,
            [FromQuery] int? minCapacity,
            [FromQuery] int? maxCapacity,
            [FromQuery] string? sortBy = "plate",
            [FromQuery] string? sortDirection = "asc",
            [FromQuery] int? pageNumber = null,
            [FromQuery] int pageSize = 10)
        {
            if (pageNumber.HasValue && pageNumber.Value < 1)
            {
                return BadRequest(new { message = "Sayfa numarası 1 veya daha büyük olmalıdır." });
            }

            if (pageSize < 1 || pageSize > 100)
            {
                return BadRequest(new { message = "Sayfa boyutu 1 ile 100 arasında olmalıdır." });
            }

            if (minCapacity < 0 || maxCapacity < 0 ||
                (minCapacity.HasValue && maxCapacity.HasValue && minCapacity > maxCapacity))
            {
                return BadRequest(new { message = "Koltuk kapasitesi aralığı geçersiz." });
            }

            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            var query = _context.Buses.AsNoTracking().AsQueryable();
            var scopeError = ApplyCompanyScope(ref query, currentUser, companyId);
            if (scopeError != null)
            {
                return scopeError;
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(bus =>
                    bus.PlateNumber.Contains(term) ||
                    (bus.Brand != null && bus.Brand.Contains(term)) ||
                    (bus.Model != null && bus.Model.Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(brand))
            {
                var brandTerm = brand.Trim();
                query = query.Where(bus => bus.Brand != null && bus.Brand.Contains(brandTerm));
            }

            if (isActive.HasValue)
            {
                query = query.Where(bus => bus.IsActive == isActive.Value);
            }

            if (minCapacity.HasValue)
            {
                query = query.Where(bus => bus.SeatCapacity >= minCapacity.Value);
            }

            if (maxCapacity.HasValue)
            {
                query = query.Where(bus => bus.SeatCapacity <= maxCapacity.Value);
            }

            var totalCount = await query.CountAsync();
            var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
            query = sortBy?.ToLowerInvariant() switch
            {
                "capacity" => descending
                    ? query.OrderByDescending(bus => bus.SeatCapacity).ThenBy(bus => bus.Id)
                    : query.OrderBy(bus => bus.SeatCapacity).ThenBy(bus => bus.Id),
                _ => descending
                    ? query.OrderByDescending(bus => bus.PlateNumber).ThenBy(bus => bus.Id)
                    : query.OrderBy(bus => bus.PlateNumber).ThenBy(bus => bus.Id)
            };

            var itemsQuery = query.Select(bus => new BusListDto
            {
                Id = bus.Id,
                CompanyId = bus.CompanyId,
                CompanyName = bus.Company!.Name,
                PlateNumber = bus.PlateNumber,
                Brand = bus.Brand,
                Model = bus.Model,
                SeatCapacity = bus.SeatCapacity,
                IsActive = bus.IsActive,
                CreatedAt = bus.CreatedAt,
                UpdatedAt = bus.UpdatedAt
            });

            if (!pageNumber.HasValue)
            {
                return Ok(await itemsQuery.ToListAsync());
            }

            var items = await itemsQuery
                .Skip((pageNumber.Value - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new BusListResponseDto
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber.Value,
                PageSize = pageSize
            });
        }

        [HttpGet("summary")]
        [Authorize(Policy = "bus.view")]
        public async Task<IActionResult> GetSummary()
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            var query = _context.Buses.AsNoTracking().AsQueryable();
            var scopeError = ApplyCompanyScope(ref query, currentUser, null);
            if (scopeError != null)
            {
                return scopeError;
            }

            var summary = new BusSummaryDto
            {
                TotalBuses = await query.CountAsync(),
                ActiveBuses = await query.CountAsync(bus => bus.IsActive),
                InactiveBuses = await query.CountAsync(bus => !bus.IsActive),
                CompaniesWithBuses = await query.Select(bus => bus.CompanyId).Distinct().CountAsync()
            };

            return Ok(summary);
        }

        [HttpGet("{id:int}")]
        [Authorize(Policy = "bus.view")]
        public async Task<IActionResult> GetBusById(int id)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            var query = _context.Buses
                .AsNoTracking()
                .Include(bus => bus.Company)
                .Where(bus => bus.Id == id);

            if (currentUser.Role == "Company")
            {
                if (!currentUser.CompanyId.HasValue)
                {
                    return Forbid();
                }

                query = query.Where(bus => bus.CompanyId == currentUser.CompanyId.Value);
            }
            else if (currentUser.Role != "Admin")
            {
                return Forbid();
            }

            var bus = await query.FirstOrDefaultAsync();
            if (bus == null)
            {
                return NotFound(new { message = "Otobüs bulunamadı." });
            }

            var now = DateTime.UtcNow;
            var trips = _context.Trips.AsNoTracking().Where(trip => trip.BusId == id);
            var upcomingTrips = await trips
                .Where(trip =>
                    trip.DepartureTime >= now &&
                    trip.Status.ToLower() != "cancelled" &&
                    trip.Status.ToLower() != "canceled" &&
                    trip.Status.ToLower() != "iptal" &&
                    trip.Status.ToLower() != "iptal edildi" &&
                    trip.Status.ToLower() != "completed" &&
                    trip.Status.ToLower() != "tamamlandı")
                .OrderBy(trip => trip.DepartureTime)
                .Take(5)
                .Select(trip => new BusTripDto
                {
                    Id = trip.Id,
                    DepartureCity = trip.Route.DepartureCity,
                    ArrivalCity = trip.Route.ArrivalCity,
                    DepartureTime = trip.DepartureTime,
                    ArrivalTime = trip.ArrivalTime,
                    Status = trip.Status,
                    AvailableSeats = trip.AvailableSeats
                })
                .ToListAsync();

            var detail = new BusDetailDto
            {
                Id = bus.Id,
                CompanyId = bus.CompanyId,
                CompanyName = bus.Company?.Name ?? string.Empty,
                CompanyIsActive = bus.Company?.IsActive ?? false,
                PlateNumber = bus.PlateNumber,
                Brand = bus.Brand,
                Model = bus.Model,
                SeatCapacity = bus.SeatCapacity,
                IsActive = bus.IsActive,
                CreatedAt = bus.CreatedAt,
                UpdatedAt = bus.UpdatedAt,
                TotalTrips = await trips.CountAsync(),
                UpcomingTripsCount = await trips.CountAsync(trip =>
                    trip.DepartureTime >= now &&
                    trip.Status.ToLower() != "cancelled" &&
                    trip.Status.ToLower() != "canceled" &&
                    trip.Status.ToLower() != "iptal" &&
                    trip.Status.ToLower() != "iptal edildi" &&
                    trip.Status.ToLower() != "completed" &&
                    trip.Status.ToLower() != "tamamlandı"),
                CompletedTripsCount = await trips.CountAsync(trip =>
                    trip.Status.ToLower() == "completed" ||
                    trip.Status.ToLower() == "tamamlandı"),
                CancelledTripsCount = await trips.CountAsync(trip =>
                    trip.Status.ToLower() == "cancelled" ||
                    trip.Status.ToLower() == "canceled" ||
                    trip.Status.ToLower() == "iptal" ||
                    trip.Status.ToLower() == "iptal edildi"),
                UpcomingTrips = upcomingTrips
            };

            return Ok(detail);
        }

        [HttpPost]
        [Authorize(Policy = "bus.create")]
        public async Task<IActionResult> CreateBus([FromBody] CreateBusDto dto)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            if (dto.SeatCapacity <= 0)
            {
                return BadRequest(new { message = "Koltuk kapasitesi 0'dan büyük olmalıdır." });
            }

            if (!TryNormalizePlate(dto.PlateNumber, out var plateNumber, out var normalizedPlate))
            {
                return BadRequest(new { message = "Geçerli bir plaka giriniz." });
            }

            int companyId;
            if (currentUser.Role == "Company")
            {
                if (!currentUser.CompanyId.HasValue)
                {
                    return Forbid();
                }

                if (dto.CompanyId.HasValue && dto.CompanyId.Value != currentUser.CompanyId.Value)
                {
                    return Forbid();
                }

                companyId = currentUser.CompanyId.Value;
            }
            else if (currentUser.Role == "Admin")
            {
                if (!dto.CompanyId.HasValue)
                {
                    return BadRequest(new { message = "Firma seçimi zorunludur." });
                }

                companyId = dto.CompanyId.Value;
            }
            else
            {
                return Forbid();
            }

            var company = await _context.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == companyId);
            if (company == null || !company.IsActive)
            {
                return BadRequest(new { message = "Firma bulunamadı veya pasif durumda." });
            }

            if (await _context.Buses.AnyAsync(bus => bus.NormalizedPlateNumber == normalizedPlate))
            {
                return Conflict(new { message = "Bu plaka başka bir otobüste kullanılıyor." });
            }

            var bus = new Bus
            {
                CompanyId = companyId,
                PlateNumber = plateNumber,
                NormalizedPlateNumber = normalizedPlate,
                Brand = CleanOptional(dto.Brand),
                Model = CleanOptional(dto.Model),
                SeatCapacity = dto.SeatCapacity,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.Buses.Add(bus);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                return Conflict(new { message = "Bu plaka başka bir otobüste kullanılıyor." });
            }

            return StatusCode(StatusCodes.Status201Created, new
            {
                message = "Otobüs başarıyla eklendi.",
                busId = bus.Id
            });
        }

        [HttpPut("{id:int}")]
        [Authorize(Policy = "bus.update")]
        public async Task<IActionResult> UpdateBus(int id, [FromBody] UpdateBusDto dto)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            var bus = await _context.Buses.FirstOrDefaultAsync(item => item.Id == id);
            if (bus == null)
            {
                return NotFound(new { message = "Otobüs bulunamadı." });
            }

            if (!CanManageBus(currentUser, bus))
            {
                return Forbid();
            }

            if (dto.SeatCapacity <= 0)
            {
                return BadRequest(new { message = "Koltuk kapasitesi 0'dan büyük olmalıdır." });
            }

            if (!TryNormalizePlate(dto.PlateNumber, out var plateNumber, out var normalizedPlate))
            {
                return BadRequest(new { message = "Geçerli bir plaka giriniz." });
            }

            int companyId = bus.CompanyId;
            if (currentUser.Role == "Admin")
            {
                if (!dto.CompanyId.HasValue)
                {
                    return BadRequest(new { message = "Firma seçimi zorunludur." });
                }

                companyId = dto.CompanyId.Value;
                var company = await _context.Companies
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == companyId);
                if (company == null || (companyId != bus.CompanyId && !company.IsActive))
                {
                    return BadRequest(new { message = "Firma bulunamadı veya pasif durumda." });
                }
            }
            else if (dto.CompanyId.HasValue && dto.CompanyId.Value != bus.CompanyId)
            {
                return Forbid();
            }

            if (await _context.Buses.AnyAsync(item =>
                item.Id != id && item.NormalizedPlateNumber == normalizedPlate))
            {
                return Conflict(new { message = "Bu plaka başka bir otobüste kullanılıyor." });
            }

            var now = DateTime.UtcNow;
            var futureTrips = _context.Trips.Where(trip =>
                trip.BusId == id &&
                trip.DepartureTime >= now &&
                trip.Status.ToLower() != "cancelled" &&
                trip.Status.ToLower() != "canceled" &&
                trip.Status.ToLower() != "iptal" &&
                trip.Status.ToLower() != "iptal edildi" &&
                trip.Status.ToLower() != "completed" &&
                trip.Status.ToLower() != "tamamlandı");

            if (companyId != bus.CompanyId && await futureTrips.AnyAsync())
            {
                return Conflict(new
                {
                    message = "Yaklaşan seferlere bağlı otobüsün firması değiştirilemez."
                });
            }

            if (dto.SeatCapacity < bus.SeatCapacity)
            {
                var hasMoreReservedSeatsThanCapacity = await futureTrips.AnyAsync(trip =>
                    bus.SeatCapacity - trip.AvailableSeats > dto.SeatCapacity);
                if (hasMoreReservedSeatsThanCapacity)
                {
                    return Conflict(new
                    {
                        message = "Kapasite, yaklaşan seferlerdeki mevcut yolcu sayısının altına düşürülemez."
                    });
                }
            }

            var requestedActive = dto.IsActive!.Value;
            if (bus.IsActive && !requestedActive && await futureTrips.AnyAsync())
            {
                return Conflict(new
                {
                    message = "Bu otobüse bağlı yaklaşan seferler bulunduğu için pasifleştirme işlemi gerçekleştirilemiyor."
                });
            }

            bus.CompanyId = companyId;
            bus.PlateNumber = plateNumber;
            bus.NormalizedPlateNumber = normalizedPlate;
            bus.Brand = CleanOptional(dto.Brand);
            bus.Model = CleanOptional(dto.Model);
            bus.SeatCapacity = dto.SeatCapacity;
            bus.IsActive = requestedActive;
            bus.UpdatedAt = now;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                return Conflict(new { message = "Bu plaka başka bir otobüste kullanılıyor." });
            }

            return Ok(new { message = "Otobüs bilgileri güncellendi." });
        }

        [HttpPatch("{id:int}/status")]
        [Authorize(Policy = "bus.update")]
        public async Task<IActionResult> UpdateBusStatus(int id, [FromBody] UpdateBusStatusDto dto)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            var bus = await _context.Buses.FirstOrDefaultAsync(item => item.Id == id);
            if (bus == null)
            {
                return NotFound(new { message = "Otobüs bulunamadı." });
            }

            if (!CanManageBus(currentUser, bus))
            {
                return Forbid();
            }

            var activate = dto.IsActive!.Value;
            if (bus.IsActive && !activate)
            {
                var now = DateTime.UtcNow;
                var hasUpcomingTrips = await _context.Trips.AnyAsync(trip =>
                    trip.BusId == id &&
                    trip.DepartureTime >= now &&
                    trip.Status.ToLower() != "cancelled" &&
                    trip.Status.ToLower() != "canceled" &&
                    trip.Status.ToLower() != "iptal" &&
                    trip.Status.ToLower() != "iptal edildi" &&
                    trip.Status.ToLower() != "completed" &&
                    trip.Status.ToLower() != "tamamlandı");

                if (hasUpcomingTrips)
                {
                    return Conflict(new
                    {
                        message = "Bu otobüse bağlı yaklaşan seferler bulunduğu için pasifleştirme işlemi gerçekleştirilemiyor."
                    });
                }
            }

            bus.IsActive = activate;
            bus.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = activate ? "Otobüs aktifleştirildi." : "Otobüs pasifleştirildi." });
        }

        private async Task<User?> GetCurrentUserAsync()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return null;
            }

            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(user => user.Id == userId);
        }

        private IActionResult? ApplyCompanyScope(
            ref IQueryable<Bus> query,
            User currentUser,
            int? requestedCompanyId)
        {
            if (currentUser.Role == "Company")
            {
                if (!currentUser.CompanyId.HasValue)
                {
                    return Forbid();
                }

                if (requestedCompanyId.HasValue && requestedCompanyId != currentUser.CompanyId)
                {
                    return Forbid();
                }

                query = query.Where(bus => bus.CompanyId == currentUser.CompanyId.Value);
                return null;
            }

            if (currentUser.Role != "Admin")
            {
                return Forbid();
            }

            if (requestedCompanyId.HasValue)
            {
                query = query.Where(bus => bus.CompanyId == requestedCompanyId.Value);
            }

            return null;
        }

        private static bool CanManageBus(User currentUser, Bus bus) =>
            currentUser.Role == "Admin" ||
            (currentUser.Role == "Company" &&
             currentUser.CompanyId.HasValue &&
             currentUser.CompanyId.Value == bus.CompanyId);

        private static bool TryNormalizePlate(
            string? input,
            out string plateNumber,
            out string normalizedPlate)
        {
            plateNumber = string.Empty;
            normalizedPlate = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            plateNumber = Regex.Replace(input.Trim().ToUpperInvariant(), @"\s+", " ");
            normalizedPlate = Regex.Replace(plateNumber, @"[\s-]", string.Empty);

            return plateNumber.Length <= 20 &&
                   normalizedPlate.Length is >= 3 and <= 20 &&
                   normalizedPlate.All(char.IsLetterOrDigit);
        }

        private static string? CleanOptional(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
            exception.GetBaseException() is SqlException { Number: 2601 or 2627 };
    }
}
