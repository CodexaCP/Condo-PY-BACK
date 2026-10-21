using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Cuentas por pagar y por cobrar por proveedor de un edificio. Solo consulta: la devolucion y los descuentos se registran sobre la
/// nota de credito (api/building-expenses/credit-notes/{id}/mark-refunded y /discounts). Cualquiera con acceso al edificio la ve.
/// </summary>
[ApiController]
[Authorize]
[Route("api/supplier-accounts")]
public class SupplierAccountsController(IAccessScopeService accessScope, SupplierAccountsService accounts) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<SupplierAccountsSummaryDto>> GetSummary([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        if (buildingId == Guid.Empty) return BadRequest("El edificio es obligatorio.");
        if (!await accessScope.CanAccessBuildingAsync(buildingId, cancellationToken)) return Forbid();

        return Ok(await accounts.SummaryAsync(buildingId, FinancePeriods.Today(), cancellationToken));
    }

    [HttpGet("receivables")]
    public async Task<ActionResult<IReadOnlyList<SupplierReceivableRowDto>>> GetReceivables(
        [FromQuery] Guid buildingId, [FromQuery] string? supplierName, [FromQuery] string? status, CancellationToken cancellationToken)
    {
        if (buildingId == Guid.Empty) return BadRequest("El edificio es obligatorio.");
        if (!await accessScope.CanAccessBuildingAsync(buildingId, cancellationToken)) return Forbid();

        var normalized = (status ?? SupplierAccountsService.StatusPending).Trim().ToLowerInvariant();
        if (normalized is not (SupplierAccountsService.StatusPending or SupplierAccountsService.StatusRecovered or SupplierAccountsService.StatusAll))
            return BadRequest("El estado no es válido (pending, recovered o all).");

        return Ok(await accounts.ReceivablesAsync(buildingId, supplierName, normalized, FinancePeriods.Today(), cancellationToken));
    }

    // Para avisar al cargar una factura del proveedor que tiene descuentos acordados sin aplicar.
    [HttpGet("pending-discount")]
    public async Task<ActionResult<SupplierPendingDiscountDto>> GetPendingDiscount(
        [FromQuery] Guid buildingId, [FromQuery] string supplierName, CancellationToken cancellationToken)
    {
        if (buildingId == Guid.Empty) return BadRequest("El edificio es obligatorio.");
        if (string.IsNullOrWhiteSpace(supplierName)) return BadRequest("El proveedor es obligatorio.");
        if (!await accessScope.CanAccessBuildingAsync(buildingId, cancellationToken)) return Forbid();

        return Ok(await accounts.PendingDiscountAsync(buildingId, supplierName, FinancePeriods.Today(), cancellationToken));
    }
}
