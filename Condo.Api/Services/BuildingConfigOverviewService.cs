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
                ConfigSectionKeys.LateFee => LateFee(building),
                ConfigSectionKeys.PaymentRule => PaymentRule(),
                ConfigSectionKeys.Funds => Funds(building),
                ConfigSectionKeys.Chart => await ChartAsync(building, finance, cancellationToken),
                ConfigSectionKeys.Budget => await BudgetAsync(building, finance, cancellationToken),
                _ => null
            };

            if (section is null)
            {
                continue;
            }

            section.Key = definition.Key;
            section.Order = definition.Order;
            section.Name = definition.Name;
            section.Required = definition.Required;
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
    // Hasta que exista la confirmacion explicita de "sin mora" (fase 3), un edificio sin mora queda como opcional y no como incompleto.

    private static ConfigSectionDto LateFee(Building b)
    {
        var configured = b.LateFeeRatePercentage is > 0m && b.LateFeeFrequency is not null;
        return new ConfigSectionDto
        {
            Status = configured ? ConfigSectionStatus.Complete : ConfigSectionStatus.Optional,
            Reasons = configured ? [] : ["El edificio no cobra interés por mora."],
            Summary =
            [
                Item("Tasa de interés", b.LateFeeRatePercentage is > 0m ? $"{b.LateFeeRatePercentage.Value.ToString("0.##", CultureInfo.InvariantCulture)} %" : null),
                Item("Frecuencia", LateFeeFrequencyLabel(b.LateFeeFrequency))
            ],
            LinkKind = "building",
            LinkTab = "accounting"
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

    private static ConfigSectionDto Funds(Building b)
    {
        var configured = b.ReserveFundPercentage is > 0m || b.ExtraordinaryPercentage is > 0m;
        return new ConfigSectionDto
        {
            Status = configured ? ConfigSectionStatus.Complete : ConfigSectionStatus.Optional,
            Reasons = configured ? [] : ["No hay aporte al fondo de reserva ni aporte extraordinario configurado."],
            Summary =
            [
                Item("Tratamiento de los ingresos", b.IncomeTreatment == IncomeTreatment.ToReserveFund ? "Van al fondo de reserva" : "Se acreditan a los propietarios"),
                Item("Aporte al fondo de reserva", b.ReserveFundPercentage is > 0m ? $"{b.ReserveFundPercentage.Value.ToString("0.##", CultureInfo.InvariantCulture)} %" : null),
                Item("Aporte extraordinario", b.ExtraordinaryPercentage is > 0m ? $"{b.ExtraordinaryPercentage.Value.ToString("0.##", CultureInfo.InvariantCulture)} %" : null)
            ],
            LinkKind = "building",
            LinkTab = "accounting"
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

        return new ConfigSectionDto
        {
            Status = lines > 0 ? ConfigSectionStatus.Complete : ConfigSectionStatus.Optional,
            Reasons = lines > 0 ? [] : ["Todavía no hay presupuesto cargado."],
            Summary = [Item("Renglones de presupuesto con importe", lines.ToString(CultureInfo.InvariantCulture))],
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
