using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Parte de una nota de credito de proveedor (periodo ya publicado) que le toco a una unidad, y el lote de saldo a favor que se le
// acredito a su propietario principal.
public class BuildingExpenseCreditNoteAllocation : CompanyScopedEntity
{
    public Guid CreditNoteId { get; set; }
    public Guid UnitId { get; set; }
    public Guid OwnerId { get; set; }
    public decimal Amount { get; set; }
    // Lote de saldo a favor creado (OwnerCreditMovement, Kind = Generated).
    public Guid OwnerCreditMovementId { get; set; }

    public BuildingExpenseCreditNote? CreditNote { get; set; }
    public Unit? Unit { get; set; }
    public ApplicationUser? Owner { get; set; }
    public OwnerCreditMovement? OwnerCreditMovement { get; set; }
}
