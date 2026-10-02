using Condo.Api.Services;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Publicaciones del marketplace (fase 3). El edificio siempre llega como parametro pero nunca se confia en el: el
/// servicio lo valida contra la relacion real del usuario, su empresa y que el modulo este habilitado en el edificio.
/// </summary>
[ApiController]
[Authorize]
[Route("api/marketplace/listings")]
public class MarketplaceListingsController(MarketplaceListingService listings) : ControllerBase
{
    // Unidades que el usuario puede publicar (las de las que es propietario principal), para el selector.
    [HttpGet("units")]
    public async Task<ActionResult<List<MarketplacePublishableUnitDto>>> GetPublishableUnits(
        [FromQuery] Guid buildingId, CancellationToken ct) =>
        (await listings.GetPublishableUnitsAsync(buildingId, ct)).ToActionResult(this);

    // Mis publicaciones en el edificio.
    [HttpGet("mine")]
    public async Task<ActionResult<List<MarketplaceListingDto>>> GetMine([FromQuery] Guid buildingId, CancellationToken ct) =>
        (await listings.GetMineAsync(buildingId, ct)).ToActionResult(this);

    // Todas las publicaciones del edificio (solo el personal que lo administra).
    [HttpGet("all")]
    public async Task<ActionResult<List<MarketplaceListingDto>>> GetAll([FromQuery] Guid buildingId, CancellationToken ct) =>
        (await listings.GetAllForStaffAsync(buildingId, ct)).ToActionResult(this);

    [HttpPost]
    public async Task<ActionResult<MarketplaceListingDto>> Create([FromBody] MarketplaceListingCreateRequest request, CancellationToken ct)
    {
        var result = await listings.CreateAsync(request, ct);
        if (!result.Ok)
        {
            return result.ToActionResult(this);
        }

        return CreatedAtAction(nameof(GetMine), new { buildingId = request.BuildingId }, result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MarketplaceListingDto>> Update(
        Guid id, [FromBody] MarketplaceListingUpdateRequest request, CancellationToken ct) =>
        (await listings.UpdateAsync(id, request, ct)).ToActionResult(this);

    [HttpPost("{id:guid}/suspend")]
    public async Task<ActionResult<MarketplaceListingDto>> Suspend(
        Guid id, [FromBody] MarketplaceListingReasonRequest? request, CancellationToken ct) =>
        (await listings.SuspendAsync(id, request?.Reason, ct)).ToActionResult(this);

    [HttpPost("{id:guid}/resume")]
    public async Task<ActionResult<MarketplaceListingDto>> Resume(Guid id, CancellationToken ct) =>
        (await listings.ResumeAsync(id, ct)).ToActionResult(this);

    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<MarketplaceListingDto>> Close(
        Guid id, [FromBody] MarketplaceListingReasonRequest? request, CancellationToken ct) =>
        (await listings.CloseAsync(id, request?.Reason, ct)).ToActionResult(this);
}
