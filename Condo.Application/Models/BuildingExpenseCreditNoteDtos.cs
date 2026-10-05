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
}

// Resultado de crear o anular una NC: la NC y como quedo el gasto. SettlementNeedsRecalculation avisa que el periodo ya tenia una
// liquidacion calculada y hay que volver a calcularla para que tome el nuevo monto del gasto.
public class BuildingExpenseCreditNoteResultDto
{
    public BuildingExpenseCreditNoteDto CreditNote { get; set; } = new();
    public BuildingExpenseDto Expense { get; set; } = new();
    public bool SettlementNeedsRecalculation { get; set; }
}
