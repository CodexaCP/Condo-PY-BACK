namespace Condo.Application.Models;

// ── Publicaciones ─────────────────────────────────────────────────────────────

// Unidad que el usuario puede publicar (de la que es propietario principal).
public class MarketplacePublishableUnitDto
{
    public Guid UnitId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Floor { get; set; } = string.Empty;
}

public class MarketplaceListingCreateRequest
{
    public Guid BuildingId { get; set; }
    public Guid UnitId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public decimal HourlyPrice { get; set; }
}

public class MarketplaceListingUpdateRequest
{
    public string Title { get; set; } = string.Empty;
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public decimal HourlyPrice { get; set; }
}

public class MarketplaceListingReasonRequest
{
    public string? Reason { get; set; }
}

public class MarketplaceListingDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public int WindowHours { get; set; }
    public decimal HourlyPrice { get; set; }
    // Active | Suspended | Closed
    public string Status { get; set; } = string.Empty;
    public string? StatusReason { get; set; }
    // La ventana ya termino (aunque el estado todavia no se haya cerrado).
    public bool WindowEnded { get; set; }
    // Reservas vivas (esperando pago, en revision o confirmadas).
    public int ActiveReservations { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

// Edificio del usuario con el marketplace efectivamente disponible (habilitado y con plan que lo incluye).
public class MarketplaceBuildingDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    // Es propietario principal de al menos una unidad del edificio: puede publicar.
    public bool CanPublish { get; set; }
}

// Edificio del personal con el marketplace disponible y lo que su rol puede hacer ahi (menu de la web y de la app del Encargado).
public class MarketplaceStaffBuildingDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public bool CanReviewPayments { get; set; }
    public bool CanViewAccount { get; set; }
    public bool CanEditAccount { get; set; }
}
