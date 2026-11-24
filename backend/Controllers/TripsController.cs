using Travela.Api.Payments;
using System.Data;
using Travela.Api.Security;
using System.Security.Claims;
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
    [Route("api/trips")]
    [Authorize]
    public partial class TripsController : ControllerBase
    {
        private readonly TravelaDbContext _context;
        private Travela.Api.Trips.TripSegmentService Segments => new(_context, TimeProvider.System);

        public TripsController(TravelaDbContext context)
        {
            _context = context;
        }

        [HttpGet("upcoming")]
        [AllowAnonymous]
        public async Task<IActionResult> GetUpcomingPublicTrips([FromQuery] int count = 6)
        {
            if (count < 1 || count > 12)
            {
                return BadRequest(new { message = "Sefer sayısı 1 ile 12 arasında olmalıdır." });
            }

            var now = DateTime.UtcNow;
            var items = await _context.Trips
                .AsNoTracking()
                .Where(trip =>
                    trip.Company.IsActive &&
                    trip.Bus.IsActive &&
                    trip.Bus.CompanyId == trip.CompanyId &&
                    trip.Route.IsActive && (trip.DepartureTerminalId == null || trip.DepartureTerminal!.IsActive) && (trip.ArrivalTerminalId == null || trip.ArrivalTerminal!.IsActive) &&
                    trip.DepartureTime > now &&
                    trip.AvailableSeats > 0 &&
                    (trip.Status.ToLower() == "scheduled" ||
                     trip.Status.ToLower() == "planlandı" ||
                     trip.Status.ToLower() == "planlandi"))
                .OrderBy(trip => trip.DepartureTime)
                .Take(count)
                .Select(trip => new PublicTripDto
                {
                    Id = trip.Id,
                    CompanyId = trip.CompanyId,
                    CompanyName = trip.Company.Name,
                    DepartureCity = trip.Route.DepartureCity,
                    ArrivalCity = trip.Route.ArrivalCity,
                    DepartureTerminalId = trip.DepartureTerminalId, ArrivalTerminalId = trip.ArrivalTerminalId, DepartureTerminalName = trip.DepartureTerminalName, DepartureTerminalAddress = trip.DepartureTerminalAddress, ArrivalTerminalName = trip.ArrivalTerminalName, ArrivalTerminalAddress = trip.ArrivalTerminalAddress, BoardingInstructions = trip.BoardingInstructions,
                    DepartureTime = trip.DepartureTime,
                    ArrivalTime = trip.ArrivalTime,
                    DurationMinutes = EF.Functions.DateDiffMinute(trip.DepartureTime, trip.ArrivalTime),
                    Price = trip.Price,
                    AvailableSeats = trip.AvailableSeats
                })
                .ToListAsync();

            foreach (var item in items)
            {
                item.DepartureTime = AsUtc(item.DepartureTime);
                item.ArrivalTime = AsUtc(item.ArrivalTime);
            }

            return Ok(items);
        }

        [HttpGet("featured-routes")]
        [AllowAnonymous]
        public async Task<IActionResult> GetFeaturedRoutes([FromQuery] int count = 6)
        {
            if (count < 1 || count > 12)
            {
                return BadRequest(new { message = "Güzergâh sayısı 1 ile 12 arasında olmalıdır." });
            }

            var now = DateTime.UtcNow;
            var routes = await _context.Trips
                .AsNoTracking()
                .Where(trip =>
                    trip.Company.IsActive &&
                    trip.Bus.IsActive &&
                    trip.Bus.CompanyId == trip.CompanyId &&
                    trip.Route.IsActive && (trip.DepartureTerminalId == null || trip.DepartureTerminal!.IsActive) && (trip.ArrivalTerminalId == null || trip.ArrivalTerminal!.IsActive) &&
                    trip.DepartureTime > now &&
                    trip.AvailableSeats > 0 &&
                    (trip.Status.ToLower() == "scheduled" ||
                     trip.Status.ToLower() == "planlandı" ||
                     trip.Status.ToLower() == "planlandi"))
                .GroupBy(trip => new
                {
                    trip.RouteId,
                    trip.Route.DepartureCity,
                    trip.Route.ArrivalCity
                })
                .Select(group => new FeaturedRouteDto
                {
                    RouteId = group.Key.RouteId,
                    DepartureCity = group.Key.DepartureCity,
                    ArrivalCity = group.Key.ArrivalCity,
                    UpcomingTripCount = group.Count(),
                    StartingPrice = group.Min(trip => trip.Price)
                })
                .OrderByDescending(route => route.UpcomingTripCount)
                .ThenBy(route => route.StartingPrice)
                .Take(count)
                .ToListAsync();

            return Ok(routes);
        }

        [HttpGet]
        [Authorize(Policy = "trip.view")]
        public async Task<IActionResult> GetTrips(
            [FromQuery] string? search,
            [FromQuery] int? companyId,
            [FromQuery] string? departureCity,
            [FromQuery] string? arrivalCity,
            [FromQuery] DateTime? departureFrom,
            [FromQuery] DateTime? departureTo,
            [FromQuery] string? status,
            [FromQuery] string? busPlate,
            [FromQuery] decimal? minPrice,
            [FromQuery] decimal? maxPrice,
            [FromQuery] string? sortBy = "date",
            [FromQuery] string? sortDirection = "asc",
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            if (pageNumber < 1 || pageSize < 1 || pageSize > 100)
            {
                return BadRequest(new { message = "Sayfa numarası 1 veya daha büyük, sayfa boyutu 1 ile 100 arasında olmalıdır." });
            }

            if (minPrice < 0 || maxPrice < 0 ||
                (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice))
            {
                return BadRequest(new { message = "Bilet fiyatı aralığı geçersiz." });
            }

            if (departureFrom.HasValue && departureTo.HasValue &&
                AsUtc(departureFrom.Value).Date > AsUtc(departureTo.Value).Date)
            {
                return BadRequest(new { message = "Kalkış tarihi aralığı geçersiz." });
            }

            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            if (user.Role is not ("Admin" or "Company"))
            {
                return Forbid();
            }

            var query = _context.Trips.AsNoTracking().AsQueryable();
            if (user.Role == "Company")
            {
                if (!user.CompanyId.HasValue)
                {
                    return Forbid();
                }

                if (companyId.HasValue && companyId.Value != user.CompanyId.Value)
                {
                    return Forbid();
                }

                query = query.Where(trip => trip.CompanyId == user.CompanyId.Value);
            }
            else if (companyId.HasValue)
            {
                query = query.Where(trip => trip.CompanyId == companyId.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var hasNumericId = int.TryParse(term, out var tripId);
                query = query.Where(trip =>
                    (hasNumericId && trip.Id == tripId) ||
                    trip.Company.Name.Contains(term) ||
                    trip.Bus.PlateNumber.Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(departureCity))
            {
                var city = departureCity.Trim();
                query = query.Where(trip => trip.Route.DepartureCity.Contains(city));
            }

            if (!string.IsNullOrWhiteSpace(arrivalCity))
            {
                var city = arrivalCity.Trim();
                query = query.Where(trip => trip.Route.ArrivalCity.Contains(city));
            }

            if (departureFrom.HasValue)
            {
                query = query.Where(trip => trip.DepartureTime >= AsUtc(departureFrom.Value).Date);
            }

            if (departureTo.HasValue)
            {
                query = query.Where(trip => trip.DepartureTime < AsUtc(departureTo.Value).Date.AddDays(1));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalizedStatus = NormalizeStatus(status);
                if (normalizedStatus == null)
                {
                    return BadRequest(new { message = "Geçersiz sefer durumu." });
                }

                var statusValue = normalizedStatus;
                query = query.Where(trip => trip.Status == statusValue);
            }

            if (!string.IsNullOrWhiteSpace(busPlate))
            {
                var plate = busPlate.Trim();
                query = query.Where(trip => trip.Bus.PlateNumber.Contains(plate));
            }

            if (minPrice.HasValue)
            {
                query = query.Where(trip => trip.Price >= minPrice.Value);
            }

            if (maxPrice.HasValue)
            {
                query = query.Where(trip => trip.Price <= maxPrice.Value);
            }

            var totalCount = await query.CountAsync();
            var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
            query = sortBy?.ToLowerInvariant() switch
            {
                "price" => descending
                    ? query.OrderByDescending(trip => trip.Price).ThenBy(trip => trip.Id)
                    : query.OrderBy(trip => trip.Price).ThenBy(trip => trip.Id),
                _ => descending
                    ? query.OrderByDescending(trip => trip.DepartureTime).ThenBy(trip => trip.Id)
                    : query.OrderBy(trip => trip.DepartureTime).ThenBy(trip => trip.Id)
            };

            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(trip => new TripListDto
                {
                    ChangeVersion = trip.ChangeVersion,
                    Id = trip.Id,
                    CompanyId = trip.CompanyId,
                    CompanyName = trip.Company.Name,
                    BusId = trip.BusId,
                    BusPlateNumber = trip.Bus.PlateNumber,
                    BusBrand = trip.Bus.Brand,
                    BusModel = trip.Bus.Model,
                    BusCapacity = trip.SeatCapacitySnapshot ?? trip.Bus.SeatCapacity, SeatLayout = trip.SeatLayout,
                    SeatPlanJson = trip.SeatPlanJson, SellableSeatCount = trip.SellableSeatCount ?? trip.Bus.SeatCapacity,
                    RouteId = trip.RouteId,
                    DepartureCity = trip.Route.DepartureCity,
                    ArrivalCity = trip.Route.ArrivalCity,
                    DepartureTerminalId = trip.DepartureTerminalId, ArrivalTerminalId = trip.ArrivalTerminalId, DepartureTerminalName = trip.DepartureTerminalName, DepartureTerminalAddress = trip.DepartureTerminalAddress, ArrivalTerminalName = trip.ArrivalTerminalName, ArrivalTerminalAddress = trip.ArrivalTerminalAddress, BoardingInstructions = trip.BoardingInstructions,
                    DepartureTime = trip.DepartureTime,
                    ArrivalTime = trip.ArrivalTime,
                    Price = trip.Price,
                    AvailableSeats = trip.AvailableSeats,
                    Status = trip.Status
                })
                .ToListAsync();

            SetUtcKinds(items);
            return Ok(new TripListResponseDto
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        [HttpGet("summary")]
        [Authorize(Policy = "trip.view")]
        public async Task<IActionResult> GetSummary()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            if (user.Role is not ("Admin" or "Company"))
            {
                return Forbid();
            }

            var query = _context.Trips.AsNoTracking().AsQueryable();
            if (user.Role == "Company")
            {
                if (!user.CompanyId.HasValue)
                {
                    return Forbid();
                }

                query = query.Where(trip => trip.CompanyId == user.CompanyId.Value);
            }

            return Ok(new TripSummaryDto
            {
                TotalTrips = await query.CountAsync(),
                ScheduledTrips = await query.CountAsync(trip =>
                    trip.Status.ToLower() == "scheduled" ||
                    trip.Status.ToLower() == "planlandı"),
                InProgressTrips = await query.CountAsync(trip =>
                    trip.Status == "InProgress" ||
                    trip.Status == "In Progress" ||
                    trip.Status == "Devam Ediyor"),
                CompletedTrips = await query.CountAsync(trip =>
                    trip.Status.ToLower() == "completed" ||
                    trip.Status.ToLower() == "tamamlandı"),
                CancelledTrips = await query.CountAsync(trip =>
                    trip.Status.ToLower() == "cancelled" ||
                    trip.Status.ToLower() == "canceled" ||
                    trip.Status.ToLower() == "iptal" ||
                    trip.Status.ToLower() == "iptal edildi")
            });
        }

        [HttpGet("{id:int}")]
        [Authorize(Policy = "trip.view")]
        public async Task<IActionResult> GetTripById(int id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            if (user.Role is not ("Admin" or "Company"))
            {
                return Forbid();
            }

            var query = _context.Trips.AsNoTracking().Where(trip => trip.Id == id);
            if (user.Role == "Company")
            {
                if (!user.CompanyId.HasValue)
                {
                    return Forbid();
                }
                query = query.Where(trip => trip.CompanyId == user.CompanyId.Value);
            }

            var trip = await query.Select(item => new TripDetailDto
            {
                ChangeVersion = item.ChangeVersion,
                Id = item.Id,
                CompanyId = item.CompanyId,
                CompanyName = item.Company.Name,
                CompanyIsActive = item.Company.IsActive,
                BusId = item.BusId,
                BusPlateNumber = item.Bus.PlateNumber,
                BusBrand = item.Bus.Brand,
                BusModel = item.Bus.Model,
                BusCapacity = item.SeatCapacitySnapshot ?? item.Bus.SeatCapacity, SeatLayout = item.SeatLayout,
                SeatPlanJson = item.SeatPlanJson, SellableSeatCount = item.SellableSeatCount ?? item.Bus.SeatCapacity,
                BusIsActive = item.Bus.IsActive,
                RouteId = item.RouteId,
                DepartureCity = item.Route.DepartureCity,
                ArrivalCity = item.Route.ArrivalCity,
                    DepartureTerminalId = item.DepartureTerminalId, ArrivalTerminalId = item.ArrivalTerminalId, DepartureTerminalName = item.DepartureTerminalName, DepartureTerminalAddress = item.DepartureTerminalAddress, ArrivalTerminalName = item.ArrivalTerminalName, ArrivalTerminalAddress = item.ArrivalTerminalAddress, BoardingInstructions = item.BoardingInstructions,
                DistanceKm = item.Route.DistanceKm,
                EstimatedDurationMinutes = item.Route.EstimatedDurationMinutes,
                DepartureTime = item.DepartureTime,
                ArrivalTime = item.ArrivalTime,
                Price = item.Price,
                AvailableSeats = item.AvailableSeats,
                Status = item.Status,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt,
                CancellationReason = item.CancellationReason,
                CancelledAt = item.CancelledAt
            }).FirstOrDefaultAsync();

            if (trip == null)
            {
                return NotFound(new { message = "Sefer bulunamadı." });
            }

            SetUtcKinds(trip);
            return Ok(trip);
        }

        [HttpPost]
        [Authorize(Policy = "trip.create")]
        public async Task<IActionResult> CreateTrip([FromBody] CreateTripDto dto)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            var companyId = await ResolveCompanyIdAsync(user, dto.CompanyId);
            if (companyId == null)
            {
                return user.Role == "Admin"
                    ? BadRequest(new { message = "Firma seçimi zorunludur." })
                    : Forbid();
            }

            var departure = AsUtc(dto.DepartureTime);
            var arrival = AsUtc(dto.ArrivalTime);
            if (departure <= DateTime.UtcNow)
            {
                return BadRequest(new { message = "Yeni seferin kalkış zamanı geçmişte olamaz." });
            }
            if (arrival <= departure)
            {
                return BadRequest(new { message = "Varış zamanı kalkış zamanından sonra olmalıdır." });
            }
            if (dto.Price <= 0)
            {
                return BadRequest(new { message = "Bilet fiyatı sıfırdan büyük olmalıdır." });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var company = await _context.Companies
                .FirstOrDefaultAsync(item => item.Id == companyId.Value);
            var bus = await _context.Buses
                .FirstOrDefaultAsync(item => item.Id == dto.BusId);
            var route = await _context.Routes
                .FirstOrDefaultAsync(item => item.Id == dto.RouteId);

            var validationError = ValidateTripResources(company, bus, route, companyId.Value);
            if (validationError != null)
            {
                return validationError;
            }

            bool overlap;
            try
            {
                overlap = await HasBusOverlapAsync(dto.BusId, departure, arrival, null);
            }
            catch (SqlException exception) when (exception.Number == 1205)
            {
                return Conflict(new { message = "Eşzamanlı bir sefer işlemi nedeniyle çakışma denetimi tamamlanamadı. Lütfen tekrar deneyin." });
            }

            if (overlap)
            {
                return Conflict(new { message = "Bu otobüs aynı zaman aralığında başka bir sefere atanmış." });
            }

            var trip = new Trip
            {
                CompanyId = companyId.Value,
                BusId = bus!.Id,
                RouteId = route!.Id,
                DepartureTime = departure,
                ArrivalTime = arrival,
                Price = decimal.Round(dto.Price, 2, MidpointRounding.ToEven),
                AvailableSeats = bus.SeatCapacity,
                Status = "Scheduled",
                CreatedAt = DateTime.UtcNow
            };

            var journeyError = await PrepareJourney(trip, bus!, route!, dto);
            if (journeyError != null) return journeyError;
            var crewError = await PrepareCrew(trip, dto.Crew);
            if (crewError != null) return crewError;
            _context.Trips.Add(trip);
            try
            {
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception))
            {
                return Conflict(new { message = "Otobüs için çakışan bir sefer eşzamanlı olarak oluşturuldu. Listeyi yenileyip tekrar deneyin." });
            }

            return StatusCode(StatusCodes.Status201Created, new
            {
                message = "Sefer başarıyla oluşturuldu.",
                tripId = trip.Id
            });
        }

        [HttpPut("{id:int}")]
        [Authorize(Policy = "trip.update")]
        public async Task<IActionResult> UpdateTrip(int id, [FromBody] UpdateTripDto dto)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var trip = await _context.Trips
                .FromSqlInterpolated($"SELECT * FROM [Trips] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
                .Include(item => item.Bus)
                .FirstOrDefaultAsync(item => item.Id == id);
            if (trip == null || !CanAccessTrip(user, trip))
            {
                return NotFound(new { message = "Sefer bulunamadı." });
            }

            if (NormalizeStatus(trip.Status) != "Scheduled")
            {
                return Conflict(new { message = "Yalnızca planlanan seferler düzenlenebilir." });
            }

            var now = DateTime.UtcNow;
            if (trip.DepartureTime <= now)
            {
                return Conflict(new { message = "Kalkış zamanı gelmiş sefer düzenlenemez." });
            }

            var departure = AsUtc(dto.DepartureTime);
            var arrival = AsUtc(dto.ArrivalTime);
            if (departure <= now || arrival <= departure)
            {
                return BadRequest(new { message = "Kalkış gelecekte olmalı ve varış kalkıştan sonra gerçekleşmelidir." });
            }
            if (dto.Price <= 0)
            {
                return BadRequest(new { message = "Bilet fiyatı sıfırdan büyük olmalıdır." });
            }

            var companyId = user.Role == "Company"
                ? user.CompanyId
                : dto.CompanyId ?? trip.CompanyId;
            if (!companyId.HasValue || (user.Role == "Company" && dto.CompanyId.HasValue && dto.CompanyId != user.CompanyId))
            {
                return Forbid();
            }

            var expiredHolds = await _context.Reservations.Where(r => r.TripId == id && r.Status == "Active" && r.Kind == "Hold" && r.HoldExpiresAt <= now).ToListAsync();
            if (companyId != trip.CompanyId && await _context.Reservations.AnyAsync(r => r.TripId == id))
                return Conflict(new { message = "İşlem geçmişi bulunan sefer başka firmaya aktarılamaz." });
            foreach (var hold in expiredHolds) Segments.Release(hold, "Ödeme süresi doldu.");
            if (await _context.Reservations.AnyAsync(r => r.TripId == id && r.Status == "Active" && (r.Kind != "Hold" || r.HoldExpiresAt > now)))
            {
                return Conflict(new { message = "Aktif bilet veya ödeme tutması var. Saat/araç için kontrollü sefer değişikliği işlemini kullanın." });
            }
            await _context.Entry(trip).Reference(t => t.Route).LoadAsync();
            await _context.Entry(trip).Reference(t => t.Company).LoadAsync();
            var oldJourney = Travela.Api.Trips.TicketJourney.Capture(trip).Write();
            var historicalTickets = await _context.Reservations.Where(r => r.TripId == id && r.Kind == "Ticket" && r.Status != "Active" && r.TicketJourneyJson == null).ToListAsync();
            foreach (var ticket in historicalTickets) ticket.TicketJourneyJson = oldJourney;
            var company = await _context.Companies.FirstOrDefaultAsync(item => item.Id == companyId.Value);
            var bus = await _context.Buses.FirstOrDefaultAsync(item => item.Id == dto.BusId);
            var route = await _context.Routes.FirstOrDefaultAsync(item => item.Id == dto.RouteId);
            var validationError = ValidateTripResources(company, bus, route, companyId.Value);
            if (validationError != null)
            {
                return validationError;
            }

            if (dto.Price != trip.Price && trip.AvailableSeats < (trip.SellableSeatCount ?? trip.Bus.SeatCapacity))
            {
                return Conflict(new
                {
                    message = "Mevcut kontenjan kullanımı nedeniyle fiyat değiştirilemiyor."
                });
            }

            var existingPassengerCount = Math.Max(0, (trip.SellableSeatCount ?? trip.Bus.SeatCapacity) - trip.AvailableSeats);
            if (bus!.SeatCapacity < existingPassengerCount)
            {
                return Conflict(new { message = "Yeni otobüs kapasitesi mevcut kontenjan kullanımını karşılamıyor." });
            }

            bool overlap;
            try
            {
                overlap = await HasBusOverlapAsync(bus.Id, departure, arrival, trip.Id);
            }
            catch (SqlException exception) when (exception.Number == 1205)
            {
                return Conflict(new { message = "Eşzamanlı bir sefer işlemi nedeniyle çakışma denetimi tamamlanamadı. Lütfen tekrar deneyin." });
            }

            if (overlap)
            {
                return Conflict(new { message = "Bu otobüs aynı zaman aralığında başka bir sefere atanmış." });
            }

            trip.CompanyId = companyId.Value;
            trip.BusId = bus.Id;
            trip.RouteId = route!.Id;
            trip.DepartureTime = departure;
            trip.ArrivalTime = arrival;
            trip.Price = decimal.Round(dto.Price, 2, MidpointRounding.ToEven);
            trip.AvailableSeats = bus.SeatCapacity - existingPassengerCount;
            var journeyError = await PrepareJourney(trip, bus, route!, dto);
            if (journeyError != null) return journeyError;
            if (trip.AvailableSeats < existingPassengerCount) return Conflict(new { message = "Satışa açık kapasite mevcut kontenjanın altına düşemez." });
            trip.UpdatedAt = now;
            trip.ChangeVersion++;

            var crewError = await PrepareCrew(trip, dto.Crew);
            if (crewError != null) return crewError;

            try
            {
                await Segments.SaveInventory(trip);
                await transaction.CommitAsync();
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception))
            {
                return Conflict(new { message = "Sefer çakışması nedeniyle güncelleme yapılamadı." });
            }

            return Ok(new { message = "Sefer bilgileri güncellendi." });
        }

        [HttpPatch("{id:int}/status")]
        [Authorize(Policy = "trip.update")]
        public async Task<IActionResult> UpdateTripStatus(int id, [FromBody] UpdateTripStatusDto dto)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            var trip = await _context.Trips.FirstOrDefaultAsync(item => item.Id == id);
            if (trip == null || !CanAccessTrip(user, trip))
            {
                return NotFound(new { message = "Sefer bulunamadı." });
            }

            return StatusCode(StatusCodes.Status410Gone, new { message = "Sefer durumu kalkış ve varış saatlerine göre otomatik belirlenir. Manuel durum değişikliği kapalıdır." });
        }

        [HttpPost("{id:int}/cancel")]
        [Authorize(Policy = "trip.update")]
        public async Task<IActionResult> CancelTrip(int id, [FromBody] CancelTripDto dto)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var trip = await _context.Trips.FromSqlInterpolated($"SELECT * FROM [Trips] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
                .SingleOrDefaultAsync();
            if (trip == null || !CanAccessTrip(user, trip))
            {
                return NotFound(new { message = "Sefer bulunamadı." });
            }

            if (NormalizeStatus(trip.Status) != "Scheduled")
            {
                return Conflict(new { message = "Yalnızca planlanan seferler iptal edilebilir." });
            }
            if (trip.DepartureTime <= DateTime.UtcNow)
            {
                return Conflict(new { message = "Kalkış zamanı gelmiş sefer iptal edilemez." });
            }
            if (string.IsNullOrWhiteSpace(dto.Reason))
            {
                return BadRequest(new { message = "İptal nedeni zorunludur." });
            }

            trip.Status = "Cancelled";
            trip.CancellationReason = dto.Reason.Trim();
            trip.CancelledAt = DateTime.UtcNow;
            trip.CancelledByUserId = GetCurrentUserId();
            trip.UpdatedAt = DateTime.UtcNow;
            trip.ChangeVersion++;
            var activeReservations = await _context.Reservations.Include(r => r.Payment).Where(r => r.TripId == id && r.Status == "Active").ToListAsync();
            await _context.SeatSelectionSessions.Where(s => s.TripId == id && s.PurchaseGroupId == null && s.ExpiresAt > trip.CancelledAt)
                .ExecuteUpdateAsync(update => update.SetProperty(s => s.ExpiresAt, trip.CancelledAt));
            foreach (var reservation in activeReservations)
            {
                if (reservation.Payment != null) PaymentService.RequestRefund(reservation.Payment, DateTime.UtcNow, _context);
                reservation.Status = "Cancelled";
                reservation.CancelledAt = trip.CancelledAt;
                reservation.CancellationReason = "Sefer iptal edildi: " + trip.CancellationReason;
                if (reservation.Kind == "Ticket") await Travela.Api.Notifications.TravelNotifications.Queue(_context, reservation, "Cancellation", "cancel-trip:" + trip.Id, DateTime.UtcNow, trip.CancellationReason);
            }
            await Segments.SaveInventory(trip);
            await transaction.CommitAsync();
            return Ok(new { message = "Sefer iptal edildi." });
        }

        private async Task<User?> GetCurrentUserAsync()
        {
            var id = GetCurrentUserId();
            if (!id.HasValue)
            {
                return null;
            }

            return await _context.Users
                .AsNoTracking()
                .ForSession(User, _context)
                .FirstOrDefaultAsync(user => user.Id == id.Value);
        }

        private int? GetCurrentUserId() =>
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                ? userId
                : null;

        private async Task<int?> ResolveCompanyIdAsync(User user, int? requestedCompanyId)
        {
            if (user.Role == "Company")
            {
                if (user.CompanyId.HasValue &&
                    (!requestedCompanyId.HasValue || requestedCompanyId == user.CompanyId))
                {
                    return user.CompanyId;
                }
                return null;
            }

            return user.Role == "Admin" ? requestedCompanyId : null;
        }

        private IActionResult? ValidateTripResources(
            Company? company,
            Bus? bus,
            Travela.Api.Models.Route? route,
            int companyId)
        {
            if (company == null || !company.IsActive)
            {
                return BadRequest(new { message = "Firma bulunamadı veya pasif durumda." });
            }
            if (bus == null || !bus.IsActive)
            {
                return BadRequest(new { message = "Otobüs bulunamadı veya pasif durumda." });
            }
            if (bus.CompanyId != companyId)
            {
                return BadRequest(new { message = "Seçilen otobüs bu firmaya bağlı değil." });
            }
            if (route == null || !route.IsActive)
            {
                return BadRequest(new { message = "Güzergâh bulunamadı veya pasif durumda." });
            }
            if (string.Equals(route.DepartureCity.Trim(), route.ArrivalCity.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Kalkış ve varış şehirleri aynı olamaz." });
            }

            return null;
        }

        private async Task<bool> HasBusOverlapAsync(
            int busId,
            DateTime departure,
            DateTime arrival,
            int? exceptTripId)
        {
            var query = _context.Trips.Where(trip =>
                trip.BusId == busId &&
                trip.DepartureTime < arrival &&
                trip.ArrivalTime > departure &&
                trip.Status.ToLower() != "cancelled" &&
                trip.Status.ToLower() != "canceled" &&
                trip.Status.ToLower() != "iptal" &&
                trip.Status.ToLower() != "iptal edildi");

            if (exceptTripId.HasValue)
            {
                query = query.Where(trip => trip.Id != exceptTripId.Value);
            }

            return await query.AnyAsync();
        }

        private static bool CanAccessTrip(User user, Trip trip) =>
            user.Role == "Admin" ||
            (user.Role == "Company" &&
             user.CompanyId.HasValue &&
             user.CompanyId.Value == trip.CompanyId);

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        private static string? NormalizeStatus(string? status)
        {
            return status?.Trim().ToLowerInvariant() switch
            {
                "scheduled" or "planlandı" or "planlandi" => "Scheduled",
                "inprogress" or "in progress" or "devam ediyor" => "InProgress",
                "completed" or "tamamlandı" or "tamamlandi" => "Completed",
                "cancelled" or "canceled" or "iptal" or "iptal edildi" => "Cancelled",
                _ => null
            };
        }

        private static bool IsConcurrencyConflict(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is SqlException { Number: 1205 or 2601 or 2627 or 51011 or 51012 })
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetUtcKinds(IEnumerable<TripListDto> trips)
        {
            foreach (var trip in trips)
            {
                trip.DepartureTime = AsUtc(trip.DepartureTime);
                trip.ArrivalTime = AsUtc(trip.ArrivalTime);
            }
        }

        private static void SetUtcKinds(TripDetailDto trip)
        {
            trip.DepartureTime = AsUtc(trip.DepartureTime);
            trip.ArrivalTime = AsUtc(trip.ArrivalTime);
            trip.CreatedAt = AsUtc(trip.CreatedAt);
            if (trip.UpdatedAt.HasValue) trip.UpdatedAt = AsUtc(trip.UpdatedAt.Value);
            if (trip.CancelledAt.HasValue) trip.CancelledAt = AsUtc(trip.CancelledAt.Value);
        }
    }
}
