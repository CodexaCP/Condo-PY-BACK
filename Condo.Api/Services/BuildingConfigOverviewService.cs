using System.Globalization;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Arma el resumen del Centro de configuracion de un edificio: por cada seccion, su estado (completa, incompleta, opcional o no
/// disponible), el motivo y un resumen de solo lectura de lo configurado. No modifica nada. Las reglas de "completa" son las de la
/// especificacion (3.3); cada seccion solo aparece para los roles que la ven.
/// </summary>
public class BuildingConfigOverviewService(ICondoDbContext dbContext, FinanceModuleGate gate)
{
    public async Task<BuildingConfigOverviewDto?> BuildAsync(Guid buildingId, string role, CancellationToken cancellationToken)
    {
        var building = await dbContext.Buildings.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == buildingId, cancellationToken);
        if (building is null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activeBankAccounts = await dbContext.BuildingBankAccounts.AsNoTracking()
            .CountAsync(x => !x.IsDeleted && x.IsActive && x.BuildingId == buildingId, cancellationToken);
        var currentSeries = await dbContext.InvoiceSeries.AsNoTracking()
            .CountAsync(x => !x.IsDeleted && x.Activo && x.BuildingId == buildingId
                             && x.VigenciaDesde <= today && x.VigenciaHasta >= today, cancellationToken);
        var finance = await gate.GetStateAsync(buildingId, cancellationToken);
        var exemptUnits = await dbContext.Units.AsNoTracking()
            .CountAsync(x => !x.IsDeleted && x.BuildingId == buildingId && x.LateFeeExempt, cancellationToken);

        var sections = new List<ConfigSectionDto>();
        foreach (var definition in ConfigSections.All.OrderBy(x => x.Order))
        {
            if (!ConfigSections.CanView(definition, role))
            {
                continue;
            }

            var section = definition.Key switch
            {
                ConfigSectionKeys.Identity => Identity(building, currentSeries),
                ConfigSectionKeys.Collection => Collection(building, activeBankAccounts),
                ConfigSectionKeys.LateFee => LateFee(building, exemptUnits),
                ConfigSectionKeys.PaymentRule => PaymentRule(),
                ConfigSectionKeys.Funds => Funds(building),
                ConfigSectionKeys.Chart => await ChartAsync(building, finance, cancellationToken),
                ConfigSectionKeys.Taxes => await TaxesAsync(building, finance, cancellationToken),
                ConfigSectionKeys.Suppliers => await SuppliersAsync(building, cancellationToken),
                ConfigSectionKeys.Closing => await ClosingAsync(building, finance, cancellationToken),
                ConfigSectionKeys.Budget => await BudgetAsync(building, finance, cancellationToken),
                ConfigSectionKeys.Documents => await DocumentsAsync(building, cancellationToken),
                _ => null
            };

            if (section is null)
            {
                continue;
            }

            section.Key = definition.Key;
            section.Order = definition.Order;
            section.Name = definition.Name;
            // El IVA de las compras es obligatorio solo si el edificio discrimina IVA (regimen general).
            section.Required = definition.Key == ConfigSectionKeys.Taxes
                ? building.VatRegime == VatRegime.General && section.Status != ConfigSectionStatus.NotAvailable
                : definition.Required;
            section.CanEdit = ConfigSections.CanEdit(definition, role);
            sections.Add(section);
        }

        var required = sections.Where(x => x.Required && x.Status != ConfigSectionStatus.NotAvailable).ToList();
        var ready = required.Count(x => x.Status == ConfigSectionStatus.Complete);

        return new BuildingConfigOverviewDto
        {
            BuildingId = building.Id,
            BuildingName = building.Name,
            Sections = sections,
            RequiredCount = required.Count,
            ReadyCount = ready,
            ReadyToOperate = required.Count > 0 && ready == required.Count,
            FinanceAvailable = finance.IsAvailable,
            CanViewAudit = ConfigSections.AuditRoles.Contains(role, StringComparer.OrdinalIgnoreCase)
        };
    }

    // ── 1. Identidad y fiscal ────────────────────────────────────────────────

    private static ConfigSectionDto Identity(Building b, int currentSeries)
    {
        var reasons = new List<string>();
        if (string.IsNullOrWhiteSpace(b.Ruc)) reasons.Add("Falta el RUC del edificio.");
        if (string.IsNullOrWhiteSpace(b.LegalName)) reasons.Add("Falta la razón social.");
        if (b.VatRegime is null) reasons.Add("Falta el régimen de IVA.");
        if (currentSeries == 0) reasons.Add("No hay un timbrado vigente (activo y dentro de su fecha de vigencia).");

        return new ConfigSectionDto
        {
            Status = reasons.Count == 0 ? ConfigSectionStatus.Complete : ConfigSectionStatus.Incomplete,
            Reasons = reasons,
            Summary =
            [
                Item("RUC", b.Ruc),
                Item("Razón social", b.LegalName),
                Item("Régimen de IVA", VatRegimeLabel(b.VatRegime)),
                Item("Modo de facturación", InvoicingModeLabel(b.InvoicingMode)),
                Item("Timbrados vigentes", currentSeries.ToString(CultureInfo.InvariantCulture))
            ],
            LinkKind = "building",
            LinkTab = "billing"
        };
    }

    // ── 2. Cobro y vencimientos ──────────────────────────────────────────────

    private static ConfigSectionDto Collection(Building b, int activeBankAccounts)
    {
        var reasons = new List<string>();
        if (b.DefaultDueDay is null) reasons.Add("Falta el día de vencimiento por defecto.");
        if (activeBankAccounts == 0 && string.IsNullOrWhiteSpace(b.PaymentInstructions))
        {
            reasons.Add("Falta indicar cómo se paga: cargá una cuenta bancaria o las instrucciones de pago.");
        }

        return new ConfigSectionDto
        {
            Status = reasons.Count == 0 ? ConfigSectionStatus.Complete : ConfigSectionStatus.Incomplete,
            Reasons = reasons,
            Summary =
            [
                Item("Día de vencimiento", b.DefaultDueDay?.ToString(CultureInfo.InvariantCulture)),
                Item("Días de gracia", b.GraceDays?.ToString(CultureInfo.InvariantCulture)),
                Item("Cuentas bancarias activas", activeBankAccounts.ToString(CultureInfo.InvariantCulture)),
                Item("Instrucciones de pago", string.IsNullOrWhiteSpace(b.PaymentInstructions) ? null : "Cargadas")
            ],
            LinkKind = "building",
            LinkTab = "accounting"
        };
    }

    // ── 3. Política de mora ──────────────────────────────────────────────────
    // Completa si hay tasa y frecuencia, o si el administrador confirmó expresamente que el edificio no cobra mora.

    private static ConfigSectionDto LateFee(Building b, int exemptUnits)
    {
        var configured = b.LateFeeRatePercentage is > 0m && b.LateFeeFrequency is not null;
        var complete = configured || b.LateFeePolicyConfirmed;
        return new ConfigSectionDto
        {
            Status = complete ? ConfigSectionStatus.Complete : ConfigSectionStatus.Incomplete,
            Reasons = complete ? [] : ["Falta revisar la política de mora: definila o confirmá que este edificio no cobra mora."],
            Summary =
            [
                Item("Interés por mora", configured
                    ? $"{b.LateFeeRatePercentage!.Value.ToString("0.##", CultureInfo.InvariantCulture)} % {LateFeeFrequencyLabel(b.LateFeeFrequency)?.ToLowerInvariant()}"
                    : b.LateFeePolicyConfirmed ? "No cobra mora" : null),
                Item("Días de gracia", b.GraceDays?.ToString(CultureInfo.InvariantCulture)),
                Item("Tope de la mora", b.LateFeeCapPercentage is > 0m ? $"{b.LateFeeCapPercentage.Value.ToString("0.##", CultureInfo.InvariantCulture)} % de la base" : "Sin tope"),
                Item("Mora mínima por intervalo", b.LateFeeMinAmount is > 0m ? $"Gs. {b.LateFeeMinAmount.Value.ToString("N0", CultureInfo.InvariantCulture)}" : "Sin mínimo"),
                Item("Unidades exoneradas", exemptUnits.ToString(CultureInfo.InvariantCulture))
            ],
            LinkKind = "self",
            LinkTab = ConfigSectionKeys.LateFee
        };
    }

    // ── 4. Regla de pago (fija, informativa) ─────────────────────────────────

    private static ConfigSectionDto PaymentRule() => new()
    {
        Status = ConfigSectionStatus.Complete,
        Reasons = [],
        Summary =
        [
            Item("Comprobante", "Se paga completo (todo lo pendiente de la unidad en el período, con mora)"),
            Item("Orden de aplicación", "Del más antiguo al más nuevo"),
            Item("Pagos parciales y saldo a favor", "No se admiten")
        ]
    };

    // ── 5. Fondos ────────────────────────────────────────────────────────────
    // Completa si hay algún aporte, o si el administrador confirmó expresamente que el edificio no tiene aportes a fondos.

    private static ConfigSectionDto Funds(Building b)
    {
        var configured = b.ReserveFundPercentage is > 0m || b.ExtraordinaryPercentage is > 0m;
        var complete = configured || b.FundPolicyConfirmed;
        return new ConfigSectionDto
        {
            Status = complete ? ConfigSectionStatus.Complete : ConfigSectionStatus.Incomplete,
            Reasons = complete ? [] : ["Falta revisar los fondos: definí los aportes o confirmá que este edificio no tiene aportes a fondos."],
            Summary =
            [
                Item("Tratamiento de los ingresos", b.IncomeTreatment == IncomeTreatment.ToReserveFund ? "Van al fondo de reserva" : "Se acreditan a los propietarios"),
                Item("Aporte al fondo de reserva", b.ReserveFundPercentage is > 0m ? $"{b.ReserveFundPercentage.Value.ToString("0.##", CultureInfo.InvariantCulture)} %" : null),
                Item("Aporte extraordinario", b.ExtraordinaryPercentage is > 0m ? $"{b.ExtraordinaryPercentage.Value.ToString("0.##", CultureInfo.InvariantCulture)} %" : null),
                Item("Uso del fondo de reserva", ReserveUsePolicyLabel(b.ReserveUsePolicy)
                    + (b.ReserveUseThreshold is > 0m ? $" desde Gs. {b.ReserveUseThreshold.Value.ToString("N0", CultureInfo.InvariantCulture)}" : string.Empty))
            ],
            LinkKind = "self",
            LinkTab = ConfigSectionKeys.Funds
        };
    }

    private static string ReserveUsePolicyLabel(ReserveUsePolicy policy) => policy switch
    {
        ReserveUsePolicy.RequiresPresidentApproval => "Requiere aprobación del presidente",
        ReserveUsePolicy.RequiresAssemblyApproval => "Requiere aprobación de la asamblea",
        _ => "Uso libre"
    };

    // ── 12. Documentos y comunicación ────────────────────────────────────────

    private async Task<ConfigSectionDto> DocumentsAsync(Building b, CancellationToken cancellationToken)
    {
        var rules = await dbContext.BuildingNoticeRules.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == b.Id)
            .Select(x => new { x.Kind, x.IsActive })
            .ToListAsync(cancellationToken);
        var kinds = new[] { NoticeKind.BeforeDue, NoticeKind.OnDue, NoticeKind.LateFeeApplied, NoticeKind.PaymentReceived, NoticeKind.PeriodPublished };
        var activeCount = kinds.Count(kind => rules.FirstOrDefault(r => r.Kind == kind)?.IsActive ?? NoticeRules.DefaultActive(kind));

        return new ConfigSectionDto
        {
            Status = rules.Count > 0 ? ConfigSectionStatus.Complete : ConfigSectionStatus.Optional,
            Reasons = rules.Count > 0 ? [] : ["Los avisos automáticos están con su configuración por defecto (solo pago recibido y período publicado)."],
            Summary =
            [
                Item("Modelos de documentos", b.UseStandardTemplates ? "Estándar de CONDOPY" : "Propios del edificio"),
                Item("Avisos automáticos encendidos", $"{activeCount} de {kinds.Length}")
            ],
            LinkKind = "self",
            LinkTab = ConfigSectionKeys.Documents
        };
    }


    // ── 6. Plan de cuentas y cuentas financieras ─────────────────────────────

    private async Task<ConfigSectionDto> ChartAsync(Building b, FinanceModuleState finance, CancellationToken cancellationToken)
    {
        var unavailable = FinanceUnavailable(finance);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var settings = await dbContext.FinanceSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == b.Id, cancellationToken);
        var accounts = await dbContext.FinancialAccounts.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == b.Id)
            .Select(x => new { x.Id, x.Name, x.IsActive, x.Type })
            .ToListAsync(cancellationToken);
        var activeCategories = await dbContext.LedgerCategories.AsNoTracking()
            .CountAsync(x => !x.IsDeleted && x.IsActive && x.BuildingId == b.Id, cancellationToken);

        var reasons = new List<string>();
        if (settings?.FinanceStartDate is null) reasons.Add("Falta la fecha de arranque.");
        if (!accounts.Any(x => x.IsActive && x.Type != FinancialAccountType.ReserveFund)) reasons.Add("Falta al menos una cuenta de caja o banco.");
        if (activeCategories == 0) reasons.Add("El plan de cuentas no tiene cuentas activas.");

        var complete = settings?.SetupCompleted == true;
        if (!complete && reasons.Count == 0)
        {
            reasons.Add("Falta confirmar la configuración inicial de Finanzas.");
        }

        var defaultAccount = settings?.DefaultAccountId is { } id ? accounts.FirstOrDefault(x => x.Id == id)?.Name : null;

        return new ConfigSectionDto
        {
            Status = complete ? ConfigSectionStatus.Complete : ConfigSectionStatus.Incomplete,
            Reasons = complete ? [] : reasons,
            Summary =
            [
                Item("Fecha de arranque", settings?.FinanceStartDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                Item("Cuentas financieras", accounts.Count.ToString(CultureInfo.InvariantCulture)),
                Item("Cuentas del plan activas", activeCategories.ToString(CultureInfo.InvariantCulture)),
                Item("Cuenta por defecto del libro", defaultAccount ?? "Automática")
            ],
            LinkKind = "finance",
            LinkTab = "settings"
        };
    }

    // ── 7. Impuestos (IVA de las compras) ────────────────────────────────────
    // Obligatoria solo con regimen general: entonces cada cuenta final de egresos activa tiene que tener su tratamiento de IVA.

    private async Task<ConfigSectionDto> TaxesAsync(Building b, FinanceModuleState finance, CancellationToken cancellationToken)
    {
        var unavailable = FinanceUnavailable(finance);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var all = await dbContext.LedgerCategories.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == b.Id)
            .Select(x => new { x.Id, x.ParentId, x.Type, x.IsActive, x.VatTreatment })
            .ToListAsync(cancellationToken);
        var parentIds = all.Where(x => x.ParentId.HasValue).Select(x => x.ParentId!.Value).ToHashSet();
        var leaves = all.Where(x => x.Type == LedgerCategoryType.Expense && x.IsActive && !parentIds.Contains(x.Id)).ToList();
        var defined = leaves.Count(x => x.VatTreatment.HasValue);
        var missing = leaves.Count - defined;
        var applies = b.VatRegime == VatRegime.General;

        string? reason = null;
        if (!applies)
        {
            reason = b.VatRegime is null
                ? "Definí el régimen de IVA del edificio (Identidad y fiscal) para saber si discrimina IVA en sus compras."
                : "El régimen de IVA del edificio no discrimina IVA en las compras: el tratamiento por cuenta es opcional.";
        }
        else if (missing > 0)
        {
            reason = $"Faltan {missing} cuentas de egresos sin tratamiento de IVA.";
        }

        return new ConfigSectionDto
        {
            Status = applies && missing == 0 && leaves.Count > 0 ? ConfigSectionStatus.Complete
                : applies ? ConfigSectionStatus.Incomplete : ConfigSectionStatus.Optional,
            Reasons = reason is null ? [] : [reason],
            Summary =
            [
                Item("Régimen de IVA del edificio", VatRegimeLabel(b.VatRegime)),
                Item("Cuentas de egresos con tratamiento", $"{defined.ToString(CultureInfo.InvariantCulture)} de {leaves.Count.ToString(CultureInfo.InvariantCulture)}")
            ],
            LinkKind = "self",
            LinkTab = ConfigSectionKeys.Taxes
        };
    }

    // ── 8. Proveedores (de la empresa, compartidos entre sus edificios) ──────

    private async Task<ConfigSectionDto> SuppliersAsync(Building b, CancellationToken cancellationToken)
    {
        var companyId = b.CompanyId ?? await dbContext.Condominiums.AsNoTracking()
            .Where(x => x.Id == b.CondominiumId)
            .Select(x => (Guid?)x.CompanyId)
            .FirstOrDefaultAsync(cancellationToken);

        var suppliers = companyId.HasValue
            ? await dbContext.Suppliers.AsNoTracking()
                .Where(x => !x.IsDeleted && x.CompanyId == companyId.Value)
                .Select(x => x.IsActive)
                .ToListAsync(cancellationToken)
            : [];
        var active = suppliers.Count(x => x);

        return new ConfigSectionDto
        {
            Status = active > 0 ? ConfigSectionStatus.Complete : ConfigSectionStatus.Optional,
            Reasons = active > 0 ? [] : ["Todavía no hay proveedores cargados: se pueden cargar al registrar el primer gasto."],
            Summary =
            [
                Item("Proveedores activos", active.ToString(CultureInfo.InvariantCulture)),
                Item("Proveedores desactivados", (suppliers.Count - active).ToString(CultureInfo.InvariantCulture))
            ],
            LinkKind = "self",
            LinkTab = ConfigSectionKeys.Suppliers
        };
    }

    // ── 9. Período y cierre ──────────────────────────────────────────────────

    private async Task<ConfigSectionDto> ClosingAsync(Building b, FinanceModuleState finance, CancellationToken cancellationToken)
    {
        var unavailable = FinanceUnavailable(finance);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var enabled = await dbContext.FinanceSettings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == b.Id)
            .Select(x => x.PeriodClosingEnabled)
            .FirstOrDefaultAsync(cancellationToken);

        var closed = await dbContext.FinancePeriodClosures.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == b.Id && x.ReopenedAtUtc == null)
            .Select(x => new { x.Year, x.Month })
            .ToListAsync(cancellationToken);
        var last = closed.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).FirstOrDefault();

        return new ConfigSectionDto
        {
            Status = enabled ? ConfigSectionStatus.Complete : ConfigSectionStatus.Optional,
            Reasons = enabled ? [] : ["El cierre de período está apagado: los meses no se bloquean."],
            Summary =
            [
                Item("Cierre de período", enabled ? "Encendido" : "Apagado"),
                Item("Meses cerrados", closed.Count.ToString(CultureInfo.InvariantCulture)),
                Item("Último mes cerrado", last is null ? null : $"{last.Month:00}/{last.Year}")
            ],
            LinkKind = "self",
            LinkTab = ConfigSectionKeys.Closing
        };
    }

    // ── 10. Presupuesto y alertas ────────────────────────────────────────────

    private async Task<ConfigSectionDto> BudgetAsync(Building b, FinanceModuleState finance, CancellationToken cancellationToken)
    {
        var unavailable = FinanceUnavailable(finance);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var lines = await dbContext.BudgetLines.AsNoTracking()
            .CountAsync(x => !x.IsDeleted && x.BuildingId == b.Id && x.Amount != 0m, cancellationToken);
        var warn = await dbContext.FinanceSettings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == b.Id)
            .Select(x => (int?)x.BudgetWarnPercent)
            .FirstOrDefaultAsync(cancellationToken) ?? 10;

        return new ConfigSectionDto
        {
            Status = lines > 0 ? ConfigSectionStatus.Complete : ConfigSectionStatus.Optional,
            Reasons = lines > 0 ? [] : ["Todavía no hay presupuesto cargado."],
            Summary =
            [
                Item("Renglones de presupuesto con importe", lines.ToString(CultureInfo.InvariantCulture)),
                Item("Umbral del semáforo", $"{warn.ToString(CultureInfo.InvariantCulture)} %")
            ],
            LinkKind = "finance",
            LinkTab = "budget"
        };
    }

    private static ConfigSectionDto? FinanceUnavailable(FinanceModuleState finance)
    {
        if (finance.IsAvailable)
        {
            return null;
        }

        var reason = !finance.Enabled
            ? "El módulo Finanzas del edificio no está habilitado para este edificio."
            : "El plan actual del edificio no incluye el módulo Finanzas del edificio.";

        return new ConfigSectionDto { Status = ConfigSectionStatus.NotAvailable, Reasons = [reason], Summary = [] };
    }

    // ── Etiquetas ────────────────────────────────────────────────────────────

    private static ConfigSummaryItemDto Item(string label, string? value) =>
        new() { Label = label, Value = string.IsNullOrWhiteSpace(value) ? "Sin definir" : value };

    private static string? VatRegimeLabel(VatRegime? value) => value switch
    {
        VatRegime.General => "General",
        VatRegime.Resimple => "RESIMPLE",
        VatRegime.Exempt => "Exento",
        _ => null
    };

    private static string InvoicingModeLabel(InvoicingMode value) => value switch
    {
        InvoicingMode.Preimpresa => "Preimpresa",
        InvoicingMode.Autoimpresa => "Autoimpresa",
        InvoicingMode.Electronica => "Electrónica",
        _ => value.ToString()
    };

    private static string? LateFeeFrequencyLabel(LateFeeFrequency? value) => value switch
    {
        LateFeeFrequency.Daily => "Diaria",
        LateFeeFrequency.Weekly => "Semanal",
        LateFeeFrequency.Biweekly => "Quincenal",
        _ => null
    };
}
