using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Parte de una nota de credito de proveedor (tratamiento NextInvoiceDiscount) que el proveedor ya descontó en una factura posterior.
// Lo que queda por aplicar es BuildingExpenseCreditNote.Amount menos la suma de estas filas.
public class BuildingExpenseCreditNoteDiscount : CompanyScopedEntity
{
    public Guid CreditNoteId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly AppliedOn { get; set; }
    // Gasto (la factura siguiente) donde vino el descuento. Opcional: puede no haberse cargado todavia.
    public Guid? AppliedToExpenseId { get; set; }
    public string Note { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }

    public BuildingExpenseCreditNote? CreditNote { get; set; }
    public BuildingExpense? AppliedToExpense { get; set; }
}
