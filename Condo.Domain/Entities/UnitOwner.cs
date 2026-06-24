using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class UnitOwner : CompanyScopedEntity
{
    public Guid UnitId { get; set; }
    public Guid OwnerId { get; set; }
    public bool IsPrimary { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    public Company? Company { get; set; }
    public Unit? Unit { get; set; }
    public ApplicationUser? Owner { get; set; }
}
