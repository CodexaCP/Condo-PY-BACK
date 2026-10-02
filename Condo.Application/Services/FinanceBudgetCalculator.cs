using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Application.Services;

public enum BudgetStatus
{
    None = 0,
    Green = 1,
    Amber = 2,
    Red = 3
}

/// <summary>Un importe presupuestado: rubro, mes calendario y monto.</summary>
public sealed record BudgetCell(Guid CategoryId, int Year, int Month, decimal Amount);

/// <summary>
/// Presupuesto y presupuesto vs. real. Logica pura (sin acceso a datos). Lo real de los gastos es lo que se cargo como gasto del
/// edificio en el mes (por fecha del gasto, incluido lo pagado por el fondo de reserva); lo real de los ingresos es lo cobrado.
/// </summary>
public static class FinanceBudgetCalculator
{
    /// <summary>Desvio (como fraccion de lo presupuestado) hasta el cual el semaforo queda en amarillo; mas alla, rojo.</summary>
    public const decimal AmberThreshold = 0.10m;

    public static BudgetStatus Status(LedgerCategoryType type, decimal budget, decimal actual)
    {
        if (budget == 0m && actual == 0m)
        {
            return BudgetStatus.None;
        }

        if (type == LedgerCategoryType.Expense)
        {
            if (actual <= budget)
            {
                return BudgetStatus.Green;
            }

            // Gasto sin presupuesto: todo lo gastado es desvio.
            if (budget == 0m)
            {
                return BudgetStatus.Red;
            }

            return (actual - budget) / budget <= AmberThreshold ? BudgetStatus.Amber : BudgetStatus.Red;
        }

        // Ingresos: preocupa quedar por debajo de lo esperado. Sin ingreso esperado no hay nada que incumplir.
        if (actual >= budget || budget <= 0m)
        {
            return BudgetStatus.Green;
        }

        return (budget - actual) / budget <= AmberThreshold ? BudgetStatus.Amber : BudgetStatus.Red;
    }

    public static decimal? VariancePct(decimal budget, decimal actual) =>
        budget == 0m ? null : decimal.Round((actual - budget) / budget * 100m, 1);

    /// <summary>Rubros que se pueden presupuestar: los de ingresos y gastos que no tienen subrubros (los fondos no llevan presupuesto).</summary>
    public static List<LedgerCategory> BudgetableCategories(IReadOnlyCollection<LedgerCategory> categories)
    {
        var parentIds = categories.Where(c => c.ParentId.HasValue).Select(c => c.ParentId!.Value).ToHashSet();
        return categories
            .Where(c => c.Type is LedgerCategoryType.Income or LedgerCategoryType.Expense && !parentIds.Contains(c.Id))
            .OrderBy(c => c.Code, StringComparer.Ordinal)
            .ToList();
    }

    private static (string Code, string Name) GroupOf(IReadOnlyCollection<LedgerCategory> categories, LedgerCategory c)
    {
        var group = c.ParentId.HasValue ? categories.FirstOrDefault(x => x.Id == c.ParentId.Value) : null;
        return (group?.Code ?? string.Empty, group?.Name ?? string.Empty);
    }

    // ── Grilla del presupuesto ────────────────────────────────────────────────

    public static FinanceBudgetDto BuildBudget(
        Guid buildingId, int fiscalYear, int fiscalYearStartMonth, IReadOnlyCollection<LedgerCategory> categories, IReadOnlyCollection<BudgetCell> cells)
    {
        var months = FinancePeriods.FiscalMonths(fiscalYear, fiscalYearStartMonth);
        var (start, end) = FinancePeriods.FiscalYearRange(fiscalYear, fiscalYearStartMonth);

        decimal CellAmount(Guid categoryId, (int Year, int Month) m) =>
            cells.Where(c => c.CategoryId == categoryId && c.Year == m.Year && c.Month == m.Month).Sum(c => c.Amount);

        var rows = new List<FinanceBudgetRowDto>();
        foreach (var c in BudgetableCategories(categories))
        {
            var amounts = months.Select(m => CellAmount(c.Id, m)).ToArray();
            // Un rubro inactivo solo se muestra si todavia tiene presupuesto cargado.
            if (!c.IsActive && amounts.All(a => a == 0m))
            {
                continue;
            }

            var (groupCode, groupName) = GroupOf(categories, c);
            rows.Add(new FinanceBudgetRowDto
            {
                CategoryId = c.Id,
                Code = c.Code,
                Name = c.Name,
                GroupCode = groupCode,
                GroupName = groupName,
                Type = c.Type,
                IsActive = c.IsActive,
                Amounts = amounts,
                Total = amounts.Sum()
            });
        }

        decimal[] Total(LedgerCategoryType type) =>
            Enumerable.Range(0, 12).Select(i => rows.Where(r => r.Type == type).Sum(r => r.Amounts[i])).ToArray();

        return new FinanceBudgetDto
        {
            BuildingId = buildingId,
            FiscalYear = fiscalYear,
            FiscalYearStart = start,
            FiscalYearEnd = end,
            Months = months.Select(m => new FinanceMonthRefDto { Year = m.Year, Month = m.Month }).ToList(),
            Rows = rows,
            TotalIncome = Total(LedgerCategoryType.Income),
            TotalExpense = Total(LedgerCategoryType.Expense)
        };
    }

    // ── Presupuesto vs. real ──────────────────────────────────────────────────

