using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Security;
using Travela.Api.Staff;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

namespace Travela.Api.Controllers;

[ApiController, Route("api/company/staff"), Authorize(Roles = "Company", Policy = "trip.view")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public partial class CompanyStaffController(TravelaDbContext db, TimeProvider clock) : ControllerBase
{
    private async Task<int?> CompanyId(CancellationToken ct) => (await db.Users.AsNoTracking().ForSession(User, db)
        .SingleOrDefaultAsync(u => u.Role == "Company", ct))?.CompanyId;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? duty, [FromQuery] bool? isActive,
        [FromQuery] string? documentStatus, CancellationToken ct)
    {
        var companyId = await CompanyId(ct); if (companyId == null) return Forbid();
        if (duty != null && duty is not ("Driver" or "Attendant")) return BadRequest(new { message = "Geçerli bir görev seçin." });
        if (documentStatus != null && documentStatus is not ("Expiring" or "Expired")) return BadRequest(new { message = "Geçerli bir belge durumu seçin." });
        var today = StaffDates.Today(clock); var limit = today.AddDays(30);
        var query = db.CompanyStaff.AsNoTracking().Where(s => s.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(s => EF.Functions.Collate(s.FullName, "Turkish_100_CI_AI").Contains(search.Trim()));
        if (duty != null) query = query.Where(s => s.Duty == duty);
        if (isActive.HasValue) query = query.Where(s => s.IsActive == isActive);
        if (documentStatus == "Expired") query = query.Where(s => db.StaffDocuments.Any(d => d.StaffId == s.Id && !d.IsIndefinite && d.ExpiresOn < today));
        if (documentStatus == "Expiring") query = query.Where(s => db.StaffDocuments.Any(d => d.StaffId == s.Id && !d.IsIndefinite && d.ExpiresOn >= today && d.ExpiresOn <= limit));
        var people = await query.OrderBy(s => EF.Functions.Collate(s.FullName, "Turkish_100_CI_AS")).ThenBy(s => s.Id).ToListAsync(ct);
        var ids = people.Select(s => s.Id).ToArray();
        var photos = await db.StaffPhotos.Where(p => p.CompanyId == companyId && ids.Contains(p.StaffId)).Select(p => p.StaffId).ToListAsync(ct);
        var docs = await DocumentMetadata(db.StaffDocuments.AsNoTracking().Where(d => d.CompanyId == companyId && ids.Contains(d.StaffId))).ToListAsync(ct);
        return Ok(people.Select(s => Profile(s, photos.Contains(s.Id), docs.Where(d => d.StaffId == s.Id), today)));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var companyId = await CompanyId(ct); if (companyId == null) return Forbid();
        var staff = await db.CompanyStaff.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId, ct);
        if (staff == null) return NotFound(new { message = "Personel bulunamadı." });
        var docs = await DocumentMetadata(db.StaffDocuments.AsNoTracking().Where(d => d.StaffId == id && d.CompanyId == companyId)).ToListAsync(ct);
        return Ok(Profile(staff, await db.StaffPhotos.AnyAsync(p => p.StaffId == id && p.CompanyId == companyId, ct), docs, StaffDates.Today(clock)));
    }

    // Keep the original JSON contract for crew/legacy clients; omitted profile fields are preserved.
    [HttpPost, Authorize(Policy = "trip.create")]
    public Task<IActionResult> Create(StaffInputDto dto, CancellationToken ct) => Save(null, dto, null, null, false, ct);
    [HttpPut("{id:int}"), Authorize(Policy = "trip.update")]
    public Task<IActionResult> Update(int id, StaffInputDto dto, CancellationToken ct) => Save(id, dto, null, null, false, ct);

    [HttpPost("profile"), Authorize(Policy = "trip.create"), RequestSizeLimit(6 * 1024 * 1024)]
    public Task<IActionResult> CreateProfile([FromForm] string profile, [FromForm] IFormFile? photo, CancellationToken ct)
        => SaveProfile(null, profile, photo, false, ct);
    [HttpPut("{id:int}/profile"), Authorize(Policy = "trip.update"), RequestSizeLimit(6 * 1024 * 1024)]
    public Task<IActionResult> UpdateProfile(int id, [FromForm] string profile, [FromForm] IFormFile? photo, [FromForm] bool removePhoto, CancellationToken ct)
        => SaveProfile(id, profile, photo, removePhoto, ct);

    private async Task<IActionResult> SaveProfile(int? id, string json, IFormFile? photo, bool removePhoto, CancellationToken ct)
    {
        StaffProfileInputDto? dto;
        try { dto = JsonSerializer.Deserialize<StaffProfileInputDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return BadRequest(new { message = "Profil alanları geçerli biçimde gönderilmelidir." }); }
        if (dto == null) return BadRequest(new { message = "Profil bilgileri gereklidir." });
        if (!TryValidateModel(dto)) return ValidationProblem(ModelState);
        var today = StaffDates.Today(clock);
        if (dto.CareerStartYear > today.Year || dto.EmploymentStartDate > today)
            return BadRequest(new { message = "Mesleğe başlama yılı ve işe başlama tarihi gelecekte olamaz." });
        if (dto.Languages.Concat(dto.LicenseClasses).Concat(dto.VehicleTypes).Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 80))
            return BadRequest(new { message = "Dil, ehliyet ve araç türü seçimleri geçersiz." });
        if (!TryPhone(dto.EmergencyContactPhone, out var emergencyPhone)) return BadRequest(new { message = "Acil durum telefonu geçersiz." });
        dto.EmergencyContactPhone = emergencyPhone;
        return await Save(id, dto, dto, photo, removePhoto, ct);
    }

    private async Task<IActionResult> Save(int? id, StaffInputDto dto, StaffProfileInputDto? profile, IFormFile? photo, bool removePhoto, CancellationToken ct)
    {
        var companyId = await CompanyId(ct); if (companyId == null) return Forbid();
        var fullName = dto.FullName.Trim();
        if (fullName.Length < 2) return BadRequest(new { message = "Ad soyad en az iki karakter olmalıdır." });
        if (!TryPhone(dto.Phone, out var phone)) return BadRequest(new { message = "Geçerli bir telefon numarası girin veya alanı boş bırakın." });
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var staff = id.HasValue ? await db.CompanyStaff.FromSqlInterpolated($"SELECT * FROM CompanyStaff WITH (UPDLOCK,HOLDLOCK) WHERE Id={id.Value} AND CompanyId={companyId.Value}").SingleOrDefaultAsync(ct) : new CompanyStaff { CompanyId = companyId.Value };
        if (staff == null) return NotFound(new { message = "Personel bulunamadı." });
        (byte[] Data, string ContentType, string FileName)? upload = null;
        if (photo != null) {
            try { upload = await StaffFiles.Read(photo, true, ct); }
            catch (ArgumentException error) { return BadRequest(new { message = error.Message }); }
        }
        staff.FullName = fullName; staff.Phone = phone; staff.Duty = dto.Duty; staff.IsActive = dto.IsActive;
        if (profile != null) {
            staff.CareerStartYear = profile.CareerStartYear; staff.EmploymentStartDate = profile.EmploymentStartDate;
            staff.EmergencyContactName = Clean(profile.EmergencyContactName); staff.EmergencyContactRelationship = Clean(profile.EmergencyContactRelationship);
            staff.EmergencyContactPhone = profile.EmergencyContactPhone; staff.Languages = Choices(profile.Languages);
            staff.LicenseClasses = Choices(profile.LicenseClasses); staff.VehicleTypes = Choices(profile.VehicleTypes);
            staff.ServiceExperience = profile.ServiceExperience; staff.ServiceExperienceNotes = Clean(profile.ServiceExperienceNotes);
            staff.BaggageExperience = profile.BaggageExperience; staff.BaggageExperienceNotes = Clean(profile.BaggageExperienceNotes);
            staff.PassengerCommunicationExperience = profile.PassengerCommunicationExperience;
            staff.PassengerCommunicationExperienceNotes = Clean(profile.PassengerCommunicationExperienceNotes);
        }
        if (!id.HasValue) db.CompanyStaff.Add(staff);
        await db.SaveChangesAsync(ct);
        if (upload.HasValue || removePhoto) {
            var stored = await db.StaffPhotos.SingleOrDefaultAsync(p => p.StaffId == staff.Id && p.CompanyId == companyId, ct);
            if (upload.HasValue) {
                if (stored == null) { stored = new StaffPhoto { StaffId = staff.Id, CompanyId = companyId.Value }; db.StaffPhotos.Add(stored); }
                stored.Data = upload.Value.Data; stored.ContentType = upload.Value.ContentType;
            } else if (stored != null) db.StaffPhotos.Remove(stored);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return StatusCode(id.HasValue ? 200 : 201, new { staff.Id, staff.FullName, staff.Duty, staff.Phone, staff.IsActive, message = "Personel kaydedildi." });
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string[] Choices(string[] values) => values.Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static bool TryPhone(string? value, out string? phone) {
        phone = string.IsNullOrWhiteSpace(value) ? null : new string(value.Where(c => !char.IsWhiteSpace(c) && c != '(' && c != ')' && c != '-').ToArray());
        return phone == null || System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?[0-9]{10,15}$");
    }
    private record DocumentInfo(int Id, int StaffId, string Name, string DocumentType, DateOnly? IssuedOn, DateOnly? ExpiresOn, bool IsIndefinite, string FileName, string ContentType, int Size);
    private static IQueryable<DocumentInfo> DocumentMetadata(IQueryable<StaffDocument> query) => query.OrderBy(d => d.ExpiresOn).ThenBy(d => d.Id)
        .Select(d => new DocumentInfo(d.Id, d.StaffId, d.Name, d.DocumentType, d.IssuedOn, d.ExpiresOn, d.IsIndefinite, d.FileName, d.ContentType, d.Size));
    private static object Profile(CompanyStaff s, bool hasPhoto, IEnumerable<DocumentInfo> documents, DateOnly today) => new {
        s.Id, s.FullName, s.Duty, s.Phone, s.IsActive, hasPhoto, s.CareerStartYear,
        experienceYears = s.CareerStartYear.HasValue ? today.Year - s.CareerStartYear : null,
        s.EmploymentStartDate, s.EmergencyContactName, s.EmergencyContactRelationship, s.EmergencyContactPhone, s.Languages,
        s.LicenseClasses, s.VehicleTypes, s.ServiceExperience, s.ServiceExperienceNotes, s.BaggageExperience, s.BaggageExperienceNotes,
        s.PassengerCommunicationExperience, s.PassengerCommunicationExperienceNotes,
        documents = documents.Select(d => new { d.Id, d.Name, d.DocumentType, d.IssuedOn, d.ExpiresOn, d.IsIndefinite, d.FileName, d.ContentType, d.Size,
            validity = StaffDates.Validity(d.ExpiresOn, d.IsIndefinite, today) })
    };
}
