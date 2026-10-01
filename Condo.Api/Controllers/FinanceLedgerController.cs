using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Libro del modulo "Finanzas del edificio" (fase 2): saldos por cuenta, movimientos, flujo de caja y tablero. Todo es de
/// consulta (lo ven los cuatro roles administrativos con acceso al edificio) y se arma al vuelo con los cobros, gastos e
/// ingresos existentes, hasta hoy y desde la fecha de arranque. Exige la configuracion inicial completa.
/// </summary>
[Route("api/finance")]
public class FinanceLedgerController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinanceLedgerService ledger) : FinanceControllerBase(dbContext, accessScope, tenantContext, gate)
{
    public const string SetupIncompleteCode = "finance_setup_incomplete";

    [HttpGet("balances")]
    public async Task<ActionResult<FinanceBalancesDto>> GetBalances(
        [FromQuery] Guid buildingId, [FromQuery] DateOnly? asOf, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var today = FinancePeriods.Today();
        var cutoff = asOf is null || asOf.Value > today ? today : asOf.Value;
        var buckets = await ledger.GetBucketsAsync(ctx!, ctx!.StartDate, cutoff, cancellationToken);

        return Ok(FinanceReportBuilder.BuildBalances(ctx, buckets, cutoff, ctx.Resolved.DefaultId));
    }

    [HttpGet("movements")]
    public async Task<ActionResult<FinanceMovementsPageDto>> GetMovements(
        [FromQuery] Guid buildingId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? accountId,
        [FromQuery] bool unassigned = false,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] LedgerDirection? direction = null,
        [FromQuery] bool newestFirst = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var today = FinancePeriods.Today();
        var end = to is null || to.Value > today ? today : to.Value;
        var begin = from ?? new DateOnly(end.Year, end.Month, 1);
        if (begin < ctx!.StartDate)
        {
            begin = ctx.StartDate;
        }

        if (begin > end)
        {
            return BadRequest("La fecha desde no puede ser posterior a la fecha hasta (y el libro empieza en la fecha de arranque).");
        }

        if (end.DayNumber - begin.DayNumber > FinanceLedgerService.MaxRowsRangeDays)
        {
            return BadRequest($"El rango no puede superar los {FinanceLedgerService.MaxRowsRangeDays} días. Para períodos más largos usá el flujo de caja.");
        }

        if (accountId.HasValue && !ctx.Accounts.Any(a => a.Id == accountId.Value))
        {
            return BadRequest("La cuenta no existe en este edificio.");
        }

        if (categoryId.HasValue && !ctx.ById.ContainsKey(categoryId.Value))
        {
            return BadRequest("El rubro no existe en este edificio.");
        }

        var rows = await ledger.GetRowsAsync(ctx, begin, end, cancellationToken);

        // Saldo del alcance filtrado al comienzo del rango (solo cuando no se filtra por rubro ni por sentido).
        decimal? opening = null;
        if (!categoryId.HasValue && !direction.HasValue)
        {
            var before = begin > ctx.StartDate
                ? await ledger.GetBucketsAsync(ctx, ctx.StartDate, begin.AddDays(-1), cancellationToken)
                : [];

            decimal Net(IEnumerable<LedgerBucket> b) => b.Sum(x => x.Direction == LedgerDirection.In ? x.Amount : -x.Amount);

            if (accountId.HasValue)
            {
                opening = ctx.Accounts.First(a => a.Id == accountId.Value).OpeningBalance + Net(before.Where(b => b.AccountId == accountId.Value));
            }
            else if (unassigned)
            {
                opening = Net(before.Where(b => b.AccountId == null));
            }
            else
            {
                opening = ctx.TotalOpening + Net(before);
            }
        }

