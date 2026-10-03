using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Application.Services;

/// <summary>
/// Plan de cuentas generico de CondoPY: la base de la que parte cada edificio (el SuperAdmin elige cuales cuentas usa, o lo reemplaza
/// por el plan del cliente). Esta fijo en codigo y se versiona con el sistema; lo que se edita es el plan de cada edificio.
/// Cinco clases: 1 Activo, 2 Pasivo, 3 Patrimonio / Fondos, 4 Ingresos y 5 Egresos; clase → grupo → cuenta (hasta 3 niveles, y los
/// planes importados pueden tener mas). Solo las cuentas de ingresos y egresos reciben movimientos; las demas son de referencia
/// (para exportar al contador). Cada cuenta de egresos o ingresos lleva la categoria con la que cuenta en la liquidacion.
/// </summary>
public static class FinanceChartTemplate
{
    /// <param name="Core">Cuenta recomendada: nace activa; el resto del plan generico nace inactivo y el SuperAdmin activa las que usa.</param>
    public sealed record Node(
        string Code,
        string Name,
        LedgerCategoryType Type,
        string? ParentCode,
        string? SystemKey,
        BuildingExpenseCategory? ExpenseCategory,
        BuildingIncomeCategory? IncomeCategory,
        bool Core);

    /// <summary>
    /// Funcion especial de una cuenta (<c>SystemKey</c>, unica por edificio). La cobranza de expensas no sale de gastos ni ingresos
    /// cargados a mano: el libro la clasifica segun el tipo de cargo que pago el propietario. Las cuentas «por defecto» de una categoria
    /// reciben los gastos o ingresos que se cargan sin elegir rubro. Ninguna es obligatoria: si falta, el libro muestra la linea con un
    /// nombre generico (por ejemplo «ANDE (sin rubro)») en lugar de un codigo.
    /// </summary>
    public sealed record RoleInfo(
        string Key,
        string Label,
        string FallbackName,
        LedgerCategoryType Type,
        BuildingExpenseCategory? ExpenseCategory,
        BuildingIncomeCategory? IncomeCategory);

    // Claves de las cuentas de cobranza de expensas (ingresos que salen de los pagos de los propietarios).
    public const string CollectionOrdinary = "Collection.Ordinary";
    public const string CollectionExtraordinary = "Collection.Extraordinary";
    public const string CollectionIndividualAdjustment = "Collection.IndividualAdjustment";
    public const string CollectionLateFee = "Collection.LateFee";
    public const string CollectionReserveFund = "Collection.ReserveFund";

    public static string ExpenseKey(BuildingExpenseCategory category) => $"Expense.{category}";
    public static string IncomeKey(BuildingIncomeCategory category) => $"Income.{category}";

    public const int MaxDepth = 6;

    // ── Funciones especiales ──────────────────────────────────────────────────

    private static string ExpenseText(BuildingExpenseCategory c) => c switch
    {
        BuildingExpenseCategory.Utilities => "Servicios",
        BuildingExpenseCategory.Cleaning => "Limpieza",
        BuildingExpenseCategory.Security => "Seguridad",
        BuildingExpenseCategory.Maintenance => "Mantenimiento",
        BuildingExpenseCategory.Elevator => "Ascensor",
        BuildingExpenseCategory.Insurance => "Seguro",
        BuildingExpenseCategory.Payroll => "Salarios",
        BuildingExpenseCategory.Taxes => "Impuestos",
        BuildingExpenseCategory.Administration => "Administración",
        BuildingExpenseCategory.ReserveFund => "Fondo de reserva",
        BuildingExpenseCategory.Extraordinary => "Extraordinario",
        BuildingExpenseCategory.Supplies => "Insumos",
        BuildingExpenseCategory.Ande => "ANDE",
        BuildingExpenseCategory.Essap => "ESSAP",
        BuildingExpenseCategory.InternetPhone => "Internet y telefonía",
        _ => "Otros gastos"
    };

