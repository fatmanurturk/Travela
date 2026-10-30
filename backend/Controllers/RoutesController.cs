using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

namespace Travela.Api.Controllers
{
    [ApiController]
    [Route("api/routes")]
    [Authorize(Policy = "trip.view")]
    public class RoutesController : ControllerBase
    {
        private readonly TravelaDbContext _context;

        public RoutesController(TravelaDbContext context)
        {
            _context = context;
        }

        [HttpGet("public")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPublicRoutes()
        {
            var routes = await _context.Routes
                .AsNoTracking()
                .Where(route => route.IsActive)
                .OrderBy(route => route.DepartureCity)
                .ThenBy(route => route.ArrivalCity)
                .Select(route => new RouteDto
                {
                    Id = route.Id,
                    DepartureCity = route.DepartureCity,
                    ArrivalCity = route.ArrivalCity,
                    DistanceKm = route.DistanceKm,
                    EstimatedDurationMinutes = route.EstimatedDurationMinutes
                })
                .ToListAsync();

            return Ok(routes);
        }

        [HttpGet]
        public async Task<IActionResult> GetRoutes()
        {
            var user = await GetCurrentUserAsync();
            if (user?.Role is not ("Admin" or "Company"))
            {
                return Forbid();
            }

            var routes = await _context.Routes
                .AsNoTracking()
                .Where(route => route.IsActive)
                .OrderBy(route => route.DepartureCity)
                .ThenBy(route => route.ArrivalCity)
                .Select(route => new RouteDto
                {
                    Id = route.Id,
                    DepartureCity = route.DepartureCity,
                    ArrivalCity = route.ArrivalCity,
                    DistanceKm = route.DistanceKm,
                    EstimatedDurationMinutes = route.EstimatedDurationMinutes
                })
                .ToListAsync();

            return Ok(routes);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateRoute([FromBody] CreateRouteDto dto)
        {
            var departure = dto.DepartureCity.Trim();
            var arrival = dto.ArrivalCity.Trim();
            if (string.IsNullOrWhiteSpace(departure) || string.IsNullOrWhiteSpace(arrival))
            {
                return BadRequest(new { message = "Kalkış ve varış şehirleri zorunludur." });
            }
            if (string.Equals(departure, arrival, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Kalkış ve varış şehirleri aynı olamaz." });
            }
            if (dto.DistanceKm < 0 || dto.EstimatedDurationMinutes <= 0)
            {
                return BadRequest(new { message = "Mesafe negatif olamaz, tahmini süre sıfırdan büyük olmalıdır." });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var departureKey = departure.ToLower();
            var arrivalKey = arrival.ToLower();
            bool exists;
            try
            {
                exists = await _context.Routes.AnyAsync(route =>
                    route.DepartureCity.Trim().ToLower() == departureKey &&
                    route.ArrivalCity.Trim().ToLower() == arrivalKey);
            }
            catch (Exception exception) when (IsDeadlock(exception))
            {
                return Conflict(new { message = "Eşzamanlı bir güzergâh işlemi nedeniyle kayıt doğrulanamadı. Lütfen tekrar deneyin." });
            }

            if (exists)
            {
                return Conflict(new { message = "Bu güzergâh zaten kayıtlı." });
            }

            var route = new Travela.Api.Models.Route
            {
                DepartureCity = departure,
                ArrivalCity = arrival,
                DistanceKm = dto.DistanceKm,
                EstimatedDurationMinutes = dto.EstimatedDurationMinutes,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Routes.Add(route);
            try
            {
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception exception) when (IsDeadlock(exception))
            {
                return Conflict(new { message = "Eşzamanlı bir güzergâh işlemi nedeniyle kayıt tamamlanamadı. Lütfen tekrar deneyin." });
            }

            return StatusCode(StatusCodes.Status201Created, new
            {
                message = "Güzergâh oluşturuldu.",
                route = new RouteDto
                {
                    Id = route.Id,
                    DepartureCity = route.DepartureCity,
                    ArrivalCity = route.ArrivalCity,
                    DistanceKm = route.DistanceKm,
                    EstimatedDurationMinutes = route.EstimatedDurationMinutes
                }
            });
        }

        private async Task<User?> GetCurrentUserAsync()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            {
                return null;
            }

            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(user => user.Id == id);
        }

        private static bool IsDeadlock(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is SqlException { Number: 1205 })
                {
                    return true;
                }
            }

            return false;
        }
    }
}
