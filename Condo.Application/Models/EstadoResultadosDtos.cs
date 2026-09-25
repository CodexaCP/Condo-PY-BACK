namespace Condo.Application.Models;

public class EstadoResultadosLineDto
{
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class EstadoResultadosReportDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public IReadOnlyList<EstadoResultadosLineDto> IncomeLines { get; set; } = [];
    public decimal TotalIncome { get; set; }
    public IReadOnlyList<EstadoResultadosLineDto> ExpenseLines { get; set; } = [];
    public decimal TotalExpense { get; set; }
    public decimal NetResult { get; set; }
}
