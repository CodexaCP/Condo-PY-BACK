using Condo.Api.Documents;
using Condo.Api.Services;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Cuenta aparte del marketplace por edificio (fase 6). El extracto lo ven SuperAdmin, Administrador de empresa y Encargado;
/// los movimientos manuales y la reversa de una acreditacion son solo del SuperAdmin. El edificio y los permisos se validan
/// siempre en el servidor.
/// </summary>
[ApiController]
[Authorize]
[Route("api/marketplace/account")]
public class MarketplaceAccountController(MarketplaceAccountService accounts, MarketplaceCreditService credits) : ControllerBase
{
    // Extracto del periodo (por defecto, el mes en curso). Fechas en hora de Paraguay.
    [HttpGet("statement")]
    public async Task<ActionResult<MarketplaceStatementDto>> GetStatement(
        [FromQuery] Guid buildingId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        (await accounts.GetStatementAsync(buildingId, from, to, ct)).ToActionResult(this);

    // El mismo extracto en Excel, como respaldo para repartir la comision de la gestion.
    [HttpGet("statement/export")]
    public async Task<IActionResult> Export(
        [FromQuery] Guid buildingId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var result = await accounts.GetStatementAsync(buildingId, from, to, ct);
        if (!result.Ok)
        {
            return result.ToActionResult(this).Result!;
        }

        var statement = result.Value!;
        return File(MarketplaceAccountExcelExporter.Build(statement, DateTime.UtcNow),
            MarketplaceAccountExcelExporter.ContentType, MarketplaceAccountExcelExporter.FileName(statement));
    }

    // Movimiento manual (solo SuperAdmin): ajustes con signo y concepto.
    [HttpPost("adjustments")]
    public async Task<ActionResult<MarketplaceAccountRowDto>> AddAdjustment(
        [FromBody] MarketplaceAdjustmentRequest request, CancellationToken ct) =>
        (await accounts.AddAdjustmentAsync(request, ct)).ToActionResult(this);

    // Revierte una acreditacion cuyo saldo sigue intacto (solo SuperAdmin).
    [HttpPost("reservations/{id:guid}/reverse-credit")]
    public async Task<ActionResult<MarketplaceReversalDto>> ReverseCredit(
        Guid id, [FromBody] MarketplaceReverseCreditRequest request, CancellationToken ct) =>
        (await credits.ReverseCreditAsync(id, request.Reason, ct)).ToActionResult(this);
}
