using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Staff;
using Travela.DTOs;
using Travela.Models;

namespace Travela.Api.Controllers;

public partial class CompanyStaffController
{
    [HttpGet("{id:int}/photo")]
    public async Task<IActionResult> Photo(int id, CancellationToken ct)
    {
        var companyId = await CompanyId(ct); if (companyId == null) return Forbid();
        var photo = await db.StaffPhotos.AsNoTracking().SingleOrDefaultAsync(p => p.StaffId == id && p.CompanyId == companyId, ct);
        if (photo == null) return NotFound(new { message = "Fotoğraf bulunamadı." });
        PrivateFileHeaders();
        return File(photo.Data, photo.ContentType);
    }

    [HttpGet("{id:int}/documents/{documentId:int}/file")]
    public async Task<IActionResult> DocumentFile(int id, int documentId, [FromQuery] bool download, CancellationToken ct)
    {
        var companyId = await CompanyId(ct); if (companyId == null) return Forbid();
        var doc = await db.StaffDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId && d.StaffId == id && d.CompanyId == companyId, ct);
        if (doc == null) return NotFound(new { message = "Belge bulunamadı." });
        PrivateFileHeaders();
        return download ? File(doc.Data, doc.ContentType, doc.FileName) : File(doc.Data, doc.ContentType);
    }

    [HttpPost("{id:int}/documents"), Authorize(Policy = "trip.update"), RequestSizeLimit(12 * 1024 * 1024)]
    public Task<IActionResult> AddDocument(int id, [FromForm] StaffDocumentInputDto dto, CancellationToken ct) => SaveDocument(id, null, dto, ct);
    [HttpPut("{id:int}/documents/{documentId:int}"), Authorize(Policy = "trip.update"), RequestSizeLimit(12 * 1024 * 1024)]
    public Task<IActionResult> UpdateDocument(int id, int documentId, [FromForm] StaffDocumentInputDto dto, CancellationToken ct) => SaveDocument(id, documentId, dto, ct);

    private async Task<IActionResult> SaveDocument(int id, int? documentId, StaffDocumentInputDto dto, CancellationToken ct)
    {
        var companyId = await CompanyId(ct); if (companyId == null) return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Belge adı gereklidir." });
        if (dto.IssuedOn > StaffDates.Today(clock)) return BadRequest(new { message = "Düzenlenme tarihi gelecekte olamaz." });
        if (!dto.IsIndefinite && (!dto.ExpiresOn.HasValue || dto.ExpiresOn < dto.IssuedOn))
            return BadRequest(new { message = "Son geçerlilik tarihi gereklidir ve düzenlenme tarihinden önce olamaz." });
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Serialize uploads and deletes per staff, including creation of new documents.
        var staff = await db.CompanyStaff.FromSqlInterpolated($"SELECT * FROM CompanyStaff WITH (UPDLOCK,HOLDLOCK) WHERE Id={id} AND CompanyId={companyId.Value}").SingleOrDefaultAsync(ct);
        if (staff == null) return NotFound(new { message = "Personel bulunamadı." });
        var doc = documentId.HasValue ? await db.StaffDocuments.SingleOrDefaultAsync(d => d.Id == documentId && d.StaffId == id && d.CompanyId == companyId, ct)
            : new StaffDocument { StaffId = id, CompanyId = companyId.Value };
        if (doc == null) return NotFound(new { message = "Belge bulunamadı." });
        if (dto.File == null && !documentId.HasValue) return BadRequest(new { message = "Bir belge dosyası seçin." });
        if (dto.File != null) {
            try {
                var upload = await StaffFiles.Read(dto.File, false, ct);
                doc.Data = upload.Data; doc.ContentType = upload.ContentType; doc.FileName = upload.FileName; doc.Size = upload.Data.Length;
            } catch (ArgumentException error) { return BadRequest(new { message = error.Message }); }
        }
        doc.Name = dto.Name.Trim(); doc.DocumentType = dto.DocumentType; doc.IssuedOn = dto.IssuedOn!.Value;
        doc.IsIndefinite = dto.IsIndefinite; doc.ExpiresOn = dto.IsIndefinite ? null : dto.ExpiresOn;
        if (!documentId.HasValue) db.StaffDocuments.Add(doc);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return StatusCode(documentId.HasValue ? 200 : 201, new { doc.Id, message = "Belge kaydedildi." });
    }

    [HttpDelete("{id:int}/documents/{documentId:int}"), Authorize(Policy = "trip.update")]
    public async Task<IActionResult> DeleteDocument(int id, int documentId, [FromQuery] bool confirm, CancellationToken ct)
    {
        var companyId = await CompanyId(ct); if (companyId == null) return Forbid();
        if (!confirm) return BadRequest(new { message = "Belgeyi silmek için onay gereklidir." });
        var count = await db.StaffDocuments.Where(d => d.Id == documentId && d.StaffId == id && d.CompanyId == companyId).ExecuteDeleteAsync(ct);
        return count == 0 ? NotFound(new { message = "Belge bulunamadı." }) : Ok(new { message = "Belge silindi." });
    }

    private void PrivateFileHeaders()
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
    }
}
