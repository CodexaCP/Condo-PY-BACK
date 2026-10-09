using ClosedXML.Excel;
using Condo.Api.Documents;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Asientos contables sugeridos (partida doble, criterio de caja) para el contador y la asignacion de las cuentas del plan que hacen de
/// contrapartida (cada cuenta financiera y el IVA credito). Son sugeridos: no asientan nada ni cambian saldos. Los ven los cuatro roles
/// administrativos; la asignacion la editan solo SuperAdmin y Administrador de empresa. Exige la configuracion inicial de Finanzas completa.
/// </summary>
[Route("api/finance/accounting")]
public class FinanceAccountingController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinanceLedgerService ledgerService,
    AccountingEntriesService accounting) : FinanceLedgerControllerBase(dbContext, accessScope, tenantContext, gate, ledgerService)
{
    private bool CanEdit => Tenant.IsSuperAdmin || Tenant.IsCompanyAdmin;

    [HttpGet("roles")]
    public async Task<ActionResult<AccountingRolesDto>> GetRoles([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        return Ok(await accounting.RolesAsync(ctx!, CanEdit, cancellationToken));
    }

    [HttpPut("roles")]
    public async Task<ActionResult<AccountingRolesDto>> UpdateRoles(
        [FromQuery] Guid buildingId, [FromBody] UpdateAccountingRolesRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;
        if (!CanEdit)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "accounting_forbidden",
                message = "Solo el SuperAdmin o el Administrador de empresa pueden asignar las cuentas del plan de los asientos."
            });
        }

        var companyId = await ResolveCompanyIdAsync(buildingId, cancellationToken);
        if (!companyId.HasValue) return BadRequest(NoCompanyMessage);

        var (roles, error) = await accounting.UpdateRolesAsync(ctx!, request, companyId.Value, cancellationToken);
        return error is not null ? BadRequest(error) : Ok(roles);
    }

    [HttpGet("entries")]
    public async Task<ActionResult<AccountingEntriesDto>> GetEntries(
        [FromQuery] Guid buildingId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        var (entries, error) = await accounting.EntriesAsync(ctx!, from, to, cancellationToken);
        return error is not null ? BadRequest(error) : Ok(entries);
    }

    [HttpGet("entries/export")]
    public async Task<IActionResult> ExportEntries(
        [FromQuery] Guid buildingId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        var (entries, error) = await accounting.EntriesAsync(ctx!, from, to, cancellationToken);
        if (error is not null) return BadRequest(error);

        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddAccountingEntries(workbook, entries!);
        return File(FinanceExcelExporter.Save(workbook), FinanceExcelExporter.ContentType,
            FinanceExcelExporter.FileName("asientos", ctx!.BuildingName, $"{entries!.From:yyyyMMdd}-{entries.To:yyyyMMdd}"));
    }
}
