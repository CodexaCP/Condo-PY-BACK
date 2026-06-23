using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class MorositySummaryDto
{
    public int TotalUnitsInArrears { get; set; }
    public int TotalOverduePeriods { get; set; }
    public decimal TotalOverdueAmount { get; set; }
    public decimal OrdinaryOverdueAmount { get; set; }
    public decimal ReserveFundOverdueAmount { get; set; }
    public decimal ExtraordinaryOverdueAmount { get; set; }
    public decimal IndividualOverdueAmount { get; set; }
    public decimal AdjustmentOverdueAmount { get; set; }
    public decimal TotalCreditBalanceAmount { get; set; }
    public int OccupiedUnitsInArrears { get; set; }
    public int VacantUnitsInArrears { get; set; }
    public decimal OccupiedOverdueAmount { get; set; }
    public decimal VacantOverdueAmount { get; set; }
}

public class MorosityItemDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public int DaysOverdue { get; set; }
    public decimal TotalCharges { get; set; }
    public decimal TotalPayments { get; set; }
    public decimal Balance { get; set; }
    public decimal OrdinaryBalance { get; set; }
    public decimal ReserveFundBalance { get; set; }
    public decimal ExtraordinaryBalance { get; set; }
    public decimal IndividualBalance { get; set; }
    public decimal AdjustmentBalance { get; set; }
    public decimal CreditBalanceAmount { get; set; }
    public bool IsOccupied { get; set; }
    public string ResponsibleType { get; set; } = string.Empty;
    public string ResponsibleName { get; set; } = string.Empty;
}

public class MorosityReportDto
{
    public MorositySummaryDto Summary { get; set; } = new();
    public IReadOnlyList<MorosityItemDto> Items { get; set; } = [];
}
