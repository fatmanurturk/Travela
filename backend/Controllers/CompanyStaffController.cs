using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Security;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

namespace Travela.Api.Controllers;

[ApiController, Route("api/company/staff"), Authorize(Roles = "Company", Policy = "trip.view")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CompanyStaffController(TravelaDbContext db) : ControllerBase
{
    private async Task<int?> CompanyId(CancellationToken ct) => (await db.Users.AsNoTracking().ForSession(User, db)
        .SingleOrDefaultAsync(u => u.Role == "Company", ct))?.CompanyId;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? duty, [FromQuery] bool? isActive, CancellationToken ct)
    {
        var companyId = await CompanyId(ct); if (companyId == null) return Forbid();
        if (duty != null && duty is not ("Driver" or "Attendant")) return BadRequest(new { message = "Geçerli bir görev seçin." });
        var query = db.CompanyStaff.AsNoTracking().Where(s => s.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(s => EF.Functions.Collate(s.FullName, "Turkish_100_CI_AI").Contains(search.Trim()));
        if (duty != null) query = query.Where(s => s.Duty == duty);
        if (isActive.HasValue) query = query.Where(s => s.IsActive == isActive);
        return Ok(await query.OrderBy(s => EF.Functions.Collate(s.FullName, "Turkish_100_CI_AS")).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.FullName, s.Duty, s.Phone, s.IsActive }).ToListAsync(ct));
    }

    [HttpPost, Authorize(Policy = "trip.create")]
    public Task<IActionResult> Create(StaffInputDto dto, CancellationToken ct) => Save(null, dto, ct);

    [HttpPut("{id:int}"), Authorize(Policy = "trip.update")]
    public Task<IActionResult> Update(int id, StaffInputDto dto, CancellationToken ct) => Save(id, dto, ct);

    private async Task<IActionResult> Save(int? id, StaffInputDto dto, CancellationToken ct)
    {
        var companyId = await CompanyId(ct); if (companyId == null) return Forbid();
        var fullName = dto.FullName.Trim();
        if (fullName.Length < 2) return BadRequest(new { message = "Ad soyad en az iki karakter olmalıdır." });
        var phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : new string(dto.Phone.Where(c => !char.IsWhiteSpace(c) && c != '(' && c != ')' && c != '-').ToArray());
        if (phone != null && !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?[0-9]{10,15}$"))
            return BadRequest(new { message = "Geçerli bir telefon numarası girin veya alanı boş bırakın." });
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var staff = id.HasValue ? await db.CompanyStaff.FromSqlInterpolated($"SELECT * FROM CompanyStaff WITH (UPDLOCK,HOLDLOCK) WHERE Id={id.Value} AND CompanyId={companyId.Value}").SingleOrDefaultAsync(ct) : new CompanyStaff { CompanyId = companyId.Value };
        if (staff == null) return NotFound(new { message = "Personel bulunamadı." });
        staff.FullName = fullName; staff.Phone = phone; staff.Duty = dto.Duty; staff.IsActive = dto.IsActive;
        if (!id.HasValue) db.CompanyStaff.Add(staff);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return StatusCode(id.HasValue ? 200 : 201, new { staff.Id, staff.FullName, staff.Duty, staff.Phone, staff.IsActive, message = "Personel kaydedildi." });
    }
}
