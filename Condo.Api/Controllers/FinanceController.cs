using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Modulo "Finanzas del edificio", fase 1: acceso, interruptor por edificio (solo SuperAdmin) y configuracion
/// (fecha de arranque y asistente). Las cuentas y el plan de cuentas viven en sus propios controladores.
/// </summary>
[Route("api/finance")]
public class FinanceController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate) : FinanceControllerBase(dbContext, accessScope, tenantContext, gate)
{
    private static readonly DateOnly MinStartDate = new(2000, 1, 1);

    // ─── Descubrimiento: edificios del usuario con el modulo disponible (menu y selector) ──────────────────────────
    [HttpGet("buildings")]
    public async Task<ActionResult<IReadOnlyList<FinanceBuildingAccessDto>>> GetBuildings(CancellationToken cancellationToken)
    {
        if (!IsFinanceRole)
        {
            return FinanceForbidden(FinanceModuleGate.ForbiddenCode, FinanceModuleGate.RoleNotAllowedMessage);
        }

        var accessibleIds = await AccessScope.GetAccessibleBuildingIdsAsync(cancellationToken);
        var withPlan = await Gate.PlansIncludingModuleAsync(accessibleIds, cancellationToken);
        if (withPlan.Count == 0)
        {
            return Ok(Array.Empty<FinanceBuildingAccessDto>());
        }

        var buildings = await Db.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.FinanceModuleEnabled && withPlan.Contains(x.Id))
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        var buildingIds = buildings.Select(x => x.Id).ToList();
        var completed = (await Db.FinanceSettings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.SetupCompleted && buildingIds.Contains(x.BuildingId))
            .Select(x => x.BuildingId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        return Ok(buildings.Select(x => new FinanceBuildingAccessDto
        {
            BuildingId = x.Id,
            BuildingName = x.Name,
            SetupCompleted = completed.Contains(x.Id)
        }).ToList());
    }

    // ─── SuperAdmin: todos los edificios con su plan y el interruptor ──────────────────────────────────────────────
    [HttpGet("admin/buildings")]
    public async Task<ActionResult<IReadOnlyList<FinanceAdminBuildingDto>>> GetAdminBuildings(CancellationToken cancellationToken)
    {
        if (!Tenant.IsSuperAdmin)
        {
            return FinanceForbidden(FinanceModuleGate.ForbiddenCode, "Solo el SuperAdmin administra el módulo Finanzas por edificio.");
        }

        return Ok(await BuildAdminRowsAsync(null, cancellationToken));
    }

    [HttpPost("settings/enable")]
    public async Task<ActionResult<FinanceAdminBuildingDto>> Enable([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        if (!Tenant.IsSuperAdmin)
        {
            return FinanceForbidden(FinanceModuleGate.ForbiddenCode, "Solo el SuperAdmin puede habilitar el módulo Finanzas del edificio.");
        }

        var building = await Db.Buildings
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == buildingId, cancellationToken);
        if (building is null)
        {
            return NotFound();
        }

        var companyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!companyId.HasValue)
        {
            return BadRequest(NoCompanyMessage);
        }

        var state = await Gate.GetStateAsync(buildingId, cancellationToken);
        if (!state.PlanIncludesModule)
        {
            return BadRequest("El plan vigente del edificio no incluye el módulo Finanzas del edificio. Asigná un plan que lo incluya antes de habilitarlo.");
        }

        var now = DateTime.UtcNow;
        building.FinanceModuleEnabled = true;

