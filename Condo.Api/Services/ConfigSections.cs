namespace Condo.Api.Services;

/// <summary>
/// Secciones del Centro de configuracion del edificio. La clave es estable (se guarda en el historial y la usa el frente);
/// el nombre es el que ve el usuario. Cada seccion declara que roles la ven y cuales la editan (especificacion, 3.2).
/// </summary>
public static class ConfigSectionKeys
{
    public const string Identity = "identity";
    public const string Collection = "collection";
    public const string LateFee = "late-fee";
    public const string PaymentRule = "payment-rule";
    public const string Funds = "funds";
    public const string Chart = "chart";
    public const string Budget = "budget";
    public const string Closing = "closing";
}

public sealed record ConfigSectionDefinition(
    string Key, int Order, string Name, bool Required, string[] ViewRoles, string[] EditRoles);

public static class ConfigSections
{
    private const string Super = "SuperAdmin";
    private const string Admin = "CompanyAdmin";
    private const string Operator = "CompanyOperator";
    private const string Manager = "BuildingManager";

    private static readonly string[] AllStaff = [Super, Admin, Operator, Manager];
    private static readonly string[] NoManager = [Super, Admin, Operator];
    private static readonly string[] SuperAndAdmin = [Super, Admin];
    private static readonly string[] SuperOnly = [Super];
    private static readonly string[] Nobody = [];

    /// <summary>Roles que pueden abrir el Centro (los administrativos; el resto de los roles usa la app movil).</summary>
    public static readonly string[] StaffRoles = AllStaff;

    /// <summary>Quienes ven el historial de cambios: contiene correos y nombres de quienes configuran, por eso es mas acotado.</summary>
    public static readonly string[] AuditRoles = SuperAndAdmin;

    public static readonly IReadOnlyList<ConfigSectionDefinition> All =
    [
        new(ConfigSectionKeys.Identity, 1, "Identidad y fiscal", true, NoManager, SuperAndAdmin),
        new(ConfigSectionKeys.Collection, 2, "Cobro y vencimientos", true, NoManager, SuperAndAdmin),
        new(ConfigSectionKeys.LateFee, 3, "Política de mora", false, AllStaff, SuperAndAdmin),
        new(ConfigSectionKeys.PaymentRule, 4, "Regla de pago", true, NoManager, Nobody),
        new(ConfigSectionKeys.Funds, 5, "Fondos", false, NoManager, SuperAndAdmin),
        new(ConfigSectionKeys.Chart, 6, "Plan de cuentas y cuentas financieras", true, AllStaff, SuperOnly),
        new(ConfigSectionKeys.Closing, 9, "Período y cierre", false, AllStaff, SuperAndAdmin),
        new(ConfigSectionKeys.Budget, 10, "Presupuesto y alertas", false, AllStaff, SuperAndAdmin)
    ];

    public static ConfigSectionDefinition? Find(string key) =>
        All.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    public static string NameOf(string key) => Find(key)?.Name ?? key;

    public static bool CanView(ConfigSectionDefinition section, string role) =>
        section.ViewRoles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public static bool CanEdit(ConfigSectionDefinition section, string role) =>
        section.EditRoles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
