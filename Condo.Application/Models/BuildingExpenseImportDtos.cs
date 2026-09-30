namespace Condo.Application.Models;

// Resultado de la carga masiva de gastos desde Excel (vista previa o importacion confirmada).
public class BuildingExpenseImportResultDto
{
    public bool Imported { get; set; }
    public int ImportedCount { get; set; }
    // Gastos que el periodo ya tenia (con "reemplazar" se eliminan al importar).
    public int ExistingCount { get; set; }
    public int DeletedCount { get; set; }
    public int OkCount { get; set; }
    public int WarningCount { get; set; }
    public int DuplicateCount { get; set; }
    public int ErrorCount { get; set; }
    public List<BuildingExpenseImportRowDto> Rows { get; set; } = new();
}

public class BuildingExpenseImportRowDto
{
    public int RowNumber { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    // Ok, Warning (se importa), Duplicate (ya existe: se omite) o Error (no se importa nada mientras haya errores).
    public string Status { get; set; } = "Ok";
    public string Message { get; set; } = string.Empty;
}
