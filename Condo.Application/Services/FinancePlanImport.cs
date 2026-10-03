using System.Globalization;
using System.Text;
using Condo.Domain.Enums;

namespace Condo.Application.Services;

/// <summary>Una cuenta del plan ya definida (de la plantilla generica o de un plan importado), lista para guardarse en un edificio.</summary>
public sealed record PlanRowSpec(
    string Code,
    string Name,
    string? ParentCode,
    LedgerCategoryType Type,
    string? ExternalCode,
    bool IsActive,
    string? SystemKey,
    BuildingExpenseCategory? ExpenseCategory,
    BuildingIncomeCategory? IncomeCategory);

/// <summary>Fila tal como llega (del Excel o de la vista previa corregida por el usuario), antes de validar.</summary>
public sealed class PlanRowInput
{
    public int RowNumber { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    // Vacio = se deduce por el codigo (el mas largo que lo antecede) al leer el Excel.
    public string? ParentCode { get; set; }
    // Vacio = el del grupo; en un grupo raiz, el que diga el primer numero del codigo (1 activo ... 5 egresos).
    public LedgerCategoryType? Type { get; set; }
    public string? ExternalCode { get; set; }
    public bool IsActive { get; set; } = true;
    public BuildingExpenseCategory? ExpenseCategory { get; set; }
    public BuildingIncomeCategory? IncomeCategory { get; set; }
    public string? SystemKey { get; set; }
    // Problemas ya detectados al leer el archivo (texto de tipo o categoria que no se reconoce).
    public List<string> ParseErrors { get; set; } = new();
    public List<string> ParseWarnings { get; set; } = new();
}

/// <summary>Fila ya resuelta: padre, tipo y categoria definitivos, mas lo que hay que corregir (errores) o revisar (avisos).</summary>
public sealed class PlanRowResult
{
    public int RowNumber { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentCode { get; set; }
    public LedgerCategoryType Type { get; set; }
    public int Level { get; set; }
    // Cuenta (sin subcuentas): la unica que recibe gastos o ingresos.
    public bool IsLeaf { get; set; }
    public string? ExternalCode { get; set; }
    public bool IsActive { get; set; }
    public string? SystemKey { get; set; }
    public BuildingExpenseCategory? ExpenseCategory { get; set; }
    public BuildingIncomeCategory? IncomeCategory { get; set; }
    // La categoria la sugirio el sistema por el nombre (no venia en el archivo): conviene revisarla.
    public bool CategorySuggested { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public PlanRowSpec ToSpec() => new(Code, Name, ParentCode, Type, ExternalCode, IsActive, SystemKey, ExpenseCategory, IncomeCategory);
}

public sealed class PlanResolution
{
    public List<PlanRowResult> Rows { get; set; } = new();
    public int ErrorCount => Rows.Sum(r => r.Errors.Count);
    public bool HasErrors => ErrorCount > 0;
}

/// <summary>
/// Valida y resuelve las filas de un plan de cuentas importado: arma el arbol (padre explicito o deducido por el codigo), el tipo
/// de cada cuenta, la categoria de la liquidacion de las hojas de ingresos y egresos y las funciones especiales. Es logica pura:
/// la misma validacion corre en la vista previa y, de nuevo, al confirmar (el servidor no confia en lo que manda el cliente).
/// </summary>
public static class FinancePlanValidator
{
    public const int MaxCodeLength = 30;
    public const int MaxNameLength = 200;
    public const int MaxExternalCodeLength = 50;
    public const int MaxRows = 3000;

    /// <param name="inferParents">Deduce el padre por el codigo cuando no viene. En la confirmacion va en falso: ya vino resuelto.</param>
    /// <param name="inferTypes">Deduce el tipo de un grupo raiz por el primer numero de su codigo cuando no viene.</param>
    /// <param name="suggestCategories">Sugiere la categoria de la liquidacion (por el nombre) de las hojas de ingresos y egresos que no la traen.</param>
    /// <param name="childTypeFollowsParent">Ignora el tipo declarado en una cuenta con padre y usa el del padre (en la confirmacion el usuario solo cambia el de las raices).</param>
    public static PlanResolution Resolve(
        IReadOnlyList<PlanRowInput> input, bool inferParents, bool inferTypes, bool suggestCategories, bool childTypeFollowsParent)
    {
        var result = new PlanResolution();
        if (input.Count > MaxRows)
        {
            var only = new PlanRowResult { RowNumber = 0 };
            only.Errors.Add($"El plan no puede tener más de {MaxRows} cuentas.");
            result.Rows.Add(only);
            return result;
        }

        // 1) Normalizar y detectar repetidos.
        var rows = new List<PlanRowResult>(input.Count);
        var byCode = new Dictionary<string, PlanRowResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in input)
        {
            var row = new PlanRowResult
            {
                RowNumber = r.RowNumber,
                Code = (r.Code ?? string.Empty).Trim(),
                Name = (r.Name ?? string.Empty).Trim(),
                ParentCode = string.IsNullOrWhiteSpace(r.ParentCode) ? null : r.ParentCode.Trim(),
                ExternalCode = string.IsNullOrWhiteSpace(r.ExternalCode) ? null : r.ExternalCode.Trim(),
                IsActive = r.IsActive,
                SystemKey = string.IsNullOrWhiteSpace(r.SystemKey) ? null : r.SystemKey.Trim(),
                ExpenseCategory = r.ExpenseCategory,
                IncomeCategory = r.IncomeCategory
            };
            row.Errors.AddRange(r.ParseErrors);
            row.Warnings.AddRange(r.ParseWarnings);
            rows.Add(row);

            if (row.Code.Length == 0)
            {
                row.Errors.Add("Falta el código.");
            }
            else if (row.Code.Length > MaxCodeLength)
            {
                row.Errors.Add($"El código no puede superar los {MaxCodeLength} caracteres.");
            }
            else if (!byCode.TryAdd(row.Code, row))
            {
                row.Errors.Add($"El código {row.Code} está repetido (ya figura en la fila {byCode[row.Code].RowNumber}).");
            }

            if (row.Name.Length == 0)
            {
                row.Errors.Add("Falta el nombre.");
            }
            else if (row.Name.Length > MaxNameLength)
            {
                row.Errors.Add($"El nombre no puede superar los {MaxNameLength} caracteres.");
            }

            if ((row.ExternalCode?.Length ?? 0) > MaxExternalCodeLength)
            {
                row.Errors.Add($"El código del contador no puede superar los {MaxExternalCodeLength} caracteres.");
            }
        }

        // 2) Padres.
        var codes = byCode.Keys.ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Code.Length == 0 || !ReferenceEquals(byCode.GetValueOrDefault(row.Code), row))
            {
                continue;
            }

            if (row.ParentCode is null)
            {
                if (inferParents)
                {
                    row.ParentCode = codes
                        .Where(c => c.Length < row.Code.Length && row.Code.StartsWith(c, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(c => c.Length)
                        .FirstOrDefault();
                }

                continue;
            }

            if (string.Equals(row.ParentCode, row.Code, StringComparison.OrdinalIgnoreCase))
            {
                row.Errors.Add("Una cuenta no puede ser su propio grupo.");
                row.ParentCode = null;
            }
            else if (!byCode.TryGetValue(row.ParentCode, out var parentRow))
            {
                row.Errors.Add($"El grupo «{row.ParentCode}» no existe en el archivo.");
                row.ParentCode = null;
            }
            else
            {
                row.ParentCode = parentRow.Code;
            }
        }

        // 3) Niveles (y ciclos).
        var children = new Dictionary<string, List<PlanRowResult>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Where(r => r.ParentCode is not null))
        {
            if (!children.TryGetValue(row.ParentCode!, out var list))
            {
                children[row.ParentCode!] = list = new List<PlanRowResult>();
            }

            list.Add(row);
        }

        foreach (var row in rows)
        {
            row.Level = 1;
            var current = row;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { row.Code };
            while (current.ParentCode is not null)
            {
                if (!seen.Add(current.ParentCode))
                {
                    row.Errors.Add("El grupo forma un ciclo (una cuenta es grupo de sí misma).");
                    row.ParentCode = null;
                    row.Level = 1;
                    break;
                }

                current = byCode[current.ParentCode];
                row.Level++;
            }

            if (row.Level > FinanceChartTemplate.MaxDepth)
            {
                row.Errors.Add($"El plan admite hasta {FinanceChartTemplate.MaxDepth} niveles; esta cuenta está en el nivel {row.Level}.");
            }
        }

        // 4) Tipos, de arriba hacia abajo (los grupos antes que sus cuentas).
        var declared = rows.Zip(input, (r, i) => (r, i)).ToDictionary(x => x.r, x => x.i.Type);
        foreach (var row in rows.OrderBy(r => r.Level))
        {
            var declaredHere = declared[row];
            if (row.ParentCode is not null && byCode.TryGetValue(row.ParentCode, out var parent))
            {
                if (!childTypeFollowsParent && declaredHere.HasValue && declaredHere.Value != parent.Type)
                {
                    row.Errors.Add($"El tipo ({TypeLabel(declaredHere.Value)}) no coincide con el de su grupo ({TypeLabel(parent.Type)}).");
                }

                row.Type = parent.Type;
            }
            else if (declaredHere.HasValue)
            {
                row.Type = declaredHere.Value;
            }
            else if (inferTypes && TypeFromCode(row.Code) is { } inferred)
            {
                row.Type = inferred;
                row.Warnings.Add($"Tipo deducido del primer número del código ({TypeLabel(inferred)}). Verificalo.");
            }
            else
            {
                row.Type = LedgerCategoryType.Expense;
                row.Errors.Add("No se pudo deducir el tipo de esta cuenta: indicalo (activo, pasivo, patrimonio, ingreso o egreso).");
            }
        }

        // 5) Hojas: categoria de la liquidacion y funcion especial.
        var parentsWithChildren = children.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keysSeen = new Dictionary<string, PlanRowResult>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            row.IsLeaf = !parentsWithChildren.Contains(row.Code);

            if (!row.IsLeaf)
            {
                // Un grupo solo agrupa: no guarda categoria ni funcion.
                row.ExpenseCategory = null;
                row.IncomeCategory = null;
                if (row.SystemKey is not null)
                {
                    row.Errors.Add("Un grupo no puede tener función especial: asignala a una de sus cuentas.");
                    row.SystemKey = null;
                }

                continue;
            }

            var movementType = row.Type is LedgerCategoryType.Expense or LedgerCategoryType.Income;
            if (movementType && row.ParentCode is null)
            {
                row.Errors.Add("Esta cuenta de ingresos o egresos no tiene grupo. Indicá el código de su grupo (o agregá el grupo al archivo).");
            }

            // Una cuenta de ingresos que se llama «Expensas ordinarias», «Intereses por mora»... es de cobranza: se sugiere su funcion (si el
            // archivo no trae categoria ni funcion y nadie la tiene todavia). El usuario puede quitarla en la vista previa.
            if (suggestCategories && row.Type == LedgerCategoryType.Income && row.SystemKey is null && !row.IncomeCategory.HasValue
                && LiquidationCategorySuggester.CollectionRoleFor(row.Name) is { } suggestedRole && !keysSeen.ContainsKey(suggestedRole))
            {
                row.SystemKey = suggestedRole;
                row.CategorySuggested = true;
                row.Warnings.Add($"Función sugerida por el nombre: «{FinanceChartTemplate.FindRole(suggestedRole)?.Label}». Revisala.");
            }

            if (row.SystemKey is not null)
            {
                ValidateRole(row, keysSeen);
            }

            var isCollection = row.SystemKey?.StartsWith("Collection.", StringComparison.Ordinal) == true;
            if (row.Type == LedgerCategoryType.Expense)
            {
                row.IncomeCategory = null;
                var roleCategory = FinanceChartTemplate.FindRole(row.SystemKey)?.ExpenseCategory;
                if (roleCategory.HasValue)
                {
                    row.ExpenseCategory = roleCategory;
                }
                else if (!row.ExpenseCategory.HasValue)
                {
                    var matched = false;
                    row.ExpenseCategory = suggestCategories ? LiquidationCategorySuggester.ForExpense(row.Name, out matched) : BuildingExpenseCategory.Other;
                    row.CategorySuggested = suggestCategories;
                    if (suggestCategories && !matched)
                    {
                        row.Warnings.Add("Sin coincidencia por el nombre: se asignó «Otro» en la liquidación. Revisalo.");
                    }
                }

                if (row.ExpenseCategory == BuildingExpenseCategory.ReserveFund)
                {
                    row.Errors.Add("El aporte al fondo de reserva ya tiene su propia cuenta de cobranza: elegí otra categoría de liquidación.");
                }
            }
            else if (row.Type == LedgerCategoryType.Income)
            {
                row.ExpenseCategory = null;
                if (isCollection)
                {
                    row.IncomeCategory = null;
                }
                else
                {
                    var roleCategory = FinanceChartTemplate.FindRole(row.SystemKey)?.IncomeCategory;
                    if (roleCategory.HasValue)
                    {
                        row.IncomeCategory = roleCategory;
                    }
                    else if (!row.IncomeCategory.HasValue)
                    {
                        var matched = false;
                        row.IncomeCategory = suggestCategories ? LiquidationCategorySuggester.ForIncome(row.Name, out matched) : BuildingIncomeCategory.Other;
                        row.CategorySuggested = suggestCategories;
                        if (suggestCategories && !matched)
                        {
                            row.Warnings.Add("Sin coincidencia por el nombre: se asignó «Otro» en la liquidación. Revisalo.");
                        }
                    }

                    if (row.IncomeCategory is BuildingIncomeCategory.AccumulatedBalance or BuildingIncomeCategory.OperationalFund)
                    {
                        row.Errors.Add("El saldo acumulado y el fondo operativo no son ingresos nuevos: elegí otra categoría de liquidación.");
                    }
                }
            }
            else
            {
                // Activo, pasivo y patrimonio: de referencia, sin categoria ni funcion.
                row.ExpenseCategory = null;
                row.IncomeCategory = null;
                if (row.SystemKey is not null)
                {
                    row.Errors.Add("Las cuentas de activo, pasivo y patrimonio no tienen función especial.");
                    row.SystemKey = null;
                }
            }
        }

