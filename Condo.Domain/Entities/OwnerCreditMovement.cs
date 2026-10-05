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
    // Reserva del marketplace de la que sale este lote de saldo (un solo lote por reserva).
    public Guid? MarketplaceReservationId { get; set; }
    // Nota de credito de proveedor sobre un gasto de la que sale este lote, con el edificio y la unidad de origen. Solo trazabilidad:
    // el lote se consume igual que cualquier otro (no se restringe por edificio).
    public Guid? SupplierCreditNoteId { get; set; }
    // Lote retenido: se dio de baja al propietario principal de la unidad y todavia no hay uno nuevo. No se consume ni cuenta en el saldo
    // de nadie; al asignar el nuevo propietario principal pasa a el.
    public bool OnHold { get; set; }
    public Guid? BuildingId { get; set; }
    public Guid? UnitId { get; set; }
    public CreditApplyMode? ApplyMode { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? ExpenseChargeId { get; set; }
    public string Description { get; set; } = string.Empty;

    public CreditNote? CreditNote { get; set; }
}
