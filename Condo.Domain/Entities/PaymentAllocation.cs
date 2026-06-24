using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class PaymentAllocation : CompanyScopedEntity
{
    public Guid PaymentId { get; set; }
    public Guid ExpenseChargeId { get; set; }
    public decimal AllocatedAmount { get; set; }

    public Company? Company { get; set; }
    public Payment? Payment { get; set; }
    public ExpenseCharge? Charge { get; set; }
}