        // 6) Un grupo esta activo si alguna de sus cuentas lo esta (asi nunca hay una cuenta activa bajo un grupo apagado).
        bool AnyActive(PlanRowResult r) =>
            r.IsActive || (children.TryGetValue(r.Code, out var list) && list.Any(AnyActive));
        foreach (var row in rows.Where(r => !r.IsLeaf))
        {
            row.IsActive = AnyActive(row);
        }

        result.Rows = rows;
        return result;
    }

    private static void ValidateRole(PlanRowResult row, Dictionary<string, PlanRowResult> keysSeen)
    {
        var role = FinanceChartTemplate.FindRole(row.SystemKey);
        if (role is null)
        {
            row.Errors.Add($"La función «{row.SystemKey}» no existe.");
            row.SystemKey = null;
            return;
        }

        if (role.Type != row.Type)
        {
            row.Errors.Add($"La función «{role.Label}» es de {TypeLabel(role.Type).ToLowerInvariant()}: no se puede asignar a una cuenta de {TypeLabel(row.Type).ToLowerInvariant()}.");
            row.SystemKey = null;
            return;
        }

        if (!keysSeen.TryAdd(role.Key, row))
        {
            row.Errors.Add($"La función «{role.Label}» ya la tiene la cuenta {keysSeen[role.Key].Code}: cada función va en una sola cuenta.");
            row.SystemKey = null;
        }
    }

