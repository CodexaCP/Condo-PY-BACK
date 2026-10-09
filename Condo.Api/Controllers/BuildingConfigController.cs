using System.Text.Json;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
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

    // ─── Periodo y cierre ────────────────────────────────────────────────────

    private const int ReasonMaxLength = 500;
    private const int MaxClosingMonths = 120;

    private static readonly string[] ClosingViewRoles = ConfigSections.Find(ConfigSectionKeys.Closing)!.ViewRoles;
    private static readonly string[] ClosingEditRoles = ConfigSections.Find(ConfigSectionKeys.Closing)!.EditRoles;

    [HttpGet("closing")]
    public async Task<ActionResult<PeriodClosingDto>> GetClosing(Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, ClosingViewRoles, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var state = await new FinanceModuleGate(dbContext).GetStateAsync(buildingId, cancellationToken);
        if (!state.BuildingFound)
        {
            return NotFound();
        }

        var canEdit = ClosingEditRoles.Contains(tenantContext.Role, StringComparer.OrdinalIgnoreCase);
        if (!state.IsAvailable)
        {
            return Ok(new PeriodClosingDto { BuildingId = buildingId, FinanceAvailable = false, CanEdit = canEdit });
        }

        var settings = await dbContext.FinanceSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == buildingId, cancellationToken);

        var closures = await dbContext.FinancePeriodClosures.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .OrderByDescending(x => x.ClosedAtUtc)
            .ToListAsync(cancellationToken);
        var names = await UserNamesAsync(
            closures.Select(x => x.ClosedByUserId).Concat(closures.Where(x => x.ReopenedByUserId.HasValue).Select(x => x.ReopenedByUserId!.Value)),
            cancellationToken);

        var enabled = settings?.PeriodClosingEnabled == true;
        var months = new List<ClosingMonthDto>();
        if (settings?.FinanceStartDate is { } start)
        {
            var today = FinancePeriods.Today();
            var current = new DateOnly(today.Year, today.Month, 1);
            var first = new DateOnly(start.Year, start.Month, 1);
            if (current.DayNumber - first.DayNumber > MaxClosingMonths * 31)
            {
                first = current.AddMonths(-(MaxClosingMonths - 1));
            }

            var active = closures.Where(x => x.ReopenedAtUtc is null).ToDictionary(x => (x.Year, x.Month));
            var firstOpenFound = false;
            for (var month = first; month <= current; month = month.AddMonths(1))
            {
                var dto = new ClosingMonthDto { Year = month.Year, Month = month.Month, Label = $"{month.Month:00}/{month.Year}" };
                if (active.TryGetValue((month.Year, month.Month), out var closure))
                {
                    dto.Status = ClosingMonthStatus.Closed;
                    dto.ClosedAtUtc = closure.ClosedAtUtc;
                    dto.ClosedByName = names.GetValueOrDefault(closure.ClosedByUserId);
                    dto.CanReopen = canEdit;
                }
                else if (month == current)
                {
                    dto.Status = ClosingMonthStatus.Current;
                    dto.CannotCloseReason = "El mes todavía no terminó.";
                }
                else
                {
                    dto.Status = ClosingMonthStatus.Open;
                    if (!enabled)
                    {
                        dto.CannotCloseReason = "El cierre de período está apagado.";
                    }
                    else if (!canEdit)
                    {
                        dto.CannotCloseReason = "Solo el SuperAdmin o el Administrador de empresa pueden cerrar meses.";
                    }
                    else if (firstOpenFound)
                    {
                        dto.CannotCloseReason = "Hay un mes anterior sin cerrar: se cierra en orden.";
                    }
                    else
                    {
                        dto.CanClose = true;
                    }

                    firstOpenFound = true;
                }

                months.Add(dto);
            }
        }

        return Ok(new PeriodClosingDto
        {
            BuildingId = buildingId,
            FinanceAvailable = true,
            Enabled = enabled,
            CanEdit = canEdit,
            FinanceStartDate = settings?.FinanceStartDate,
            FiscalYearStartMonth = settings?.FiscalYearStartMonth ?? 1,
            Months = months,
            History = closures.Take(24).Select(x => new ClosureHistoryDto
            {
                Id = x.Id,
                Year = x.Year,
                Month = x.Month,
                Label = $"{x.Month:00}/{x.Year}",
                ClosedAtUtc = x.ClosedAtUtc,
                ClosedByName = names.GetValueOrDefault(x.ClosedByUserId) ?? string.Empty,
                ReopenedAtUtc = x.ReopenedAtUtc,
                ReopenedByName = x.ReopenedByUserId.HasValue ? names.GetValueOrDefault(x.ReopenedByUserId.Value) : null,
                ReopenReason = x.ReopenReason
            }).ToList()
        });
    }

    // Enciende o apaga el cierre. No se apaga mientras haya meses cerrados (primero se reabren, con motivo).
    [HttpPut("closing")]
    public async Task<ActionResult<PeriodClosingDto>> SetClosingEnabled(
        Guid buildingId, [FromBody] SetPeriodClosingRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireClosingWriteAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var settings = await dbContext.FinanceSettings.FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == buildingId, cancellationToken);
        if (settings is null)
        {
            return SetupIncomplete();
        }

        if (settings.PeriodClosingEnabled != request.Enabled)
        {
            if (!request.Enabled
                && await dbContext.FinancePeriodClosures.AnyAsync(
                    x => !x.IsDeleted && x.BuildingId == buildingId && x.ReopenedAtUtc == null, cancellationToken))
            {
                return Conflict(new
                {
                    error = "closing_has_closed_months",
                    message = "No se puede apagar el cierre mientras haya meses cerrados. Reabrí primero los meses cerrados (con su motivo)."
                });
            }

            if (request.Enabled && settings.FinanceStartDate is null)
            {
                return SetupIncomplete();
            }

            settings.PeriodClosingEnabled = request.Enabled;
            new ConfigAuditWriter(dbContext, tenantContext).Add(
                settings.CompanyId, buildingId, ConfigSectionKeys.Closing, request.Enabled ? "Enabled" : "Disabled",
                request.Enabled ? "Se encendió el cierre de período." : "Se apagó el cierre de período.",
                "FinanceSettings", settings.Id,
                [new ConfigChange("periodClosingEnabled", "Cierre de período", request.Enabled ? "No" : "Sí", request.Enabled ? "Sí" : "No")]);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GetClosing(buildingId, cancellationToken);
    }

    [HttpPost("closing/{year:int}/{month:int}/close")]
    public async Task<ActionResult<PeriodClosingDto>> CloseMonth(Guid buildingId, int year, int month, CancellationToken cancellationToken)
    {
        var denied = await RequireClosingWriteAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        if (year is < 2000 or > 2100 || month is < 1 or > 12)
        {
            return BadRequest("El mes no es válido.");
        }

        var settings = await dbContext.FinanceSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == buildingId, cancellationToken);
        if (settings?.FinanceStartDate is not { } start)
        {
            return SetupIncomplete();
        }

        if (!settings.PeriodClosingEnabled)
        {
            return Conflict(new { error = "closing_disabled", message = "El cierre de período está apagado para este edificio. Encendelo primero." });
        }

        var target = new DateOnly(year, month, 1);
        var first = new DateOnly(start.Year, start.Month, 1);
        var today = FinancePeriods.Today();
        var current = new DateOnly(today.Year, today.Month, 1);
        if (target < first)
        {
            return BadRequest("Ese mes es anterior a la fecha de arranque de Finanzas.");
        }

        if (target >= current)
        {
            return BadRequest("Solo se pueden cerrar meses ya terminados.");
        }

        var closedSet = await new FinancePeriodGuard(dbContext).ClosedSetAsync(buildingId, cancellationToken);
        if (closedSet.Contains((year, month)))
        {
            return Conflict(new { error = "month_already_closed", message = $"El mes {month:00}/{year} ya está cerrado." });
        }

        // Se cierra en orden: todos los meses desde el arranque hasta el anterior tienen que estar cerrados.
        for (var m = first; m < target; m = m.AddMonths(1))
        {
            if (!closedSet.Contains((m.Year, m.Month)))
            {
                return Conflict(new
                {
                    error = "previous_month_open",
                    message = $"Antes de cerrar {month:00}/{year} hay que cerrar {m.Month:00}/{m.Year}: los meses se cierran en orden."
                });
            }
        }

        var closure = new FinancePeriodClosure
        {
            CompanyId = settings.CompanyId,
            BuildingId = buildingId,
            Year = year,
            Month = month,
            ClosedAtUtc = DateTime.UtcNow,
            ClosedByUserId = tenantContext.UserId
        };
        dbContext.FinancePeriodClosures.Add(closure);
        new ConfigAuditWriter(dbContext, tenantContext).Add(
            settings.CompanyId, buildingId, ConfigSectionKeys.Closing, "Closed", $"Se cerró el mes {month:00}/{year}.",
            "FinancePeriodClosure", closure.Id);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "month_already_closed", message = $"El mes {month:00}/{year} se cerró al mismo tiempo desde otra pantalla. Actualizá la página." });
        }

        return await GetClosing(buildingId, cancellationToken);
    }

    [HttpPost("closing/{year:int}/{month:int}/reopen")]
    public async Task<ActionResult<PeriodClosingDto>> ReopenMonth(
        Guid buildingId, int year, int month, [FromBody] ReopenPeriodRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireClosingWriteAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var reason = request?.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            return BadRequest("El motivo de la reapertura es obligatorio.");
        }

        if (reason.Length > ReasonMaxLength)
        {
            return BadRequest($"El motivo no puede superar los {ReasonMaxLength} caracteres.");
        }

        var closure = await dbContext.FinancePeriodClosures.FirstOrDefaultAsync(
            x => !x.IsDeleted && x.BuildingId == buildingId && x.Year == year && x.Month == month && x.ReopenedAtUtc == null,
            cancellationToken);
        if (closure is null)
        {
            return NotFound();
        }

        closure.ReopenedAtUtc = DateTime.UtcNow;
        closure.ReopenedByUserId = tenantContext.UserId;
        closure.ReopenReason = reason;
        closure.UpdatedAtUtc = DateTime.UtcNow;
        new ConfigAuditWriter(dbContext, tenantContext).Add(
            closure.CompanyId, buildingId, ConfigSectionKeys.Closing, "Reopened", $"Se reabrió el mes {month:00}/{year}. Motivo: {reason}",
            "FinancePeriodClosure", closure.Id,
            [new ConfigChange("closed", "Mes cerrado", "Sí", "No"), new ConfigChange("reason", "Motivo", null, reason)]);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetClosing(buildingId, cancellationToken);
    }

    // Rol que edita el cierre, edificio al que tiene acceso y modulo Finanzas disponible (el cierre depende de el).
    private async Task<ActionResult?> RequireClosingWriteAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, ClosingEditRoles, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var state = await new FinanceModuleGate(dbContext).GetStateAsync(buildingId, cancellationToken);
        if (!state.BuildingFound)
        {
            return NotFound();
        }

        if (!state.Enabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = FinanceModuleGate.DisabledCode, message = FinanceModuleGate.DisabledMessage });
        }

        if (!state.PlanIncludesModule)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = FinanceModuleGate.PlanNotIncludedCode, message = FinanceModuleGate.PlanNotIncludedMessage });
        }

        return null;
    }

    private ObjectResult SetupIncomplete() => StatusCode(StatusCodes.Status409Conflict, new
    {
        error = "finance_setup_incomplete",
        message = "Completá la configuración inicial de Finanzas del edificio (fecha de arranque, cuentas y plan de cuentas) para usar el cierre de período."
    });

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await dbContext.ApplicationUsers.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);
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
