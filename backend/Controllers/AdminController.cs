using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.API.DTOs;
using Travela.Api.Models;
using Travela.Data;
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
            var activeUsers = await _context.Users.CountAsync(u => u.IsActive);
            var passiveUsers = await _context.Users.CountAsync(u => !u.IsActive);
            var companyUsers = await _context.Users.CountAsync(u => u.Role == "Company");

            return Ok(new
            {
                totalCompanies,
                totalUsers,
                totalBuses,
                activeUsers,
                passiveUsers,
                companyUsers
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

        [HttpGet("companies/{id:int}")]
        public async Task<IActionResult> GetCompanyDetails(int id)
        {
            var company = await _context.Companies
                .AsNoTracking()
                .Where(c => c.Id == id)
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
                .FirstOrDefaultAsync();

            if (company == null)
            {
                return NotFound(new { message = "Firma bulunamadı." });
            }

            var users = await _context.Users
                .AsNoTracking()
                .Where(u => u.CompanyId == id)
                .OrderBy(u => u.FullName)
                .Select(u => new { u.Id, u.FullName, u.Email, u.Role, u.IsActive, u.MustChangePassword, u.CreatedAt })
                .ToListAsync();

            var buses = await _context.Buses
                .AsNoTracking()
                .Where(b => b.CompanyId == id)
                .OrderBy(b => b.PlateNumber)
                .Select(b => new { b.Id, b.PlateNumber, b.Brand, b.Model, b.SeatCapacity, b.IsActive, b.CreatedAt })
                .ToListAsync();

            return Ok(new
            {
                company.Id,
                company.Name,
                company.Email,
                company.Phone,
                company.Address,
                company.CreatedAt,
                company.IsActive,
                TotalUsers = users.Count,
                TotalBuses = buses.Count,
                Users = users,
                Buses = buses
            });
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers(
            [FromQuery] string? search,
            [FromQuery] string? role,
            [FromQuery] int? companyId,
            [FromQuery] string? status,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? sortBy = "createdAt",
            [FromQuery] string? sortDirection = "desc",
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;

            var query = _context.Users
                .AsNoTracking()
                .Select(u => new UserListDto
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    Role = u.Role,
                    CompanyId = u.CompanyId,
                    CompanyName = u.CompanyId.HasValue
                        ? _context.Companies.Where(c => c.Id == u.CompanyId.Value).Select(c => c.Name).FirstOrDefault()
                        : null,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    LastLoginAt = u.LastLoginAt,
                    UpdatedAt = u.UpdatedAt
                });

            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim();
                query = query.Where(u => u.FullName.Contains(keyword) || u.Email.Contains(keyword));
            }

            if (!string.IsNullOrWhiteSpace(role) && !role.Equals("Tümü", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(u => u.Role == role);
            }

            if (companyId.HasValue)
            {
                query = query.Where(u => u.CompanyId == companyId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                bool isActive = status.Equals("Aktif", StringComparison.OrdinalIgnoreCase) || status.Equals("Active", StringComparison.OrdinalIgnoreCase);
                query = query.Where(u => u.IsActive == isActive);
            }

            if (startDate.HasValue)
            {
                query = query.Where(u => u.CreatedAt >= startDate.Value.Date);
            }

            if (endDate.HasValue)
            {
                query = query.Where(u => u.CreatedAt <= endDate.Value.Date.AddDays(1).AddTicks(-1));
            }

            var totalCount = await query.CountAsync();

            query = sortBy?.ToLowerInvariant() switch
            {
                "name" => sortDirection == "asc"
                    ? query.OrderBy(u => u.FullName)
                    : query.OrderByDescending(u => u.FullName),
                "email" => sortDirection == "asc"
                    ? query.OrderBy(u => u.Email)
                    : query.OrderByDescending(u => u.Email),
                "createdat" => sortDirection == "asc"
                    ? query.OrderBy(u => u.CreatedAt)
                    : query.OrderByDescending(u => u.CreatedAt),
                _ => sortDirection == "asc"
                    ? query.OrderBy(u => u.CreatedAt)
                    : query.OrderByDescending(u => u.CreatedAt)
            };

            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new UserListResponseDto
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        [HttpGet("users/{id:int}")]
        public async Task<IActionResult> GetUserById(int id)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == id)
                .Select(u => new UserDetailDto
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    Role = u.Role,
                    CompanyId = u.CompanyId,
                    CompanyName = u.CompanyId.HasValue
                        ? _context.Companies.Where(c => c.Id == u.CompanyId.Value).Select(c => c.Name).FirstOrDefault()
                        : null,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    LastLoginAt = u.LastLoginAt,
                    UpdatedAt = u.UpdatedAt
                })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                return NotFound(new { message = "Kullanıcı bulunamadı." });
            }

            return Ok(user);
        }

        [HttpPost("users")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.FullName) || string.IsNullOrWhiteSpace(dto.Email))
            {
                return BadRequest(new { message = "Ad soyad ve e-posta zorunludur." });
            }

            var fullName = dto.FullName.Trim();
            var email = dto.Email.Trim();
            var role = dto.Role?.Trim();

            if (role != "Admin" && role != "Company" && role != "Passenger")
            {
                return BadRequest(new { message = "Geçersiz kullanıcı rolü." });
            }

            if (!IsValidEmail(email))
            {
                return BadRequest(new { message = "E-posta formatı geçersiz." });
            }

            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower()))
            {
                return Conflict(new { message = "Bu e-posta adresi zaten kayıtlı." });
            }

            int? companyId = null;
            if (role == "Company")
            {
                if (!dto.CompanyId.HasValue)
                {
                    return BadRequest(new { message = "Company rolünde firma seçimi zorunludur." });
                }

                var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == dto.CompanyId.Value);
                if (company == null || !company.IsActive)
                {
                    return BadRequest(new { message = "Seçilen firma bulunamadı veya pasif durumda." });
                }

                companyId = dto.CompanyId.Value;
            }
            else if (dto.CompanyId.HasValue)
            {
                return BadRequest(new { message = "Passenger veya Admin rolünde firma ataması yapılmaz." });
            }

            var user = new User
            {
                FullName = fullName,
                Email = email,
                Role = role,
                CompanyId = companyId,
                IsActive = dto.IsActive,
                MustChangePassword = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                SecurityVersion = 1
            };

            var activationToken = string.Empty;
            if (!string.IsNullOrWhiteSpace(dto.Password))
            {
                if (dto.Password.Length < 8)
                {
                    return BadRequest(new { message = "Şifre en az 8 karakter olmalıdır." });
                }

                var passwordHasher = new PasswordHasher<User>();
                user.PasswordHash = passwordHasher.HashPassword(user, dto.Password);
                user.IsActive = true;
                user.MustChangePassword = role == "Company";
            }
            else
            {
                user.IsActive = false;
                activationToken = CreateSecureToken();
                user.PasswordResetTokenHash = HashToken(activationToken);
                user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddDays(2);
            }

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return StatusCode(201, new
            {
                message = user.IsActive ? "Kullanıcı başarıyla oluşturuldu." : "Kullanıcı oluşturuldu. Hesap aktivasyonu için doğrulama tokenı hazırlandı.",
                user = new
                {
                    user.Id,
                    user.FullName,
                    user.Email,
                    user.Role,
                    user.CompanyId,
                    user.IsActive,
                    user.CreatedAt
                },
                activationToken
            });
        }

        [HttpPut("users/{id:int}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
            {
                return NotFound(new { message = "Kullanıcı bulunamadı." });
            }

            if (string.IsNullOrWhiteSpace(dto.FullName) || string.IsNullOrWhiteSpace(dto.Email))
            {
                return BadRequest(new { message = "Ad soyad ve e-posta zorunludur." });
            }

            if (!IsValidEmail(dto.Email))
            {
                return BadRequest(new { message = "E-posta formatı geçersiz." });
            }

            var normalizedEmail = dto.Email.Trim();
            if (await _context.Users.AnyAsync(u => u.Id != id && u.Email.ToLower() == normalizedEmail.ToLower()))
            {
                return Conflict(new { message = "Bu e-posta adresi başka bir kullanıcıya ait." });
            }

            var role = dto.Role?.Trim();
            if (role != "Admin" && role != "Company" && role != "Passenger")
            {
                return BadRequest(new { message = "Geçersiz kullanıcı rolü." });
            }

            if (role == "Company")
            {
                if (!dto.CompanyId.HasValue)
                {
                    return BadRequest(new { message = "Company rolünde firma seçimi zorunludur." });
                }

                var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == dto.CompanyId.Value);
                if (company == null || !company.IsActive)
                {
                    return BadRequest(new { message = "Seçilen firma bulunamadı veya pasif durumda." });
                }

                user.CompanyId = dto.CompanyId.Value;
            }
            else
            {
                user.CompanyId = null;
            }

            if (role == "Admin")
            {
                user.CompanyId = null;
            }

            user.FullName = dto.FullName.Trim();
            user.Email = normalizedEmail;
            user.Role = role;
            user.UpdatedAt = DateTime.UtcNow;
            user.SecurityVersion += 1;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Kullanıcı bilgileri güncellendi." });
        }

        [HttpPatch("users/{id:int}/status")]
        public async Task<IActionResult> UpdateUserStatus(int id, [FromBody] UpdateUserStatusDto dto)
        {
            var currentUserId = GetCurrentUserId();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
            {
                return NotFound(new { message = "Kullanıcı bulunamadı." });
            }

            if (user.Role == "Admin" && currentUserId == user.Id)
            {
                return BadRequest(new { message = "Kendi hesabınızı pasifleştiremezsiniz." });
            }

            if (user.Role == "Admin" && user.IsActive && !dto.IsActive)
            {
                var activeAdmins = await _context.Users.CountAsync(u => u.Role == "Admin" && u.IsActive && u.Id != user.Id);
                if (activeAdmins == 0)
                {
                    return Conflict(new { message = "Sistemde başka aktif Admin bulunmadığı için son aktif Admin pasifleştirilemez." });
                }
            }

            user.IsActive = dto.IsActive;
            user.UpdatedAt = DateTime.UtcNow;
            user.SecurityVersion += 1;

            if (!dto.IsActive)
            {
                user.PasswordResetTokenHash = null;
                user.PasswordResetTokenExpiresAt = null;
                user.PasswordResetTokenUsedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = dto.IsActive ? "Hesap aktifleştirildi." : "Hesap pasifleştirildi." });
        }

        [HttpPatch("users/{id:int}/role")]
        public async Task<IActionResult> UpdateUserRole(int id, [FromBody] UpdateUserRoleDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
            {
                return NotFound(new { message = "Kullanıcı bulunamadı." });
            }

            var role = dto.Role?.Trim();
            if (role != "Admin" && role != "Company" && role != "Passenger")
            {
                return BadRequest(new { message = "Geçersiz kullanıcı rolü." });
            }

            if (user.Role == "Admin" && role != "Admin")
            {
                var activeAdmins = await _context.Users.CountAsync(u => u.Role == "Admin" && u.IsActive && u.Id != user.Id);
                if (activeAdmins == 0)
                {
                    return Conflict(new { message = "Son aktif Admin hesabının rolü değiştirilemez." });
                }
            }

            if (role == "Company")
            {
                if (!dto.CompanyId.HasValue)
                {
                    return BadRequest(new { message = "Company rolünde firma seçimi zorunludur." });
                }

                var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == dto.CompanyId.Value);
                if (company == null || !company.IsActive)
                {
                    return BadRequest(new { message = "Seçilen firma bulunamadı veya pasif durumda." });
                }

                user.CompanyId = dto.CompanyId.Value;
            }
            else
            {
                user.CompanyId = null;
            }

            user.Role = role;
            user.UpdatedAt = DateTime.UtcNow;
            user.SecurityVersion += 1;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Kullanıcı rolü güncellendi." });
        }

        [HttpPost("users/{id:int}/password-reset")]
        public async Task<IActionResult> StartPasswordReset(int id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
            {
                return NotFound(new { message = "Kullanıcı bulunamadı." });
            }

            var resetToken = CreateSecureToken();
            user.PasswordResetTokenHash = HashToken(resetToken);
            user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(24);
            user.PasswordResetTokenUsedAt = null;
            user.UpdatedAt = DateTime.UtcNow;
            user.IsActive = false;
            user.SecurityVersion += 1;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Şifre sıfırlama süreci başlatıldı.",
                resetToken,
                expiresAt = user.PasswordResetTokenExpiresAt
            });
        }

        [HttpPost("companies/{companyId:int}/users")]
        public async Task<IActionResult> CreateCompanyUser(int companyId, [FromBody] CreateCompanyUserDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Kullanıcı bilgileri gerekli." });
            }

            if (string.IsNullOrWhiteSpace(dto.FullName) || string.IsNullOrWhiteSpace(dto.Email))
            {
                return BadRequest(new { message = "Ad soyad ve e-posta zorunludur." });
            }

            if (dto.TemporaryPassword.Length < 8 || dto.ConfirmPassword.Length < 8)
            {
                return BadRequest(new { message = "Geçici şifre en az 8 karakter olmalıdır." });
            }

            if (!string.Equals(dto.TemporaryPassword, dto.ConfirmPassword, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Geçici şifre ile tekrarı eşleşmiyor." });
            }

            if (!IsValidEmail(dto.Email.Trim()))
            {
                return BadRequest(new { message = "E-posta formatı geçersiz." });
            }

            var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == companyId);
            if (company == null)
            {
                return NotFound(new { message = "Firma bulunamadı." });
            }

            if (!company.IsActive)
            {
                return BadRequest(new { message = "Pasif firma için kullanıcı oluşturulamaz." });
            }

            var email = dto.Email.Trim();
            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower()))
            {
                return Conflict(new { message = "Bu e-posta adresi başka bir hesaba ait." });
            }

            var passwordHasher = new PasswordHasher<User>();
            var user = new User
            {
                FullName = dto.FullName.Trim(),
                Email = email,
                Role = "Company",
                CompanyId = companyId,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                SecurityVersion = 1
            };

            user.PasswordHash = passwordHasher.HashPassword(user, dto.TemporaryPassword);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return StatusCode(201, new
            {
                message = "Firma kullanıcısı oluşturuldu. Kullanıcı ilk girişte şifresini değiştirmelidir.",
                user = new
                {
                    user.Id,
                    user.FullName,
                    user.Email,
                    user.Role,
                    user.CompanyId,
                    user.IsActive,
                    user.CreatedAt,
                    user.MustChangePassword
                }
            });
        }

        [HttpPost("companies")]
        public async Task<IActionResult> CreateCompany([FromBody] CreateCompanyDto dto)
        {
            string companyName = dto.Name.Trim();
            if (string.IsNullOrWhiteSpace(companyName))
            {
                return BadRequest(new { message = "Firma adı boş olamaz." });
            }

            bool companyExists = await _context.Companies.AnyAsync(c => c.Name == companyName);
            if (companyExists)
            {
                return Conflict(new { message = "Bu isimde bir firma zaten kayıtlı." });
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

            return CreatedAtAction(nameof(GetCompanies), new { id = company.Id }, new
            {
                company.Id,
                company.Name,
                company.Email,
                company.Phone,
                company.Address,
                company.CreatedAt,
                company.IsActive
            });
        }

        [HttpPut("companies/{id:int}")]
        public async Task<IActionResult> UpdateCompany(int id, [FromBody] UpdateCompanyDto dto)
        {
            string companyName = dto.Name?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(companyName))
            {
                return BadRequest(new { message = "Firma adı boş olamaz." });
            }

            var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == id);
            if (company == null)
            {
                return NotFound(new { message = "Firma bulunamadı." });
            }

            bool nameExists = await _context.Companies.AnyAsync(c => c.Id != id && c.Name == companyName);
            if (nameExists)
            {
                return Conflict(new { message = "Bu isimde başka bir firma zaten kayıtlı." });
            }

            company.Name = companyName;
            company.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            company.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
            company.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();
            if (dto.IsActive.HasValue)
            {
                company.IsActive = dto.IsActive.Value;
            }

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

        [HttpPatch("companies/{id:int}/status")]
        public async Task<IActionResult> UpdateCompanyStatus(int id)
        {
            var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == id);
            if (company == null)
            {
                return NotFound(new { message = "Firma bulunamadı." });
            }

            company.IsActive = !company.IsActive;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = company.IsActive ? "Firma başarıyla aktif edildi." : "Firma başarıyla pasif edildi.",
                company.Id,
                company.Name,
                company.IsActive
            });
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null && int.TryParse(claim.Value, out var userId) ? userId : 0;
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                var address = new System.Net.Mail.MailAddress(email);
                return address.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private static string CreateSecureToken()
        {
            var randomBytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(randomBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string HashToken(string token)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }

        private static string CreateResetTokenFromStoredHash(User user)
        {
            if (string.IsNullOrEmpty(user.PasswordResetTokenHash))
            {
                return string.Empty;
            }

            var token = CreateSecureToken();
            return HashToken(token) == user.PasswordResetTokenHash ? token : token;
        }
    }
}