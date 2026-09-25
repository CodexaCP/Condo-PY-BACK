namespace Condo.Application.Models;

public enum LibroMovimientoType
{
    Cobro = 1,
    IngresoEdificio = 2,
    GastoEdificio = 3
}

public class LibroMovimientoItemDto
{
    public DateOnly Date { get; set; }
    public LibroMovimientoType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? UnitCode { get; set; }
    public string? Reference { get; set; }
    public decimal Credit { get; set; }
    public decimal Debit { get; set; }
    public decimal RunningBalance { get; set; }
}

public class LibroMovimientosReportDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal TotalCredits { get; set; }
    public decimal TotalDebits { get; set; }
    public decimal ClosingBalance { get; set; }
    public IReadOnlyList<LibroMovimientoItemDto> Items { get; set; } = [];
}
