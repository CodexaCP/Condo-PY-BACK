using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class Claim : CompanyScopedEntity
{
    public Guid? CondominiumId { get; set; }
    public Guid BuildingId { get; set; }
    public Guid UnitId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Category { get; set; } = "Otro";
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Pendiente";
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }

    public Condominium? Condominium { get; set; }
    public Building Building { get; set; } = null!;
    public Unit Unit { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ApplicationUser? ResolvedByUser { get; set; }
}
