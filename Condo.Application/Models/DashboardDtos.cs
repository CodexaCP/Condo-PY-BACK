namespace Condo.Application.Models;

public class DashboardSummaryDto
{
    public int TotalBuildings { get; set; }
    public int ActiveBuildings { get; set; }
    public int TotalUnits { get; set; }
    public int ActiveUnits { get; set; }
    public int TotalResidents { get; set; }
    public int ActiveResidents { get; set; }
    public int ActiveAssignments { get; set; }
    public int OccupiedUnits { get; set; }
    public int UnitsWithoutPrimaryResident { get; set; }
    public int TotalExpensePeriods { get; set; }
    public int DraftExpensePeriods { get; set; }
    public int UnitsWithOutstandingBalance { get; set; }
    public decimal TotalChargedAmount { get; set; }
    public decimal TotalCollectedAmount { get; set; }
    public decimal PendingBalanceAmount { get; set; }
    public decimal OverdueBalanceAmount { get; set; }
    public decimal CollectionRatePercentage { get; set; }
}
