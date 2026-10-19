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

// ── Cierre del ejercicio ──────────────────────────────────────────────────────

/// <summary>Una cuenta de resultado (ingreso o gasto) que el cierre del ejercicio salda.</summary>
public class FiscalClosingAccountDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    // Income (ingreso) o Expense (gasto).
    public string Type { get; set; } = string.Empty;
    // Importe del ejercicio en positivo (lo ganado o lo gastado).
    public decimal Amount { get; set; }
}

public class FiscalYearClosingDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public int FiscalYear { get; set; }
    public IReadOnlyList<int> FiscalYears { get; set; } = [];
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public bool IsClosed { get; set; }
    // Numeros de los asientos de cierre vigentes (si esta cerrado).
    public IReadOnlyList<string> ClosingLabels { get; set; } = [];
    // Se puede cerrar ahora; si no, por que no.
    public bool CanClose { get; set; }
    public IReadOnlyList<string> Blockers { get; set; } = [];
    public IReadOnlyList<FiscalClosingAccountDto> Accounts { get; set; } = [];
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    // Ingresos menos gastos: positivo = superavit, negativo = deficit.
    public decimal Result { get; set; }
    // Cuentas del plan que reciben el resultado (por defecto 3.2.02 y 3.2.01).
    public string? ResultAccount { get; set; }
    public string? EquityAccount { get; set; }
    // Se puede reabrir (esta cerrado y no hay un ejercicio posterior cerrado).
    public bool CanReopen { get; set; }
    public bool CanEdit { get; set; }
}

public class CloseFiscalYearRequest
{
    public int FiscalYear { get; set; }
}

public class ReopenFiscalYearRequest
{
    public int FiscalYear { get; set; }
    public string? Reason { get; set; }
}

public class FiscalYearClosingResultDto
{
    public int CreatedCount { get; set; }
    public string? FirstLabel { get; set; }
    public string? LastLabel { get; set; }
}

// ── Estados: balance general (situacion patrimonial) y estado de resultados ────

public class StatementAccountDto
{
    public string Code { get; set; } = string.Empty;
    public string? ExternalCode { get; set; }
    public string Name { get; set; } = string.Empty;
    // En positivo, con el signo natural de la cuenta (el activo por su saldo deudor; pasivo, fondos, ingresos por su saldo acreedor; gastos por su saldo deudor).
    public decimal Amount { get; set; }
}

public class JournalStatementsDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public int FiscalYear { get; set; }
    public IReadOnlyList<int> FiscalYears { get; set; } = [];
    public DateOnly From { get; set; }
    public DateOnly AsOf { get; set; }
    public bool YearClosed { get; set; }
    public IReadOnlyList<StatementAccountDto> Assets { get; set; } = [];
    public IReadOnlyList<StatementAccountDto> Liabilities { get; set; } = [];
    public IReadOnlyList<StatementAccountDto> Equity { get; set; } = [];
    public IReadOnlyList<StatementAccountDto> Income { get; set; } = [];
    public IReadOnlyList<StatementAccountDto> Expenses { get; set; } = [];
    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilities { get; set; }
    public decimal TotalEquity { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    // Ingresos menos gastos del ejercicio hasta la fecha (sin contar los asientos de cierre).
    public decimal Result { get; set; }
    // Resultado que todavia no paso a fondos (ejercicio sin cerrar): se suma al patrimonio para que el balance cuadre.
    public decimal PendingResult { get; set; }
    // Activo = pasivo + fondos + resultado pendiente.
    public bool IsBalanced { get; set; }
    public decimal Difference { get; set; }
    // Hay renglones de una clase que no se pudo determinar (asientos viejos de una cuenta que ya no esta en el plan).
    public decimal UnclassifiedNet { get; set; }
}
