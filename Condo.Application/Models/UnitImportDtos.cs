namespace Condo.Application.Models;

// Carga masiva de unidades desde Excel (SuperAdmin): vista previa y resultado.
public class UnitImportResultDto
{
    // true = se pidio guardar (confirm); Created > 0 solo si no hubo errores.
    public bool Confirmed { get; set; }
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int ErrorRows { get; set; }
    public int Created { get; set; }
    // Mensaje general (por ejemplo, por que no se guardo).
    public string? Message { get; set; }
    public List<UnitImportRowDto> Rows { get; set; } = new();
    public List<UnitImportBuildingSummaryDto> Buildings { get; set; } = new();
}

public class UnitImportRowDto
{
    public int RowNumber { get; set; }
    public string Building { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Floor { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public bool IsActive { get; set; }
    // null = fila valida; con texto = por que no se puede importar.
    public string? Error { get; set; }
}

public class UnitImportBuildingSummaryDto
{
    public string Building { get; set; } = string.Empty;
    public int NewUnits { get; set; }
    public int ExistingUnits { get; set; }
    // Suma de los coeficientes de las unidades existentes y las nuevas del edificio (suele ser 1).
    public decimal CoefficientTotal { get; set; }
    public string? Warning { get; set; }
}
