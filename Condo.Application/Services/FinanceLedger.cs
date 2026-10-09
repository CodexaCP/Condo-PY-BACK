using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Application.Services;

// Libro "derivado" del modulo Finanzas del edificio: no se guarda nada. Los movimientos se arman al consultar a partir de
// los cobros, gastos e ingresos que ya existen, sin tocar esos flujos, asi que anulaciones y reversiones se reflejan solas.
// Criterio fijo (decision 1): caja y saldos por lo percibido (cobrado y pagado). Parte de los saldos iniciales de cada
// cuenta a la fecha de arranque (decision 10): solo cuenta lo posterior.

public enum LedgerSourceType
{
    OwnerPayment = 1,
    BuildingExpense = 2,
    BuildingIncome = 3,
    // Nota de credito del proveedor sobre un gasto de un periodo ya publicado (movimiento negativo con la fecha de la nota).
    SupplierCreditNote = 4
}

public enum LedgerDirection
{
    In = 1,
    Out = 2
}

// En que se aplico lo que pago un propietario (segun los cargos a los que se imputo el pago).
public enum CollectionClass
{
    Ordinary = 1,
    Extraordinary = 2,
    IndividualAdjustment = 3,
    LateFee = 4,
    ReserveFund = 5
}

/// <summary>Cuentas del edificio que usa el libro para asignar cada movimiento (las que no tienen cuenta propia en el sistema).</summary>
public sealed record LedgerAccounts(Guid? CashId, Guid? FundId, Guid? DefaultId)
{
    /// <summary>
    /// Caja y fondo de reserva: la unica activa de cada tipo. Cuenta por defecto: la configurada si sigue activa y no es el
    /// fondo; si no, el unico banco activo; si no, la unica cuenta activa que no sea el fondo.
    /// </summary>
    public static LedgerAccounts Resolve(IReadOnlyCollection<FinancialAccount> accounts, Guid? configuredDefaultId)
    {
        var active = accounts.Where(x => !x.IsDeleted && x.IsActive).ToList();
        var cash = active.Where(x => x.Type == FinancialAccountType.Cash).Select(x => (Guid?)x.Id).FirstOrDefault();
        var fund = active.Where(x => x.Type == FinancialAccountType.ReserveFund).Select(x => (Guid?)x.Id).FirstOrDefault();

        Guid? defaultId = null;
        if (configuredDefaultId.HasValue && active.Any(x => x.Id == configuredDefaultId.Value && x.Type != FinancialAccountType.ReserveFund))
        {
            defaultId = configuredDefaultId;
        }
        else
        {
            var banks = active.Where(x => x.Type == FinancialAccountType.Bank).ToList();
            if (banks.Count == 1)
            {
                defaultId = banks[0].Id;
            }
            else
            {
                var operating = active.Where(x => x.Type != FinancialAccountType.ReserveFund).ToList();
                if (operating.Count == 1)
                {
                    defaultId = operating[0].Id;
                }
            }
        }

        return new LedgerAccounts(cash, fund, defaultId);
    }
}

/// <summary>Cuenta y rubro (clave de la plantilla) en que cae un hecho, y su sentido.</summary>
public sealed record LedgerClassification(Guid? AccountId, string RubroKey, LedgerDirection Direction);

public static class FinanceLedgerRules
{
    public static CollectionClass ClassifyCharge(ExpenseChargeType type, bool isLateFee)
    {
        if (isLateFee)
        {
            return CollectionClass.LateFee;
        }

        return type switch
        {
            ExpenseChargeType.ReserveFund => CollectionClass.ReserveFund,
            ExpenseChargeType.Extraordinary => CollectionClass.Extraordinary,
            ExpenseChargeType.Individual or ExpenseChargeType.Adjustment => CollectionClass.IndividualAdjustment,
            _ => CollectionClass.Ordinary
        };
    }

    public static string CollectionKey(CollectionClass cls) => cls switch
    {
        CollectionClass.ReserveFund => FinanceChartTemplate.CollectionReserveFund,
        CollectionClass.Extraordinary => FinanceChartTemplate.CollectionExtraordinary,
        CollectionClass.IndividualAdjustment => FinanceChartTemplate.CollectionIndividualAdjustment,
        CollectionClass.LateFee => FinanceChartTemplate.CollectionLateFee,
        _ => FinanceChartTemplate.CollectionOrdinary
    };

