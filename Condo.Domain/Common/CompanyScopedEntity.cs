namespace Condo.Domain.Common;

public abstract class CompanyScopedEntity : BaseEntity
{
    public Guid CompanyId { get; set; }
}