        var settings = await Db.FinanceSettings.FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == buildingId, cancellationToken);
        if (settings is null)
        {
            settings = new FinanceSettings { CompanyId = companyId.Value, BuildingId = buildingId };
            Db.FinanceSettings.Add(settings);
        }

        settings.EnabledAtUtc = now;
        settings.EnabledByUserId = Tenant.UserId;

        // Plantilla estandar solo la primera vez: si el edificio ya tiene rubros (apagado y vuelto a encender) se respetan.
        var hasCategories = await Db.LedgerCategories.AnyAsync(x => !x.IsDeleted && x.BuildingId == buildingId, cancellationToken);
        if (!hasCategories)
        {
            Db.LedgerCategories.AddRange(FinanceChartTemplate.CreateEntities(buildingId, companyId.Value));
        }

        var conflict = await SaveOrConflictAsync(cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        return Ok((await BuildAdminRowsAsync(buildingId, cancellationToken)).Single());
    }

    [HttpPost("settings/disable")]
    public async Task<ActionResult<FinanceAdminBuildingDto>> Disable([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        if (!Tenant.IsSuperAdmin)
        {
            return FinanceForbidden(FinanceModuleGate.ForbiddenCode, "Solo el SuperAdmin puede apagar el módulo Finanzas del edificio.");
        }

        var building = await Db.Buildings.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == buildingId, cancellationToken);
        if (building is null)
        {
            return NotFound();
        }

        // Apagar conserva la configuracion y los datos: al volver a encenderlo sigue todo como estaba.
        building.FinanceModuleEnabled = false;
        await Db.SaveChangesAsync(cancellationToken);

        return Ok((await BuildAdminRowsAsync(buildingId, cancellationToken)).Single());
    }

    // ─── Configuracion del edificio ────────────────────────────────────────────────────────────────────────────────
    [HttpGet("settings")]
    public async Task<ActionResult<FinanceSettingsDto>> GetSettings([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: false, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var settings = await Db.FinanceSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == buildingId, cancellationToken);

        return Ok(await BuildSettingsDtoAsync(buildingId, settings, cancellationToken));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<FinanceSettingsDto>> UpdateSettings(
        [FromQuery] Guid buildingId, [FromBody] FinanceSettingsUpdateRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: true, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        if (!request.FinanceStartDate.HasValue)
        {
            return BadRequest("La fecha de arranque es obligatoria.");
        }

        var startDate = request.FinanceStartDate.Value;
        var maxStartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);
        if (startDate < MinStartDate || startDate > maxStartDate)
        {
            return BadRequest($"La fecha de arranque debe estar entre {MinStartDate:dd/MM/yyyy} y {maxStartDate:dd/MM/yyyy}.");
        }

        if (request.FiscalYearStartMonth is < 1 or > 12)
        {
            return BadRequest("El mes de inicio del ejercicio debe estar entre 1 y 12.");
        }

        var settings = await GetOrCreateSettingsAsync(buildingId, cancellationToken);
        if (settings is null)
        {
            return BadRequest(NoCompanyMessage);
        }

        settings.FinanceStartDate = startDate;
        settings.FiscalYearStartMonth = request.FiscalYearStartMonth;

        var conflict = await SaveOrConflictAsync(cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        return Ok(await BuildSettingsDtoAsync(buildingId, settings, cancellationToken));
    }

    // Marca el asistente como completo. Pide fecha de arranque, al menos una caja o banco activa y un plan de cuentas.
    [HttpPost("settings/complete")]
    public async Task<ActionResult<FinanceSettingsDto>> CompleteSetup([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: true, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var settings = await GetOrCreateSettingsAsync(buildingId, cancellationToken);
        if (settings is null)
        {
            return BadRequest(NoCompanyMessage);
        }

        var missing = await ComputeMissingAsync(buildingId, settings, cancellationToken);
        if (missing.Count > 0)
        {
            return BadRequest("Todavía no se puede completar la configuración: " + string.Join(" ", missing));
        }

        if (!settings.SetupCompleted)
        {
            settings.SetupCompleted = true;
            settings.SetupCompletedAtUtc = DateTime.UtcNow;
            settings.SetupCompletedByUserId = Tenant.UserId;
        }

        var conflict = await SaveOrConflictAsync(cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        return Ok(await BuildSettingsDtoAsync(buildingId, settings, cancellationToken));
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Devuelve la configuracion (rastreada) del edificio, creandola si no existe; null si el edificio no tiene empresa.
    private async Task<FinanceSettings?> GetOrCreateSettingsAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        var settings = await Db.FinanceSettings.FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == buildingId, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        var companyId = await ResolveCompanyIdAsync(buildingId, cancellationToken);
        if (!companyId.HasValue)
        {
            return null;
        }

        settings = new FinanceSettings { CompanyId = companyId.Value, BuildingId = buildingId };
        Db.FinanceSettings.Add(settings);
        return settings;
    }

    private async Task<List<string>> ComputeMissingAsync(Guid buildingId, FinanceSettings? settings, CancellationToken cancellationToken)
    {
        var missing = new List<string>();

        if (settings?.FinanceStartDate is null)
        {
            missing.Add("Definí la fecha de arranque.");
        }

        var hasCashOrBank = await Db.FinancialAccounts.AnyAsync(
            x => !x.IsDeleted && x.IsActive && x.BuildingId == buildingId && x.Type != FinancialAccountType.ReserveFund,
            cancellationToken);
        if (!hasCashOrBank)
        {
            missing.Add("Cargá al menos una cuenta de caja o banco con su saldo inicial.");
        }

        var hasCategories = await Db.LedgerCategories.AnyAsync(
            x => !x.IsDeleted && x.IsActive && x.BuildingId == buildingId, cancellationToken);
        if (!hasCategories)
        {
            missing.Add("El plan de cuentas no tiene rubros activos.");
        }

        return missing;
    }

    private async Task<FinanceSettingsDto> BuildSettingsDtoAsync(Guid buildingId, FinanceSettings? settings, CancellationToken cancellationToken)
    {
        var building = await Db.Buildings.AsNoTracking()
            .Where(x => x.Id == buildingId)
            .Select(x => new { x.Name, x.ReserveFundPercentage })
            .FirstAsync(cancellationToken);

        var accountCount = await Db.FinancialAccounts.CountAsync(x => !x.IsDeleted && x.BuildingId == buildingId, cancellationToken);
        var categoryCount = await Db.LedgerCategories.CountAsync(x => !x.IsDeleted && x.BuildingId == buildingId, cancellationToken);

        return new FinanceSettingsDto
        {
            BuildingId = buildingId,
            BuildingName = building.Name,
            FinanceStartDate = settings?.FinanceStartDate,
            FiscalYearStartMonth = settings?.FiscalYearStartMonth ?? 1,
            SetupCompleted = settings?.SetupCompleted ?? false,
            SetupCompletedAtUtc = settings?.SetupCompletedAtUtc,
            ReserveFundPercentage = building.ReserveFundPercentage,
            AccountCount = accountCount,
            CategoryCount = categoryCount,
            MissingForSetup = await ComputeMissingAsync(buildingId, settings, cancellationToken),
            CanEdit = CanConfigure
        };
    }

    // Filas del listado del SuperAdmin (todos los edificios, o uno solo).
    private async Task<List<FinanceAdminBuildingDto>> BuildAdminRowsAsync(Guid? onlyBuildingId, CancellationToken cancellationToken)
    {
        var buildingsQuery = Db.Buildings.AsNoTracking().Where(x => !x.IsDeleted);
        if (onlyBuildingId.HasValue)
        {
            buildingsQuery = buildingsQuery.Where(x => x.Id == onlyBuildingId.Value);
        }

        var buildings = await buildingsQuery
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.FinanceModuleEnabled,
                CompanyName = x.Company != null
                    ? x.Company.Name
                    : (x.Condominium != null && x.Condominium.Company != null ? x.Condominium.Company.Name : string.Empty),
                CondominiumName = x.Condominium != null ? x.Condominium.Name : string.Empty
            })
            .ToListAsync(cancellationToken);

        var buildingIds = buildings.Select(x => x.Id).ToList();

        var plans = await Db.BuildingPlans.AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsArchived && x.Plan != null && buildingIds.Contains(x.BuildingId))
            .Select(x => new
            {
                x.BuildingId,
                x.PlanId,
                PlanName = x.Plan!.Name,
                Includes = x.Plan.IncludesFinanceModule,
                x.EndDate,
                Grace = x.Plan.GracePeriodDays
            })
            .ToListAsync(cancellationToken);
        var planByBuilding = plans.GroupBy(x => x.BuildingId).ToDictionary(g => g.Key, g => g.ToList());

        var settings = await Db.FinanceSettings.AsNoTracking()
            .Where(x => !x.IsDeleted && buildingIds.Contains(x.BuildingId))
            .ToListAsync(cancellationToken);
        var settingsByBuilding = settings.ToDictionary(x => x.BuildingId);

        var today = DateTime.UtcNow.Date;

        return buildings.Select(b =>
        {
            planByBuilding.TryGetValue(b.Id, out var buildingPlans);
            var current = buildingPlans?.OrderByDescending(x => x.EndDate).FirstOrDefault();
            var includes = buildingPlans?.Any(x => x.Includes) ?? false;
            settingsByBuilding.TryGetValue(b.Id, out var s);

            return new FinanceAdminBuildingDto
            {
                BuildingId = b.Id,
                BuildingName = b.Name,
                CompanyName = b.CompanyName,
                CondominiumName = b.CondominiumName,
                PlanId = current?.PlanId,
                PlanName = current?.PlanName ?? string.Empty,
                PlanStatus = current is null ? string.Empty : PlanAccessPolicy.GetStatusName(false, current.EndDate, current.Grace, today),
                PlanIncludesFinanceModule = includes,
                ModuleEnabled = b.FinanceModuleEnabled,
                ModuleAvailable = b.FinanceModuleEnabled && includes,
                SetupCompleted = s?.SetupCompleted ?? false,
                FinanceStartDate = s?.FinanceStartDate,
                EnabledAtUtc = s?.EnabledAtUtc
            };
        }).ToList();
    }
}
