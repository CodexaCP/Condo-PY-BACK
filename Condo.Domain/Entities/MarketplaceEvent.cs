using Condo.Domain.Common;

namespace Condo.Domain.Entities;

/// <summary>
/// Auditoria del marketplace y base del historial economico de cada operacion: quien hizo que, cuando, sobre que entidad,
/// de que estado a cual y con que importes. Solo se inserta: nunca se modifica ni se borra.
/// </summary>
public class MarketplaceEvent : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    // Nulo = proceso automatico del sistema.
    public Guid? UserId { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    // Ver MarketplaceEventActions.
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    // Importes y datos del momento (JSON), para reconstruir la operacion sin recalcular nada.
    public string? DataJson { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
}
