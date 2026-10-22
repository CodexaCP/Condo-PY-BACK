using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Conciliacion bancaria manual (Finanzas): por cuenta bancaria, se tildan los movimientos del libro que aparecen en el extracto y se compara el
/// saldo del extracto con el saldo conciliado. La ven los cuatro roles administrativos; la arman, cierran y reabren solo SuperAdmin y Administrador
/// de empresa (control interno: quien concilia no es quien carga los movimientos), y el Encargado del edificio solo si el edificio lo habilito con
/// el interruptor FinanceSettings.ManagerCanReconcile (lo cambian SuperAdmin y Administrador de empresa). Exige la configuracion inicial de Finanzas completa. Una
/// conciliacion de un edificio ajeno responde 404.
/// </summary>
[Route("api/finance/reconciliation")]
public class FinanceReconciliationController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinanceLedgerService ledgerService,
    BankReconciliationService reconciliations) : FinanceLedgerControllerBase(dbContext, accessScope, tenantContext, gate, ledgerService)
{
    // Quienes concilian siempre y quienes cambian el interruptor del Encargado.
    private bool CanConfigureReconcile => Tenant.IsSuperAdmin || Tenant.IsCompanyAdmin;

    private bool IsBuildingManager => string.Equals(Tenant.Role, "BuildingManager", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> ManagerSwitchOnAsync(Guid buildingId, CancellationToken cancellationToken) =>
        await Db.FinanceSettings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .Select(x => x.ManagerCanReconcile)
            .FirstOrDefaultAsync(cancellationToken);

    // SuperAdmin y Administrador de empresa siempre; el Encargado solo si el edificio encendio el interruptor.
    private async Task<bool> CanReconcileAsync(Guid buildingId, CancellationToken cancellationToken) =>
        CanConfigureReconcile || (IsBuildingManager && await ManagerSwitchOnAsync(buildingId, cancellationToken));

    private ObjectResult ReconciliationForbidden() => StatusCode(StatusCodes.Status403Forbidden, new
    {
        error = "reconciliation_forbidden",
        message = "Solo el SuperAdmin o el Administrador de empresa pueden armar, cerrar o reabrir una conciliación bancaria. " +
                  "El Encargado puede hacerlo si el edificio lo habilita."
    });

    private ObjectResult ToError(int status, string? code, string? message) => StatusCode(status, new { error = code, message });

    [HttpGet]
    public async Task<ActionResult<ReconciliationOverviewDto>> GetOverview([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        return Ok(await OverviewAsync(ctx!, cancellationToken));
    }

    // El resumen con el permiso del usuario y el estado del interruptor del Encargado.
    private async Task<ReconciliationOverviewDto> OverviewAsync(LedgerContext ctx, CancellationToken cancellationToken)
    {
        var overview = await reconciliations.OverviewAsync(ctx, await CanReconcileAsync(ctx.BuildingId, cancellationToken), cancellationToken);
        overview.ManagerCanReconcile = await ManagerSwitchOnAsync(ctx.BuildingId, cancellationToken);
        overview.CanConfigure = CanConfigureReconcile;
        return overview;
    }

    // Enciende o apaga que el Encargado del edificio pueda conciliar (apagado por defecto). Solo SuperAdmin y Administrador de empresa.
    [HttpPut("settings")]
    public async Task<ActionResult<ReconciliationOverviewDto>> SetManagerReconcile([FromBody] SetManagerReconcileRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(request.BuildingId, cancellationToken);
        if (denied is not null) return denied;
        if (!CanConfigureReconcile)
        {
            return ToError(StatusCodes.Status403Forbidden, "reconciliation_settings_forbidden",
                "Solo el SuperAdmin o el Administrador de empresa pueden cambiar quién concilia en el edificio.");
        }

        var settings = await Db.FinanceSettings.FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == request.BuildingId, cancellationToken);
        if (settings is null) return NotFound();

        if (settings.ManagerCanReconcile != request.Enabled)
        {
            settings.ManagerCanReconcile = request.Enabled;
            Audit.Add(
                settings.CompanyId, request.BuildingId, ConfigSectionKeys.Reconciliation, request.Enabled ? "Enabled" : "Disabled",
                request.Enabled ? "El Encargado del edificio ahora puede conciliar." : "El Encargado del edificio ya no puede conciliar.",
                "FinanceSettings", settings.Id,
                [new ConfigChange("managerCanReconcile", "El Encargado puede conciliar", request.Enabled ? "No" : "Sí", request.Enabled ? "Sí" : "No")]);
            await Db.SaveChangesAsync(cancellationToken);
        }

        return Ok(await OverviewAsync(ctx!, cancellationToken));
    }

    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<ReconciliationSummaryDto>>> GetHistory(
        [FromQuery] Guid buildingId, [FromQuery] Guid? accountId, CancellationToken cancellationToken)
    {
        var (denied, _) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        return Ok(await reconciliations.HistoryAsync(buildingId, accountId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReconciliationWorkspaceDto>> Get(
        Guid id, [FromQuery] bool includePrevious = false, CancellationToken cancellationToken = default)
    {
        var (denied, ctx) = await ResolveAsync(id, write: false, cancellationToken);
        if (denied is not null) return denied;

        return Map(await reconciliations.GetAsync(ctx!, id, await CanReconcileAsync(ctx!.BuildingId, cancellationToken), cancellationToken, includePrevious));
    }

    // Excel de la conciliacion: resumen, lo conciliado en ella y todos los pendientes (sin el tope de la pantalla); con includePrevious, tambien lo de las anteriores.
    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, [FromQuery] bool includePrevious = false, CancellationToken cancellationToken = default)
    {
        var (denied, ctx) = await ResolveAsync(id, write: false, cancellationToken);
        if (denied is not null) return denied;

        var result = await reconciliations.GetAsync(ctx!, id, await CanReconcileAsync(ctx!.BuildingId, cancellationToken), cancellationToken, includePrevious: true, full: true);
        if (!result.Ok) return ToError(result.Status, result.Code, result.Message);

        var workspace = result.Value!;
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        Condo.Api.Documents.FinanceExcelExporter.AddReconciliation(workbook, workspace, ctx!.BuildingName, includePrevious);
        return File(Condo.Api.Documents.FinanceExcelExporter.Save(workbook), Condo.Api.Documents.FinanceExcelExporter.ContentType,
            Condo.Api.Documents.FinanceExcelExporter.FileName($"conciliacion-{Condo.Api.Documents.FinanceExcelExporter.Slug(workspace.AccountName)}", ctx.BuildingName, workspace.StatementDate.ToString("yyyy-MM-dd")));
    }

    [HttpPost]
    public async Task<ActionResult<ReconciliationWorkspaceDto>> Start([FromBody] StartReconciliationRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(request.BuildingId, cancellationToken);
        if (denied is not null) return denied;
        if (!await CanReconcileAsync(request.BuildingId, cancellationToken)) return ReconciliationForbidden();

        var companyId = await ResolveCompanyIdAsync(request.BuildingId, cancellationToken);
        if (!companyId.HasValue) return BadRequest(NoCompanyMessage);

        var result = await reconciliations.StartAsync(ctx!, request, companyId.Value, cancellationToken);
        return result.Ok ? StatusCode(StatusCodes.Status201Created, result.Value) : ToError(result.Status, result.Code, result.Message);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ReconciliationWorkspaceDto>> Update(Guid id, [FromBody] UpdateReconciliationRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await ResolveAsync(id, write: true, cancellationToken);
        if (denied is not null) return denied;

        return Map(await reconciliations.UpdateAsync(ctx!, id, request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Discard(Guid id, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await ResolveAsync(id, write: true, cancellationToken);
        if (denied is not null) return denied;

        var result = await reconciliations.DiscardAsync(ctx!, id, cancellationToken);
        return result.Ok ? NoContent() : ToError(result.Status, result.Code, result.Message);
    }

    [HttpPost("{id:guid}/mark")]
    public async Task<ActionResult<ReconciliationWorkspaceDto>> Mark(Guid id, [FromBody] ReconciliationItemsRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await ResolveAsync(id, write: true, cancellationToken);
        if (denied is not null) return denied;

        return Map(await reconciliations.MarkAsync(ctx!, id, request.Items ?? [], cancellationToken));
    }

    [HttpPost("{id:guid}/unmark")]
    public async Task<ActionResult<ReconciliationWorkspaceDto>> Unmark(Guid id, [FromBody] ReconciliationItemsRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await ResolveAsync(id, write: true, cancellationToken);
        if (denied is not null) return denied;

        return Map(await reconciliations.UnmarkAsync(ctx!, id, request.Items ?? [], cancellationToken));
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<ReconciliationWorkspaceDto>> Complete(Guid id, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await ResolveAsync(id, write: true, cancellationToken);
        if (denied is not null) return denied;

        return Map(await reconciliations.CompleteAsync(ctx!, id, cancellationToken));
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<ActionResult<ReconciliationWorkspaceDto>> Reopen(Guid id, [FromBody] ReopenReconciliationRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await ResolveAsync(id, write: true, cancellationToken);
        if (denied is not null) return denied;

        return Map(await reconciliations.ReopenAsync(ctx!, id, request?.Reason ?? string.Empty, cancellationToken));
    }

    // El edificio de una conciliacion: uno ajeno o inexistente responde 404; despues rige el acceso al modulo y, al escribir, el permiso.
    private async Task<(ActionResult? Denied, LedgerContext? Context)> ResolveAsync(Guid id, bool write, CancellationToken cancellationToken)
    {
        var buildingId = await Db.BankReconciliations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => (Guid?)x.BuildingId)
            .FirstOrDefaultAsync(cancellationToken);
        if (buildingId is null || !await AccessScope.CanAccessBuildingAsync(buildingId.Value, cancellationToken))
        {
            return (NotFound(), null);
        }

        var (denied, ctx) = await RequireLedgerAsync(buildingId.Value, cancellationToken);
        if (denied is not null) return (denied, null);
        if (write && !await CanReconcileAsync(buildingId.Value, cancellationToken)) return (ReconciliationForbidden(), null);

        return (null, ctx);
    }

    private ActionResult<T> Map<T>(ReconResult<T> result) =>
        result.Ok ? Ok(result.Value) : ToError(result.Status, result.Code, result.Message);
}
