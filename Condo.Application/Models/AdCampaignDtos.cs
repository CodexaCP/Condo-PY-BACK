namespace Condo.Application.Models;

public class AdCampaignDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public string AdvertiserName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CtaText { get; set; } = string.Empty;
    public string? CtaUrl { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Position { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal? MonthlyAmount { get; set; }
    public bool IsActive { get; set; }
    public bool NotifyBeforeExpiry { get; set; }
    public List<Guid> BuildingIds { get; set; } = [];
    public int BuildingCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class AdCampaignCreateRequest
{
    public Guid CompanyId { get; set; }
    public string AdvertiserName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CtaText { get; set; } = string.Empty;
    public string? CtaUrl { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Position { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal? MonthlyAmount { get; set; }
    public bool IsActive { get; set; } = true;
    public bool NotifyBeforeExpiry { get; set; } = true;
    public List<Guid> BuildingIds { get; set; } = [];
}

public class AdCampaignUpdateRequest
{
    public string AdvertiserName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CtaText { get; set; } = string.Empty;
    public string? CtaUrl { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Position { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal? MonthlyAmount { get; set; }
    public bool IsActive { get; set; }
    public bool NotifyBeforeExpiry { get; set; }
    public List<Guid> BuildingIds { get; set; } = [];
}

// Slim DTO para la app móvil — sin monthlyAmount ni datos internos
public class AdSlotDto
{
    public Guid Id { get; set; }
    public string AdvertiserName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CtaText { get; set; } = string.Empty;
    public string? CtaUrl { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Position { get; set; }
}

// Respuesta del endpoint /api/me/ad-slots
public class BuildingAdsDto
{
    public List<AdSlotDto> Slots { get; set; } = [];
    // Teléfono de contacto del edificio (para el banner de fallback cuando no hay campañas)
    public string? ManagerPhone { get; set; }
}

// Fila del listado de edificios para activar o apagar el modulo de publicidad (solo SuperAdmin).
public class AdBuildingDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string CondominiumName { get; set; } = string.Empty;
    public bool AdsEnabled { get; set; }
    // Campañas activas y no eliminadas asignadas al edificio (vigentes o no por fecha).
    public int CampaignCount { get; set; }
}

public class AdBuildingUpdateRequest
{
    public bool Enabled { get; set; }
}
