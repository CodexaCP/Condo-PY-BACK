namespace Condo.Application.Models;

public class BuildingComparisonItemDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public decimal TotalCollected { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetResult { get; set; }
    public decimal OverdueAmount { get; set; }
    public int UnitsWithOverdueBalance { get; set; }
}

public class BuildingComparisonReportDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public IReadOnlyList<BuildingComparisonItemDto> Items { get; set; } = [];
}
