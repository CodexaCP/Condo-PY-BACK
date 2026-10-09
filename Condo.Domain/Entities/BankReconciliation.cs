using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Conciliacion bancaria manual de una cuenta bancaria del edificio a una fecha de corte (Finanzas): se tildan los movimientos del libro que
// figuran en el extracto y se compara el saldo del extracto con el saldo conciliado del libro. Solo cuentas de tipo banco. Hay a lo sumo una
// abierta por cuenta; las cerradas conservan su fecha, el saldo del extracto y la diferencia (siempre cero al cerrar).
public class BankReconciliation : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid AccountId { get; set; }

    // Fecha de corte y saldo que dice el extracto del banco a esa fecha.
    public DateOnly StatementDate { get; set; }
    public decimal StatementBalance { get; set; }
    public string? Notes { get; set; }

    public BankReconciliationStatus Status { get; set; } = BankReconciliationStatus.Open;

    public Guid CreatedByUserId { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }
    // Saldo conciliado del libro al cerrar (igual al del extracto).
    public decimal? ReconciledBalanceAtCompletion { get; set; }

    // Ultima reapertura (una conciliacion cerrada se puede reabrir con motivo si es la ultima de su cuenta).
    public DateTime? ReopenedAtUtc { get; set; }
    public Guid? ReopenedByUserId { get; set; }
    public string? ReopenReason { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public FinancialAccount? Account { get; set; }
    public ICollection<BankReconciledMovement> Movements { get; set; } = new List<BankReconciledMovement>();
}
