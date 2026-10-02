using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Arma los reportes del modulo "Finanzas del edificio" (movimientos, flujo de caja, presupuesto, presupuesto vs. real y fondo de
/// reserva) a partir del libro derivado. Lo usan los endpoints de consulta y la exportacion a Excel, asi que ambos muestran
/// exactamente los mismos numeros y aplican las mismas reglas de rango. Los errores de parametros se devuelven como texto.
/// </summary>
public class FinanceReportService(ICondoDbContext dbContext, FinanceLedgerService ledger, FinanceBudgetService budgets)
{
    /// <summary>Filtros de los movimientos (el mismo conjunto que la pantalla).</summary>
    public sealed record MovementsQuery(
        DateOnly? From,
        DateOnly? To,
        Guid? AccountId = null,
        bool Unassigned = false,
        Guid? CategoryId = null,
        LedgerDirection? Direction = null,
        bool NewestFirst = true,
        int Page = 1,
        int PageSize = 50,
        bool Unpaged = false);

    // Mes pedido (por defecto, el actual), acotado entre el mes de arranque y el mes actual.
    public static DateOnly ResolveMonth(int? year, int? month, DateOnly today, DateOnly start, out string? error)
    {
        error = null;
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
        {
            error = "El año o el mes no son válidos.";
            return today;
        }

        var requested = new DateOnly(year ?? today.Year, month ?? today.Month, 1);
        var current = new DateOnly(today.Year, today.Month, 1);
        var first = new DateOnly(start.Year, start.Month, 1);
        if (requested > current) requested = current;
        if (requested < first) requested = first;
        return requested;
    }

    /// <summary>Movimientos del rango (por defecto, el mes en curso), con su saldo inicial cuando no se filtra por rubro ni por sentido.</summary>
    public async Task<(FinanceMovementsPageDto? Page, string? Error)> MovementsAsync(
        LedgerContext ctx, MovementsQuery query, CancellationToken cancellationToken)
    {
        var today = FinancePeriods.Today();
        var end = query.To is null || query.To.Value > today ? today : query.To.Value;
        var begin = query.From ?? new DateOnly(end.Year, end.Month, 1);
        if (begin < ctx.StartDate)
        {
            begin = ctx.StartDate;
        }

        if (begin > end)
        {
            return (null, "La fecha desde no puede ser posterior a la fecha hasta (y el libro empieza en la fecha de arranque).");
        }

        if (end.DayNumber - begin.DayNumber > FinanceLedgerService.MaxRowsRangeDays)
        {
            return (null, $"El rango no puede superar los {FinanceLedgerService.MaxRowsRangeDays} días. Para períodos más largos usá el flujo de caja.");
        }

        if (query.AccountId.HasValue && !ctx.Accounts.Any(a => a.Id == query.AccountId.Value))
        {
            return (null, "La cuenta no existe en este edificio.");
        }

        if (query.CategoryId.HasValue && !ctx.ById.ContainsKey(query.CategoryId.Value))
        {
            return (null, "El rubro no existe en este edificio.");
        }

        var rows = await ledger.GetRowsAsync(ctx, begin, end, cancellationToken);

        // Saldo del alcance filtrado al comienzo del rango (solo cuando no se filtra por rubro ni por sentido).
        decimal? opening = null;
        if (!query.CategoryId.HasValue && !query.Direction.HasValue)
        {
            var before = begin > ctx.StartDate
                ? await ledger.GetBucketsAsync(ctx, ctx.StartDate, begin.AddDays(-1), cancellationToken)
                : [];

            static decimal Net(IEnumerable<LedgerBucket> b) => b.Sum(x => x.Direction == LedgerDirection.In ? x.Amount : -x.Amount);

            if (query.AccountId.HasValue)
            {
                opening = ctx.Accounts.First(a => a.Id == query.AccountId.Value).OpeningBalance + Net(before.Where(b => b.AccountId == query.AccountId.Value));
            }
            else if (query.Unassigned)
            {
                opening = Net(before.Where(b => b.AccountId == null));
            }
            else
            {
                opening = ctx.TotalOpening + Net(before);
            }
        }

        return (FinanceReportBuilder.MovementsPage(
            ctx, rows, begin, end, query.AccountId, query.Unassigned, query.CategoryId, query.Direction, query.NewestFirst,
            query.Page, query.PageSize, opening, query.Unpaged), null);
    }

    /// <summary>Flujo de caja del ejercicio (por defecto, el actual), por rubro y mes, hasta hoy.</summary>
    public async Task<(FinanceCashFlowDto? Dto, string? Error)> CashFlowAsync(LedgerContext ctx, int? fiscalYear, CancellationToken cancellationToken)
    {
        var today = FinancePeriods.Today();
        var fy = fiscalYear ?? FinancePeriods.FiscalYearOf(today, ctx.FiscalYearStartMonth);
        if (fy is < 2000 or > 2100)
        {
            return (null, "El ejercicio no es válido.");
        }

        var (_, fiscalEnd) = FinancePeriods.FiscalYearRange(fy, ctx.FiscalYearStartMonth);
        var asOf = fiscalEnd > today ? today : fiscalEnd;

        var buckets = await ledger.GetBucketsAsync(ctx, ctx.StartDate, asOf, cancellationToken);
        return (FinanceReportBuilder.CashFlow(ctx, buckets, fy, asOf), null);
    }

    /// <summary>Grilla del presupuesto del ejercicio (rubros de ingresos y gastos por mes).</summary>
    public async Task<FinanceBudgetDto> BudgetAsync(Guid buildingId, int fiscalYear, int startMonth, int affected, CancellationToken cancellationToken)
    {
        var categories = await dbContext.LedgerCategories.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .ToListAsync(cancellationToken);
        var months = FinancePeriods.FiscalMonths(fiscalYear, startMonth);
        var cells = await budgets.GetCellsAsync(
            buildingId, new DateOnly(months[0].Year, months[0].Month, 1), new DateOnly(months[11].Year, months[11].Month, 1), cancellationToken);

        var dto = FinanceBudgetCalculator.BuildBudget(buildingId, fiscalYear, startMonth, categories, cells);
        dto.AffectedCells = affected;
        return dto;
    }

    /// <summary>Presupuesto contra lo real de un mes (por defecto, el actual) y del acumulado del ejercicio.</summary>
    public async Task<(FinanceBudgetVsActualDto? Dto, string? Error)> BudgetVsActualAsync(
        LedgerContext ctx, int? year, int? month, CancellationToken cancellationToken)
    {
        var today = FinancePeriods.Today();
        var monthStart = ResolveMonth(year, month, today, ctx.StartDate, out var error);
        if (error is not null)
        {
            return (null, error);
        }

        var asOf = FinancePeriods.EndOfMonth(monthStart.Year, monthStart.Month);
        if (asOf > today)
        {
            asOf = today;
        }

        var fiscalYear = FinancePeriods.FiscalYearOf(monthStart, ctx.FiscalYearStartMonth);
        var (fiscalStart, _) = FinancePeriods.FiscalYearRange(fiscalYear, ctx.FiscalYearStartMonth);

        var actuals = await budgets.GetActualsAsync(ctx, fiscalStart, asOf, cancellationToken);
        var cells = await budgets.GetCellsAsync(ctx.BuildingId, fiscalStart, monthStart, cancellationToken);

        return (FinanceBudgetCalculator.BuildVsActual(ctx, monthStart.Year, monthStart.Month, asOf, cells, actuals), null);
    }

    /// <summary>Libro del fondo de reserva: aportes y usos mes a mes desde el arranque y los movimientos del rango (por defecto, el ultimo ano).</summary>
    public async Task<(FinanceReserveFundDto? Dto, string? Error)> ReserveFundAsync(
        LedgerContext ctx, DateOnly? from, DateOnly? to, CancellationToken cancellationToken, bool unpagedMovements = false)
    {
        var today = FinancePeriods.Today();
        var end = to is null || to.Value > today ? today : to.Value;
        var begin = from ?? end.AddDays(-364);
        if (begin < ctx.StartDate)
        {
            begin = ctx.StartDate;
        }

        if (begin > end)
        {
            return (null, "La fecha desde no puede ser posterior a la fecha hasta (y el libro empieza en la fecha de arranque).");
        }

        if (end.DayNumber - begin.DayNumber > FinanceLedgerService.MaxRowsRangeDays)
        {
            return (null, $"El rango no puede superar los {FinanceLedgerService.MaxRowsRangeDays} días.");
        }

        var percentage = await dbContext.Buildings.AsNoTracking()
            .Where(x => x.Id == ctx.BuildingId)
            .Select(x => x.ReserveFundPercentage)
            .FirstOrDefaultAsync(cancellationToken);

        var buckets = await ledger.GetBucketsAsync(ctx, ctx.StartDate, today, cancellationToken);
        var fund = FinanceReportBuilder.FundAccountOf(ctx);

        var movements = new FinanceMovementsPageDto { From = begin, To = end, Page = 1, PageSize = 500 };
        if (fund is not null)
        {
            var rows = await ledger.GetRowsAsync(ctx, begin, end, cancellationToken);
            var before = begin > ctx.StartDate
                ? await ledger.GetBucketsAsync(ctx, ctx.StartDate, begin.AddDays(-1), cancellationToken)
                : [];
            var opening = fund.OpeningBalance + before.Where(b => b.AccountId == fund.Id)
                .Sum(b => b.Direction == LedgerDirection.In ? b.Amount : -b.Amount);

            movements = FinanceReportBuilder.MovementsPage(ctx, rows, begin, end, fund.Id, false, null, null, true, 1, 500, opening, unpagedMovements);
        }

        return (FinanceReportBuilder.ReserveFund(ctx, buckets, today, percentage, movements), null);
    }

    /// <summary>Saldos por cuenta hasta una fecha (por defecto, hoy).</summary>
    public async Task<FinanceBalancesDto> BalancesAsync(LedgerContext ctx, DateOnly? asOf, CancellationToken cancellationToken)
    {
        var today = FinancePeriods.Today();
        var cutoff = asOf is null || asOf.Value > today ? today : asOf.Value;
        var buckets = await ledger.GetBucketsAsync(ctx, ctx.StartDate, cutoff, cancellationToken);
        return FinanceReportBuilder.BuildBalances(ctx, buckets, cutoff, ctx.Resolved.DefaultId);
    }
}