    private static string IncomeText(BuildingIncomeCategory c) => c switch
    {
        BuildingIncomeCategory.CommonAreaRental => "Alquiler de áreas comunes",
        BuildingIncomeCategory.Interest => "Intereses",
        BuildingIncomeCategory.OperationalFund => "Fondo operativo",
        BuildingIncomeCategory.CreditAdjustment => "Ajustes a favor",
        BuildingIncomeCategory.ExtraordinaryContribution => "Aportes extraordinarios",
        BuildingIncomeCategory.AccumulatedBalance => "Saldo acumulado",
        _ => "Otros ingresos"
    };

    /// <summary>Todas las funciones que se pueden asignar a una cuenta.</summary>
    public static IReadOnlyList<RoleInfo> Roles { get; } = BuildRoles();

    private static List<RoleInfo> BuildRoles()
    {
        var roles = new List<RoleInfo>
        {
            new(CollectionOrdinary, "Cobranza de expensas ordinarias", "Cobro de expensas ordinarias", LedgerCategoryType.Income, null, null),
            new(CollectionExtraordinary, "Cobranza de aportes extraordinarios", "Aporte extraordinario cobrado", LedgerCategoryType.Income, null, null),
            new(CollectionIndividualAdjustment, "Cobranza de cargos individuales y ajustes", "Cargos individuales y ajustes cobrados", LedgerCategoryType.Income, null, null),
            new(CollectionLateFee, "Cobranza de intereses por mora", "Intereses por mora cobrados", LedgerCategoryType.Income, null, null),
            new(CollectionReserveFund, "Cobranza de aportes al fondo de reserva", "Aporte al fondo de reserva cobrado", LedgerCategoryType.Income, null, null)
        };

        // El aporte al fondo de reserva (gasto) y el saldo acumulado / fondo operativo (ingresos) tienen un trato especial en el libro.
        foreach (var c in Enum.GetValues<BuildingExpenseCategory>().Where(x => x != BuildingExpenseCategory.ReserveFund))
        {
            roles.Add(new(ExpenseKey(c), $"Cuenta por defecto de gastos: {ExpenseText(c)}", $"{ExpenseText(c)} (sin rubro)", LedgerCategoryType.Expense, c, null));
        }

        foreach (var c in Enum.GetValues<BuildingIncomeCategory>()
                     .Where(x => x is not (BuildingIncomeCategory.AccumulatedBalance or BuildingIncomeCategory.OperationalFund)))
        {
            roles.Add(new(IncomeKey(c), $"Cuenta por defecto de ingresos: {IncomeText(c)}", $"{IncomeText(c)} (sin rubro)", LedgerCategoryType.Income, null, c));
        }

        return roles;
    }

    private static readonly Dictionary<string, RoleInfo> RolesByKey = Roles.ToDictionary(r => r.Key, StringComparer.Ordinal);

    public static RoleInfo? FindRole(string? key) => key is not null && RolesByKey.TryGetValue(key, out var role) ? role : null;

    /// <summary>
    /// Nombre con el que el libro muestra un movimiento cuyo rubro no existe en el plan del edificio (por ejemplo, un gasto cargado sin
    /// elegir rubro en un plan que no tiene la cuenta por defecto de su categoria).
    /// </summary>
    public static string FallbackName(string rubroKey) =>
        FindRole(rubroKey)?.FallbackName
        ?? (rubroKey == ExpenseKey(BuildingExpenseCategory.ReserveFund) ? "Fondo de reserva (sin rubro)" : rubroKey);

    // ── Categoria de la liquidacion de una cuenta ─────────────────────────────

    /// <summary>
    /// Categoria de gasto con la que cuenta en la liquidacion un gasto cargado en la cuenta: la guardada o, si no tiene, la que dice su
    /// funcion. Nula si la cuenta no es de egresos. Una cuenta de egresos sin dato cuenta como «Otro».
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

