using System.Text.Json;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Centro de configuracion del edificio: resumen del estado de cada seccion y historial de cambios. Lee lo que ya existe (la ficha,
/// Finanzas, timbrados); no reemplaza sus pantallas ni sus endpoints. Un edificio ajeno responde 404, igual que uno inexistente.
/// </summary>
[ApiController]
[Authorize]
[Route("api/building-config/{buildingId:guid}")]
public class BuildingConfigController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    BuildingConfigOverviewService overview) : ControllerBase
{
    private const int MaxPageSize = 100;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [HttpGet("overview")]
    public async Task<ActionResult<BuildingConfigOverviewDto>> GetOverview(Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, ConfigSections.StaffRoles, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var result = await overview.BuildAsync(buildingId, tenantContext.Role, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("audit")]
    public async Task<ActionResult<ConfigAuditPageDto>> GetAudit(
        Guid buildingId,
        [FromQuery] string? section,
        [FromQuery] Guid? userId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var denied = await RequireAccessAsync(buildingId, ConfigSections.AuditRoles, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = dbContext.FinanceAuditLogs.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId);

        if (!string.IsNullOrWhiteSpace(section))
        {
            var key = section.Trim();
            query = query.Where(x => x.Section == key);
        }

        if (userId.HasValue)
        {
            query = query.Where(x => x.UserId == userId.Value);
        }

        if (from.HasValue)
        {
            var fromUtc = from.Value.ToUniversalTime();
            query = query.Where(x => x.CreatedAtUtc >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = to.Value.ToUniversalTime();
            query = query.Where(x => x.CreatedAtUtc <= toUtc);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(x => x.UserId).Distinct().ToList();
        var names = await dbContext.ApplicationUsers.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        return Ok(new ConfigAuditPageDto
        {
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            Items = rows.Select(x => new ConfigAuditEntryDto
            {
                Id = x.Id,
                CreatedAtUtc = x.CreatedAtUtc,
                Section = x.Section,
                SectionName = ConfigSections.NameOf(x.Section),
                Action = x.Action,
                Summary = x.Summary,
                EntityType = x.EntityType,
                EntityId = x.EntityId,
                UserId = x.UserId,
                UserName = names.GetValueOrDefault(x.UserId) ?? x.UserEmail,
                UserEmail = x.UserEmail,
                UserRole = x.UserRole,
                Changes = ParseChanges(x.ChangesJson)
            }).ToList()
        });
    }

    // Rol permitido + edificio al que el usuario tiene acceso. Devuelve null si todo esta bien.
    private async Task<ActionResult?> RequireAccessAsync(Guid buildingId, string[] roles, CancellationToken cancellationToken)
    {
        if (!roles.Contains(tenantContext.Role, StringComparer.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "building_config_forbidden",
                message = "Tu rol no tiene acceso a esta parte del Centro de configuración."
            });
        }

        if (buildingId == Guid.Empty || !await accessScope.CanAccessBuildingAsync(buildingId, cancellationToken))
        {
            return NotFound();
        }

        return null;
    }

    private static IReadOnlyList<ConfigChangeDto> ParseChanges(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ConfigChangeDto>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
