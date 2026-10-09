using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Politicas que se editan desde el Centro de configuracion del edificio: mora, fondos, alertas del presupuesto y avisos automaticos.
/// Guardan en los mismos campos que ya usa la ficha del edificio (una sola fuente de verdad); cada cambio queda en el historial del
/// Centro. Un edificio ajeno responde 404 y un rol sin permiso, 403.
/// </summary>
[ApiController]
[Authorize]
[Route("api/building-config/{buildingId:guid}")]
public class BuildingConfigPoliciesController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    PushDispatcher pushDispatcher) : ControllerBase
{
    private const decimal MaxCapPercentage = 1000m;
    private const decimal MaxMinAmount = 100_000_000m;
    private const decimal MaxThreshold = 999_999_999_999m;

    private static ConfigSectionDefinition Section(string key) => ConfigSections.Find(key)!;

    private ConfigAuditWriter Audit => new(dbContext, tenantContext);

    // ═════════════════════════════════════════════════════════════════════════
    // Politica de mora
    // ═════════════════════════════════════════════════════════════════════════

    [HttpGet("late-fee")]
    public async Task<ActionResult<LateFeePolicyDto>> GetLateFee(Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, Section(ConfigSectionKeys.LateFee).ViewRoles, cancellationToken);
        if (denied is not null) return denied;