    /// <summary>
    /// Cobro de un propietario. Lo aplicado a cargos de fondo de reserva entra a la cuenta del fondo; el resto, a la caja si se
    /// pago en efectivo y, si no, a la cuenta por defecto. Sin cuenta del fondo, el aporte queda en la cuenta por defecto.
    /// </summary>
    public static LedgerClassification ForPayment(PaymentMethod method, CollectionClass cls, LedgerAccounts accounts)
    {
        Guid? account;
        if (cls == CollectionClass.ReserveFund)
        {
            account = accounts.FundId ?? accounts.DefaultId;
        }
        else if (method == PaymentMethod.Cash)
        {
            account = accounts.CashId ?? accounts.DefaultId;
        }
        else
        {
            account = accounts.DefaultId;
        }

        return new LedgerClassification(account, CollectionKey(cls), LedgerDirection.In);
    }

    /// <summary>
    /// Ingreso propio del edificio. Quedan afuera el saldo acumulado (arrastre entre periodos, no es plata nueva) y el fondo
    /// operativo (informativo: la liquidacion tampoco lo cuenta). Si el edificio manda los ingresos al fondo de reserva, entran
    /// a su cuenta; si no, a la cuenta por defecto.
    /// </summary>
    public static LedgerClassification? ForIncome(
        BuildingIncomeCategory category, IncomeTreatment treatment, LedgerAccounts accounts, string? rubroKey = null)
    {
        if (category is BuildingIncomeCategory.AccumulatedBalance or BuildingIncomeCategory.OperationalFund)
        {
            return null;
        }

        var account = treatment == IncomeTreatment.ToReserveFund ? accounts.FundId ?? accounts.DefaultId : accounts.DefaultId;
        return new LedgerClassification(account, rubroKey ?? FinanceChartTemplate.IncomeKey(category), LedgerDirection.In);
    }

    /// <summary>
    /// Gasto del edificio, pagado en la fecha del gasto (hasta que exista el estado de pago de la fase 4). Lo pagado por el
    /// fondo sale de su cuenta. Un gasto de categoria Fondo de reserva es el aporte que se cobra a las unidades, no un egreso:
    /// el dinero se ve del lado de los cobros, por eso queda afuera.
    /// </summary>
    public static LedgerClassification? ForExpense(
        BuildingExpenseCategory category, bool paidByReserveFund, LedgerAccounts accounts, string? rubroKey = null)
    {
        if (category == BuildingExpenseCategory.ReserveFund && !paidByReserveFund)
        {
            return null;
        }

        var account = paidByReserveFund ? accounts.FundId ?? accounts.DefaultId : accounts.DefaultId;
        return new LedgerClassification(account, rubroKey ?? FinanceChartTemplate.ExpenseKey(category), LedgerDirection.Out);
    }
}

/// <summary>Meses y ejercicios. El ejercicio se identifica por el año calendario en que empieza.</summary>
public static class FinancePeriods
{
    // Hoy en Paraguay (UTC-3): el libro cuenta los movimientos hasta hoy; lo fechado a futuro todavia no entra.
    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    public static int FiscalYearOf(DateOnly date, int fiscalYearStartMonth) =>
        date.Month >= fiscalYearStartMonth ? date.Year : date.Year - 1;

    public static (DateOnly Start, DateOnly End) FiscalYearRange(int fiscalYear, int fiscalYearStartMonth)
    {
        var start = new DateOnly(fiscalYear, fiscalYearStartMonth, 1);
        return (start, start.AddMonths(12).AddDays(-1));
    }

    public static DateOnly EndOfMonth(int year, int month) => new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);

    /// <summary>Los 12 meses del ejercicio, en orden.</summary>
    public static List<(int Year, int Month)> FiscalMonths(int fiscalYear, int fiscalYearStartMonth)
    {
        var start = new DateOnly(fiscalYear, fiscalYearStartMonth, 1);
        return Enumerable.Range(0, 12).Select(i => start.AddMonths(i)).Select(d => (d.Year, d.Month)).ToList();
    }
}

/// <summary>Suma de movimientos de un mes, en una cuenta (nula = sin cuenta asignada) y un rubro.</summary>
public sealed record LedgerBucket(int Year, int Month, Guid? AccountId, string RubroKey, LedgerDirection Direction, decimal Amount);