        return Ok(FinanceReportBuilder.MovementsPage(
            ctx, rows, begin, end, accountId, unassigned, categoryId, direction, newestFirst, page, pageSize, opening));
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<FinanceDashboardDto>> GetDashboard(
        [FromQuery] Guid buildingId, [FromQuery] int? year, [FromQuery] int? month, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var today = FinancePeriods.Today();
        var monthStart = ResolveMonth(year, month, today, ctx!.StartDate, out var error);
        if (error is not null)
        {
            return error;
        }

        var asOf = FinancePeriods.EndOfMonth(monthStart.Year, monthStart.Month);
        if (asOf > today)
        {
            asOf = today;
        }

        var buckets = await ledger.GetBucketsAsync(ctx, ctx.StartDate, asOf, cancellationToken);
        var monthBuckets = buckets.Where(b => b.Year == monthStart.Year && b.Month == monthStart.Month).ToList();

        var fiscalYear = FinancePeriods.FiscalYearOf(monthStart, ctx.FiscalYearStartMonth);
        var (fiscalStart, _) = FinancePeriods.FiscalYearRange(fiscalYear, ctx.FiscalYearStartMonth);
        var ytd = buckets.Where(b => new DateOnly(b.Year, b.Month, 1) >= fiscalStart).ToList();

        return Ok(new FinanceDashboardDto
        {
            BuildingId = ctx.BuildingId,
            BuildingName = ctx.BuildingName,
            FinanceStartDate = ctx.StartDate,
            AsOf = asOf,
            Year = monthStart.Year,
            Month = monthStart.Month,
            Balances = FinanceReportBuilder.BuildBalances(ctx, buckets, asOf, ctx.Resolved.DefaultId),
            MonthFlow = FinanceReportBuilder.Flow(monthBuckets),
            MonthIn = FinanceReportBuilder.RubroAmounts(ctx, monthBuckets, LedgerDirection.In),
            MonthOut = FinanceReportBuilder.RubroAmounts(ctx, monthBuckets, LedgerDirection.Out),
            FiscalYear = fiscalYear,
            FiscalYearStart = fiscalStart,
            FiscalYearToDate = FinanceReportBuilder.Flow(ytd),
            Series = FinanceReportBuilder.Series(ctx, buckets, monthStart.Year, monthStart.Month)
        });
    }

    [HttpGet("cash-flow")]
    public async Task<ActionResult<FinanceCashFlowDto>> GetCashFlow(
        [FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var today = FinancePeriods.Today();
        var fy = fiscalYear ?? FinancePeriods.FiscalYearOf(today, ctx!.FiscalYearStartMonth);
        if (fy is < 2000 or > 2100)
        {
            return BadRequest("El ejercicio no es válido.");
        }

        var (_, fiscalEnd) = FinancePeriods.FiscalYearRange(fy, ctx!.FiscalYearStartMonth);
        var asOf = fiscalEnd > today ? today : fiscalEnd;

        var buckets = await ledger.GetBucketsAsync(ctx, ctx.StartDate, asOf, cancellationToken);
        return Ok(FinanceReportBuilder.CashFlow(ctx, buckets, fy, asOf));
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Modulo disponible + configuracion inicial completa. Si falta la configuracion responde 409 con un mensaje claro.
    private async Task<(ActionResult? Denied, LedgerContext? Context)> RequireLedgerAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: false, cancellationToken);
        if (denied is not null)
        {
            return (denied, null);
        }

        var ctx = await ledger.LoadContextAsync(buildingId, cancellationToken);
        if (ctx is null)
        {
            return (StatusCode(StatusCodes.Status409Conflict, new
            {
                error = SetupIncompleteCode,
                message = "Completá la configuración inicial de Finanzas del edificio (fecha de arranque, cuentas y plan de cuentas) para ver los saldos y movimientos."
            }), null);
        }

        return (null, ctx);
    }

    // Mes pedido (por defecto, el actual), acotado entre el mes de arranque y el mes actual.
    private ActionResult? ResolveMonthError(int? year, int? month) =>
        year is < 2000 or > 2100 || month is < 1 or > 12 ? BadRequest("El año o el mes no son válidos.") : null;

    private DateOnly ResolveMonth(int? year, int? month, DateOnly today, DateOnly start, out ActionResult? error)
    {
        error = ResolveMonthError(year, month);
        if (error is not null)
        {
            return today;
        }

        var requested = new DateOnly(year ?? today.Year, month ?? today.Month, 1);
        var current = new DateOnly(today.Year, today.Month, 1);
        var first = new DateOnly(start.Year, start.Month, 1);
        if (requested > current) requested = current;
        if (requested < first) requested = first;
        return requested;
    }
}
