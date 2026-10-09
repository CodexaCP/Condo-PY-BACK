using System.Globalization;
using System.Text.Json;
using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Api.Services;

/// <summary>Un campo que cambio: clave estable, etiqueta para mostrar y los valores antes y despues ya formateados.</summary>
public sealed record ConfigChange(string Field, string Label, string? Before, string? After);

/// <summary>
/// Escribe el historial de cambios de la configuracion (FinanceAuditLog). Solo agrega la fila al contexto: no guarda, para que
/// quede en la misma transaccion (el mismo SaveChanges) que el cambio que registra y no haya historial sin cambio ni cambio sin historial.
/// </summary>
public sealed class ConfigAuditWriter(ICondoDbContext dbContext, ITenantContext tenant)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public FinanceAuditLog Add(
        Guid companyId, Guid buildingId, string section, string action, string summary,
        string? entityType = null, Guid? entityId = null, IReadOnlyList<ConfigChange>? changes = null)
    {
        var entry = new FinanceAuditLog
        {
            CompanyId = companyId,
            BuildingId = buildingId,
            Section = section,
            Action = action,
            Summary = Truncate(summary, 500),
            EntityType = entityType,
            EntityId = entityId,
            ChangesJson = changes is { Count: > 0 } ? JsonSerializer.Serialize(changes, JsonOptions) : null,
            UserId = tenant.UserId,
            UserEmail = Truncate(tenant.Email ?? string.Empty, 256),
            UserRole = Truncate(tenant.Role ?? string.Empty, 40)
        };
        dbContext.FinanceAuditLogs.Add(entry);
        return entry;
    }

    /// <summary>Agrega una fila por cada seccion con cambios (los campos de una misma seccion van juntos). Devuelve cuantas agrego.</summary>
    public int AddBuildingChanges(Guid companyId, Guid buildingId, BuildingConfigSnapshot before, BuildingConfigSnapshot after)
    {
        var count = 0;
        foreach (var (section, changes) in BuildingConfigSnapshot.Diff(before, after))
        {
            var labels = string.Join(", ", changes.Select(x => x.Label));
            Add(companyId, buildingId, section, "Updated", $"{ConfigSections.NameOf(section)}: cambió {labels}.", "Building", buildingId, changes);
            count++;
        }
        return count;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>
/// Foto de los datos de configuracion de un edificio (los que se editan desde la ficha) para comparar antes y despues de guardar.
/// Cada campo sabe a que seccion del Centro pertenece.
/// </summary>
public sealed class BuildingConfigSnapshot
{
    private readonly List<(string Key, string Section, string Label, string? Value)> _fields = [];

    public static BuildingConfigSnapshot Capture(Building b, IEnumerable<BuildingBankAccount> bankAccounts)
    {
        var s = new BuildingConfigSnapshot();

        // Identidad y fiscal
        s.Add("name", ConfigSectionKeys.Identity, "Nombre", b.Name);
        s.Add("code", ConfigSectionKeys.Identity, "Código", b.Code);
        s.Add("address", ConfigSectionKeys.Identity, "Dirección", b.Address);
        s.Add("isActive", ConfigSectionKeys.Identity, "Activo", YesNo(b.IsActive));
        s.Add("propertyType", ConfigSectionKeys.Identity, "Tipo de inmueble", b.PropertyType?.ToString());
        s.Add("department", ConfigSectionKeys.Identity, "Departamento", b.Department);
        s.Add("city", ConfigSectionKeys.Identity, "Ciudad", b.City);
        s.Add("ruc", ConfigSectionKeys.Identity, "RUC", b.Ruc);
        s.Add("legalName", ConfigSectionKeys.Identity, "Razón social", b.LegalName);
        s.Add("taxpayerType", ConfigSectionKeys.Identity, "Tipo de contribuyente", b.TaxpayerType?.ToString());
        s.Add("vatRegime", ConfigSectionKeys.Identity, "Régimen de IVA", b.VatRegime?.ToString());
        s.Add("economicActivity", ConfigSectionKeys.Identity, "Actividad económica", b.EconomicActivity);
        s.Add("fiscalAddress", ConfigSectionKeys.Identity, "Dirección fiscal", b.FiscalAddress);
        s.Add("invoiceEmail", ConfigSectionKeys.Identity, "Correo para facturas", b.InvoiceEmail);
        s.Add("invoicingMode", ConfigSectionKeys.Identity, "Modo de facturación", b.InvoicingMode.ToString());
        s.Add("administratorName", ConfigSectionKeys.Identity, "Administrador", b.AdministratorName);
        s.Add("administratorPhone", ConfigSectionKeys.Identity, "Teléfono del administrador", b.AdministratorPhone);

        // Cobro y vencimientos
        s.Add("defaultDueDay", ConfigSectionKeys.Collection, "Día de vencimiento", Number(b.DefaultDueDay));
        s.Add("graceDays", ConfigSectionKeys.Collection, "Días de gracia", Number(b.GraceDays));
        s.Add("paymentInstructions", ConfigSectionKeys.Collection, "Instrucciones de pago", b.PaymentInstructions);
        s.Add("blockOverdueAmenityReservations", ConfigSectionKeys.Collection, "Bloquear reservas con deuda", YesNo(b.BlockOverdueAmenityReservations));
        s.Add("bankAccounts", ConfigSectionKeys.Collection, "Cuentas bancarias del edificio", BankAccountsLabel(bankAccounts));

        // Mora
        s.Add("lateFeeRatePercentage", ConfigSectionKeys.LateFee, "Tasa de mora (%)", Percent(b.LateFeeRatePercentage));
        s.Add("lateFeeFrequency", ConfigSectionKeys.LateFee, "Frecuencia de la mora", b.LateFeeFrequency?.ToString());
        s.Add("lateFeeCapPercentage", ConfigSectionKeys.LateFee, "Tope de la mora (%)", Percent(b.LateFeeCapPercentage));
        s.Add("lateFeeMinAmount", ConfigSectionKeys.LateFee, "Mora mínima por intervalo", b.LateFeeMinAmount?.ToString("0.##", CultureInfo.InvariantCulture));
        s.Add("lateFeeAppliesToReserve", ConfigSectionKeys.LateFee, "Mora sobre el aporte al fondo de reserva", YesNo(b.LateFeeAppliesToReserve));
        s.Add("lateFeeAppliesToExtraordinary", ConfigSectionKeys.LateFee, "Mora sobre aportes extraordinarios", YesNo(b.LateFeeAppliesToExtraordinary));
        s.Add("lateFeeAppliesToIndividual", ConfigSectionKeys.LateFee, "Mora sobre cargos individuales", YesNo(b.LateFeeAppliesToIndividual));
        s.Add("lateFeePolicyConfirmed", ConfigSectionKeys.LateFee, "Política de mora confirmada", YesNo(b.LateFeePolicyConfirmed));

        // Fondos
        s.Add("incomeTreatment", ConfigSectionKeys.Funds, "Tratamiento de los ingresos", b.IncomeTreatment.ToString());
        s.Add("reserveFundPercentage", ConfigSectionKeys.Funds, "Aporte al fondo de reserva (%)", Percent(b.ReserveFundPercentage));
        s.Add("extraordinaryPercentage", ConfigSectionKeys.Funds, "Aporte extraordinario (%)", Percent(b.ExtraordinaryPercentage));
        s.Add("reserveUsePolicy", ConfigSectionKeys.Funds, "Política de uso del fondo de reserva", b.ReserveUsePolicy.ToString());
        s.Add("reserveUseThreshold", ConfigSectionKeys.Funds, "Monto desde el que rige la política de uso", b.ReserveUseThreshold?.ToString("0.##", CultureInfo.InvariantCulture));
        s.Add("fundPolicyConfirmed", ConfigSectionKeys.Funds, "Política de fondos confirmada", YesNo(b.FundPolicyConfirmed));

        // Documentos y comunicacion
        s.Add("useStandardTemplates", ConfigSectionKeys.Documents, "Modelos estándar de CONDOPY", YesNo(b.UseStandardTemplates));

        return s;
    }

    /// <summary>Cambios por seccion, en el orden en que se declararon los campos. Solo los campos cuyo valor cambio.</summary>
    public static Dictionary<string, List<ConfigChange>> Diff(BuildingConfigSnapshot before, BuildingConfigSnapshot after)
    {
        var result = new Dictionary<string, List<ConfigChange>>();
        var previous = before._fields.ToDictionary(x => x.Key, x => x.Value);
        foreach (var (key, section, label, value) in after._fields)
        {
            previous.TryGetValue(key, out var old);
            if (string.Equals(Normalize(old), Normalize(value), StringComparison.Ordinal))
            {
                continue;
            }

            if (!result.TryGetValue(section, out var list))
            {
                list = [];
                result[section] = list;
            }
            list.Add(new ConfigChange(key, label, old, value));
        }
        return result;
    }

    private void Add(string key, string section, string label, string? value) => _fields.Add((key, section, label, value));

    // Vacio y nulo son lo mismo: no hay cambio entre "sin dato" y un texto en blanco.
    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private static string YesNo(bool value) => value ? "Sí" : "No";

    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Percent(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture);

    private static string? BankAccountsLabel(IEnumerable<BuildingBankAccount> accounts)
    {
        var lines = accounts
            .Where(x => !x.IsDeleted)
            .Select(x => $"{x.BankName} {x.AccountNumber}{(x.IsActive ? string.Empty : " (inactiva)")}".Trim())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        return lines.Count == 0 ? null : string.Join("; ", lines);
    }
}
