using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Operaciones guiadas de Finanzas (hoy: transferencias entre cuentas propias). Las ven los cuatro roles administrativos; las cargan y anulan
/// solo SuperAdmin y Administrador de empresa. Una operacion no se edita: se anula con motivo. Exige la configuracion inicial de Finanzas completa.
/// </summary>
[Route("api/finance/operations")]
public class FinanceOperationsController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinanceLedgerService ledgerService,
    FinanceTransferService transfers) : FinanceLedgerControllerBase(dbContext, accessScope, tenantContext, gate, ledgerService)
{
    private bool CanOperate => Tenant.IsSuperAdmin || Tenant.IsCompanyAdmin;

    [HttpGet("transfers")]
    public async Task<ActionResult<FinanceTransfersDto>> List([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        return Ok(await transfers.ListAsync(ctx!, CanOperate, cancellationToken));
    }

    // Como quedarian los saldos y que asiento saldria, sin guardar nada.
    [HttpPost("transfers/preview")]
    public async Task<ActionResult<FinanceTransferPreviewDto>> Preview(
        [FromQuery] Guid buildingId, [FromBody] CreateFinanceTransferRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;
        if (!CanOperate) return Forbidden();

        return Respond(await transfers.PreviewAsync(ctx!, request, cancellationToken));
    }

    [HttpPost("transfers")]
    public async Task<ActionResult<FinanceTransferDto>> Create(
        [FromQuery] Guid buildingId, [FromBody] CreateFinanceTransferRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;
        if (!CanOperate) return Forbidden();

        var companyId = await ResolveCompanyIdAsync(buildingId, cancellationToken);
        if (!companyId.HasValue) return BadRequest(NoCompanyMessage);

        return Respond(await transfers.CreateAsync(ctx!, request, companyId.Value, cancellationToken));
    }

    [HttpPost("transfers/{id:guid}/void")]
    public async Task<ActionResult<FinanceTransferDto>> Void(
        Guid id, [FromQuery] Guid buildingId, [FromBody] VoidFinanceTransferRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;
        if (!CanOperate) return Forbidden();

        var companyId = await ResolveCompanyIdAsync(buildingId, cancellationToken);
        if (!companyId.HasValue) return BadRequest(NoCompanyMessage);

        return Respond(await transfers.VoidAsync(ctx!, id, request.Reason, companyId.Value, cancellationToken));
    }

    private ObjectResult Forbidden() =>
        FinanceForbidden("operations_forbidden", "Solo el SuperAdmin o el Administrador de empresa pueden cargar y anular operaciones de Finanzas.");

    // Errores con el mismo formato { error, message } que el resto de Finanzas.
    private ActionResult<T> Respond<T>(FinanceOpResult<T> result) =>
        result.Ok ? Ok(result.Value) : StatusCode(result.Status, new { error = result.Code, message = result.Message });
}