    public static FinanceBudgetVsActualDto BuildVsActual(
        LedgerContext ctx,
        int year,
        int month,
        DateOnly asOf,
        IReadOnlyCollection<BudgetCell> cells,
        IReadOnlyDictionary<(int Year, int Month, string RubroKey), decimal> actuals)
    {
        var fiscalYear = FinancePeriods.FiscalYearOf(new DateOnly(year, month, 1), ctx.FiscalYearStartMonth);
        var (fiscalStart, fiscalEnd) = FinancePeriods.FiscalYearRange(fiscalYear, ctx.FiscalYearStartMonth);

        // Meses del ejercicio hasta el elegido (acumulado).
        var ytdMonths = FinancePeriods.FiscalMonths(fiscalYear, ctx.FiscalYearStartMonth)
            .Where(m => new DateOnly(m.Year, m.Month, 1) <= new DateOnly(year, month, 1))
            .ToList();

        decimal Budget(Guid id, IEnumerable<(int Year, int Month)> months) =>
            months.Sum(m => cells.Where(c => c.CategoryId == id && c.Year == m.Year && c.Month == m.Month).Sum(c => c.Amount));

        decimal Actual(LedgerCategory c, IEnumerable<(int Year, int Month)> months) =>
            months.Sum(m => actuals.GetValueOrDefault((m.Year, m.Month, c.RubroKey)));

        var lines = new List<FinanceBudgetVsActualLineDto>();
        foreach (var c in BudgetableCategories(ctx.Categories.ToList()))
        {
            var monthOnly = new[] { (year, month) };
            var monthBudget = Budget(c.Id, monthOnly);
            var monthActual = Actual(c, monthOnly);
            var ytdBudget = Budget(c.Id, ytdMonths);
            var ytdActual = Actual(c, ytdMonths);

            if (monthBudget == 0m && monthActual == 0m && ytdBudget == 0m && ytdActual == 0m)
            {
                continue;
            }

            var (groupCode, groupName) = GroupOf(ctx.Categories.ToList(), c);
            lines.Add(new FinanceBudgetVsActualLineDto
            {
                CategoryId = c.Id,
                Code = c.Code,
                Name = c.Name,
                GroupCode = groupCode,
                GroupName = groupName,
                Type = c.Type,
                MonthBudget = monthBudget,
                MonthActual = monthActual,
                MonthVariance = monthActual - monthBudget,
                MonthVariancePct = VariancePct(monthBudget, monthActual),
                MonthStatus = Status(c.Type, monthBudget, monthActual),
                YtdBudget = ytdBudget,
                YtdActual = ytdActual,
                YtdVariance = ytdActual - ytdBudget,
                YtdVariancePct = VariancePct(ytdBudget, ytdActual),
                YtdStatus = Status(c.Type, ytdBudget, ytdActual)
            });
        }

        FinanceBudgetTotalsDto Totals(LedgerCategoryType type)
        {
            var own = lines.Where(l => l.Type == type).ToList();
            var t = new FinanceBudgetTotalsDto
            {
                MonthBudget = own.Sum(l => l.MonthBudget),
                MonthActual = own.Sum(l => l.MonthActual),
                YtdBudget = own.Sum(l => l.YtdBudget),
                YtdActual = own.Sum(l => l.YtdActual)
            };
            t.MonthStatus = Status(type, t.MonthBudget, t.MonthActual);
            t.YtdStatus = Status(type, t.YtdBudget, t.YtdActual);
            return t;
        }

        return new FinanceBudgetVsActualDto
        {
            BuildingId = ctx.BuildingId,
            BuildingName = ctx.BuildingName,
            Year = year,
            Month = month,
            FiscalYear = fiscalYear,
            FiscalYearStart = fiscalStart,
            FiscalYearEnd = fiscalEnd,
            AsOf = asOf,
            AmberThresholdPct = AmberThreshold * 100m,
            IncomeLines = lines.Where(l => l.Type == LedgerCategoryType.Income).ToList(),
            ExpenseLines = lines.Where(l => l.Type == LedgerCategoryType.Expense).ToList(),
            IncomeTotals = Totals(LedgerCategoryType.Income),
            ExpenseTotals = Totals(LedgerCategoryType.Expense)
        };
    }

    /// <summary>Resumen del mes para el tablero: totales, cuantos renglones estan en amarillo y rojo y los que mas se pasaron.</summary>
    public static FinanceBudgetSummaryDto Summarize(FinanceBudgetVsActualDto report, bool hasAnyBudget)
    {
        var all = report.IncomeLines.Concat(report.ExpenseLines).ToList();
        return new FinanceBudgetSummaryDto
        {
            HasBudget = hasAnyBudget,
            MonthExpenseBudget = report.ExpenseTotals.MonthBudget,
            MonthExpenseActual = report.ExpenseTotals.MonthActual,
            MonthIncomeBudget = report.IncomeTotals.MonthBudget,
            MonthIncomeActual = report.IncomeTotals.MonthActual,
            RedCount = all.Count(l => l.MonthStatus == BudgetStatus.Red),
            AmberCount = all.Count(l => l.MonthStatus == BudgetStatus.Amber),
            TopOverBudget = report.ExpenseLines
                .Where(l => l.MonthVariance > 0)
                .OrderByDescending(l => l.MonthVariance)
                .Take(5)
                .ToList()
        };
    }
}
