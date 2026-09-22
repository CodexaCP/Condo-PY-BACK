using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

/// <summary>
/// Historial del saldo a favor de un propietario. "Generated" es un lote de saldo (con el comprobante
/// de pago del que salio y lo que aun queda sin usar); "Applied" es cada porcion de un lote aplicada
/// a un cargo, con el pago generado, el cargo y la forma de aplicacion.
/// </summary>
public class OwnerCreditMovement : CompanyScopedEntity
{
    public Guid OwnerId { get; set; }
    public OwnerCreditMovementKind Kind { get; set; }
    public decimal Amount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string? SourceReference { get; set; }
    public Guid? OwnerPaymentId { get; set; }
    public Guid? CreditNoteId { get; set; }
    public CreditApplyMode? ApplyMode { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? ExpenseChargeId { get; set; }
    public string Description { get; set; } = string.Empty;

    public CreditNote? CreditNote { get; set; }
}
