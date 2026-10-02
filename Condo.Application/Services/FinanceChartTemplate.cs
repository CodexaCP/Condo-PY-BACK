using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Application.Services;

/// <summary>
/// Plantilla estandar del plan de cuentas de un edificio: dos niveles (rubro y subrubro) basados en las categorias
/// actuales de gastos e ingresos. Cada subrubro lleva una clave estable (<c>SystemKey</c>) para que el libro derivado
/// mapee esas categorias sin tocar los gastos ni los ingresos existentes. Los codigos son una propuesta editable.
/// </summary>
public static class FinanceChartTemplate
{
    public sealed record Node(string Code, string Name, LedgerCategoryType Type, string? ParentCode, string? SystemKey);

    // Claves de los subrubros que no salen de una categoria de gasto o ingreso: cobros de cargos a propietarios y usos del fondo.
    public const string CollectionOrdinary = "Collection.Ordinary";
    public const string CollectionExtraordinary = "Collection.Extraordinary";
    public const string CollectionIndividualAdjustment = "Collection.IndividualAdjustment";
    public const string CollectionLateFee = "Collection.LateFee";
    public const string CollectionReserveFund = "Collection.ReserveFund";
    public const string FundUsage = "Fund.Usage";

    public static string ExpenseKey(BuildingExpenseCategory category) => $"Expense.{category}";
    public static string IncomeKey(BuildingIncomeCategory category) => $"Income.{category}";

    /// <summary>
    /// Categoria de gasto con la que cuenta en la liquidacion un gasto cargado en el rubro: la guardada en los rubros propios o,
    /// en los de la plantilla, la que dice su clave. Nula si el rubro no es de gastos. Un rubro propio sin categoria cuenta como "Otro".
    /// </summary>
    public static BuildingExpenseCategory? ExpenseCategoryOf(LedgerCategory category)
    {
        if (category.Type != LedgerCategoryType.Expense)
        {
            return null;
        }

        if (category.ExpenseCategory.HasValue)
        {
            return category.ExpenseCategory;
        }

        const string prefix = "Expense.";
        if (category.SystemKey is not null)
        {
            return category.SystemKey.StartsWith(prefix, StringComparison.Ordinal)
                && Enum.TryParse<BuildingExpenseCategory>(category.SystemKey[prefix.Length..], out var parsed) && Enum.IsDefined(parsed)
                    ? parsed
                    : null;
        }

        return BuildingExpenseCategory.Other;
    }

    /// <summary>Lo mismo para los ingresos: la categoria de ingreso con la que cuenta un ingreso cargado en el rubro.</summary>
    public static BuildingIncomeCategory? IncomeCategoryOf(LedgerCategory category)
    {
        if (category.Type != LedgerCategoryType.Income)
        {
            return null;
        }

        if (category.IncomeCategory.HasValue)
        {
            return category.IncomeCategory;
        }

        const string prefix = "Income.";
        if (category.SystemKey is not null)
        {
            // Los rubros de cobranza de expensas (Collection.*) no son ingresos del edificio: salen de los pagos de los propietarios.
            return category.SystemKey.StartsWith(prefix, StringComparison.Ordinal)
                && Enum.TryParse<BuildingIncomeCategory>(category.SystemKey[prefix.Length..], out var parsed) && Enum.IsDefined(parsed)
                    ? parsed
                    : null;
        }

        return BuildingIncomeCategory.Other;
    }