        var building = await LoadBuildingAsync(buildingId, tracking: false, cancellationToken);
        return building is null ? NotFound() : Ok(await ToLateFeeDtoAsync(building, cancellationToken));
    }

    [HttpPut("late-fee")]
    public async Task<ActionResult<LateFeePolicyDto>> UpdateLateFee(
        Guid buildingId, [FromBody] UpdateLateFeePolicyRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, Section(ConfigSectionKeys.LateFee).EditRoles, cancellationToken);
        if (denied is not null) return denied;

        var error = ValidateLateFee(request);
        if (error is not null) return BadRequest(error);

        var building = await LoadBuildingAsync(buildingId, tracking: true, cancellationToken);
        if (building is null) return NotFound();
        var companyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!companyId.HasValue) return BadRequest("El edificio no tiene empresa asignada.");

        var before = BuildingConfigSnapshot.Capture(building, []);
        var previousRate = building.LateFeeRatePercentage;
        var previousFrequency = building.LateFeeFrequency;

        // Sin tasa no hay frecuencia (igual que en la ficha); un valor en cero o vacio significa "no aplica".
        var rate = request.RatePercentage is > 0m ? decimal.Round(request.RatePercentage.Value, 2) : (decimal?)null;
        building.LateFeeRatePercentage = rate;
        building.LateFeeFrequency = rate.HasValue ? request.Frequency : null;
        building.GraceDays = request.GraceDays is > 0 ? request.GraceDays : null;
        building.LateFeeCapPercentage = request.CapPercentage is > 0m ? decimal.Round(request.CapPercentage.Value, 2) : null;
        building.LateFeeMinAmount = request.MinAmount is > 0m ? decimal.Round(request.MinAmount.Value, 2) : null;
        building.LateFeeAppliesToReserve = request.AppliesToReserve;
        building.LateFeeAppliesToExtraordinary = request.AppliesToExtraordinary;
        building.LateFeeAppliesToIndividual = request.AppliesToIndividual;
        // Guardar la politica es revisarla: con la tasa vacia queda como decision explicita de "sin mora".
        building.LateFeePolicyConfirmed = true;
        building.UpdatedAtUtc = DateTime.UtcNow;

        Audit.AddBuildingChanges(companyId.Value, building.Id, before, BuildingConfigSnapshot.Capture(building, []));

        LateFeeChangeNotifier.PendingPush? latePush = null;
        if (previousRate != building.LateFeeRatePercentage || previousFrequency != building.LateFeeFrequency)
        {
            latePush = await new LateFeeChangeNotifier(dbContext, tenantContext).QueueAsync(
                building, companyId.Value, previousRate, previousFrequency, building.LateFeeRatePercentage, building.LateFeeFrequency, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (latePush is not null)
        {
            await pushDispatcher.NotifyUsersAsync(latePush.RecipientIds, latePush.Title, latePush.Body, "Building", building.Id, cancellationToken);
        }

        return Ok(await ToLateFeeDtoAsync(building, cancellationToken));
    }

    private static string? ValidateLateFee(UpdateLateFeePolicyRequest request)
    {
        if (request.RatePercentage is < 0m or > 100m) return "La tasa de interés por mora debe estar entre 0 y 100.";
        if (request.RatePercentage is > 0m && !request.Frequency.HasValue) return "Definí el incremento de la mora (diario, semanal o quincenal).";
        if (request.Frequency.HasValue && !Enum.IsDefined(request.Frequency.Value)) return "El incremento de la mora no es válido.";
        if (request.GraceDays is < 0 or > 365) return "Los días de gracia deben estar entre 0 y 365.";
        if (request.CapPercentage is < 0m or > MaxCapPercentage) return $"El tope de la mora debe estar entre 0 y {MaxCapPercentage:0}.";
        if (request.MinAmount is < 0m or > MaxMinAmount) return "La mora mínima no es válida.";
        return null;
    }

    private async Task<LateFeePolicyDto> ToLateFeeDtoAsync(Building b, CancellationToken cancellationToken) => new()
    {
        BuildingId = b.Id,
        RatePercentage = b.LateFeeRatePercentage,
        Frequency = b.LateFeeFrequency,
        GraceDays = b.GraceDays,
        CapPercentage = b.LateFeeCapPercentage,
        MinAmount = b.LateFeeMinAmount,
        AppliesToReserve = b.LateFeeAppliesToReserve,
        AppliesToExtraordinary = b.LateFeeAppliesToExtraordinary,
        AppliesToIndividual = b.LateFeeAppliesToIndividual,
        Confirmed = b.LateFeePolicyConfirmed,
        ExemptUnitCount = await dbContext.Units.AsNoTracking().CountAsync(x => !x.IsDeleted && x.BuildingId == b.Id && x.LateFeeExempt, cancellationToken),
        CanEdit = CanEdit(ConfigSectionKeys.LateFee)
    };

    // ═════════════════════════════════════════════════════════════════════════
    // Fondos
    // ═════════════════════════════════════════════════════════════════════════

    [HttpGet("fund-policy")]
    public async Task<ActionResult<FundPolicyDto>> GetFundPolicy(Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, Section(ConfigSectionKeys.Funds).ViewRoles, cancellationToken);
        if (denied is not null) return denied;

        var building = await LoadBuildingAsync(buildingId, tracking: false, cancellationToken);
        return building is null ? NotFound() : Ok(ToFundDto(building));
    }

    [HttpPut("fund-policy")]
    public async Task<ActionResult<FundPolicyDto>> UpdateFundPolicy(
        Guid buildingId, [FromBody] UpdateFundPolicyRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, Section(ConfigSectionKeys.Funds).EditRoles, cancellationToken);
        if (denied is not null) return denied;

        if (!Enum.IsDefined(request.IncomeTreatment)) return BadRequest("El tratamiento de los ingresos no es válido.");
        if (!Enum.IsDefined(request.ReserveUsePolicy)) return BadRequest("La política de uso del fondo no es válida.");
        if (request.ReserveFundPercentage is < 0m or > 100m) return BadRequest("El porcentaje de fondo de reserva debe estar entre 0 y 100.");
        if (request.ExtraordinaryPercentage is < 0m or > 100m) return BadRequest("El porcentaje de aporte extraordinario debe estar entre 0 y 100.");
        if (request.ReserveUseThreshold is < 0m or > MaxThreshold) return BadRequest("El monto desde el que rige la política de uso no es válido.");

        var building = await LoadBuildingAsync(buildingId, tracking: true, cancellationToken);
        if (building is null) return NotFound();
        var companyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!companyId.HasValue) return BadRequest("El edificio no tiene empresa asignada.");

        var before = BuildingConfigSnapshot.Capture(building, []);

        building.IncomeTreatment = request.IncomeTreatment;
        building.ReserveFundPercentage = request.ReserveFundPercentage is > 0m ? decimal.Round(request.ReserveFundPercentage.Value, 2) : null;
        building.ExtraordinaryPercentage = request.ExtraordinaryPercentage is > 0m ? decimal.Round(request.ExtraordinaryPercentage.Value, 2) : null;
        building.ReserveUsePolicy = request.ReserveUsePolicy;
        building.ReserveUseThreshold = request.ReserveUseThreshold is > 0m ? decimal.Round(request.ReserveUseThreshold.Value, 2) : null;
        // Guardar la politica es revisarla: sin aportes queda como decision explicita de "no aplica".
        building.FundPolicyConfirmed = true;
        building.UpdatedAtUtc = DateTime.UtcNow;

        Audit.AddBuildingChanges(companyId.Value, building.Id, before, BuildingConfigSnapshot.Capture(building, []));
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToFundDto(building));
    }

    private FundPolicyDto ToFundDto(Building b) => new()
    {
        BuildingId = b.Id,
        IncomeTreatment = b.IncomeTreatment,
        ReserveFundPercentage = b.ReserveFundPercentage,
        ExtraordinaryPercentage = b.ExtraordinaryPercentage,
        ReserveUsePolicy = b.ReserveUsePolicy,
        ReserveUseThreshold = b.ReserveUseThreshold,
        Confirmed = b.FundPolicyConfirmed,
        CanEdit = CanEdit(ConfigSectionKeys.Funds)
    };

    // ═════════════════════════════════════════════════════════════════════════
    // Alertas del presupuesto
    // ═════════════════════════════════════════════════════════════════════════

    [HttpGet("budget-alerts")]
    public async Task<ActionResult<BudgetAlertsDto>> GetBudgetAlerts(Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, Section(ConfigSectionKeys.Budget).ViewRoles, cancellationToken);
        if (denied is not null) return denied;

        var state = await new FinanceModuleGate(dbContext).GetStateAsync(buildingId, cancellationToken);
        if (!state.BuildingFound) return NotFound();

        var warn = await dbContext.FinanceSettings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .Select(x => (int?)x.BudgetWarnPercent)
            .FirstOrDefaultAsync(cancellationToken) ?? 10;

        return Ok(new BudgetAlertsDto
        {
            BuildingId = buildingId,
            FinanceAvailable = state.IsAvailable,
            WarnPercent = warn,
            CanEdit = CanEdit(ConfigSectionKeys.Budget)
        });
    }

    [HttpPut("budget-alerts")]
    public async Task<ActionResult<BudgetAlertsDto>> UpdateBudgetAlerts(
        Guid buildingId, [FromBody] UpdateBudgetAlertsRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, Section(ConfigSectionKeys.Budget).EditRoles, cancellationToken);
        if (denied is not null) return denied;

        var state = await new FinanceModuleGate(dbContext).GetStateAsync(buildingId, cancellationToken);
        if (!state.BuildingFound) return NotFound();
        if (!state.Enabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = FinanceModuleGate.DisabledCode, message = FinanceModuleGate.DisabledMessage });
        }

        if (!state.PlanIncludesModule)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = FinanceModuleGate.PlanNotIncludedCode, message = FinanceModuleGate.PlanNotIncludedMessage });
        }

        if (request.WarnPercent is < 1 or > 100) return BadRequest("El umbral del semáforo debe estar entre 1 y 100.");

        var settings = await dbContext.FinanceSettings.FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == buildingId, cancellationToken);
        if (settings is null)
        {
            return Conflict(new
            {
                error = "finance_setup_incomplete",
                message = "Completá la configuración inicial de Finanzas del edificio para configurar las alertas del presupuesto."
            });
        }

        if (settings.BudgetWarnPercent != request.WarnPercent)
        {
            Audit.Add(settings.CompanyId, buildingId, ConfigSectionKeys.Budget, "Updated",
                $"Presupuesto: el umbral del semáforo pasó de {settings.BudgetWarnPercent} % a {request.WarnPercent} %.", "FinanceSettings", settings.Id,
                [new ConfigChange("budgetWarnPercent", "Umbral del semáforo (%)", settings.BudgetWarnPercent.ToString(), request.WarnPercent.ToString())]);
            settings.BudgetWarnPercent = request.WarnPercent;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new BudgetAlertsDto
        {
            BuildingId = buildingId,
            FinanceAvailable = true,
            WarnPercent = settings.BudgetWarnPercent,
            CanEdit = CanEdit(ConfigSectionKeys.Budget)
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Impuestos: tratamiento de IVA de cada cuenta de egresos
    // ═════════════════════════════════════════════════════════════════════════

    [HttpGet("vat-treatments")]
    public async Task<ActionResult<VatTreatmentsDto>> GetVatTreatments(Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, Section(ConfigSectionKeys.Taxes).ViewRoles, cancellationToken);
        if (denied is not null) return denied;

        var building = await LoadBuildingAsync(buildingId, tracking: false, cancellationToken);
        if (building is null) return NotFound();

        var state = await new FinanceModuleGate(dbContext).GetStateAsync(buildingId, cancellationToken);
        if (!state.IsAvailable)
        {
            return Ok(new VatTreatmentsDto
            {
                BuildingId = buildingId, FinanceAvailable = false, BuildingVatRegime = building.VatRegime, CanEdit = CanEdit(ConfigSectionKeys.Taxes)
            });
        }

        return Ok(await BuildVatTreatmentsAsync(building, cancellationToken));
    }

    [HttpPut("vat-treatments")]
    public async Task<ActionResult<VatTreatmentsDto>> UpdateVatTreatments(
        Guid buildingId, [FromBody] UpdateVatTreatmentsRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, Section(ConfigSectionKeys.Taxes).EditRoles, cancellationToken);
        if (denied is not null) return denied;

        var building = await LoadBuildingAsync(buildingId, tracking: false, cancellationToken);
        if (building is null) return NotFound();
        var companyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!companyId.HasValue) return BadRequest("El edificio no tiene empresa asignada.");

        var state = await new FinanceModuleGate(dbContext).GetStateAsync(buildingId, cancellationToken);
        if (!state.Enabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = FinanceModuleGate.DisabledCode, message = FinanceModuleGate.DisabledMessage });
        }

        if (!state.PlanIncludesModule)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = FinanceModuleGate.PlanNotIncludedCode, message = FinanceModuleGate.PlanNotIncludedMessage });
        }

        var items = request.Items ?? [];
        if (items.Any(x => x.Treatment.HasValue && !Enum.IsDefined(x.Treatment.Value))) return BadRequest("Hay un tratamiento de IVA que no es válido.");
        if (items.GroupBy(x => x.CategoryId).Any(g => g.Count() > 1)) return BadRequest("Hay una cuenta repetida.");

        var all = await dbContext.LedgerCategories
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .ToListAsync(cancellationToken);
        var parentIds = all.Where(x => x.ParentId.HasValue).Select(x => x.ParentId!.Value).ToHashSet();
        var leaves = all.Where(x => x.Type == LedgerCategoryType.Expense && !parentIds.Contains(x.Id)).ToDictionary(x => x.Id);

        if (items.Any(x => !leaves.ContainsKey(x.CategoryId)))
        {
            return BadRequest("Alguna cuenta no existe en este edificio o no es una cuenta final de egresos.");
        }

        var changes = new List<ConfigChange>();
        foreach (var item in items)
        {
            var category = leaves[item.CategoryId];
            if (category.VatTreatment == item.Treatment) continue;

            changes.Add(new ConfigChange($"vat.{category.Code}", $"{category.Code} {category.Name}", VatLabel(category.VatTreatment), VatLabel(item.Treatment)));
            category.VatTreatment = item.Treatment;
            category.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (changes.Count > 0)
        {
            Audit.Add(companyId.Value, buildingId, ConfigSectionKeys.Taxes, "Updated",
                $"IVA de las compras: cambió el tratamiento de {changes.Count} cuentas de egresos.", "LedgerCategory", null, changes);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(await BuildVatTreatmentsAsync(building, cancellationToken));
    }

    private async Task<VatTreatmentsDto> BuildVatTreatmentsAsync(Building building, CancellationToken cancellationToken)
    {
        var all = await dbContext.LedgerCategories.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == building.Id)
            .ToListAsync(cancellationToken);
        var byId = all.ToDictionary(x => x.Id);
        var parentIds = all.Where(x => x.ParentId.HasValue).Select(x => x.ParentId!.Value).ToHashSet();

        var items = all
            .Where(x => x.Type == LedgerCategoryType.Expense && !parentIds.Contains(x.Id))
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .Select(x => new VatTreatmentItemDto
            {
                CategoryId = x.Id,
                Code = x.Code,
                Name = x.Name,
                GroupName = x.ParentId is { } pid && byId.TryGetValue(pid, out var parent) ? $"{parent.Code} {parent.Name}" : string.Empty,
                IsActive = x.IsActive,
                Treatment = x.VatTreatment
            })
            .ToList();

        var active = items.Where(x => x.IsActive).ToList();
        return new VatTreatmentsDto
        {
            BuildingId = building.Id,
            FinanceAvailable = true,
            BuildingVatRegime = building.VatRegime,
            Applies = building.VatRegime == VatRegime.General,
            DefinedCount = active.Count(x => x.Treatment.HasValue),
            TotalCount = active.Count,
            CanEdit = CanEdit(ConfigSectionKeys.Taxes),
            Items = items
        };
    }

    private static string? VatLabel(VatTreatment? treatment) => treatment switch
    {
        VatTreatment.Vat10 => "IVA 10 %",
        VatTreatment.Vat5 => "IVA 5 %",
        VatTreatment.Exempt => "Exento",
        VatTreatment.NotApplicable => "No corresponde",
        _ => null
    };

    // ═════════════════════════════════════════════════════════════════════════
    // Avisos automaticos
    // ═════════════════════════════════════════════════════════════════════════

    private static readonly NoticeKind[] KindOrder =
        [NoticeKind.BeforeDue, NoticeKind.OnDue, NoticeKind.LateFeeApplied, NoticeKind.PaymentReceived, NoticeKind.PeriodPublished];

    [HttpGet("notice-rules")]
    public async Task<ActionResult<NoticeRulesDto>> GetNoticeRules(Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, Section(ConfigSectionKeys.Documents).ViewRoles, cancellationToken);
        if (denied is not null) return denied;

        if (!await dbContext.Buildings.AsNoTracking().AnyAsync(x => !x.IsDeleted && x.Id == buildingId, cancellationToken)) return NotFound();
        return Ok(await BuildNoticeRulesAsync(buildingId, cancellationToken));
    }

    [HttpPut("notice-rules")]
    public async Task<ActionResult<NoticeRulesDto>> UpdateNoticeRules(
        Guid buildingId, [FromBody] UpdateNoticeRulesRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, Section(ConfigSectionKeys.Documents).EditRoles, cancellationToken);
        if (denied is not null) return denied;

        var building = await LoadBuildingAsync(buildingId, tracking: false, cancellationToken);
        if (building is null) return NotFound();
        var companyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!companyId.HasValue) return BadRequest("El edificio no tiene empresa asignada.");

        var items = request.Rules ?? [];
        if (items.Any(x => !Enum.IsDefined(x.Kind))) return BadRequest("Hay un tipo de aviso que no es válido.");
        if (items.GroupBy(x => x.Kind).Any(g => g.Count() > 1)) return BadRequest("Hay un tipo de aviso repetido.");
        foreach (var item in items.Where(x => x.Kind == NoticeKind.BeforeDue && x.IsActive))
        {
            if (item.OffsetDays is null or < NoticeRules.MinOffsetDays or > NoticeRules.MaxOffsetDays)
            {
                return BadRequest($"Los días de anticipación del aviso antes del vencimiento deben estar entre {NoticeRules.MinOffsetDays} y {NoticeRules.MaxOffsetDays}.");
            }
        }

        var existing = await dbContext.BuildingNoticeRules
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .ToListAsync(cancellationToken);
        var current = (await BuildNoticeRulesAsync(buildingId, cancellationToken)).Rules.ToDictionary(x => x.Kind);

        var changes = new List<ConfigChange>();
        foreach (var item in items)
        {
            var offset = item.Kind == NoticeKind.BeforeDue ? (item.IsActive ? item.OffsetDays : current[item.Kind].OffsetDays) : null;
            var now = current[item.Kind];
            var label = NoticeRules.Label(item.Kind);
            if (now.IsActive != item.IsActive)
            {
                changes.Add(new ConfigChange($"{item.Kind}.active", label, now.IsActive ? "Encendido" : "Apagado", item.IsActive ? "Encendido" : "Apagado"));
            }

            if (item.Kind == NoticeKind.BeforeDue && item.IsActive && now.OffsetDays != offset)
            {
                changes.Add(new ConfigChange($"{item.Kind}.offsetDays", $"{label}: días de anticipación", now.OffsetDays?.ToString(), offset?.ToString()));
            }

            var rule = existing.FirstOrDefault(x => x.Kind == item.Kind);
            if (rule is null)
            {
                rule = new BuildingNoticeRule { CompanyId = companyId.Value, BuildingId = buildingId, Kind = item.Kind };
                dbContext.BuildingNoticeRules.Add(rule);
            }

            rule.IsActive = item.IsActive;
            rule.OffsetDays = offset;
            rule.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (changes.Count > 0)
        {
            Audit.Add(companyId.Value, buildingId, ConfigSectionKeys.Documents, "Updated",
                $"Avisos automáticos: cambió {string.Join(", ", changes.Select(x => x.Label).Distinct())}.", "BuildingNoticeRule", null, changes);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict("Otro cambio se hizo al mismo tiempo. Actualizá la pantalla y reintentá.");
        }

        return Ok(await BuildNoticeRulesAsync(buildingId, cancellationToken));
    }

    private async Task<NoticeRulesDto> BuildNoticeRulesAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        var rules = await dbContext.BuildingNoticeRules.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .ToListAsync(cancellationToken);

        return new NoticeRulesDto
        {
            BuildingId = buildingId,
            CanEdit = CanEdit(ConfigSectionKeys.Documents),
            Rules = KindOrder.Select(kind =>
            {
                var rule = rules.FirstOrDefault(x => x.Kind == kind);
                return new NoticeRuleDto
                {
                    Kind = kind,
                    Label = NoticeRules.Label(kind),
                    IsActive = rule?.IsActive ?? NoticeRules.DefaultActive(kind),
                    OffsetDays = kind == NoticeKind.BeforeDue ? rule?.OffsetDays ?? NoticeRules.DefaultOffsetDays : null,
                    IsDefault = rule is null
                };
            }).ToList()
        };
    }

    // ═════════════════════════════════════════════════════════════════════════

    private bool CanEdit(string sectionKey) => ConfigSections.CanEdit(Section(sectionKey), tenantContext.Role);

    private async Task<Building?> LoadBuildingAsync(Guid buildingId, bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<Building> query = dbContext.Buildings.Include(x => x.Condominium);
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == buildingId, cancellationToken);
    }

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
}
