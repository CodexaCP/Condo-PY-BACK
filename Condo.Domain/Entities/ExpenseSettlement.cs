using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class ExpenseSettlement : CompanyScopedEntity
{
    public Guid ExpensePeriodId { get; set; }
    public Guid BuildingId { get; set; }
    public decimal TotalBuildingExpenses { get; set; }
    public decimal TotalBuildingIncomes { get; set; }
    public decimal ReserveFundAmount { get; set; }
    public decimal ExtraordinaryAmount { get; set; }
    public decimal NetCommonAmount { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public Guid GeneratedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public ExpenseSettlementStatus Status { get; set; } = ExpenseSettlementStatus.Draft;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public ExpensePeriod? ExpensePeriod { get; set; }
    public ApplicationUser? GeneratedByUser { get; set; }
    public ApplicationUser? ApprovedByUser { get; set; }
    public ApplicationUser? PublishedByUser { get; set; }
    public ICollection<ExpenseCharge> ExpenseCharges { get; set; } = new List<ExpenseCharge>();
}