    public static string TypeLabel(LedgerCategoryType type) => type switch
    {
        LedgerCategoryType.Asset => "Activo",
        LedgerCategoryType.Liability => "Pasivo",
        LedgerCategoryType.Fund => "Patrimonio / Fondos",
        LedgerCategoryType.Income => "Ingresos",
        _ => "Egresos"
    };

    /// <summary>Clase del plan segun el primer numero del codigo (1 activo, 2 pasivo, 3 patrimonio, 4 ingresos, 5 egresos); nula si no es un numero de 1 a 5.</summary>
    public static LedgerCategoryType? TypeFromCode(string code) =>
        code.Length > 0 ? code[0] switch
        {
            '1' => LedgerCategoryType.Asset,
            '2' => LedgerCategoryType.Liability,
            '3' => LedgerCategoryType.Fund,
            '4' => LedgerCategoryType.Income,
            '5' => LedgerCategoryType.Expense,
            _ => null
        } : null;

    /// <summary>Texto que se reconoce como tipo en el Excel (sin tildes ni mayusculas).</summary>
    public static LedgerCategoryType? ParseType(string? text)
    {
        var t = LiquidationCategorySuggester.Normalize(text);
        return t switch
        {
            "activo" or "activos" or "a" or "1" => LedgerCategoryType.Asset,
            "pasivo" or "pasivos" or "p" or "2" => LedgerCategoryType.Liability,
            "patrimonio" or "fondos" or "fondo" or "patrimonio / fondos" or "patrimonio neto" or "3" => LedgerCategoryType.Fund,
            "ingreso" or "ingresos" or "i" or "4" => LedgerCategoryType.Income,
            "egreso" or "egresos" or "gasto" or "gastos" or "e" or "5" => LedgerCategoryType.Expense,
            _ => null
        };
    }
}

