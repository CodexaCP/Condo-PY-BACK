using Condo.Application.Services;
using Condo.Domain.Enums;

namespace Condo.Application.Models;

// ── Saldos por cuenta ─────────────────────────────────────────────────────────

public class FinanceAccountBalanceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public FinancialAccountType Type { get; set; }
    public bool IsActive { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal Inflows { get; set; }
    public decimal Outflows { get; set; }
    // Saldo inicial + entradas - salidas desde la fecha de arranque.
    public decimal Balance { get; set; }
}

public class FinanceBalancesDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public DateOnly FinanceStartDate { get; set; }
    public DateOnly AsOf { get; set; }
    public IReadOnlyList<FinanceAccountBalanceDto> Accounts { get; set; } = [];
    // Movimientos que no se pudieron asignar a ninguna cuenta (neto: entradas - salidas).
    public decimal UnassignedNet { get; set; }
    public decimal TotalBalance { get; set; }
    public decimal CashBalance { get; set; }
    public decimal BankBalance { get; set; }
    public decimal ReserveFundBalance { get; set; }
    public Guid? DefaultAccountId { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = [];
}

// ── Movimientos ───────────────────────────────────────────────────────────────

public class FinanceMovementDto
{
    public DateOnly Date { get; set; }
    public Guid? AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public LedgerDirection Direction { get; set; }
    // Siempre positivo; el sentido lo da Direction.
    public decimal Amount { get; set; }
    // Con signo: entradas positivas y salidas negativas.
    public decimal SignedAmount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ThirdParty { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public LedgerSourceType SourceType { get; set; }
    public Guid SourceId { get; set; }
    // Saldo corrido; solo cuando no se filtra por rubro ni por sentido.
    public decimal? RunningBalance { get; set; }
}

public class FinanceMovementsPageDto
{
    public IReadOnlyList<FinanceMovementDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    // Saldo al comienzo del rango y al final, para la cuenta filtrada o para todas; nulo si se filtra por rubro o sentido.
    public decimal? OpeningBalance { get; set; }
    public decimal? ClosingBalance { get; set; }
}

// ── Flujo de caja y tablero ───────────────────────────────────────────────────

public class FinanceFlowDto
{
    public decimal In { get; set; }
    public decimal Out { get; set; }
    public decimal Net { get; set; }
}

public class FinanceRubroAmountDto
{
    public Guid? CategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class FinanceMonthPointDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal In { get; set; }
    public decimal Out { get; set; }
    public decimal Net { get; set; }
    // Saldo total (todas las cuentas) al final del mes.
    public decimal EndBalance { get; set; }
}

public class FinanceDashboardDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public DateOnly FinanceStartDate { get; set; }
    // Hasta cuando se cuentan los movimientos: el fin del mes elegido o hoy, lo que ocurra primero.
    public DateOnly AsOf { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public FinanceBalancesDto Balances { get; set; } = new();
    public FinanceFlowDto MonthFlow { get; set; } = new();
    public IReadOnlyList<FinanceRubroAmountDto> MonthIn { get; set; } = [];
    public IReadOnlyList<FinanceRubroAmountDto> MonthOut { get; set; } = [];
    public int FiscalYear { get; set; }
    public DateOnly FiscalYearStart { get; set; }
    public FinanceFlowDto FiscalYearToDate { get; set; } = new();
    public IReadOnlyList<FinanceMonthPointDto> Series { get; set; } = [];
    // Fase 3: presupuesto del mes contra lo real y fondo de reserva.
    public FinanceBudgetSummaryDto Budget { get; set; } = new();
    public FinanceReserveSummaryDto ReserveFund { get; set; } = new();
}

public class FinanceCashFlowLineDto
{
    public Guid? CategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public LedgerDirection Direction { get; set; }
    // Un importe por mes del ejercicio (en el mismo orden que Months), positivos.
    public IReadOnlyList<decimal> Amounts { get; set; } = [];
    public decimal Total { get; set; }
}

public class FinanceCashFlowDto
{
    public Guid BuildingId { get; set; }
    public DateOnly FinanceStartDate { get; set; }
    public int FiscalYear { get; set; }
    public DateOnly FiscalYearStart { get; set; }
    public DateOnly FiscalYearEnd { get; set; }
    public DateOnly AsOf { get; set; }
    public IReadOnlyList<FinanceMonthRefDto> Months { get; set; } = [];
    public IReadOnlyList<FinanceCashFlowLineDto> InLines { get; set; } = [];
    public IReadOnlyList<FinanceCashFlowLineDto> OutLines { get; set; } = [];
    public IReadOnlyList<decimal> TotalIn { get; set; } = [];
    public IReadOnlyList<decimal> TotalOut { get; set; } = [];
    public IReadOnlyList<decimal> Net { get; set; } = [];
    // Saldo total antes del primer mes del ejercicio y al final de cada mes.
    public decimal OpeningBalance { get; set; }
    public IReadOnlyList<decimal> ClosingBalance { get; set; } = [];
}

public class FinanceMonthRefDto
{
    public int Year { get; set; }
    public int Month { get; set; }
}

public class FinanceDefaultAccountRequest
{
    // Nulo = sin cuenta por defecto (el sistema elige el unico banco activo, si hay uno).
    public Guid? AccountId { get; set; }
}
