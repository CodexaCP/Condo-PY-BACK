using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Nota de credito que el PROVEEDOR emite sobre un gasto del edificio (no es la nota de credito al propietario: ver CreditNote).
// Mientras el periodo no esta publicado baja el monto del gasto (BuildingExpense.Amount pasa a ser el neto y OriginalAmount
// guarda el monto facturado por el proveedor). Nunca toca comprobantes ya emitidos a los propietarios.
public class BuildingExpenseCreditNote : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid BuildingExpenseId { get; set; }
    public Guid ExpensePeriodId { get; set; }
    // Numero del documento del proveedor.
    public string Numero { get; set; } = string.Empty;
    public string? Timbrado { get; set; }
    public DateOnly IssueDate { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    // Archivo del documento del proveedor (ruta relativa de /api/uploads).
    public string? DocumentUrl { get; set; }
    public BuildingExpenseCreditNoteMode Mode { get; set; } = BuildingExpenseCreditNoteMode.Netted;
    public BuildingExpenseCreditNoteStatus Status { get; set; } = BuildingExpenseCreditNoteStatus.Applied;
    public Guid CreatedByUserId { get; set; }
    public string VoidReason { get; set; } = string.Empty;
    public DateTime? VoidedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public BuildingExpense? BuildingExpense { get; set; }
    public ExpensePeriod? ExpensePeriod { get; set; }
}
