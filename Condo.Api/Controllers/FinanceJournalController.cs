using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Libro Diario: asientos asentados con numero correlativo por edificio y ejercicio. Los ven los cuatro roles administrativos; asientan solo
/// SuperAdmin y Administrador de empresa. Lo asentado no se edita ni se borra. Exige la configuracion inicial de Finanzas completa.
/// </summary>
[Route("api/finance/journal")]
public class FinanceJournalController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinanceLedgerService ledgerService,
    JournalPostingService journal,
    JournalBooksService books) : FinanceLedgerControllerBase(dbContext, accessScope, tenantContext, gate, ledgerService)
{
    private bool CanPost => Tenant.IsSuperAdmin || Tenant.IsCompanyAdmin;

    [HttpGet]
    public async Task<ActionResult<JournalDto>> Get(
        [FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int? number, [FromQuery] int page = 1, [FromQuery] int pageSize = JournalPostingService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        var result = await journal.GetAsync(ctx!, fiscalYear, from, to, number, page, pageSize, CanPost, cancellationToken);
        result.SuggestedStartFolio = await books.SuggestedStartFolioAsync(buildingId, cancellationToken);
        return Ok(result);
    }

    // Asientos ya asentados que cambiaron desde entonces (se corrigen con contraasiento al asentar).
    [HttpGet("differences")]
    public async Task<ActionResult<JournalDifferencesDto>> Differences([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        return Ok(await journal.DifferencesAsync(ctx!, cancellationToken));
    }

    [HttpGet("preview")]
    public async Task<ActionResult<JournalPreviewDto>> Preview(
        [FromQuery] Guid buildingId, [FromQuery] DateOnly through, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        var (preview, error) = await journal.PreviewAsync(ctx!, through, cancellationToken);
        return error is not null ? BadRequest(error) : Ok(preview);
    }

    [HttpPost("post")]
    public async Task<ActionResult<PostJournalResultDto>> Post(
        [FromQuery] Guid buildingId, [FromBody] PostJournalRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;
        if (!CanPost)
        {
            return FinanceForbidden("journal_forbidden", "Solo el SuperAdmin o el Administrador de empresa pueden asentar el Libro Diario.");
        }

        var companyId = await ResolveCompanyIdAsync(buildingId, cancellationToken);
        if (!companyId.HasValue) return BadRequest(NoCompanyMessage);

        var (result, error, conflict) = await journal.PostAsync(ctx!, request.Through, companyId.Value, cancellationToken);
        if (error is null) return Ok(result);

        return conflict ? Conflict(error) : BadRequest(error);
    }
}
