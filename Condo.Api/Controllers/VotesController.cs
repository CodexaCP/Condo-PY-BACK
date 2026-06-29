using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/votes")]
public class VotesController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VoteDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        CancellationToken cancellationToken)
    {
        var accessibleIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        if (buildingId.HasValue && !accessScope.IsSuperAdmin && !accessibleIds.Contains(buildingId.Value))
            return Forbid();

        var query = dbContext.Votes.AsNoTracking().Where(x => !x.IsDeleted);

        if (buildingId.HasValue)
            query = query.Where(x => x.BuildingId == buildingId.Value);
        else if (!accessScope.IsSuperAdmin)
            query = query.Where(x => accessibleIds.Contains(x.BuildingId));

        var votes = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id, x.BuildingId, BuildingName = x.Building.Name,
                x.Title, x.Description, x.QuorumPercentage, x.WeightType, x.Status,
                x.OpenedAtUtc, x.ClosedAtUtc, x.CreatedAtUtc,
                CreatedByName = x.CreatedBy != null ? x.CreatedBy.FullName : string.Empty,
                OptionCount = x.Options.Count(),
                CastCount = x.Casts.Count()
            })
            .ToListAsync(cancellationToken);

        var buildingIds = votes.Select(x => x.BuildingId).Distinct().ToList();
        var unitCounts = await dbContext.Units
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && buildingIds.Contains(x.BuildingId))
            .GroupBy(x => x.BuildingId)
            .Select(g => new { BuildingId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var unitCountMap = unitCounts.ToDictionary(x => x.BuildingId, x => x.Count);

        return Ok(votes.Select(v =>
        {
            var totalUnits = unitCountMap.GetValueOrDefault(v.BuildingId, 0);
            var quorumReached = totalUnits > 0 && (v.CastCount * 100m / totalUnits) >= v.QuorumPercentage;
            return new VoteDto
            {
                Id = v.Id, BuildingId = v.BuildingId, BuildingName = v.BuildingName,
                Title = v.Title, Description = v.Description,
                QuorumPercentage = v.QuorumPercentage, WeightType = v.WeightType,
                Status = v.Status, OpenedAtUtc = v.OpenedAtUtc, ClosedAtUtc = v.ClosedAtUtc,
                CreatedByName = v.CreatedByName, CreatedAtUtc = v.CreatedAtUtc,
                TotalUnits = totalUnits, ParticipatingUnits = v.CastCount,
                QuorumReached = quorumReached
            };
        }).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VoteDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var vote = await dbContext.Votes
            .AsNoTracking()
            .Include(x => x.Building)
            .Include(x => x.Options.OrderBy(o => o.DisplayOrder))
            .Include(x => x.Casts).ThenInclude(c => c.Unit)
            .Include(x => x.Casts).ThenInclude(c => c.Option)
            .Include(x => x.CreatedBy)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (vote is null) return NotFound();
        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(vote.BuildingId, cancellationToken))
            return Forbid();

        return Ok(await BuildDetailDto(vote, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<VoteDto>> Create([FromBody] VoteUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(request.BuildingId, cancellationToken))
            return Forbid();

        var error = Validate(request);
        if (error is not null) return BadRequest(error);

        var vote = new Vote
        {
            BuildingId = request.BuildingId,
            Title = request.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            QuorumPercentage = request.QuorumPercentage,
            WeightType = request.WeightType,
            Status = "Draft",
            CreatedByUserId = tenant.UserId
        };

        for (int i = 0; i < request.OptionLabels.Count; i++)
            vote.Options.Add(new VoteOption { Label = request.OptionLabels[i].Trim(), DisplayOrder = i });

        dbContext.Votes.Add(vote);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = vote.Id },
            await LoadDetailDto(vote.Id, cancellationToken));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<VoteDto>> Update(Guid id, [FromBody] VoteUpsertRequest request, CancellationToken cancellationToken)
    {
        var vote = await dbContext.Votes
            .Include(x => x.Options)
            .Include(x => x.Casts)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (vote is null) return NotFound();
        if (vote.Status != "Draft") return BadRequest("Solo se pueden editar votaciones en estado Borrador.");
        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(vote.BuildingId, cancellationToken))
            return Forbid();

        var error = Validate(request);
        if (error is not null) return BadRequest(error);

        vote.Title = request.Title.Trim();
        vote.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        vote.QuorumPercentage = request.QuorumPercentage;
        vote.WeightType = request.WeightType;

        // Replace options only if no casts exist yet
        if (!vote.Casts.Any())
        {
            dbContext.VoteOptions.RemoveRange(vote.Options);
            vote.Options.Clear();
            for (int i = 0; i < request.OptionLabels.Count; i++)
                vote.Options.Add(new VoteOption { VoteId = vote.Id, Label = request.OptionLabels[i].Trim(), DisplayOrder = i });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await LoadDetailDto(vote.Id, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var vote = await dbContext.Votes.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (vote is null) return NotFound();
        if (vote.Status != "Draft") return BadRequest("Solo se pueden eliminar votaciones en estado Borrador.");
        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(vote.BuildingId, cancellationToken))
            return Forbid();

        vote.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/open")]
    public async Task<ActionResult<VoteDto>> Open(Guid id, CancellationToken cancellationToken)
    {
        var vote = await dbContext.Votes.Include(x => x.Options)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (vote is null) return NotFound();
        if (vote.Status != "Draft") return BadRequest("La votación ya fue abierta o cerrada.");
        if (vote.Options.Count < 2) return BadRequest("Se necesitan al menos 2 opciones para abrir la votación.");
        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(vote.BuildingId, cancellationToken))
            return Forbid();

        vote.Status = "Open";
        vote.OpenedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await LoadDetailDto(vote.Id, cancellationToken));
    }

    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<VoteDto>> Close(Guid id, CancellationToken cancellationToken)
    {
        var vote = await dbContext.Votes.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (vote is null) return NotFound();
        if (vote.Status != "Open") return BadRequest("La votación no está abierta.");
        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(vote.BuildingId, cancellationToken))
            return Forbid();

        vote.Status = "Closed";
        vote.ClosedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await LoadDetailDto(vote.Id, cancellationToken));
    }

    [HttpPost("{id:guid}/cast")]
    public async Task<ActionResult<VoteDto>> Cast(Guid id, [FromBody] VoteCastRequest request, CancellationToken cancellationToken)
    {
        var vote = await dbContext.Votes.Include(x => x.Options).Include(x => x.Casts)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (vote is null) return NotFound();
        if (vote.Status != "Open") return BadRequest("La votación no está abierta.");
        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(vote.BuildingId, cancellationToken))
            return Forbid();

        var option = vote.Options.FirstOrDefault(x => x.Id == request.VoteOptionId);
        if (option is null) return BadRequest("Opción inválida.");

        var unit = await dbContext.Units.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.UnitId && x.BuildingId == vote.BuildingId, cancellationToken);
        if (unit is null) return BadRequest("La unidad no pertenece al edificio de esta votación.");

        var existing = vote.Casts.FirstOrDefault(x => x.UnitId == request.UnitId);
        if (existing is not null)
            dbContext.VoteCasts.Remove(existing);

        dbContext.VoteCasts.Add(new VoteCast
        {
            VoteId = vote.Id,
            VoteOptionId = request.VoteOptionId,
            UnitId = request.UnitId,
            CoefficientWeight = unit.Coefficient,
            CastAtUtc = DateTime.UtcNow,
            CastByUserId = tenant.UserId
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await LoadDetailDto(vote.Id, cancellationToken));
    }

    [HttpDelete("{id:guid}/casts/{castId:guid}")]
    public async Task<ActionResult<VoteDto>> RemoveCast(Guid id, Guid castId, CancellationToken cancellationToken)
    {
        var vote = await dbContext.Votes.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (vote is null) return NotFound();
        if (vote.Status != "Open") return BadRequest("Solo se pueden eliminar votos de una votación abierta.");
        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(vote.BuildingId, cancellationToken))
            return Forbid();

        var cast = await dbContext.VoteCasts.FirstOrDefaultAsync(x => x.Id == castId && x.VoteId == id, cancellationToken);
        if (cast is null) return NotFound();

        dbContext.VoteCasts.Remove(cast);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await LoadDetailDto(vote.Id, cancellationToken));
    }

    private async Task<VoteDto> LoadDetailDto(Guid id, CancellationToken ct)
    {
        var vote = await dbContext.Votes
            .AsNoTracking()
            .Include(x => x.Building)
            .Include(x => x.Options.OrderBy(o => o.DisplayOrder))
            .Include(x => x.Casts).ThenInclude(c => c.Unit)
            .Include(x => x.Casts).ThenInclude(c => c.Option)
            .Include(x => x.CreatedBy)
            .FirstAsync(x => x.Id == id, ct);

        return await BuildDetailDto(vote, ct);
    }

    private async Task<VoteDto> BuildDetailDto(Vote vote, CancellationToken ct)
    {
        var allUnits = await dbContext.Units
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.BuildingId == vote.BuildingId)
            .OrderBy(x => x.Code)
            .ToListAsync(ct);

        var castByUnit = vote.Casts.ToDictionary(x => x.UnitId);
        var totalWeight = vote.Casts.Sum(x => x.CoefficientWeight);
        var totalCasts = vote.Casts.Count;
        var totalUnits = allUnits.Count;
        var quorumReached = totalUnits > 0 && (totalCasts * 100m / totalUnits) >= vote.QuorumPercentage;

        var options = vote.Options.Select(opt =>
        {
            var optCasts = vote.Casts.Where(c => c.VoteOptionId == opt.Id).ToList();
            var castCount = optCasts.Count;
            var castWeight = optCasts.Sum(c => c.CoefficientWeight);
            var pct = vote.WeightType == "ByCoefficient"
                ? (totalWeight > 0 ? Math.Round(castWeight * 100m / totalWeight, 1) : 0)
                : (totalCasts > 0 ? Math.Round(castCount * 100m / totalCasts, 1) : 0);
            return new VoteOptionDto { Id = opt.Id, Label = opt.Label, DisplayOrder = opt.DisplayOrder, CastCount = castCount, CastWeight = castWeight, Percentage = pct };
        }).ToList();

        var units = allUnits.Select(u =>
        {
            castByUnit.TryGetValue(u.Id, out var cast);
            return new VoteUnitSummaryDto
            {
                UnitId = u.Id, UnitCode = u.Code, Coefficient = u.Coefficient,
                CastId = cast?.Id, VotedOptionId = cast?.VoteOptionId,
                VotedOptionLabel = cast?.Option?.Label, CastAtUtc = cast?.CastAtUtc
            };
        }).ToList();

        return new VoteDto
        {
            Id = vote.Id, BuildingId = vote.BuildingId, BuildingName = vote.Building.Name,
            Title = vote.Title, Description = vote.Description,
            QuorumPercentage = vote.QuorumPercentage, WeightType = vote.WeightType,
            Status = vote.Status, OpenedAtUtc = vote.OpenedAtUtc, ClosedAtUtc = vote.ClosedAtUtc,
            CreatedByName = vote.CreatedBy?.FullName ?? string.Empty,
            CreatedAtUtc = vote.CreatedAtUtc,
            TotalUnits = totalUnits, ParticipatingUnits = totalCasts,
            QuorumReached = quorumReached,
            Options = options, Units = units
        };
    }

    private static string? Validate(VoteUpsertRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Title)) return "El título es obligatorio.";
        if (r.Title.Length > 200) return "El título no puede superar 200 caracteres.";
        if (r.QuorumPercentage < 1 || r.QuorumPercentage > 100) return "El quórum debe estar entre 1 y 100.";
        if (r.WeightType != "ByUnit" && r.WeightType != "ByCoefficient") return "Tipo de peso inválido.";
        var labels = r.OptionLabels.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (labels.Count < 2) return "Se necesitan al menos 2 opciones.";
        return null;
    }
}
