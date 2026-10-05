using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class CreateBuildingExpenseCreditNoteRequest
{
    // Numero del documento que emitio el proveedor.
    public string Numero { get; set; } = string.Empty;
    public string? Timbrado { get; set; }
    public DateOnly IssueDate { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    // Ruta devuelta por /api/uploads (opcional).
    public string? DocumentUrl { get; set; }
}

public class VoidBuildingExpenseCreditNoteRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class BuildingExpenseCreditNoteDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public Guid BuildingExpenseId { get; set; }
    public Guid ExpensePeriodId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string? Timbrado { get; set; }
    public DateOnly IssueDate { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? DocumentUrl { get; set; }
    public BuildingExpenseCreditNoteMode Mode { get; set; }
    public BuildingExpenseCreditNoteStatus Status { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string VoidReason { get; set; } = string.Empty;
    public DateTime? VoidedAtUtc { get; set; }
    // Solo en notas de periodo publicado (Credited): lo acreditado a cada unidad.
    public List<BuildingExpenseCreditNoteAllocationDto> Allocations { get; set; } = new();
}

public class BuildingExpenseCreditNoteAllocationDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

// Una fila del anexo de notas de credito de proveedor de un edificio y periodo (para la liquidacion y para el contador).
public class PeriodSupplierCreditNoteDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public Guid BuildingExpenseId { get; set; }
    public string ExpenseDescription { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Rubro { get; set; }
    public string Numero { get; set; } = string.Empty;
    public string? Timbrado { get; set; }
    public DateOnly IssueDate { get; set; }
    public decimal Amount { get; set; }
    public BuildingExpenseCreditNoteMode Mode { get; set; }
    public BuildingExpenseCreditNoteStatus Status { get; set; }
    // Que se hizo con la nota: se descontó del gasto, se acreditó a las unidades, volvió al fondo de reserva...
    public string Treatment { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? DocumentUrl { get; set; }
    public string VoidReason { get; set; } = string.Empty;
    public int AllocationsCount { get; set; }
    public decimal AllocatedAmount { get; set; }
}

public class BuildingExpenseCreditNotePreviewRequest
{
    public decimal Amount { get; set; }
}

// Simulacion (no guarda nada) de lo que pasaria al registrar la nota con ese monto.
public class BuildingExpenseCreditNotePreviewDto
{
    public BuildingExpenseCreditNoteMode Mode { get; set; }
    public decimal Amount { get; set; }
    // Periodo en borrador: el monto del gasto despues de la nota.
    public decimal NewExpenseAmount { get; set; }
    // Periodo publicado: lo ya acreditado por otras notas, lo que todavia se puede acreditar y el total cobrado del gasto.
    public decimal CreditedSoFar { get; set; }
    public decimal MaxAmount { get; set; }
    public decimal ChargedTotal { get; set; }
    public List<BuildingExpenseCreditNotePreviewRowDto> Rows { get; set; } = new();
    // Unidades del reparto sin propietario principal: mientras haya alguna, la nota no se puede registrar.
    public List<string> UnitsWithoutOwner { get; set; } = new();
    public string? Message { get; set; }
}

public class BuildingExpenseCreditNotePreviewRowDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid? OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public decimal ChargeAmount { get; set; }
    public decimal CreditAmount { get; set; }
}

// Resultado de crear o anular una NC: la NC y como quedo el gasto. SettlementNeedsRecalculation avisa que el periodo ya tenia una
// liquidacion calculada y hay que volver a calcularla para que tome el nuevo monto del gasto.
public class BuildingExpenseCreditNoteResultDto
{
    public BuildingExpenseCreditNoteDto CreditNote { get; set; } = new();
    public BuildingExpenseDto Expense { get; set; } = new();
    public bool SettlementNeedsRecalculation { get; set; }
    // Periodo publicado: total acreditado como saldo a favor de las unidades (0 si el gasto no se reparte).
    public decimal CreditedToOwners { get; set; }
}
