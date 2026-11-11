using Condo.Api.Services;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

// Votaciones del edificio, vistas por el personal. La logica (estados, permisos por accion, historial, resultado congelado y
// concurrencia) vive en VoteService.
[ApiController]
[Authorize(Roles = "SuperAdmin,CompanyAdmin,CompanyOperator,BuildingManager")]
[Route("api/votes")]
public class VotesController(VoteService votes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VoteDto>>> GetAll([FromQuery] Guid? buildingId, CancellationToken cancellationToken)
    {
        var result = await votes.ListAsync(buildingId, cancellationToken);
        return result.Ok ? Ok(result.Value) : ToError(result.Error!);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VoteDto>> GetById(Guid id, CancellationToken cancellationToken) =>
        ToAction(await votes.GetAsync(id, cancellationToken));

    // Historial de votos de la votacion (o de una unidad): cada voto, cambio y quita, con quien y por que canal.
    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<IReadOnlyList<VoteCastEventDto>>> GetHistory(Guid id, [FromQuery] Guid? unitId, CancellationToken cancellationToken)
    {
        var result = await votes.GetHistoryAsync(id, unitId, cancellationToken);
        return result.Ok ? Ok(result.Value) : ToError(result.Error!);
    }

    [HttpPost]
    public async Task<ActionResult<VoteDto>> Create([FromBody] VoteUpsertRequest request, CancellationToken cancellationToken)
    {
        var result = await votes.CreateAsync(request, cancellationToken);
        return result.Ok ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value) : ToError(result.Error!);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<VoteDto>> Update(Guid id, [FromBody] VoteUpsertRequest request, CancellationToken cancellationToken) =>
        ToAction(await votes.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await votes.DeleteAsync(id, cancellationToken);
        return result.Ok ? NoContent() : ToError(result.Error!);
    }

    [HttpPost("{id:guid}/open")]
    public async Task<ActionResult<VoteDto>> Open(Guid id, CancellationToken cancellationToken) =>
        ToAction(await votes.OpenAsync(id, cancellationToken));

    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<VoteDto>> Close(Guid id, CancellationToken cancellationToken) =>
        ToAction(await votes.CloseAsync(id, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<VoteDto>> Cancel(Guid id, [FromBody] VoteCancelRequest request, CancellationToken cancellationToken) =>
        ToAction(await votes.CancelAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/cast")]
    public async Task<ActionResult<VoteDto>> Cast(Guid id, [FromBody] VoteCastRequest request, CancellationToken cancellationToken) =>
        ToAction(await votes.CastAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}/casts/{castId:guid}")]
    public async Task<ActionResult<VoteDto>> RemoveCast(Guid id, Guid castId, CancellationToken cancellationToken) =>
        ToAction(await votes.RemoveCastAsync(id, castId, cancellationToken));

    private ActionResult<VoteDto> ToAction(VoteResult<VoteDto> result) => result.Ok ? Ok(result.Value) : ToError(result.Error!);

    // Texto plano en 400/403/409 (lo que lee el web con extractApiErrorMessage); 404 sin cuerpo.
    private ActionResult ToError(VoteError error) => error.StatusCode switch
    {
        StatusCodes.Status404NotFound => NotFound(),
        _ => StatusCode(error.StatusCode, error.Message)
    };
}
