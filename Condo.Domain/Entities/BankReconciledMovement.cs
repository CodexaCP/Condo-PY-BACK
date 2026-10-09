using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Un movimiento del libro marcado como conciliado. El libro es virtual (se arma de cobros, gastos e ingresos), asi que el movimiento se
// identifica por su origen (tipo + id del origen) y la cuenta; un movimiento no se concilia dos veces (indice unico entre las marcas vigentes).
// Se guarda una foto de la fecha, el importe y la descripcion al marcar para detectar si cambio despues. Desmarcar no borra: queda quien y
// cuando (IsDeleted marca la marca como deshecha).
public class BankReconciledMovement : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid AccountId { get; set; }
    public Guid ReconciliationId { get; set; }

    // LedgerSourceType (1 cobro, 2 gasto, 3 ingreso, 4 nota de credito de proveedor) y el id del origen.
    public int SourceType { get; set; }
    public Guid SourceId { get; set; }

    public DateOnly Date { get; set; }
    // Con signo: entradas positivas y salidas negativas.
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;

    public Guid MarkedByUserId { get; set; }
    public DateTime MarkedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UnmarkedAtUtc { get; set; }
    public Guid? UnmarkedByUserId { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public FinancialAccount? Account { get; set; }
    public BankReconciliation? Reconciliation { get; set; }
}
