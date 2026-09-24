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
    public string RejectionReason { get; set; } = string.Empty;
    public DateTime? RejectedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public ExpenseSettlementStatus Status { get; set; } = ExpenseSettlementStatus.Draft;

    // Revision del presidente del consorcio, entre la aprobacion del BuildingManager y la
    // publicacion del CompanyAdmin. Solo uno de los dos (aprobado/rechazado) puede estar seteado.
    public DateTime? PresidentApprovedAtUtc { get; set; }
    public Guid? PresidentApprovedByUserId { get; set; }
    public string PresidentRejectionReason { get; set; } = string.Empty;
    public DateTime? PresidentRejectedAtUtc { get; set; }
    public Guid? PresidentRejectedByUserId { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public ExpensePeriod? ExpensePeriod { get; set; }
    public ApplicationUser? GeneratedByUser { get; set; }
    public ApplicationUser? ApprovedByUser { get; set; }
    public ApplicationUser? PublishedByUser { get; set; }
    public ApplicationUser? RejectedByUser { get; set; }
    public ApplicationUser? PresidentApprovedByUser { get; set; }
    public ApplicationUser? PresidentRejectedByUser { get; set; }
    public ICollection<ExpenseCharge> ExpenseCharges { get; set; } = new List<ExpenseCharge>();
}