/// <summary>Un movimiento del libro (una linea de cobro, de gasto o de ingreso).</summary>
public sealed record LedgerRow(
    DateOnly Date,
    Guid? AccountId,
    string RubroKey,
    LedgerDirection Direction,
    decimal Amount,
    LedgerSourceType SourceType,
    Guid SourceId,
    string Description,
    string ThirdParty,
    string Reference)
{
    public decimal Signed => Direction == LedgerDirection.In ? Amount : -Amount;
}

/// <summary>Todo lo que hace falta para clasificar y presentar el libro de un edificio.</summary>
public sealed class LedgerContext
{
    public required Guid BuildingId { get; init; }
    public required string BuildingName { get; init; }
    public required DateOnly StartDate { get; init; }
    public required int FiscalYearStartMonth { get; init; }
    // Umbral del semaforo del presupuesto vs. real, en % de desvio (configurable por edificio en el Centro de configuracion).
    public int BudgetWarnPercent { get; init; } = 10;
    public required IncomeTreatment IncomeTreatment { get; init; }
    public required IReadOnlyList<FinancialAccount> Accounts { get; init; }
    public required LedgerAccounts Resolved { get; init; }
    public required IReadOnlyList<LedgerCategory> Categories { get; init; }

    private IReadOnlyDictionary<string, LedgerCategory>? _byKey;
    private IReadOnlyDictionary<Guid, LedgerCategory>? _byId;

    // Todos los rubros por su clave: la de la plantilla o, en los rubros propios, la derivada de su id.
    public IReadOnlyDictionary<string, LedgerCategory> ByKey =>
        _byKey ??= Categories.ToDictionary(x => x.RubroKey);

    public IReadOnlyDictionary<Guid, LedgerCategory> ById =>
        _byId ??= Categories.ToDictionary(x => x.Id);

    public LedgerCategory? CategoryFor(string rubroKey) => ByKey.TryGetValue(rubroKey, out var c) ? c : null;

    /// <summary>
    /// Clave de rubro de un movimiento que eligio rubro; nula si no eligio ninguno (o si el rubro ya no existe), y entonces el
    /// libro lo clasifica por su categoria como siempre.
    /// </summary>
    public string? RubroKeyOf(Guid? categoryId) =>
        categoryId.HasValue && ById.TryGetValue(categoryId.Value, out var c) ? c.RubroKey : null;

    /// <summary>Clave de rubro de un gasto: la de la cuenta que eligio o, si no eligio ninguna, la cuenta por defecto de su categoria.</summary>
    public string ExpenseKey(Guid? categoryId, BuildingExpenseCategory category) =>
        RubroKeyOf(categoryId)
        ?? DefaultKey(FinanceChartTemplate.ExpenseKey(category), LedgerCategoryType.Expense, c => FinanceChartTemplate.ExpenseCategoryOf(c) == category);

    /// <summary>Clave de rubro de un ingreso propio del edificio (misma regla que los gastos).</summary>
    public string IncomeKey(Guid? categoryId, BuildingIncomeCategory category) =>
        RubroKeyOf(categoryId)
        ?? DefaultKey(FinanceChartTemplate.IncomeKey(category), LedgerCategoryType.Income, c => FinanceChartTemplate.IncomeCategoryOf(c) == category);

    private readonly Dictionary<string, string> _defaultKeys = new();

    // Cuenta por defecto de una categoria: la que tiene esa funcion especial o, si el plan no la tiene (por ejemplo, un plan importado),
    // la primera cuenta final activa de esa categoria (por codigo). Si no hay ninguna, queda la clave generica y el reporte la rotula.
    private string DefaultKey(string roleKey, LedgerCategoryType type, Func<LedgerCategory, bool> sameCategory)
    {
        if (ByKey.ContainsKey(roleKey))
        {
            return roleKey;
        }

        if (_defaultKeys.TryGetValue(roleKey, out var cached))
        {
            return cached;
        }

        var parents = Categories.Where(c => c.ParentId.HasValue).Select(c => c.ParentId!.Value).ToHashSet();
        var pick = Categories
            .Where(c => c.Type == type && c.ParentId.HasValue && !parents.Contains(c.Id) && sameCategory(c))
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .FirstOrDefault();
        return _defaultKeys[roleKey] = pick?.RubroKey ?? roleKey;
    }

    public decimal TotalOpening => Accounts.Sum(x => x.OpeningBalance);
}
