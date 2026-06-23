using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class UnitResident : CompanyScopedEntity
{
    public Guid UnitId { get; set; }
    public Guid ResidentId { get; set; }
    public bool IsPrimary { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? EndDate { get; set; }

    public Company? Company { get; set; }
    public Unit? Unit { get; set; }
    public Resident? Resident { get; set; }
}