    /// <summary>
    /// Rubros en los que se pueden cargar gastos o ingresos del edificio: los subrubros (hojas) activos de tipo gasto o ingreso que
    /// tienen categoria de liquidacion. Un rubro principal solo agrupa, el fondo de reserva tiene su propia cuenta y la cobranza de
    /// expensas sale de los pagos de los propietarios: ninguno recibe gastos ni ingresos cargados a mano.
    /// </summary>
    public static List<LedgerCategory> AssignableCategories(IReadOnlyCollection<LedgerCategory> categories, LedgerCategoryType type)
    {
        var parentIds = categories.Where(c => c.ParentId.HasValue).Select(c => c.ParentId!.Value).ToHashSet();
        return categories
            .Where(c => c.Type == type && c.IsActive && c.ParentId.HasValue && !parentIds.Contains(c.Id) && CanReceiveMovements(c))
            .OrderBy(c => c.Code, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>El subrubro tiene categoria de liquidacion, o sea que se le pueden cargar gastos (si es de gastos) o ingresos (si es de ingresos).</summary>
    public static bool CanReceiveMovements(LedgerCategory category) =>
        category.Type == LedgerCategoryType.Expense ? ExpenseCategoryOf(category) is not null
        : category.Type == LedgerCategoryType.Income && IncomeCategoryOf(category) is not null;

    private static Node Group(string code, string name, LedgerCategoryType type) => new(code, name, type, null, null);
    private static Node Leaf(string code, string name, LedgerCategoryType type, string parentCode, string key) => new(code, name, type, parentCode, key);

    public static IReadOnlyList<Node> Nodes { get; } = Build();

    /// <summary>Rubros de la plantilla como entidades nuevas del edificio (sin guardar), con los subrubros ya enlazados a su rubro.</summary>
    public static IReadOnlyList<LedgerCategory> CreateEntities(Guid buildingId, Guid companyId)
    {
        var byCode = Nodes.ToDictionary(
            n => n.Code,
            n => new LedgerCategory
            {
                CompanyId = companyId,
                BuildingId = buildingId,
                Code = n.Code,
                Name = n.Name,
                Type = n.Type,
                SystemKey = n.SystemKey,
                IsActive = true
            });

        foreach (var node in Nodes.Where(n => n.ParentCode is not null))
        {
            byCode[node.Code].ParentId = byCode[node.ParentCode!].Id;
        }

        return byCode.Values.ToList();
    }

    private static List<Node> Build()
    {
        const LedgerCategoryType I = LedgerCategoryType.Income;
        const LedgerCategoryType E = LedgerCategoryType.Expense;
        const LedgerCategoryType F = LedgerCategoryType.Fund;

        // BuildingIncomeCategory.AccumulatedBalance queda afuera a proposito: es el arrastre de saldo entre periodos, no un ingreso.
        return
        [
            Group("3.1", "Fondo de reserva", F),
            Leaf("3.1.01", "Aportes al fondo de reserva", F, "3.1", CollectionReserveFund),
            Leaf("3.1.02", "Gastos pagados por el fondo", F, "3.1", FundUsage),

            Group("4.1", "Cobranza de expensas", I),
            Leaf("4.1.01", "Expensas ordinarias", I, "4.1", CollectionOrdinary),
            Leaf("4.1.02", "Aporte extraordinario cobrado", I, "4.1", CollectionExtraordinary),
            Leaf("4.1.03", "Cargos individuales y ajustes", I, "4.1", CollectionIndividualAdjustment),
            Leaf("4.1.04", "Intereses por mora", I, "4.1", CollectionLateFee),
            Group("4.2", "Otros ingresos del edificio", I),
            Leaf("4.2.01", "Alquiler de áreas comunes", I, "4.2", IncomeKey(BuildingIncomeCategory.CommonAreaRental)),
            Leaf("4.2.02", "Intereses ganados", I, "4.2", IncomeKey(BuildingIncomeCategory.Interest)),
            Leaf("4.2.03", "Fondo operativo", I, "4.2", IncomeKey(BuildingIncomeCategory.OperationalFund)),
            Leaf("4.2.04", "Ajustes a favor", I, "4.2", IncomeKey(BuildingIncomeCategory.CreditAdjustment)),
            Leaf("4.2.05", "Aportes extraordinarios", I, "4.2", IncomeKey(BuildingIncomeCategory.ExtraordinaryContribution)),
            Leaf("4.2.99", "Otros ingresos", I, "4.2", IncomeKey(BuildingIncomeCategory.Other)),

            Group("5.1", "Servicios básicos", E),
            Leaf("5.1.01", "ANDE", E, "5.1", ExpenseKey(BuildingExpenseCategory.Ande)),
            Leaf("5.1.02", "ESSAP", E, "5.1", ExpenseKey(BuildingExpenseCategory.Essap)),
            Leaf("5.1.03", "Internet y telefonía", E, "5.1", ExpenseKey(BuildingExpenseCategory.InternetPhone)),
            Leaf("5.1.04", "Otros servicios", E, "5.1", ExpenseKey(BuildingExpenseCategory.Utilities)),
            Group("5.2", "Personal", E),
            Leaf("5.2.01", "Salarios", E, "5.2", ExpenseKey(BuildingExpenseCategory.Payroll)),
            Group("5.3", "Mantenimiento y operación", E),
            Leaf("5.3.01", "Limpieza", E, "5.3", ExpenseKey(BuildingExpenseCategory.Cleaning)),
            Leaf("5.3.02", "Seguridad", E, "5.3", ExpenseKey(BuildingExpenseCategory.Security)),
            Leaf("5.3.03", "Mantenimiento", E, "5.3", ExpenseKey(BuildingExpenseCategory.Maintenance)),
            Leaf("5.3.04", "Ascensor", E, "5.3", ExpenseKey(BuildingExpenseCategory.Elevator)),
            Leaf("5.3.05", "Insumos", E, "5.3", ExpenseKey(BuildingExpenseCategory.Supplies)),
            Group("5.4", "Seguros e impuestos", E),
            Leaf("5.4.01", "Seguros", E, "5.4", ExpenseKey(BuildingExpenseCategory.Insurance)),
            Leaf("5.4.02", "Impuestos", E, "5.4", ExpenseKey(BuildingExpenseCategory.Taxes)),
            Group("5.5", "Administración", E),
            Leaf("5.5.01", "Administración", E, "5.5", ExpenseKey(BuildingExpenseCategory.Administration)),
            Group("5.6", "Extraordinario y fondo", E),
            Leaf("5.6.01", "Extraordinario", E, "5.6", ExpenseKey(BuildingExpenseCategory.Extraordinary)),
            Leaf("5.6.02", "Aporte al fondo de reserva", E, "5.6", ExpenseKey(BuildingExpenseCategory.ReserveFund)),
            Group("5.9", "Otros gastos", E),
            Leaf("5.9.01", "Otros gastos", E, "5.9", ExpenseKey(BuildingExpenseCategory.Other))
        ];
    }
}
