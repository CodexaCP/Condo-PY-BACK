using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ad-campaigns")]
public class AdCampaignsController(
    ICondoDbContext dbContext,
    ITenantContext tenantContext,
    IWebHostEnvironment env) : ControllerBase
{
    private static readonly HashSet<string> AllowedImageExtensions = [".jpg", ".jpeg", ".png"];
    private const long MaxImageBytes = 2 * 1024 * 1024; // 2 MB

    // ── POST /api/ad-campaigns/upload-image ───────────────────────────────────
    [HttpPost("upload-image")]
    public async Task<ActionResult<AdBannerUploadResultDto>> UploadImage(IFormFile file, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        if (file is null || file.Length == 0)
            return BadRequest("No se recibió ningún archivo.");

        if (file.Length > MaxImageBytes)
            return BadRequest("El archivo supera el límite de 2 MB.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(ext))
            return BadRequest("Solo se permiten imágenes JPG o PNG.");

        var bannersPath = Path.Combine(env.WebRootPath, "uploads", "ad-banners");
        Directory.CreateDirectory(bannersPath);

        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(bannersPath, fileName);

        await using var stream = System.IO.File.Create(filePath);
        await file.CopyToAsync(stream, ct);

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(new AdBannerUploadResultDto { ImageUrl = $"{baseUrl}/uploads/ad-banners/{fileName}" });
    }

    // ── GET /api/ad-campaigns/buildings ───────────────────────────────────────
    // Todos los edificios con su interruptor de publicidad (como el Marketplace y las Finanzas: lo opera el SuperAdmin).
    [HttpGet("buildings")]
    public async Task<ActionResult<IReadOnlyList<AdBuildingDto>>> GetBuildings(CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        return Ok(await BuildRowsAsync(null, ct));
    }

    // ── PUT /api/ad-campaigns/buildings/{buildingId} ──────────────────────────
    // Apagar conserva las campañas cargadas, pero el edificio deja de mostrar anuncios.
    [HttpPut("buildings/{buildingId:guid}")]
    public async Task<ActionResult<AdBuildingDto>> UpdateBuilding(
        Guid buildingId, [FromBody] AdBuildingUpdateRequest req, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var building = await dbContext.Buildings.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == buildingId, ct);
        if (building is null) return NotFound();

        building.AdsEnabled = req.Enabled;
        await dbContext.SaveChangesAsync(ct);

        return Ok((await BuildRowsAsync(buildingId, ct)).Single());
    }

    // ── GET /api/ad-campaigns ─────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdCampaignDto>>> GetAll(CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var campaigns = await dbContext.AdCampaigns
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.Company)
            .Include(x => x.Buildings)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(campaigns.Select(ToDto).ToList());
    }

    // ── GET /api/ad-campaigns/{id} ────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdCampaignDto>> GetById(Guid id, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var campaign = await dbContext.AdCampaigns
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Include(x => x.Company)
            .Include(x => x.Buildings)
            .FirstOrDefaultAsync(ct);

        if (campaign is null) return NotFound();

        return Ok(ToDto(campaign));
    }

    // ── POST /api/ad-campaigns ────────────────────────────────────────────────
    [HttpPost]
    public async Task<ActionResult<AdCampaignDto>> Create([FromBody] AdCampaignCreateRequest req, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var error = Validate(req.AdvertiserName, req.CtaText, req.ImageUrl, req.Category,
                             req.Position, req.StartDate, req.EndDate, req.BuildingIds);
        if (error is not null) return BadRequest(error);

        if (!Enum.TryParse<AdCampaignCategory>(req.Category, true, out var category))
            return BadRequest($"Categoría inválida. Valores: {string.Join(", ", Enum.GetNames<AdCampaignCategory>())}");

        var buildingError = await ValidateBuildingsAsync(req.CompanyId, req.BuildingIds, [], ct);
        if (buildingError is not null) return BadRequest(buildingError);

        var conflict = await PositionConflict(null, req.Position, req.BuildingIds, ct);
        if (conflict is not null) return Conflict(conflict);

        var campaign = new AdCampaign
        {
            CompanyId        = req.CompanyId,
            CreatedByUserId  = tenantContext.UserId,
            AdvertiserName   = req.AdvertiserName.Trim(),
            Description      = req.Description?.Trim(),
            CtaText          = req.CtaText.Trim(),
            CtaUrl           = req.CtaUrl?.Trim(),
            ImageUrl         = req.ImageUrl.Trim(),
            Category         = category,
            Position         = req.Position,
            StartDate        = req.StartDate,
            EndDate          = req.EndDate,
            MonthlyAmount    = req.MonthlyAmount,
            IsActive         = req.IsActive,
            NotifyBeforeExpiry = req.NotifyBeforeExpiry,
            Buildings        = req.BuildingIds
                                  .Select(bid => new AdCampaignBuilding { BuildingId = bid })
                                  .ToList()
        };

        dbContext.AdCampaigns.Add(campaign);
        await dbContext.SaveChangesAsync(ct);

        var created = await dbContext.AdCampaigns
            .AsNoTracking()
            .Where(x => x.Id == campaign.Id)
            .Include(x => x.Company)
            .Include(x => x.Buildings)
            .FirstAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    // ── PUT /api/ad-campaigns/{id} ────────────────────────────────────────────
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AdCampaignDto>> Update(Guid id, [FromBody] AdCampaignUpdateRequest req, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var campaign = await dbContext.AdCampaigns
            .Where(x => !x.IsDeleted && x.Id == id)
            .Include(x => x.Company)
            .Include(x => x.Buildings)
            .FirstOrDefaultAsync(ct);

        if (campaign is null) return NotFound();

        var error = Validate(req.AdvertiserName, req.CtaText, req.ImageUrl, req.Category,
                             req.Position, req.StartDate, req.EndDate, req.BuildingIds);
        if (error is not null) return BadRequest(error);

        if (!Enum.TryParse<AdCampaignCategory>(req.Category, true, out var category))
            return BadRequest($"Categoría inválida. Valores: {string.Join(", ", Enum.GetNames<AdCampaignCategory>())}");

        // Los edificios que la campaña ya tenía se conservan aunque se haya apagado su módulo; solo los nuevos lo exigen.
        var buildingError = await ValidateBuildingsAsync(
            campaign.CompanyId, req.BuildingIds, campaign.Buildings.Select(b => b.BuildingId).ToHashSet(), ct);
        if (buildingError is not null) return BadRequest(buildingError);

        var conflict = await PositionConflict(id, req.Position, req.BuildingIds, ct);
        if (conflict is not null) return Conflict(conflict);

        campaign.AdvertiserName    = req.AdvertiserName.Trim();
        campaign.Description       = req.Description?.Trim();
        campaign.CtaText           = req.CtaText.Trim();
        campaign.CtaUrl            = req.CtaUrl?.Trim();
        campaign.ImageUrl          = req.ImageUrl.Trim();
        campaign.Category          = category;
        campaign.Position          = req.Position;
        campaign.StartDate         = req.StartDate;
        campaign.EndDate           = req.EndDate;
        campaign.MonthlyAmount     = req.MonthlyAmount;
        campaign.IsActive          = req.IsActive;
        campaign.NotifyBeforeExpiry = req.NotifyBeforeExpiry;

        // Sync buildings: replace all
        dbContext.AdCampaignBuildings.RemoveRange(campaign.Buildings);
        campaign.Buildings = req.BuildingIds
            .Select(bid => new AdCampaignBuilding { AdCampaignId = id, BuildingId = bid })
            .ToList();

        await dbContext.SaveChangesAsync(ct);

        return Ok(ToDto(campaign));
    }

    // ── DELETE /api/ad-campaigns/{id} ─────────────────────────────────────────
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var campaign = await dbContext.AdCampaigns
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);

        if (campaign is null) return NotFound();

        campaign.IsDeleted = true;
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    // ── PATCH /api/ad-campaigns/{id}/toggle ───────────────────────────────────
    [HttpPatch("{id:guid}/toggle")]
    public async Task<ActionResult<AdCampaignDto>> Toggle(Guid id, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var campaign = await dbContext.AdCampaigns
            .Where(x => !x.IsDeleted && x.Id == id)
            .Include(x => x.Company)
            .Include(x => x.Buildings)
            .FirstOrDefaultAsync(ct);

        if (campaign is null) return NotFound();

        campaign.IsActive = !campaign.IsActive;
        await dbContext.SaveChangesAsync(ct);

        return Ok(ToDto(campaign));
    }

    // ── HELPERS ───────────────────────────────────────────────────────────────

    private async Task<List<AdBuildingDto>> BuildRowsAsync(Guid? onlyBuildingId, CancellationToken ct)
    {
        var query = dbContext.Buildings.AsNoTracking().Where(x => !x.IsDeleted);
        if (onlyBuildingId.HasValue) query = query.Where(x => x.Id == onlyBuildingId.Value);

        var rows = await query
            .OrderBy(x => x.Name)
            .Select(x => new AdBuildingDto
            {
                BuildingId = x.Id,
                BuildingName = x.Name,
                CompanyId = x.CompanyId ?? (x.Condominium != null ? x.Condominium.CompanyId : null),
                CompanyName = x.Company != null
                    ? x.Company.Name
                    : (x.Condominium != null && x.Condominium.Company != null ? x.Condominium.Company.Name : string.Empty),
                CondominiumName = x.Condominium != null ? x.Condominium.Name : string.Empty,
                AdsEnabled = x.AdsEnabled
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.BuildingId).ToList();
        var counts = await dbContext.AdCampaignBuildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && ids.Contains(x.BuildingId) && !x.AdCampaign!.IsDeleted && x.AdCampaign.IsActive)
            .GroupBy(x => x.BuildingId)
            .Select(g => new { BuildingId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BuildingId, x => x.Count, ct);

        foreach (var row in rows)
            row.CampaignCount = counts.GetValueOrDefault(row.BuildingId);

        return rows;
    }

    // Los edificios elegidos tienen que existir, ser de la empresa de la campaña y tener la publicidad encendida
    // (los que la campaña ya tenía, en una edición, no exigen lo último).
    private async Task<string?> ValidateBuildingsAsync(
        Guid companyId, List<Guid> buildingIds, HashSet<Guid> alreadyAssigned, CancellationToken ct)
    {
        var ids = buildingIds.Distinct().ToList();

        var buildings = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && ids.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.AdsEnabled,
                CompanyId = x.CompanyId ?? (x.Condominium != null ? x.Condominium.CompanyId : null)
            })
            .ToListAsync(ct);

        if (buildings.Count != ids.Count)
            return "Alguno de los edificios seleccionados no existe.";

        var otherCompany = buildings.FirstOrDefault(b => b.CompanyId != companyId);
        if (otherCompany is not null)
            return $"El edificio \"{otherCompany.Name}\" no pertenece a la empresa de la campaña.";

        var disabled = buildings.FirstOrDefault(b => !b.AdsEnabled && !alreadyAssigned.Contains(b.Id));
        if (disabled is not null)
            return $"El edificio \"{disabled.Name}\" no tiene el módulo de Publicidad activado.";

        return null;
    }

    private async Task<string?> PositionConflict(Guid? excludeId, int position, List<Guid> buildingIds, CancellationToken ct)
    {
        var taken = await dbContext.AdCampaignBuildings
            .AsNoTracking()
            .Where(x => !x.AdCampaign!.IsDeleted
                     && x.AdCampaign.IsActive
                     && x.AdCampaign.Position == position
                     && buildingIds.Contains(x.BuildingId)
                     && (excludeId == null || x.AdCampaignId != excludeId))
            .Select(x => x.BuildingId)
            .FirstOrDefaultAsync(ct);

        return taken == Guid.Empty
            ? null
            : $"La posición {position} ya está ocupada en uno de los edificios seleccionados.";
    }

    private static string? Validate(string advertiserName, string ctaText, string imageUrl,
                                    string category, int position, DateTime startDate,
                                    DateTime endDate, List<Guid> buildingIds)
    {
        if (string.IsNullOrWhiteSpace(advertiserName))   return "El nombre del anunciante es obligatorio.";
        if (advertiserName.Trim().Length > 200)          return "El nombre del anunciante no puede superar 200 caracteres.";
        if (string.IsNullOrWhiteSpace(ctaText))          return "El texto del botón es obligatorio.";
        if (ctaText.Trim().Length > 60)                  return "El texto del botón no puede superar 60 caracteres.";
        if (string.IsNullOrWhiteSpace(imageUrl))         return "La URL de la imagen es obligatoria.";
        if (string.IsNullOrWhiteSpace(category))         return "La categoría es obligatoria.";
        if (position < 1 || position > 7)                return "La posición debe estar entre 1 y 7.";
        if (endDate <= startDate)                        return "La fecha de fin debe ser posterior a la fecha de inicio.";
        if (buildingIds is null || buildingIds.Count == 0) return "Debe seleccionar al menos un edificio.";
        return null;
    }

    private static AdCampaignDto ToDto(AdCampaign c) => new()
    {
        Id                 = c.Id,
        CompanyId          = c.CompanyId,
        CompanyName        = c.Company?.Name ?? string.Empty,
        CreatedByUserId    = c.CreatedByUserId,
        AdvertiserName     = c.AdvertiserName,
        Description        = c.Description,
        CtaText            = c.CtaText,
        CtaUrl             = c.CtaUrl,
        ImageUrl           = c.ImageUrl,
        Category           = c.Category.ToString(),
        Position           = c.Position,
        StartDate          = c.StartDate,
        EndDate            = c.EndDate,
        MonthlyAmount      = c.MonthlyAmount,
        IsActive           = c.IsActive,
        NotifyBeforeExpiry = c.NotifyBeforeExpiry,
        BuildingIds        = c.Buildings.Select(b => b.BuildingId).ToList(),
        BuildingCount      = c.Buildings.Count,
        CreatedAtUtc       = c.CreatedAtUtc,
        UpdatedAtUtc       = c.UpdatedAtUtc
    };
}

public class AdBannerUploadResultDto
{
    public string ImageUrl { get; set; } = string.Empty;
}