/// <summary>
/// Sugiere la categoria de la liquidacion de una cuenta por las palabras de su nombre (sin tildes ni mayusculas). Es solo una ayuda:
/// el usuario revisa y corrige la categoria en la vista previa antes de confirmar la importacion.
/// </summary>
public static class LiquidationCategorySuggester
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }


    // Palabras clave por categoria, en orden de prioridad (la primera que coincide gana). Una palabra suelta de hasta 4 letras debe
    // coincidir entera ("gas" no coincide con "gastos"); una mas larga, con el comienzo de una palabra ("ascensor" coincide con "ascensores");
    // una frase con espacios, en cualquier parte del nombre.
    private static readonly (BuildingExpenseCategory Category, string[] Words)[] ExpenseRules =
    [
        (BuildingExpenseCategory.Ande, ["ande", "energia electrica", "consumo electrico", "luz"]),
        (BuildingExpenseCategory.Essap, ["essap", "aguatera", "agua y alcantarillado", "agua potable", "alcantarillado"]),
        (BuildingExpenseCategory.InternetPhone, ["internet", "telefon", "telecom"]),
        (BuildingExpenseCategory.Elevator, ["ascensor", "elevador"]),
        (BuildingExpenseCategory.Insurance, ["seguro", "poliza"]),
        (BuildingExpenseCategory.Payroll, ["sueldo", "salario", "jornal", "aguinaldo", "vacacion", "ips", "personal", "horas extra", "bonificacion", "preaviso", "indemnizacion", "guardavida", "suplencia"]),
        (BuildingExpenseCategory.Taxes, ["impuesto", "tasa", "tasas", "municipal", "inmobiliario", "iva", "dnit", "mtess"]),
        (BuildingExpenseCategory.Security, ["segurid", "vigilan", "monitoreo", "alarma", "cctv", "camara", "control de acceso"]),
        (BuildingExpenseCategory.Cleaning, ["limpieza", "desinfecc", "fumigac", "plaga", "residuos", "basura"]),
        (BuildingExpenseCategory.Administration, ["administrac", "honorario", "papeleria", "impresion", "asamblea", "contab", "auditor", "banco", "bancari", "comision", "software", "timbrado", "notificac"]),
        (BuildingExpenseCategory.Extraordinary, ["extraordinari", "obra", "obras", "fachada", "renovacion", "mejora", "estructural"]),
        (BuildingExpenseCategory.Supplies, ["insumo", "articulo", "material", "ferreteria"]),
        (BuildingExpenseCategory.Maintenance, ["mantenimiento", "reparacion", "plomeria", "pintura", "albanil", "herreria", "carpinteria", "impermeabiliz", "destapacion", "jardiner", "piscina", "bomba", "generador", "portones", "aire acondicionado"]),
        (BuildingExpenseCategory.Utilities, ["servicio", "gas", "glp"])
    ];

    private static readonly (BuildingIncomeCategory Category, string[] Words)[] IncomeRules =
    [
        (BuildingIncomeCategory.CommonAreaRental, ["alquiler", "arrendamiento", "uso de", "sum", "quincho", "cochera", "antena"]),
        (BuildingIncomeCategory.Interest, ["interes", "renta financ", "rendimiento", "plazo fijo"]),
        (BuildingIncomeCategory.CreditAdjustment, ["ajuste a favor"]),
        (BuildingIncomeCategory.ExtraordinaryContribution, ["aporte extraordinario"])
    ];

    private static bool Matches(string normalized, string[] tokens, string word)
    {
        if (word.Contains(' '))
        {
            return normalized.Contains(word, StringComparison.Ordinal);
        }

        return word.Length <= 4
            ? tokens.Contains(word)
            : tokens.Any(t => t.StartsWith(word, StringComparison.Ordinal));
    }

    private static (string Normalized, string[] Tokens) Prepare(string name)
    {
        var normalized = Normalize(name);
        var tokens = normalized.Split(
            normalized.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(),
            StringSplitOptions.RemoveEmptyEntries);
        return (normalized, tokens);
    }

    /// <summary>
    /// Funcion de cobranza de expensas que sugiere el nombre de una cuenta de ingresos («Expensas ordinarias», «Intereses por mora»,
    /// «Aporte al fondo de reserva»...); nula si no parece de cobranza.
    /// </summary>
    public static string? CollectionRoleFor(string name)
    {
        var (normalized, tokens) = Prepare(name);
        bool Has(string word) => Matches(normalized, tokens, word);

        if (Has("mora") || Has("punitorio")) return FinanceChartTemplate.CollectionLateFee;
        if (Has("extraordinari") && (Has("expensa") || Has("aporte") || Has("cuota"))) return FinanceChartTemplate.CollectionExtraordinary;
        if (Has("fondo de reserva") || (Has("reserva") && Has("aporte"))) return FinanceChartTemplate.CollectionReserveFund;
        if (Has("cargo") && (Has("particular") || Has("individual"))) return FinanceChartTemplate.CollectionIndividualAdjustment;
        if (Has("expensa")) return FinanceChartTemplate.CollectionOrdinary;
        return null;
    }

    public static BuildingExpenseCategory ForExpense(string name, out bool matched)
    {
        var (normalized, tokens) = Prepare(name);
        foreach (var (category, words) in ExpenseRules)
        {
            if (words.Any(w => Matches(normalized, tokens, w)))
            {
                matched = true;
                return category;
            }
        }

        matched = false;
        return BuildingExpenseCategory.Other;
    }

    public static BuildingIncomeCategory ForIncome(string name, out bool matched)
    {
        var (normalized, tokens) = Prepare(name);
        foreach (var (category, words) in IncomeRules)
        {
            if (words.Any(w => Matches(normalized, tokens, w)))
            {
                matched = true;
                return category;
            }
        }

        matched = false;
        return BuildingIncomeCategory.Other;
    }
}
