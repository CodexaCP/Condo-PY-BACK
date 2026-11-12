using Condo.Api.Services;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

// Votaciones vistas por el propietario (canal de la APP). La titularidad y la privacidad se resuelven en VoteService.
[ApiController]
[Authorize]
[Route("api/me/votes")]
public class OwnerVotesController(VoteService votes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OwnerVoteDto>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await votes.ListForOwnerAsync(cancellationToken);
        return result.Ok ? Ok(result.Value) : ToError(result.Error!);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OwnerVoteDto>> GetById(Guid id, CancellationToken cancellationToken) =>
        ToAction(await votes.GetForOwnerAsync(id, cancellationToken));

    [HttpPost("{id:guid}/cast")]
    public async Task<ActionResult<OwnerVoteDto>> Cast(Guid id, [FromBody] VoteCastRequest request, CancellationToken cancellationToken) =>
        ToAction(await votes.CastAsOwnerAsync(id, request, cancellationToken));

    private ActionResult<OwnerVoteDto> ToAction(VoteResult<OwnerVoteDto> result) => result.Ok ? Ok(result.Value) : ToError(result.Error!);

    // Texto plano en 400/403/409; 404 sin cuerpo.
    private ActionResult ToError(VoteError error) => error.StatusCode switch
    {
        StatusCodes.Status404NotFound => NotFound(),
        _ => StatusCode(error.StatusCode, error.Message)
    };
}