    /// <summary>Lo mismo para los ingresos. Las cuentas de cobranza de expensas (Collection.*) no son ingresos cargados a mano: salen de los pagos de los propietarios.</summary>
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
            return category.SystemKey.StartsWith(prefix, StringComparison.Ordinal)
                && Enum.TryParse<BuildingIncomeCategory>(category.SystemKey[prefix.Length..], out var parsed) && Enum.IsDefined(parsed)
                    ? parsed
                    : null;
        }

        return BuildingIncomeCategory.Other;
    }

    /// <summary>
    /// Cuentas en las que se pueden cargar gastos o ingresos del edificio: las hojas activas de ese tipo que tienen un grupo y categoria
    /// de liquidacion. Un grupo solo agrupa; la cobranza de expensas sale de los pagos de los propietarios; las clases de balance
    /// (activo, pasivo, patrimonio) son de referencia: ninguna recibe gastos ni ingresos cargados a mano.
    /// </summary>
    public static List<LedgerCategory> AssignableCategories(IReadOnlyCollection<LedgerCategory> categories, LedgerCategoryType type)
    {
        var parentIds = categories.Where(c => c.ParentId.HasValue).Select(c => c.ParentId!.Value).ToHashSet();
        return categories
            .Where(c => c.Type == type && c.IsActive && c.ParentId.HasValue && !parentIds.Contains(c.Id) && CanReceiveMovements(c))
            .OrderBy(c => c.Code, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>La cuenta tiene categoria de liquidacion, o sea que se le pueden cargar gastos (si es de egresos) o ingresos (si es de ingresos).</summary>
    public static bool CanReceiveMovements(LedgerCategory category) =>
        category.Type == LedgerCategoryType.Expense ? ExpenseCategoryOf(category) is not null
        : category.Type == LedgerCategoryType.Income && IncomeCategoryOf(category) is not null;

    // ── Plan generico ─────────────────────────────────────────────────────────

    public static IReadOnlyList<Node> Nodes { get; } = Build();

    /// <summary>
    /// El plan generico como filas listas para guardar: una cuenta nace activa si es recomendada y un grupo, si alguna de sus cuentas lo es.
    /// </summary>
    public static IReadOnlyList<PlanRowSpec> Specs { get; } = BuildSpecs();

    private static List<PlanRowSpec> BuildSpecs()
    {
        var childrenOf = Nodes.Where(n => n.ParentCode is not null).ToLookup(n => n.ParentCode!);
        bool Active(Node n) => n.Core || childrenOf[n.Code].Any(Active);

        return Nodes
            .Select(n => new PlanRowSpec(n.Code, n.Name, n.ParentCode, n.Type, null, Active(n), n.SystemKey, n.ExpenseCategory, n.IncomeCategory))
            .ToList();
    }

    /// <summary>Plan generico como entidades nuevas del edificio (sin guardar), con las cuentas ya enlazadas a su grupo.</summary>
    public static IReadOnlyList<LedgerCategory> CreateEntities(Guid buildingId, Guid companyId)
    {
        var childrenOf = Nodes.Where(n => n.ParentCode is not null).ToLookup(n => n.ParentCode!);

        // Una cuenta nace activa si es recomendada; un grupo, si alguna de sus cuentas lo es.
        bool Active(Node n) => n.Core || childrenOf[n.Code].Any(Active);

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
                ExpenseCategory = n.ExpenseCategory,
                IncomeCategory = n.IncomeCategory,
                IsActive = Active(n)
            });

        foreach (var node in Nodes.Where(n => n.ParentCode is not null))
        {
            byCode[node.Code].ParentId = byCode[node.ParentCode!].Id;
        }

        return byCode.Values.ToList();
    }

    // Formato de cada linea: codigo|nombre|marcas. Marcas (separadas por espacio): * cuenta recomendada · E:Categoria categoria de gasto de la
    // liquidacion (un grupo la hereda a sus cuentas) · I:Categoria categoria de ingreso · K:Clave funcion especial de la cuenta.
    // La clase sale del primer numero del codigo y el padre, del codigo sin su ultimo tramo.
    private const string PlanText = """
        1|ACTIVO
        1.1|Disponibilidades
        1.1.01|Caja administración|*
        1.1.02|Fondo fijo / caja chica encargado
        1.1.03|Banco cuenta corriente|*
        1.1.04|Banco caja de ahorro|*
        1.1.05|Billeteras y pasarelas de pago
        1.1.06|Valores a depositar
        1.1.07|Recaudación en tránsito
        1.1.08|Banco cuenta en USD
        1.1.09|Caja en USD
        1.2|Inversiones
        1.2.01|Plazo fijo / CDA
        1.2.02|Fondos de inversión
        1.2.03|Intereses a cobrar
        1.3|Créditos por expensas
        1.3.01|Expensas ordinarias a cobrar|*
        1.3.02|Expensas extraordinarias a cobrar|*
        1.3.03|Fondo de reserva a cobrar|*
        1.3.04|Intereses por mora a cobrar|*
        1.3.05|Multas a cobrar
        1.3.06|Cargos particulares a recuperar
        1.3.07|Deudores con convenio de pago
        1.3.08|Deudores en gestión judicial
        1.3.09|(-) Previsión para incobrables|*
        1.4|Otros créditos
        1.4.01|Anticipos a proveedores
        1.4.02|Anticipos al personal
        1.4.03|Alquileres de espacios comunes a cobrar
        1.4.04|Reservas de amenities a cobrar
        1.4.05|Siniestros a cobrar
        1.4.06|IVA crédito fiscal (10% / 5%)
        1.4.07|Depósitos en garantía entregados
        1.4.08|Gastos pagados por adelantado
        1.4.09|Retenciones de IVA / IRE sufridas
        1.5|Bienes de uso
        1.5.01|Muebles y útiles
        1.5.02|Equipos y herramientas
        1.5.03|Instalaciones y equipamiento
        1.5.04|(-) Depreciación acumulada
        2|PASIVO
        2.1|Deudas comerciales
        2.1.01|Proveedores|*
        2.1.02|Servicios públicos a pagar
        2.1.03|Abonos a pagar
        2.1.04|Cheques diferidos emitidos
        2.1.05|Fondo de reparo retenido a contratistas
        2.2|Remuneraciones y cargas sociales
        2.2.01|Sueldos a pagar|*
        2.2.02|IPS a pagar (aporte obrero + patronal)|*
        2.2.03|Provisión aguinaldo
        2.2.04|Provisión vacaciones
        2.2.05|Provisión preaviso e indemnizaciones
        2.3|Deudas fiscales
        2.3.01|Impuesto inmobiliario y tasas municipales a pagar
        2.3.02|Retenciones IVA / IRE a ingresar (DNIT)
        2.3.03|IVA a pagar (DNIT)
        2.3.04|Facilidades de pago DNIT / IPS
        2.4|Honorarios
        2.4.01|Honorarios de administración a pagar
        2.4.02|Honorarios profesionales a pagar
        2.5|Otros pasivos
        2.5.01|Expensas cobradas por adelantado|*
        2.5.02|Saldos a favor de propietarios|*
        2.5.03|Cobros no identificados
        2.5.04|Depósitos en garantía recibidos
        2.5.05|Préstamos
        2.5.06|Previsión juicios y contingencias
        3|PATRIMONIO / FONDOS
        3.1|Fondos
        3.1.01|Fondo de reserva|*
        3.1.02|Fondo para obras
        3.1.03|Fondo de previsión laboral
        3.1.04|Fondo operativo
        3.2|Resultados
        3.2.01|Resultados acumulados|*
        3.2.02|Resultado del período|*
        4|INGRESOS
        4.1|Expensas
        4.1.01|Expensas ordinarias|* K:Collection.Ordinary
        4.1.02|Expensas extraordinarias|* K:Collection.Extraordinary
        4.1.03|Aportes al fondo de reserva|* K:Collection.ReserveFund
        4.1.04|Cargos particulares a unidades|* K:Collection.IndividualAdjustment
        4.2|Recargos|I:Other
        4.2.01|Intereses por mora|* K:Collection.LateFee
        4.2.02|Multas por infracción al reglamento|*
        4.2.03|Recupero de gastos de cobranza y judiciales
        4.3|Otros ingresos|I:Other
        4.3.01|Alquiler de espacios comunes (antenas, cartelería, terraza)|* I:CommonAreaRental K:Income.CommonAreaRental
        4.3.02|Alquiler de cocheras / bauleras|I:CommonAreaRental
        4.3.03|Uso de amenities (SUM, quincho, parrilla)|* I:CommonAreaRental
        4.3.04|Intereses y rentas financieras|* I:Interest K:Income.Interest
        4.3.05|Recupero de siniestros
        4.3.06|Venta de bienes / rezagos
        4.3.07|Ingresos varios|* K:Income.Other
        4.3.08|Diferencia de cambio positiva
        5|EGRESOS
        5.01|Personal|E:Payroll
        5.01.01|Sueldos encargado y ayudantes|* K:Expense.Payroll
        5.01.02|Horas extras|*
        5.01.03|Suplencias
        5.01.04|Aguinaldo|*
        5.01.05|Vacaciones|*
        5.01.06|Aporte patronal IPS|*
        5.01.07|Bonificación familiar
        5.01.08|Preaviso e indemnizaciones
        5.01.09|Ropa de trabajo y elementos de seguridad
        5.01.10|Liquidación de sueldos
        5.02|Servicios públicos|E:Utilities
        5.02.01|Electricidad (ANDE)|* E:Ande K:Expense.Ande
        5.02.02|Agua y alcantarillado (ESSAP / aguatera)|* E:Essap K:Expense.Essap
        5.02.03|Gas (GLP granel / garrafas)
        5.02.04|Teléfono e internet|* E:InternetPhone K:Expense.InternetPhone
        5.02.05|Recolección de residuos|* K:Expense.Utilities
        5.03|Abonos de servicios|E:Maintenance
        5.03.01|Ascensores|* E:Elevator K:Expense.Elevator
        5.03.02|Bombas de agua|*
        5.03.03|Limpieza de tanques|E:Cleaning
        5.03.04|Desinfección y control de plagas|E:Cleaning
        5.03.05|Extintores e instalación contra incendio
        5.03.06|Portero eléctrico / control de acceso|E:Security
        5.03.07|Portones automáticos
        5.03.08|Generador
        5.03.09|Calderas / termocalefones
        5.03.10|Aire acondicionado central
        5.03.11|CCTV y alarmas|E:Security
        5.03.12|Jardinería
        5.03.13|Piscina
        5.03.14|Software de gestión|E:Administration
        5.04|Mantenimiento y reparaciones|E:Maintenance
        5.04.01|Electricidad|*
        5.04.02|Plomería y sanitarios|*
        5.04.03|Gas
        5.04.04|Albañilería|*
        5.04.05|Pintura|*
        5.04.06|Herrería y cerrajería
        5.04.07|Carpintería y vidrios
        5.04.08|Impermeabilización y techos
        5.04.09|Ascensores (fuera de abono)|E:Elevator
        5.04.10|Destapaciones
        5.04.11|Materiales y ferretería|* K:Expense.Maintenance
        5.05|Limpieza|E:Cleaning
        5.05.01|Servicio de limpieza tercerizado|* K:Expense.Cleaning
        5.05.02|Artículos de limpieza|* E:Supplies K:Expense.Supplies
        5.05.03|Retiro de residuos / contenedores
        5.06|Seguridad|E:Security
        5.06.01|Vigilancia|* K:Expense.Security
        5.06.02|Monitoreo
        5.07|Administración|E:Administration
        5.07.01|Honorarios de administración|* K:Expense.Administration
        5.07.02|Papelería, impresiones y copias|*
        5.07.03|Correo y notificaciones
        5.07.04|Gastos de asamblea
        5.07.05|Certificaciones y libros
        5.07.06|Honorarios contables / auditoría|*
        5.07.07|Timbrado y documentos fiscales
        5.07.08|Gestiones MTESS / IPS (planillas laborales)
        5.08|Seguros|E:Insurance
        5.08.01|Seguro integral del edificio|* K:Expense.Insurance
        5.08.02|Responsabilidad civil
        5.08.03|Incendio
        5.08.04|Otros seguros
        5.09|Impuestos y tasas|E:Taxes
        5.09.01|Impuesto inmobiliario (municipal)|* K:Expense.Taxes
        5.09.02|Tasas municipales (basura, barrido, pavimento)|*
        5.09.03|Habilitaciones e inspecciones
        5.09.04|IVA no recuperable
        5.09.05|Multas y recargos DNIT / IPS / MTESS
        5.10|Bancarios y financieros|E:Administration
        5.10.01|Comisiones y mantenimiento de cuenta|*
        5.10.02|Comisiones de cobranza
        5.10.03|Intereses pagados
        5.10.04|Diferencia de cambio negativa
        5.11|Legales|E:Other
        5.11.01|Honorarios de abogados
        5.11.02|Gastos y tasas judiciales
        5.11.03|Escribanía
        5.11.04|Juicios y sentencias
        5.12|Amenities|E:Maintenance
        5.12.01|SUM / quincho
        5.12.02|Gimnasio
        5.12.03|Piscina (insumos)|E:Supplies
        5.12.04|Guardavidas|E:Payroll
        5.13|Obras y mejoras (extraordinarios)|E:Extraordinary
        5.13.01|Obras de fachada|* K:Expense.Extraordinary
        5.13.02|Obras estructurales
        5.13.03|Renovación de ascensores
        5.13.04|Renovación de instalaciones
        5.13.05|Compra de equipamiento
        5.13.06|Honorarios de dirección de obra
        5.14|Gastos particulares de unidades|E:Other
        5.14.01|Reparaciones en unidades
        5.14.02|Consumos individuales medidos
        5.15|Otros|E:Other
        5.15.01|Incobrables
        5.15.02|Depreciaciones
        5.15.03|Gastos varios|* K:Expense.Other
        """;

    private static List<Node> Build()
    {
        var parsed = new List<(string Code, string Name, string[] Flags)>();
        foreach (var raw in PlanText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split('|');
            parsed.Add((parts[0], parts[1], parts.Length > 2 ? parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries) : []));
        }

        var expenseOf = new Dictionary<string, BuildingExpenseCategory>();
        var incomeOf = new Dictionary<string, BuildingIncomeCategory>();
        var nodes = new List<Node>();

        foreach (var (code, name, flags) in parsed)
        {
            var type = code[0] switch
            {
                '1' => LedgerCategoryType.Asset,
                '2' => LedgerCategoryType.Liability,
                '3' => LedgerCategoryType.Fund,
                '4' => LedgerCategoryType.Income,
                _ => LedgerCategoryType.Expense
            };
            var dot = code.LastIndexOf('.');
            var parentCode = dot < 0 ? null : code[..dot];

            // Un grupo hereda la categoria de liquidacion a sus cuentas, salvo que la cuenta traiga la suya.
            BuildingExpenseCategory? expense = parentCode is not null && expenseOf.TryGetValue(parentCode, out var pe) ? pe : null;
            BuildingIncomeCategory? income = parentCode is not null && incomeOf.TryGetValue(parentCode, out var pi) ? pi : null;
            string? key = null;
            var core = false;

            foreach (var flag in flags)
            {
                if (flag == "*") core = true;
                else if (flag.StartsWith("E:", StringComparison.Ordinal)) expense = Enum.Parse<BuildingExpenseCategory>(flag[2..]);
                else if (flag.StartsWith("I:", StringComparison.Ordinal)) income = Enum.Parse<BuildingIncomeCategory>(flag[2..]);
                else if (flag.StartsWith("K:", StringComparison.Ordinal)) key = flag[2..];
                else throw new InvalidOperationException($"Marca desconocida «{flag}» en la cuenta {code}.");
            }

            if (expense.HasValue) expenseOf[code] = expense.Value;
            if (income.HasValue) incomeOf[code] = income.Value;

            // Las cuentas de cobranza de expensas no llevan categoria: no reciben ingresos cargados a mano.
            var isCollection = key?.StartsWith("Collection.", StringComparison.Ordinal) == true;
            nodes.Add(new Node(
                code, name, type, parentCode, key,
                type == LedgerCategoryType.Expense ? expense : null,
                type == LedgerCategoryType.Income && !isCollection ? income : null,
                core));
        }

        // Los grupos no llevan categoria propia (la heredan sus cuentas): solo las hojas la guardan.
        var parents = nodes.Where(n => n.ParentCode is not null).Select(n => n.ParentCode!).ToHashSet();
        return nodes
            .Select(n => parents.Contains(n.Code) ? n with { ExpenseCategory = null, IncomeCategory = null } : n)
            .ToList();
    }
}
