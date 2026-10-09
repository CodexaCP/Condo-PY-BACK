using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Cuentas por pagar del modulo Finanzas: las facturas de proveedores con vencimiento, pendientes hasta que se registra su pago (el pago se
/// registra en el gasto: <c>PUT api/building-expenses/{id}/payment</c>). Solo consulta; la ven los cuatro roles administrativos con acceso
/// al edificio y el modulo disponible.
/// </summary>
[Route("api/finance/payables")]
public class FinancePayablesController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinancePayablesService payables) : FinanceControllerBase(dbContext, accessScope, tenantContext, gate)
{
    [HttpGet]
    public async Task<ActionResult<PayablesPageDto>> GetAll(
        [FromQuery] Guid buildingId,
        [FromQuery] string? status,
        [FromQuery] Guid? supplierId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var denied = await RequireModuleAsync(buildingId, write: false, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var normalized = (status ?? FinancePayablesService.StatusAll).Trim().ToLowerInvariant();
        if (normalized is not (FinancePayablesService.StatusAll or FinancePayablesService.StatusPending
            or FinancePayablesService.StatusOverdue or FinancePayablesService.StatusPaid))
        {
            return BadRequest("El estado no es válido (all, pending, overdue o paid).");
        }

        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            return BadRequest("La fecha desde no puede ser posterior a la fecha hasta.");
        }

        return Ok(await payables.ListAsync(buildingId, normalized, supplierId, from, to, page, pageSize, FinancePeriods.Today(), cancellationToken));
    }

    [HttpGet("summary")]
    public async Task<ActionResult<PayablesSummaryDto>> GetSummary([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: false, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        return Ok(await payables.SummaryAsync(buildingId, FinancePeriods.Today(), cancellationToken));
    }
}
