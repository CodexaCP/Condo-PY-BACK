using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class OwnerCredit : CompanyScopedEntity
{
    public Guid OwnerId { get; set; }
    public decimal Amount { get; set; }

    public ApplicationUser? Owner { get; set; }
    public Company? Company { get; set; }
}
