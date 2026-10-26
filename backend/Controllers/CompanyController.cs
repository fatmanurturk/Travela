using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Data;
using Travela.Models;

namespace Travela.API.Controllers
{
    public class CompanyProfileUpdateDto
    {
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
    }

    [ApiController]
    [Route("api/company")]
    [Authorize(Roles = "Company")]
    public class CompanyController : ControllerBase
    {
        private readonly TravelaDbContext _context;

        public CompanyController(TravelaDbContext context)
        {
            _context = context;
        }

        [HttpGet("dashboard")]
        public IActionResult GetDashboard()
        {
            return Ok(new
            {
                Message = "Travela Firma Paneline Hoş Geldiniz!",
                Role = "Company",
                Access = "Authorized"
            });
        }

        [HttpGet("me")]
        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var currentUser = await GetCurrentCompanyUserAsync();
            if (currentUser == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            if (!currentUser.CompanyId.HasValue)
            {
                return Forbid();
            }

            var company = await _context.Companies
                .AsNoTracking()
                .Where(item => item.Id == currentUser.CompanyId.Value)
                .Select(item => new
                {
                    item.Id,
                    item.Name,
                    item.Email,
                    item.Phone,
                    item.Address,
                    item.CreatedAt,
                    item.IsActive
                })
                .FirstOrDefaultAsync();

            return company == null
                ? NotFound(new { message = "Kullanıcıya bağlı firma bulunamadı." })
                : Ok(company);
        }

        [HttpPut("me")]
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] CompanyProfileUpdateDto dto)
        {
            var currentUser = await GetCurrentCompanyUserAsync();
            if (currentUser == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            if (!currentUser.CompanyId.HasValue)
            {
                return Forbid();
            }

            if (dto == null)
            {
                return BadRequest(new { message = "Güncelleme verisi gerekli." });
            }

            var company = await _context.Companies
                .FirstOrDefaultAsync(item => item.Id == currentUser.CompanyId.Value);

            if (company == null)
            {
                return NotFound(new { message = "Kullanıcıya bağlı firma bulunamadı." });
            }

            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                var email = dto.Email.Trim();
                if (!IsValidEmail(email))
                {
                    return BadRequest(new { message = "E-posta adresi geçersiz." });
                }

                var duplicateCompany = await _context.Companies
                    .AnyAsync(item => item.Id != company.Id && item.Email != null && item.Email.ToLower() == email.ToLower());
                if (duplicateCompany)
                {
                    return Conflict(new { message = "Bu e-posta adresi başka bir firmaya ait." });
                }

                company.Email = email;
            }
            else if (dto.Email == string.Empty)
            {
                company.Email = null;
            }

            company.Phone = !string.IsNullOrWhiteSpace(dto.Phone) ? dto.Phone.Trim() : null;
            company.Address = !string.IsNullOrWhiteSpace(dto.Address) ? dto.Address.Trim() : null;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Firma bilgileri güncellendi.",
                company = new
                {
                    company.Id,
                    company.Name,
                    company.Email,
                    company.Phone,
                    company.Address,
                    company.CreatedAt,
                    company.IsActive
                }
            });
        }

        [HttpGet("me/summary")]
        public async Task<IActionResult> GetSummary()
        {
            var currentUser = await GetCurrentCompanyUserAsync();
            if (currentUser == null)
            {
                return Unauthorized(new { message = "Kullanıcı kimliği doğrulanamadı." });
            }

            if (!currentUser.CompanyId.HasValue)
            {
                return Forbid();
            }

            var companyId = currentUser.CompanyId.Value;

            var totalBuses = await _context.Buses.AsNoTracking().CountAsync(bus => bus.CompanyId == companyId);
            var activeBuses = await _context.Buses.AsNoTracking().CountAsync(bus => bus.CompanyId == companyId && bus.IsActive);
            var totalTrips = await _context.Trips.AsNoTracking().CountAsync(trip => trip.CompanyId == companyId);
           
        var activeTrips = await _context.Trips
    .AsNoTracking()
    .CountAsync(trip =>
        trip.CompanyId == companyId &&
        trip.Status != null &&
        trip.Status != "" &&
        trip.Status.ToLower() != "completed" &&
        trip.Status.ToLower() != "cancelled" &&
        trip.Status.ToLower() != "canceled"
    );
            return Ok(new
            {
                companyId,
                totalBuses,
                activeBuses,
                totalTrips,
                activeTrips
            });
        }

        private async Task<User?> GetCurrentCompanyUserAsync()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return null;
            }

            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(user => user.Id == userId && user.Role == "Company");
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email.Trim();
            }
            catch
            {
                return false;
            }
        }
    }
}