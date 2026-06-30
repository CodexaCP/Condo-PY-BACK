using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class OwnerPaymentUnit : CompanyScopedEntity
{
    public Guid OwnerPaymentId { get; set; }
    public Guid UnitId { get; set; }
    public decimal AllocatedAmount { get; set; }

    public OwnerPayment? OwnerPayment { get; set; }
    public Unit? Unit { get; set; }
    public Company? Company { get; set; }
}
