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
public class MarketplaceReservationsController(
    MarketplaceReservationService reservations,
    MarketplaceCancellationService cancellations,
    MarketplaceClaimService claims,
    MarketplaceStartNoticeService startNotices) : ControllerBase
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

    // Reservas de MIS publicaciones (quien reservo: nombre y unidad) para que el propietario las vea y pueda cancelarlas.
    [HttpGet("reservations/on-my-listings")]
    public async Task<ActionResult<List<MarketplaceOwnerReservationDto>>> GetOnMyListings([FromQuery] Guid buildingId, CancellationToken ct) =>
        (await reservations.GetOnMyListingsAsync(buildingId, ct)).ToActionResult(this);

    // Que pasa si cancelo (monto a devolver y comision), calculado por el servidor para avisarlo antes de confirmar.
    [HttpGet("reservations/{id:guid}/cancel-preview")]
    public async Task<ActionResult<MarketplaceCancelPreviewDto>> CancelPreview(Guid id, CancellationToken ct) =>
        (await cancellations.PreviewAsync(id, ct)).ToActionResult(this);

    // El comprador cancela: sin pagar libera el horario; ya pagada y antes del inicio, se devuelve la base (la comision no).
    [HttpPost("reservations/{id:guid}/cancel")]
    public async Task<ActionResult<MarketplaceReservationDto>> Cancel(
        Guid id, [FromBody] MarketplaceCancelRequest? request, CancellationToken ct) =>
        (await cancellations.CancelByBuyerAsync(id, request?.Reason, ct)).ToActionResult(this);

    // El propietario cancela una reserva ya pagada (con motivo): se devuelve todo al comprador y el propietario asume la comision.
    [HttpPost("reservations/{id:guid}/owner-cancel")]
    public async Task<ActionResult<MarketplaceOwnerReservationDto>> OwnerCancel(
        Guid id, [FromBody] MarketplaceCancelRequest? request, CancellationToken ct) =>
        (await cancellations.CancelByOwnerAsync(id, request?.Reason, ct)).ToActionResult(this);

    // "Reportar un problema" (comprador o propietario): retiene la acreditacion y avisa al Encargado.
    [HttpPost("reservations/{id:guid}/claim")]
    public async Task<ActionResult<MarketplaceClaimDto>> OpenClaim(
        Guid id, [FromBody] MarketplaceClaimRequest request, CancellationToken ct) =>
        (await claims.OpenAsync(id, request?.Reason, ct)).ToActionResult(this);

    // Respuesta al aviso de inicio ("Si, voy" / "No la voy a usar"): solo queda registrada.
    [HttpPost("reservations/{id:guid}/start-response")]
    public async Task<ActionResult<MarketplaceReservationDto>> StartResponse(
        Guid id, [FromBody] MarketplaceStartResponseRequest request, CancellationToken ct) =>
        (await startNotices.RespondAsync(id, request, ct)).ToActionResult(this);
}
