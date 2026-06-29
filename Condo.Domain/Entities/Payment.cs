using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class Payment : CompanyScopedEntity
{
    public Guid ExpensePeriodId { get; set; }
    public Guid UnitId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;
    public string Reference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsReversed { get; set; }
    public DateTime? ReversedAt { get; set; }

    public Company? Company { get; set; }
    public ExpensePeriod? ExpensePeriod { get; set; }
    public Unit? Unit { get; set; }
    public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
}
