using Condo.Application.Abstractions;
using Condo.Application.Services;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>Lectura de las lineas de presupuesto y de lo real (gastos cargados e ingresos cobrados) por rubro y mes.</summary>
public class FinanceBudgetService(ICondoDbContext dbContext, FinanceLedgerService ledger)
{
    /// <summary>Celdas de presupuesto del edificio entre dos meses (inclusive).</summary>
    public async Task<List<BudgetCell>> GetCellsAsync(Guid buildingId, DateOnly fromMonth, DateOnly toMonth, CancellationToken cancellationToken)
    {
        var fromKey = fromMonth.Year * 100 + fromMonth.Month;
        var toKey = toMonth.Year * 100 + toMonth.Month;

        return await dbContext.BudgetLines.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Year * 100 + x.Month >= fromKey && x.Year * 100 + x.Month <= toKey)
            .Select(x => new BudgetCell(x.CategoryId, x.Year, x.Month, x.Amount))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasAnyBudgetAsync(Guid buildingId, CancellationToken cancellationToken) =>
        await dbContext.BudgetLines.AnyAsync(x => !x.IsDeleted && x.BuildingId == buildingId && x.Amount != 0m, cancellationToken);

    /// <summary>
    /// Gastos cargados por rubro y mes (por fecha del gasto): incluyen lo pagado por el fondo de reserva y el aporte al fondo, porque
    /// el presupuesto de gastos se compara con lo que se cargo como gasto del edificio. Solo desde la fecha de arranque.
    /// </summary>
    public async Task<Dictionary<(int Year, int Month, string RubroKey), decimal>> GetExpenseActualsAsync(
        LedgerContext ctx, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        from = from < ctx.StartDate ? ctx.StartDate : from;
        var result = new Dictionary<(int, int, string), decimal>();
        if (to < from)
        {
            return result;
        }

        var buildingId = ctx.BuildingId;
        var totals = await dbContext.BuildingExpenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.BuildingId == buildingId && e.ExpenseDate >= from && e.ExpenseDate <= to)
            .GroupBy(e => new { e.ExpenseDate.Year, e.ExpenseDate.Month, e.Category, e.LedgerCategoryId })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Category, g.Key.LedgerCategoryId, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        foreach (var t in totals)
        {
            // El gasto que eligio rubro cuenta en ese rubro; el que no, en el de su categoria.
            var key = (t.Year, t.Month, ctx.RubroKeyOf(t.LedgerCategoryId) ?? FinanceChartTemplate.ExpenseKey(t.Category));
            result[key] = result.GetValueOrDefault(key) + t.Amount;
        }

        return result;
    }

    /// <summary>Ingresos cobrados por rubro y mes (cobros a propietarios por tipo de cargo e ingresos propios del edificio), de los rubros de tipo ingreso.</summary>
    public static Dictionary<(int Year, int Month, string RubroKey), decimal> IncomeActuals(LedgerContext ctx, IEnumerable<LedgerBucket> buckets)
    {
        var result = new Dictionary<(int, int, string), decimal>();
        foreach (var b in buckets.Where(b => b.Direction == LedgerDirection.In))
        {
            if (ctx.CategoryFor(b.RubroKey)?.Type != LedgerCategoryType.Income)
            {
                continue;
            }

            var key = (b.Year, b.Month, b.RubroKey);
            result[key] = result.GetValueOrDefault(key) + b.Amount;
        }

        return result;
    }

    /// <summary>Lo real de gastos e ingresos entre dos fechas (los ingresos salen de los agregados del libro).</summary>
    public async Task<Dictionary<(int Year, int Month, string RubroKey), decimal>> GetActualsAsync(
        LedgerContext ctx, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var result = await GetExpenseActualsAsync(ctx, from, to, cancellationToken);
        var buckets = await ledger.GetBucketsAsync(ctx, from, to, cancellationToken);
        foreach (var kv in IncomeActuals(ctx, buckets))
        {
            result[kv.Key] = result.GetValueOrDefault(kv.Key) + kv.Value;
        }

        return result;
    }
}
