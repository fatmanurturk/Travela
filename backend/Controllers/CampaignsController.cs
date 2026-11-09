using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Payments;
using Travela.Api.Security;
using Travela.Data;
using Travela.DTOs;

namespace Travela.Api.Controllers;

[ApiController, Route("api/campaigns")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class CampaignsController(TravelaDbContext db, TimeProvider clock) : ControllerBase
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private Task<Travela.Models.User?> Current(CancellationToken ct) => db.Users.AsNoTracking().ForSession(User, db).SingleOrDefaultAsync(ct);

    [HttpGet("public"), AllowAnonymous]
    public async Task<IActionResult> Public([FromQuery] int? tripId, CancellationToken ct)
    {
        var now = Now;
        var query = db.Campaigns.AsNoTracking().Where(c => c.IsPublic && c.IsActive && c.StartsAt <= now && c.EndsAt > now &&
            db.Companies.Any(company => company.Id == c.CompanyId && company.IsActive) &&
            (c.RouteId == null || db.Routes.Any(r => r.Id == c.RouteId && r.IsActive)));
        if (tripId.HasValue)
        {
            var trip = await db.Trips.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tripId, ct);
            if (trip == null) return NotFound();
            query = query.Where(c => c.CompanyId == trip.CompanyId && (c.RouteId == null || c.RouteId == trip.RouteId) &&
                (c.DepartureFrom == null || c.DepartureFrom <= trip.DepartureTime) && (c.DepartureUntil == null || c.DepartureUntil > trip.DepartureTime));
        }
        query = query.Where(c => c.UsageLimit == null || db.PurchaseGroups.Count(g => g.CampaignId == c.Id &&
            (g.PaymentStatus == "Pending" && g.HoldExpiresAt > now || g.PaymentStatus == "Paid" && g.Items.Any(p => p.Reservation.Kind == "Ticket"))) < c.UsageLimit);
        return Ok(await query.OrderBy(c => c.EndsAt).Take(200).Select(c => new
        {
            c.Id, c.CompanyId, companyName = db.Companies.Where(company => company.Id == c.CompanyId).Select(company => company.Name).First(),
            c.RouteId, routeName = db.Routes.Where(r => r.Id == c.RouteId).Select(r => r.DepartureCity + " → " + r.ArrivalCity).FirstOrDefault(),
            c.Name, c.Description, c.Code, c.DiscountType, c.Value, c.MinimumSubtotal, c.MaximumDiscount, c.PerUserLimit,
            startsAt = DateTime.SpecifyKind(c.StartsAt, DateTimeKind.Utc), endsAt = DateTime.SpecifyKind(c.EndsAt, DateTimeKind.Utc),
            departureFrom = c.DepartureFrom == null ? (DateTime?)null : DateTime.SpecifyKind(c.DepartureFrom.Value, DateTimeKind.Utc),
            departureUntil = c.DepartureUntil == null ? (DateTime?)null : DateTime.SpecifyKind(c.DepartureUntil.Value, DateTimeKind.Utc)
        }).ToListAsync(ct));
    }

    [HttpPost("quote"), Authorize(Roles = "Passenger")]
    public async Task<IActionResult> Quote(CampaignQuoteDto dto, CancellationToken ct)
    {
        var user = await Current(ct); if (user?.Role != "Passenger") return Unauthorized();
        var trip = await db.Trips.AsNoTracking().Include(t => t.Company).Include(t => t.Bus).Include(t => t.Route)
            .Include(t => t.DepartureTerminal).Include(t => t.ArrivalTerminal).SingleOrDefaultAsync(t => t.Id == dto.TripId, ct);
        if (trip == null) return NotFound();
        if (trip.DepartureTime <= Now || trip.Status is not ("Scheduled" or "Planlandı" or "Planlandi") || !trip.Company.IsActive || !trip.Bus.IsActive ||
            trip.Bus.CompanyId != trip.CompanyId || !trip.Route.IsActive || trip.DepartureTerminal is { IsActive: false } || trip.ArrivalTerminal is { IsActive: false })
            return BadRequest(new { message = "Bu sefer satışa açık değil." });
        try { return Ok(await CampaignPricing.Quote(db, trip, user.Id, dto.PassengerCount, dto.CouponCode, Now, false, ct)); }
        catch (PaymentFlowException e) { return StatusCode(e.Status, new { message = e.Message }); }
    }

    [HttpGet, Authorize(Roles = "Admin,Company")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var user = await Current(ct); if (user == null) return Unauthorized();
        var query = db.Campaigns.AsNoTracking().Where(c => user.Role == "Admin" || c.CompanyId == user.CompanyId);
        return Ok(await Views(query).OrderByDescending(c => c.Id).Take(500).ToListAsync(ct));
    }

    [HttpGet("options"), Authorize(Roles = "Admin,Company")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        var user = await Current(ct); if (user == null) return Unauthorized();
        return Ok(new
        {
            companies = await db.Companies.AsNoTracking().Where(c => (user.Role == "Admin" || c.Id == user.CompanyId) && c.IsActive)
                .OrderBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToListAsync(ct),
            routes = await db.Routes.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.DepartureCity).ThenBy(r => r.ArrivalCity)
                .Select(r => new { r.Id, name = r.DepartureCity + " → " + r.ArrivalCity }).ToListAsync(ct)
        });
    }

    [HttpGet("{id:int}/usage"), Authorize(Roles = "Admin,Company")]
    public async Task<IActionResult> Usage(int id, CancellationToken ct)
    {
        var user = await Current(ct); if (user == null) return Unauthorized();
        if (!await db.Campaigns.AnyAsync(c => c.Id == id && (user.Role == "Admin" || c.CompanyId == user.CompanyId), ct)) return NotFound();
        return Ok(await db.PurchaseGroups.AsNoTracking().Where(g => g.CampaignId == id).OrderByDescending(g => g.CreatedAt).Take(50)
            .Select(g => new { g.Id, g.OriginalAmount, g.DiscountAmount, g.Amount, g.PaymentStatus,
                createdAt = DateTime.SpecifyKind(g.CreatedAt, DateTimeKind.Utc),
                refunded = g.Items.Where(p => p.RefundStatus == "Succeeded").Sum(p => p.RefundAmount),
                tickets = g.Items.Count(p => p.Reservation.Kind == "Ticket") }).ToListAsync(ct));
    }

    [HttpPost, Authorize(Roles = "Admin,Company")]
    public Task<IActionResult> Create(SaveCampaignDto dto, CancellationToken ct) => Save(null, dto, ct);
    [HttpPut("{id:int}"), Authorize(Roles = "Admin,Company")]
    public Task<IActionResult> Update(int id, SaveCampaignDto dto, CancellationToken ct) => Save(id, dto, ct);

    private async Task<IActionResult> Save(int? id, SaveCampaignDto dto, CancellationToken ct)
    {
        var user = await Current(ct); if (user == null) return Unauthorized();
        if (user.Role != "Admin" && dto.CompanyId != user.CompanyId) return Forbid();
        string? code;
        try { code = CampaignPricing.NormalizeCode(dto.Code); }
        catch (PaymentFlowException e) { return BadRequest(new { message = e.Message }); }
        bool Money(decimal value) => value == decimal.Round(value, 2);
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.DiscountType is not ("Percentage" or "Fixed") || dto.DiscountType == "Percentage" && dto.Value > 99 ||
            dto.StartsAt == default || dto.EndsAt <= dto.StartsAt || dto.DepartureFrom.HasValue && dto.DepartureUntil <= dto.DepartureFrom ||
            !Money(dto.Value) || !Money(dto.MinimumSubtotal) || dto.MaximumDiscount.HasValue && !Money(dto.MaximumDiscount.Value))
            return BadRequest(new { message = "Ad, tarih aralığı ve indirim tutarlarını kontrol edin. Yüzde indirimi en fazla %99 olabilir; tutarlar en fazla iki ondalık basamak içermelidir." });
        if (!await db.Companies.AnyAsync(c => c.Id == dto.CompanyId && (c.IsActive || !dto.IsActive), ct)) return BadRequest(new { message = "Aktif bir firma seçin." });
        if (dto.RouteId.HasValue && !await db.Routes.AnyAsync(r => r.Id == dto.RouteId && (r.IsActive || !dto.IsActive), ct)) return BadRequest(new { message = "Geçerli bir güzergâh seçin." });
        var campaign = id.HasValue ? await db.Campaigns.SingleOrDefaultAsync(c => c.Id == id && (user.Role == "Admin" || c.CompanyId == user.CompanyId), ct) : new Campaign { CreatedAt = Now };
        if (campaign == null) return NotFound();
        if (id.HasValue)
        {
            if (campaign.CompanyId != dto.CompanyId) return BadRequest(new { message = "Kampanyanın firması değiştirilemez." });
            if (dto.Version == null || !Regex.IsMatch(dto.Version, "^[A-Za-z0-9+/]{11}=$") || !campaign.Version.SequenceEqual(Convert.FromBase64String(dto.Version)))
                return Conflict(new { message = "Kampanya başka bir işlemde değişti. Listeyi yenileyin." });
        }
        campaign.CompanyId = dto.CompanyId; campaign.RouteId = dto.RouteId; campaign.Name = dto.Name.Trim(); campaign.Description = dto.Description.Trim();
        campaign.Code = code; campaign.DiscountType = dto.DiscountType; campaign.Value = dto.Value; campaign.MinimumSubtotal = dto.MinimumSubtotal;
        campaign.MaximumDiscount = dto.MaximumDiscount; campaign.UsageLimit = dto.UsageLimit; campaign.PerUserLimit = dto.PerUserLimit;
        campaign.StartsAt = dto.StartsAt.UtcDateTime; campaign.EndsAt = dto.EndsAt.UtcDateTime;
        campaign.DepartureFrom = dto.DepartureFrom?.UtcDateTime; campaign.DepartureUntil = dto.DepartureUntil?.UtcDateTime;
        campaign.IsActive = dto.IsActive; campaign.IsPublic = dto.IsPublic;
        if (!id.HasValue) db.Campaigns.Add(campaign);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Kampanya değişti. Listeyi yenileyip tekrar deneyin." }); }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 })
        { return Conflict(new { message = "Bu firmada aynı kupon kodu zaten var." }); }
        return Ok(new { campaign.Id, message = "Kampanya kaydedildi." });
    }

    private IQueryable<CampaignView> Views(IQueryable<Campaign> query)
    {
        var now = Now;
        return query.Select(c => new CampaignView
        {
            Id = c.Id, CompanyId = c.CompanyId, CompanyName = db.Companies.Where(company => company.Id == c.CompanyId).Select(company => company.Name).First(),
            RouteId = c.RouteId, RouteName = db.Routes.Where(r => r.Id == c.RouteId).Select(r => r.DepartureCity + " → " + r.ArrivalCity).FirstOrDefault(),
            Name = c.Name, Description = c.Description, Code = c.Code, DiscountType = c.DiscountType, Value = c.Value,
            MinimumSubtotal = c.MinimumSubtotal, MaximumDiscount = c.MaximumDiscount, UsageLimit = c.UsageLimit, PerUserLimit = c.PerUserLimit,
            StartsAt = DateTime.SpecifyKind(c.StartsAt, DateTimeKind.Utc), EndsAt = DateTime.SpecifyKind(c.EndsAt, DateTimeKind.Utc),
            DepartureFrom = c.DepartureFrom == null ? null : DateTime.SpecifyKind(c.DepartureFrom.Value, DateTimeKind.Utc),
            DepartureUntil = c.DepartureUntil == null ? null : DateTime.SpecifyKind(c.DepartureUntil.Value, DateTimeKind.Utc),
            IsActive = c.IsActive, IsPublic = c.IsPublic, Version = c.Version,
            Used = db.PurchaseGroups.Count(g => g.CampaignId == c.Id && g.PaymentStatus == "Paid" && g.Items.Any(p => p.Reservation.Kind == "Ticket")),
            Reserved = db.PurchaseGroups.Count(g => g.CampaignId == c.Id && g.PaymentStatus == "Pending" && g.HoldExpiresAt > now),
            TotalDiscount = db.PurchaseGroups.Where(g => g.CampaignId == c.Id && g.PaymentStatus == "Paid" && g.Items.Any(p => p.Reservation.Kind == "Ticket")).Sum(g => g.DiscountAmount)
        });
    }
}

public sealed class CampaignView
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public int? RouteId { get; set; }
    public string? RouteName { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Code { get; set; }
    public string DiscountType { get; set; } = "";
    public decimal Value { get; set; }
    public decimal MinimumSubtotal { get; set; }
    public decimal? MaximumDiscount { get; set; }
    public int? UsageLimit { get; set; }
    public int PerUserLimit { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public DateTime? DepartureFrom { get; set; }
    public DateTime? DepartureUntil { get; set; }
    public bool IsActive { get; set; }
    public bool IsPublic { get; set; }
    public byte[] Version { get; set; } = [];
    public int Used { get; set; }
    public int Reserved { get; set; }
    public decimal TotalDiscount { get; set; }
}
