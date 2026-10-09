using Condo.Application.Services;
using Condo.Domain.Enums;

namespace Condo.Application.Models;

// ── Conciliacion bancaria manual ──────────────────────────────────────────────

/// <summary>Una cuenta bancaria del edificio con el estado de su conciliacion.</summary>
public class ReconciliationAccountDto
{
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    // Saldo del libro a hoy.
    public decimal BookBalance { get; set; }
    // Ultima conciliacion cerrada.
    public DateOnly? LastCompletedDate { get; set; }
    public decimal? LastStatementBalance { get; set; }
    // Conciliacion abierta, si hay una.
    public Guid? OpenReconciliationId { get; set; }
    public DateOnly? OpenStatementDate { get; set; }
}

public class ReconciliationOverviewDto
{
    public Guid BuildingId { get; set; }
    public bool CanEdit { get; set; }
    public IReadOnlyList<ReconciliationAccountDto> Accounts { get; set; } = [];
}

public class ReconciliationSummaryDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public DateOnly StatementDate { get; set; }
    public decimal StatementBalance { get; set; }
    public BankReconciliationStatus Status { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? CompletedByName { get; set; }
    public decimal? ReconciledBalanceAtCompletion { get; set; }
    public int MarkedCount { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Un movimiento del libro en la pantalla de conciliacion (marcado o pendiente).</summary>
public class ReconciliationMovementDto
{
    // LedgerSourceType del origen (1 cobro, 2 gasto, 3 ingreso, 4 nota de credito de proveedor) y su id.
    public LedgerSourceType SourceType { get; set; }
    public Guid SourceId { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ThirdParty { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    // Con signo: entradas positivas y salidas negativas. En un movimiento marcado, el importe vigente del libro.
    public decimal Amount { get; set; }
    public LedgerDirection Direction { get; set; }
    // Solo en los marcados.
    public bool IsMarked { get; set; }
    public Guid? ReconciliationId { get; set; }
    // True si lo marco esta conciliacion (los de conciliaciones cerradas anteriores no se tocan desde esta).
    public bool InThisReconciliation { get; set; }
    // Ok, Changed (el importe cambio despues de marcarlo) o Missing (ya no figura en el libro a esa fecha).
    public string State { get; set; } = "Ok";
    public decimal? MarkedAmount { get; set; }
    public DateTime? MarkedAtUtc { get; set; }
    public string? MarkedByName { get; set; }
}

public class ReconciliationWorkspaceDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public DateOnly StatementDate { get; set; }
    public decimal StatementBalance { get; set; }
    public string? Notes { get; set; }
    public BankReconciliationStatus Status { get; set; }
    public bool CanEdit { get; set; }

    // Saldo inicial de la cuenta y saldo del libro a la fecha de corte (todo lo que el libro registro hasta ahi).
    public decimal OpeningBalance { get; set; }
    public decimal BookBalance { get; set; }
    // Saldo inicial mas lo ya marcado como conciliado (en todas las conciliaciones de la cuenta, hasta la fecha de corte).
    public decimal ReconciledBalance { get; set; }
    // Saldo del extracto menos el saldo conciliado. Cero = conciliada.
    public decimal Difference { get; set; }
    public bool IsBalanced { get; set; }

    // Lo del libro hasta la fecha de corte que todavia no se marco: puede ser lo que el banco aun no registro (cheques o depositos en transito).
    public decimal PendingIn { get; set; }
    public decimal PendingOut { get; set; }
    public int PendingCount { get; set; }
    public bool PendingTruncated { get; set; }

    // Marcados cuyo importe cambio despues, o que ya no figuran en el libro: hay que revisarlos (los que faltan impiden cerrar).
    public int ChangedCount { get; set; }
    public int MissingCount { get; set; }
    public IReadOnlyList<string> Alerts { get; set; } = [];

    public IReadOnlyList<ReconciliationMovementDto> Marked { get; set; } = [];
    public IReadOnlyList<ReconciliationMovementDto> Pending { get; set; } = [];
}

public class StartReconciliationRequest
{
    public Guid BuildingId { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly StatementDate { get; set; }
    public decimal StatementBalance { get; set; }
    public string? Notes { get; set; }
}

public class UpdateReconciliationRequest
{
    public DateOnly StatementDate { get; set; }
    public decimal StatementBalance { get; set; }
    public string? Notes { get; set; }
}

public class ReconciliationItemRef
{
    public LedgerSourceType SourceType { get; set; }
    public Guid SourceId { get; set; }
}

public class ReconciliationItemsRequest
{
    public List<ReconciliationItemRef> Items { get; set; } = [];
}

public class ReopenReconciliationRequest
{
    public string Reason { get; set; } = string.Empty;
}
