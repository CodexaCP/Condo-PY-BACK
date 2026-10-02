using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Libro del modulo "Finanzas del edificio" (fases 2 y 3): saldos por cuenta, movimientos, flujo de caja, tablero y libro del
/// fondo de reserva. Todo es de consulta (lo ven los cuatro roles administrativos con acceso al edificio) y se arma al vuelo con
/// los cobros, gastos e ingresos existentes, hasta hoy y desde la fecha de arranque. Exige la configuracion inicial completa.
/// </summary>
[Route("api/finance")]
public class FinanceLedgerController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinanceLedgerService ledgerService,
    FinanceBudgetService budgets,
    FinanceReportService reports) : FinanceLedgerControllerBase(dbContext, accessScope, tenantContext, gate, ledgerService)
{
    [HttpGet("balances")]
    public async Task<ActionResult<FinanceBalancesDto>> GetBalances(
        [FromQuery] Guid buildingId, [FromQuery] DateOnly? asOf, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        return Ok(await reports.BalancesAsync(ctx!, asOf, cancellationToken));
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

        var (result, error) = await reports.MovementsAsync(
            ctx!, new FinanceReportService.MovementsQuery(from, to, accountId, unassigned, categoryId, direction, newestFirst, page, pageSize), cancellationToken);
        return error is null ? Ok(result) : BadRequest(error);
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
            return BadRequest(error);
        }

        var asOf = FinancePeriods.EndOfMonth(monthStart.Year, monthStart.Month);
        if (asOf > today)
        {
            asOf = today;
        }

        var buckets = await Ledger.GetBucketsAsync(ctx, ctx.StartDate, asOf, cancellationToken);
        var monthBuckets = buckets.Where(b => b.Year == monthStart.Year && b.Month == monthStart.Month).ToList();

        var fiscalYear = FinancePeriods.FiscalYearOf(monthStart, ctx.FiscalYearStartMonth);
        var (fiscalStart, _) = FinancePeriods.FiscalYearRange(fiscalYear, ctx.FiscalYearStartMonth);
        var ytd = buckets.Where(b => new DateOnly(b.Year, b.Month, 1) >= fiscalStart).ToList();

        // Fase 3: presupuesto del mes contra lo real (gastos cargados e ingresos cobrados) y fondo de reserva.
        var actuals = await budgets.GetExpenseActualsAsync(ctx, fiscalStart, asOf, cancellationToken);
        foreach (var kv in FinanceBudgetService.IncomeActuals(ctx, buckets.Where(b => new DateOnly(b.Year, b.Month, 1) >= fiscalStart)))
        {
            actuals[kv.Key] = actuals.GetValueOrDefault(kv.Key) + kv.Value;
        }

        var cells = await budgets.GetCellsAsync(buildingId, fiscalStart, monthStart, cancellationToken);
        var vsActual = FinanceBudgetCalculator.BuildVsActual(ctx, monthStart.Year, monthStart.Month, asOf, cells, actuals);

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
            Series = FinanceReportBuilder.Series(ctx, buckets, monthStart.Year, monthStart.Month),
            Budget = FinanceBudgetCalculator.Summarize(vsActual, cells.Any(c => c.Amount != 0m)),
            ReserveFund = FinanceReportBuilder.ReserveSummary(ctx, buckets, monthStart.Year, monthStart.Month)
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

        var (result, error) = await reports.CashFlowAsync(ctx!, fiscalYear, cancellationToken);
        return error is null ? Ok(result) : BadRequest(error);
    }

    // Libro del fondo de reserva: aportes y usos mes a mes desde el arranque, y los movimientos del rango (por defecto, el ultimo ano).
    [HttpGet("reserve-fund")]
    public async Task<ActionResult<FinanceReserveFundDto>> GetReserveFund(
        [FromQuery] Guid buildingId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var (result, error) = await reports.ReserveFundAsync(ctx!, from, to, cancellationToken);
        return error is null ? Ok(result) : BadRequest(error);
    }
}
