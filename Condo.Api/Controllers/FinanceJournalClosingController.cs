using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Cierre del ejercicio del Libro Diario: vista previa (que se cierra, que falta), cerrar y reabrir. Lo ven los cuatro roles administrativos; cierran y
/// reabren solo SuperAdmin y Administrador de empresa. Lo cerrado se corrige con contraasientos (reabrir), nunca se edita.
/// </summary>
[Route("api/finance/journal/closing")]
public class FinanceJournalClosingController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinanceLedgerService ledgerService,
    FiscalYearClosingService closing) : FinanceLedgerControllerBase(dbContext, accessScope, tenantContext, gate, ledgerService)
{
    private bool CanClose => Tenant.IsSuperAdmin || Tenant.IsCompanyAdmin;

    [HttpGet]
    public async Task<ActionResult<FiscalYearClosingDto>> Get([FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        return Ok(await closing.GetAsync(ctx!, fiscalYear, CanClose, cancellationToken));
    }

    [HttpPost("close")]
    public async Task<ActionResult<FiscalYearClosingResultDto>> Close(
        [FromQuery] Guid buildingId, [FromBody] CloseFiscalYearRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;
        if (!CanClose) return FinanceForbidden("journal_forbidden", "Solo el SuperAdmin o el Administrador de empresa pueden cerrar el ejercicio.");

        var companyId = await ResolveCompanyIdAsync(buildingId, cancellationToken);
        if (!companyId.HasValue) return BadRequest(NoCompanyMessage);

        var (result, error, conflict) = await closing.CloseAsync(ctx!, request.FiscalYear, companyId.Value, cancellationToken);
        if (error is null) return Ok(result);

        return conflict ? Conflict(error) : BadRequest(error);
    }

    [HttpPost("reopen")]
    public async Task<ActionResult<FiscalYearClosingResultDto>> Reopen(
        [FromQuery] Guid buildingId, [FromBody] ReopenFiscalYearRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;
        if (!CanClose) return FinanceForbidden("journal_forbidden", "Solo el SuperAdmin o el Administrador de empresa pueden reabrir el ejercicio.");

        var companyId = await ResolveCompanyIdAsync(buildingId, cancellationToken);
        if (!companyId.HasValue) return BadRequest(NoCompanyMessage);

        var (result, error, conflict) = await closing.ReopenAsync(ctx!, request.FiscalYear, request.Reason, companyId.Value, cancellationToken);
        if (error is null) return Ok(result);

        return conflict ? Conflict(error) : BadRequest(error);
    }
}
