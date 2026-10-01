using Condo.Application.Services;
using Condo.Domain.Enums;

namespace Condo.Application.Models;

// ── Presupuesto ───────────────────────────────────────────────────────────────

public class FinanceBudgetRowDto
{
    public Guid CategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public LedgerCategoryType Type { get; set; }
    public bool IsActive { get; set; }
    // Un importe por mes del ejercicio (en el mismo orden que Months).
    public IReadOnlyList<decimal> Amounts { get; set; } = [];
    public decimal Total { get; set; }
}

public class FinanceBudgetDto
{
    public Guid BuildingId { get; set; }
    public int FiscalYear { get; set; }
    public DateOnly FiscalYearStart { get; set; }
    public DateOnly FiscalYearEnd { get; set; }
    public IReadOnlyList<FinanceMonthRefDto> Months { get; set; } = [];
    public IReadOnlyList<FinanceBudgetRowDto> Rows { get; set; } = [];
    public IReadOnlyList<decimal> TotalIncome { get; set; } = [];
    public IReadOnlyList<decimal> TotalExpense { get; set; } = [];
    // Celdas que cambio la ultima operacion (copiar o completar); 0 en una consulta.
    public int AffectedCells { get; set; }
}

public class FinanceBudgetCellDto
{
    public Guid CategoryId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
}

public class FinanceBudgetUpdateRequest
{
    public List<FinanceBudgetCellDto> Cells { get; set; } = [];
}

// ── Presupuesto vs. real ──────────────────────────────────────────────────────

public class FinanceBudgetVsActualLineDto
{
    public Guid CategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public LedgerCategoryType Type { get; set; }
    public decimal MonthBudget { get; set; }
    public decimal MonthActual { get; set; }
    // Real - presupuestado.
    public decimal MonthVariance { get; set; }
    // Desvio sobre lo presupuestado (nulo si no hay presupuesto).
    public decimal? MonthVariancePct { get; set; }
    public BudgetStatus MonthStatus { get; set; }
    public decimal YtdBudget { get; set; }
    public decimal YtdActual { get; set; }
    public decimal YtdVariance { get; set; }
    public decimal? YtdVariancePct { get; set; }
    public BudgetStatus YtdStatus { get; set; }
}

public class FinanceBudgetTotalsDto
{
    public decimal MonthBudget { get; set; }
    public decimal MonthActual { get; set; }
    public decimal YtdBudget { get; set; }
    public decimal YtdActual { get; set; }
    public BudgetStatus MonthStatus { get; set; }
    public BudgetStatus YtdStatus { get; set; }
}

public class FinanceBudgetVsActualDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public int FiscalYear { get; set; }
    public DateOnly FiscalYearStart { get; set; }
    public DateOnly FiscalYearEnd { get; set; }
    public DateOnly AsOf { get; set; }
    // Con que criterio se mide lo real de cada tipo (se rotula en pantalla).
    public string ExpenseBasis { get; set; } = "Gastos cargados";
    public string IncomeBasis { get; set; } = "Cobrado";
    // Limite del semaforo: pasado este desvio el renglon pasa de amarillo a rojo.
    public decimal AmberThresholdPct { get; set; }
    public IReadOnlyList<FinanceBudgetVsActualLineDto> IncomeLines { get; set; } = [];
    public IReadOnlyList<FinanceBudgetVsActualLineDto> ExpenseLines { get; set; } = [];
    public FinanceBudgetTotalsDto IncomeTotals { get; set; } = new();
    public FinanceBudgetTotalsDto ExpenseTotals { get; set; } = new();
}

// Resumen para el tablero.
public class FinanceBudgetSummaryDto
{
    public decimal MonthExpenseBudget { get; set; }
    public decimal MonthExpenseActual { get; set; }
    public decimal MonthIncomeBudget { get; set; }
    public decimal MonthIncomeActual { get; set; }
    public int RedCount { get; set; }
    public int AmberCount { get; set; }
    public bool HasBudget { get; set; }
    public IReadOnlyList<FinanceBudgetVsActualLineDto> TopOverBudget { get; set; } = [];
}

// ── Fondo de reserva ──────────────────────────────────────────────────────────

public class FinanceReserveMonthDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Opening { get; set; }
    public decimal Contributions { get; set; }
    public decimal Uses { get; set; }
    public decimal Closing { get; set; }
}

public class FinanceReserveFundDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    // Sin cuenta de fondo de reserva el libro no puede separar sus movimientos.
    public bool HasFundAccount { get; set; }
    public Guid? AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public DateOnly FinanceStartDate { get; set; }
    public DateOnly AsOf { get; set; }
    public decimal? ReserveFundPercentage { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal Contributions { get; set; }
    public decimal Uses { get; set; }
    public decimal Balance { get; set; }
    public IReadOnlyList<FinanceReserveMonthDto> Months { get; set; } = [];
    public FinanceMovementsPageDto Movements { get; set; } = new();
}

// Resumen para el tablero.
public class FinanceReserveSummaryDto
{
    public bool HasFundAccount { get; set; }
    public decimal Balance { get; set; }
    public decimal MonthContributions { get; set; }
    public decimal MonthUses { get; set; }
}
