using Condo.Api.Services;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Seguimiento del personal del edificio (fase 7): reembolsos pendientes al comprador, deudas por gestion de los propietarios y
/// reclamos. El edificio y los permisos se validan siempre en el servidor; con el plan vencido el middleware de planes ya bloquea
/// las escrituras del personal, igual que en los pagos.
/// </summary>
[ApiController]
[Authorize]
[Route("api/marketplace")]
public class MarketplaceFollowUpController(
    MarketplaceRefundService refunds,
    MarketplaceClaimService claims) : ControllerBase
{
    // Reembolsos del edificio: pendientes primero y, si se pide, los ultimos devueltos.
    [HttpGet("refunds")]
    public async Task<ActionResult<List<MarketplaceRefundDto>>> GetRefunds(
        [FromQuery] Guid buildingId, [FromQuery] bool includeReturned, CancellationToken ct) =>
        (await refunds.GetRefundsAsync(buildingId, includeReturned, ct)).ToActionResult(this);

    // El dinero se devolvio fuera del sistema: se marca "devuelto" (queda quien y cuando; el extracto registra la salida).
    [HttpPost("refunds/{id:guid}/return")]
    public async Task<ActionResult<MarketplaceRefundDto>> MarkReturned(Guid id, CancellationToken ct) =>
        (await refunds.MarkReturnedAsync(id, ct)).ToActionResult(this);

    // Deudas por gestion pendientes de los propietarios (se descuentan de su proxima acreditacion).
    [HttpGet("owner-debts")]
    public async Task<ActionResult<List<MarketplaceOwnerDebtDto>>> GetOwnerDebts([FromQuery] Guid buildingId, CancellationToken ct) =>
        (await refunds.GetOwnerDebtsAsync(buildingId, ct)).ToActionResult(this);

    // Reclamos del edificio: abiertos primero y, si se pide, los ultimos resueltos.
    [HttpGet("claims")]
    public async Task<ActionResult<List<MarketplaceClaimDto>>> GetClaims(
        [FromQuery] Guid buildingId, [FromQuery] bool includeResolved, CancellationToken ct) =>
        (await claims.GetClaimsAsync(buildingId, includeResolved, ct)).ToActionResult(this);

    [HttpPost("claims/{id:guid}/resolve")]
    public async Task<ActionResult<MarketplaceClaimDto>> Resolve(
        Guid id, [FromBody] MarketplaceClaimResolveRequest request, CancellationToken ct) =>
        (await claims.ResolveAsync(id, request, ct)).ToActionResult(this);
}
