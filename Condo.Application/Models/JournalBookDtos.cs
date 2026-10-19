namespace Condo.Application.Models;

// ── Libros oficiales: Libro Diario imprimible, Libro Mayor y balance de sumas y saldos ──────────────────────────────────────────────────────

/// <summary>Un movimiento de una cuenta en el Libro Mayor: el renglon de un asiento asentado con el saldo acumulado de la cuenta.</summary>
public class JournalLedgerMovementDto
{
    public DateOnly Date { get; set; }
    public Guid EntryId { get; set; }
    // Numero del asiento (2026-000012).
    public string EntryLabel { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    // Saldo acumulado de la cuenta despues de este movimiento (positivo = deudor, negativo = acreedor).
    public decimal Balance { get; set; }
}

public class JournalLedgerAccountDto
{
    public string Code { get; set; } = string.Empty;
    public string? ExternalCode { get; set; }
    public string Name { get; set; } = string.Empty;
    // Saldo antes del periodo (todo lo asentado con fecha anterior): positivo = deudor, negativo = acreedor.
    public decimal OpeningBalance { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal ClosingBalance { get; set; }
    public int MovementCount { get; set; }
    // Solo cuando se pide una cuenta (o en el Excel): los movimientos del periodo en orden.
    public IReadOnlyList<JournalLedgerMovementDto> Movements { get; set; } = [];
}

public class JournalLedgerDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public int FiscalYear { get; set; }
    public IReadOnlyList<int> FiscalYears { get; set; } = [];
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public IReadOnlyList<JournalLedgerAccountDto> Accounts { get; set; } = [];
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
}

public class JournalTrialBalanceRowDto
{
    public string Code { get; set; } = string.Empty;
    public string? ExternalCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal OpeningDebit { get; set; }
    public decimal OpeningCredit { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal ClosingDebit { get; set; }
    public decimal ClosingCredit { get; set; }
}

public class JournalTrialBalanceDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public int FiscalYear { get; set; }
    public IReadOnlyList<int> FiscalYears { get; set; } = [];
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public IReadOnlyList<JournalTrialBalanceRowDto> Rows { get; set; } = [];
    public JournalTrialBalanceRowDto Totals { get; set; } = new();
    // Las sumas del debe y del haber coinciden y los saldos deudores igualan a los acreedores.
    public bool IsBalanced { get; set; }
    // Diferencia entre saldos deudores y acreedores finales (cero si cuadra).
    public decimal Difference { get; set; }
}
