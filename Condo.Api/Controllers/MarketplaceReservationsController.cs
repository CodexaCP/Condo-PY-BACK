using Condo.Api.Services;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Explorar y reservar (fase 4). El edificio siempre se valida en el servidor; el precio, la comision y el plazo para
/// pagar los calcula el servidor: nunca se aceptan del cliente.
/// </summary>
[ApiController]
[Authorize]
[Route("api/marketplace")]
public class MarketplaceReservationsController(MarketplaceReservationService reservations) : ControllerBase
{
    // Publicaciones de otros vecinos de mi edificio con horario libre.
    [HttpGet("listings/explore")]
    public async Task<ActionResult<List<MarketplaceExploreItemDto>>> Explore([FromQuery] Guid buildingId, CancellationToken ct) =>
        (await reservations.ExploreAsync(buildingId, ct)).ToActionResult(this);

    // Desglose exacto (precio, comision de gestion y total) de un horario, sin reservar.
    [HttpPost("reservations/quote")]
    public async Task<ActionResult<MarketplaceQuoteDto>> Quote([FromBody] MarketplaceQuoteRequest request, CancellationToken ct) =>
        (await reservations.QuoteAsync(request, ct)).ToActionResult(this);

    [HttpPost("reservations")]
    public async Task<ActionResult<MarketplaceReservationDto>> Reserve([FromBody] MarketplaceQuoteRequest request, CancellationToken ct)
    {
        var result = await reservations.ReserveAsync(request, ct);
        if (!result.Ok)
        {
            return result.ToActionResult(this);
        }

        return CreatedAtAction(nameof(GetMine), new { buildingId = result.Value!.BuildingId }, result.Value);
    }

    // Mis reservas en el edificio.
    [HttpGet("reservations/mine")]
    public async Task<ActionResult<List<MarketplaceReservationDto>>> GetMine([FromQuery] Guid buildingId, CancellationToken ct) =>
        (await reservations.GetMineAsync(buildingId, ct)).ToActionResult(this);

    // Cancelar una reserva que todavia no se pago (libera el horario).
    [HttpPost("reservations/{id:guid}/cancel")]
    public async Task<ActionResult<MarketplaceReservationDto>> Cancel(Guid id, CancellationToken ct) =>
        (await reservations.CancelPendingAsync(id, ct)).ToActionResult(this);
}
