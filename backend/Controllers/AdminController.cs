using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Data;
using Travela.API.DTOs;
using Travela.Api.Models;
using Microsoft.AspNetCore.Identity;
using Travela.DTOs;
using Travela.Models;

namespace Travela.API.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly TravelaDbContext _context;

        public AdminController(TravelaDbContext context)
        {
            _context = context;
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var totalCompanies = await _context.Companies.CountAsync();
            var totalUsers = await _context.Users.CountAsync();
            var totalBuses = await _context.Buses.CountAsync();

            return Ok(new
            {
                totalCompanies,
                totalUsers,
                totalBuses
            });
        }

        [HttpGet("companies")]
public async Task<IActionResult> GetCompanies()
{
    var companies = await _context.Companies
        .AsNoTracking()
        .OrderBy(c => c.Name)
        .Select(c => new
        {
            c.Id,
            c.Name,
            c.Email,
            c.Phone,
            c.Address,
            c.CreatedAt,
            c.IsActive
        })
        .ToListAsync();

    return Ok(companies);
}


[HttpGet("users")]
public async Task<IActionResult> GetUsers()
{
    var users = await _context.Users
        .AsNoTracking()
        .GroupJoin(
            _context.Companies,
            user => user.CompanyId,
            company => (int?)company.Id,
            (user, companies) => new
            {
                user,
                companies
            }
        )
        .SelectMany(
            x => x.companies.DefaultIfEmpty(),
            (x, company) => new
            {
                x.user.Id,
                x.user.FullName,
                x.user.Email,
                x.user.Role,
                x.user.CompanyId,
                CompanyName = company != null ? company.Name : null,
                x.user.CreatedAt
            }
        )
        .OrderBy(u => u.FullName)
        .ToListAsync();

    return Ok(users);
}


[HttpPost("users")]
public async Task<IActionResult> CreateUser(
    [FromBody] CreateUserDto dto)
{
    string fullName = dto.FullName?.Trim() ?? "";
    string email = dto.Email?.Trim().ToLowerInvariant() ?? "";
    string role = dto.Role?.Trim() ?? "";

    // Zorunlu alan kontrolü
    if (string.IsNullOrWhiteSpace(fullName) ||
        string.IsNullOrWhiteSpace(email))
    {
        return BadRequest(new
        {
            message = "Ad soyad ve e-posta zorunludur."
        });
    }

    // Yalnızca sistemde tanımlı roller kullanılabilir
    if (role != "Admin" &&
        role != "Company" &&
        role != "Passenger")
    {
        return BadRequest(new
        {
            message = "Geçersiz kullanıcı rolü."
        });
    }

    // E-posta adresi daha önce kullanılmış mı?
    bool emailExists = await _context.Users
        .AnyAsync(u => u.Email == email);

    if (emailExists)
    {
        return Conflict(new
        {
            message = "Bu e-posta adresi zaten kayıtlı."
        });
    }

    int? companyId = null;

    // Firma rolündeki kullanıcı mutlaka bir firmaya bağlı olmalı
    if (role == "Company")
    {
        if (!dto.CompanyId.HasValue)
        {
            return BadRequest(new
            {
                message = "Firma kullanıcısı için firma seçimi zorunludur."
            });
        }

        bool companyExists = await _context.Companies
            .AnyAsync(c =>
                c.Id == dto.CompanyId.Value &&
                c.IsActive);

        if (!companyExists)
        {
            return BadRequest(new
            {
                message = "Seçilen firma bulunamadı veya aktif değil."
            });
        }

        companyId = dto.CompanyId.Value;
    }
    else if (dto.CompanyId.HasValue)
    {
        return BadRequest(new
        {
            message = "Yalnızca Company rolündeki kullanıcıya firma atanabilir."
        });
    }

    // Kullanıcı oluştur
    var user = new User
    {
        FullName = fullName,
        Email = email,
        Role = role,
        CompanyId = companyId,
        CreatedAt = DateTime.UtcNow
    };

    // Şifreyi hash'leyerek sakla
    var passwordHasher = new PasswordHasher<User>();

    user.PasswordHash = passwordHasher.HashPassword(
        user,
        dto.Password
    );

    _context.Users.Add(user);
    await _context.SaveChangesAsync();

    // Şifre veya PasswordHash yanıt içinde gönderilmez
    return StatusCode(201, new
    {
        message = "Kullanıcı başarıyla oluşturuldu.",
        user = new
        {
            user.Id,
            user.FullName,
            user.Email,
            user.Role,
            user.CompanyId,
            user.CreatedAt
        }
    });
}


[HttpPost("companies")]
public async Task<IActionResult> CreateCompany(
    [FromBody] CreateCompanyDto dto)
{
    string companyName = dto.Name.Trim();

    if (string.IsNullOrWhiteSpace(companyName))
    {
        return BadRequest("Firma adı boş olamaz.");
    }

    bool companyExists = await _context.Companies
        .AnyAsync(c => c.Name == companyName);

    if (companyExists)
    {
        return Conflict("Bu isimde bir firma zaten kayıtlı.");
    }

    var company = new Company
    {
        Name = companyName,
        Email = dto.Email?.Trim(),
        Phone = dto.Phone?.Trim(),
        Address = dto.Address?.Trim(),
        CreatedAt = DateTime.UtcNow,
        IsActive = true
    };

    _context.Companies.Add(company);

    await _context.SaveChangesAsync();

    return CreatedAtAction(
        nameof(GetCompanies),
        new { id = company.Id },
        new
        {
            company.Id,
            company.Name,
            company.Email,
            company.Phone,
            company.Address,
            company.CreatedAt,
            company.IsActive
        }
    );
}

[HttpPut("companies/{id:int}")]
public async Task<IActionResult> UpdateCompany(
    int id,
    [FromBody] UpdateCompanyDto dto)
{
    string companyName = dto.Name?.Trim() ?? "";

    if (string.IsNullOrWhiteSpace(companyName))
    {
        return BadRequest(new
        {
            message = "Firma adı boş olamaz."
        });
    }

    var company = await _context.Companies
        .FirstOrDefaultAsync(c => c.Id == id);

    if (company == null)
    {
        return NotFound(new
        {
            message = "Firma bulunamadı."
        });
    }

    bool nameExists = await _context.Companies
        .AnyAsync(c =>
            c.Id != id &&
            c.Name == companyName);

    if (nameExists)
    {
        return Conflict(new
        {
            message = "Bu isimde başka bir firma zaten kayıtlı."
        });
    }

    company.Name = companyName;
    company.Email = string.IsNullOrWhiteSpace(dto.Email)
        ? null : dto.Email.Trim();
    company.Phone = string.IsNullOrWhiteSpace(dto.Phone)
        ? null : dto.Phone.Trim();
    company.Address = string.IsNullOrWhiteSpace(dto.Address)
        ? null : dto.Address.Trim();

    await _context.SaveChangesAsync();

    return Ok(new
    {
        message = "Firma bilgileri başarıyla güncellendi.",
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
    }
}