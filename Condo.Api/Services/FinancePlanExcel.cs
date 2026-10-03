using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Condo.Application.Services;
using Condo.Domain.Enums;

namespace Condo.Api.Services;

/// <summary>
/// Excel del plan de cuentas del cliente: genera la plantilla y lee el archivo que se importa. Solo interpreta el archivo (una fila por cuenta);
/// armar el arbol, el tipo y la categoria de la liquidacion lo hace <see cref="FinancePlanValidator"/>, igual en la vista previa y al confirmar.
/// </summary>
public static class FinancePlanExcel
{
    public const int MaxRows = FinancePlanValidator.MaxRows;
    public const string SheetName = "Plan de cuentas";

    private static readonly string[] CodeHeaders = ["codigo", "cuenta", "codigo de cuenta", "nro de cuenta", "numero de cuenta"];
    private static readonly string[] NameHeaders = ["nombre", "descripcion", "nombre de la cuenta", "denominacion"];
    private static readonly string[] ParentHeaders = ["codigo padre", "padre", "grupo", "cuenta padre", "codigo del grupo"];
    private static readonly string[] TypeHeaders = ["tipo", "clase", "naturaleza"];
    private static readonly string[] ExternalHeaders = ["codigo del contador", "codigo contador", "codigo externo"];
    private static readonly string[] CategoryHeaders = ["categoria en la liquidacion", "categoria de la liquidacion", "categoria", "liquidacion"];
    private static readonly string[] ActiveHeaders = ["activo", "estado", "activa"];

    private static readonly Dictionary<string, BuildingExpenseCategory> ExpenseLookup = BuildExpenseLookup();
    private static readonly Dictionary<string, BuildingIncomeCategory> IncomeLookup = BuildIncomeLookup();

    public static string Normalize(string? text) => Regex.Replace(LiquidationCategorySuggester.Normalize(text), @"\s+", " ");

    // ── Lectura ───────────────────────────────────────────────────────────────

    public static (List<PlanRowInput>? Rows, string? Error) Parse(Stream stream)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception)
        {
            return (null, "El archivo no es un Excel (.xlsx) válido.");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault(w => Normalize(w.Name) == Normalize(SheetName)) ?? workbook.Worksheets.FirstOrDefault();
            var lastRow = sheet?.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = sheet?.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (sheet is null || lastRow == 0 || lastColumn == 0)
            {
                return (null, "El archivo está vacío.");
            }

            // Los encabezados se buscan en las primeras filas (por si la planilla trae un titulo arriba).
            var headerRow = 0;
            int codeCol = 0, nameCol = 0, parentCol = 0, typeCol = 0, externalCol = 0, categoryCol = 0, activeCol = 0;
            for (var row = 1; row <= Math.Min(10, lastRow) && headerRow == 0; row++)
            {
                int Find(string[] names)
                {
                    for (var column = 1; column <= lastColumn; column++)
                    {
                        if (names.Contains(Normalize(sheet.Cell(row, column).GetString())))
                        {
                            return column;
                        }
                    }

                    return 0;
                }

                var code = Find(CodeHeaders);
                var name = Find(NameHeaders);
                if (code > 0 && name > 0 && code != name)
                {
                    headerRow = row;
                    codeCol = code;
                    nameCol = name;
                    parentCol = Find(ParentHeaders);
                    typeCol = Find(TypeHeaders);
                    externalCol = Find(ExternalHeaders);
                    categoryCol = Find(CategoryHeaders);
                    activeCol = Find(ActiveHeaders);
                }
            }

            if (headerRow == 0)
            {
                return (null, "No se encontraron las columnas obligatorias: Código y Nombre. Descargá la plantilla para ver el formato.");
            }

            var rows = new List<PlanRowInput>();
            for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
            {
                string Text(int column) => column == 0 ? string.Empty : sheet.Cell(rowNumber, column).GetFormattedString().Trim();

                var codeText = Text(codeCol);
                var nameText = Text(nameCol);
                if (codeText.Length == 0 && nameText.Length == 0 && Text(parentCol).Length == 0)
                {
                    continue;
                }

                if (rows.Count >= MaxRows)
                {
                    return (null, $"El archivo tiene más de {MaxRows} cuentas.");
                }

                var input = new PlanRowInput { RowNumber = rowNumber, Code = codeText, Name = nameText };
                input.ParentCode = Text(parentCol) is { Length: > 0 } parent ? parent : null;
                input.ExternalCode = Text(externalCol) is { Length: > 0 } external ? external : null;

                var typeText = Text(typeCol);
                if (typeText.Length > 0)
                {
                    var type = FinancePlanValidator.ParseType(typeText);
                    if (type.HasValue) input.Type = type;
                    else input.ParseErrors.Add($"Tipo desconocido: «{typeText}». Usá Activo, Pasivo, Patrimonio, Ingreso o Egreso.");
                }

                var activeText = Normalize(Text(activeCol));
                input.IsActive = activeText is not ("no" or "n" or "inactivo" or "inactiva" or "0" or "false" or "falso");

                var categoryText = Normalize(Text(categoryCol));
                if (categoryText.Length > 0)
                {
                    var matched = false;
                    if (ExpenseLookup.TryGetValue(categoryText, out var expense))
                    {
                        input.ExpenseCategory = expense;
                        matched = true;
                    }

                    if (IncomeLookup.TryGetValue(categoryText, out var income))
                    {
                        input.IncomeCategory = income;
                        matched = true;
                    }

                    if (!matched)
                    {
                        input.ParseWarnings.Add($"Categoría de liquidación «{Text(categoryCol)}» no reconocida: se sugirió una por el nombre.");
                    }
                }

                rows.Add(input);
            }

            return rows.Count == 0 ? (null, "El archivo no tiene cuentas.") : (rows, null);
        }
    }

    // ── Plantilla ─────────────────────────────────────────────────────────────

    /// <summary>Plantilla con las columnas, instrucciones y unas lineas de ejemplo (las primeras del plan generico).</summary>
    public static byte[] BuildTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);

        string[] headers = ["Código", "Nombre", "Código padre", "Tipo", "Código del contador", "Categoría en la liquidación", "Activo"];
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold();
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#1385B6"));
            cell.Style.Font.SetFontColor(XLColor.White);
        }

        // Los codigos se guardan como texto: si no, Excel convierte 5.10 en 5,1.
        sheet.Column(1).Style.NumberFormat.Format = "@";
        sheet.Column(3).Style.NumberFormat.Format = "@";
        sheet.Column(5).Style.NumberFormat.Format = "@";

        var example = FinanceChartTemplate.Specs
            .Where(s => s.Code is "4" or "4.1" or "4.1.01" or "4.3" or "4.3.01" or "5" or "5.02" or "5.02.01" or "5.02.02" or "5.04" or "5.04.02")
            .ToList();
        var row = 2;
        foreach (var s in example)
        {
            sheet.Cell(row, 1).Value = s.Code;
            sheet.Cell(row, 2).Value = s.Name;
            sheet.Cell(row, 3).Value = s.ParentCode ?? string.Empty;
            sheet.Cell(row, 4).Value = s.ParentCode is null ? FinancePlanValidator.TypeLabel(s.Type) : string.Empty;
            sheet.Cell(row, 6).Value = s.ExpenseCategory is { } e ? CategoryLabels.ExpenseLabel(e)
                : s.IncomeCategory is { } i && s.SystemKey is null ? CategoryLabels.IncomeLabel(i) : string.Empty;
            sheet.Cell(row, 7).Value = "Sí";
            row++;
        }

        sheet.Column(1).Width = 14;
        sheet.Column(2).Width = 48;
        sheet.Column(3).Width = 16;
        sheet.Column(4).Width = 14;
        sheet.Column(5).Width = 22;
        sheet.Column(6).Width = 30;
        sheet.Column(7).Width = 8;
        sheet.SheetView.FreezeRows(1);

        var help = workbook.Worksheets.Add("Instrucciones");
        string[] lines =
        [
            "Cómo completar el plan de cuentas del cliente",
            "",
            "• Una fila por cuenta. Obligatorios: Código y Nombre. Las demás columnas son opcionales.",
            "• Código padre: el código del grupo al que pertenece. Si lo dejás vacío se deduce por el código (5.02.01 queda dentro de 5.02, y este dentro de 5).",
            "• Tipo: solo hace falta en las cuentas de primer nivel (Activo, Pasivo, Patrimonio, Ingreso o Egreso). Las demás heredan el de su grupo. Si lo dejás vacío se deduce del primer número del código (1 Activo, 2 Pasivo, 3 Patrimonio, 4 Ingresos, 5 Egresos).",
            "• Código del contador: opcional, para exportar con el código que usa el contador del cliente.",
            "• Categoría en la liquidación: solo en las cuentas finales de ingresos y egresos. Indica en qué línea de la liquidación cuentan los gastos o ingresos cargados en esa cuenta (ver la hoja Categorías). Si la dejás vacía, el sistema sugiere una por el nombre y la podés corregir en la vista previa.",
            "• Activo: Sí o No (vacío = Sí). Un grupo queda activo si alguna de sus cuentas lo está.",
            "• Se admiten hasta 6 niveles de profundidad. Solo las cuentas finales (sin subcuentas) de ingresos y egresos reciben gastos e ingresos; el resto del plan es de referencia y se exporta al contador.",
            "• Escribí los códigos como texto (la plantilla ya lo hace): Excel convierte 5.10 en 5,1 si la celda es numérica."
        ];
        for (var i = 0; i < lines.Length; i++)
        {
            help.Cell(i + 1, 1).Value = lines[i];
            help.Cell(i + 1, 1).Style.Alignment.WrapText = true;
        }

        help.Cell(1, 1).Style.Font.SetBold();
        help.Column(1).Width = 130;

        var categories = workbook.Worksheets.Add("Categorías");
        categories.Cell(1, 1).Value = "Categorías de gastos";
        categories.Cell(1, 2).Value = "Categorías de ingresos";
        categories.Cell(1, 3).Value = "Tipos";
        categories.Range(1, 1, 1, 3).Style.Font.SetBold();
        var r = 2;
        foreach (var c in Enum.GetValues<BuildingExpenseCategory>().Where(c => c != BuildingExpenseCategory.ReserveFund))
        {
            categories.Cell(r++, 1).Value = CategoryLabels.ExpenseLabel(c);
        }

        r = 2;
        foreach (var c in Enum.GetValues<BuildingIncomeCategory>().Where(c => c is not (BuildingIncomeCategory.AccumulatedBalance or BuildingIncomeCategory.OperationalFund)))
        {
            categories.Cell(r++, 2).Value = CategoryLabels.IncomeLabel(c);
        }

        r = 2;
        foreach (var t in new[] { LedgerCategoryType.Asset, LedgerCategoryType.Liability, LedgerCategoryType.Fund, LedgerCategoryType.Income, LedgerCategoryType.Expense })
        {
            categories.Cell(r++, 3).Value = FinancePlanValidator.TypeLabel(t);
        }

        categories.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ── Reconocimiento de categorias ──────────────────────────────────────────

    private static Dictionary<string, BuildingExpenseCategory> BuildExpenseLookup()
    {
        var lookup = new Dictionary<string, BuildingExpenseCategory>();
        foreach (var c in Enum.GetValues<BuildingExpenseCategory>())
        {
            lookup[Normalize(CategoryLabels.ExpenseLabel(c))] = c;
            lookup[Normalize(c.ToString())] = c;
        }

        lookup["servicios publicos"] = BuildingExpenseCategory.Utilities;
        lookup["salarios"] = BuildingExpenseCategory.Payroll;
        lookup["sueldos"] = BuildingExpenseCategory.Payroll;
        lookup["personal"] = BuildingExpenseCategory.Payroll;
        lookup["seguros"] = BuildingExpenseCategory.Insurance;
        lookup["internet"] = BuildingExpenseCategory.InternetPhone;
        lookup["telefono"] = BuildingExpenseCategory.InternetPhone;
        lookup["otros"] = BuildingExpenseCategory.Other;
        lookup["otros gastos"] = BuildingExpenseCategory.Other;
        lookup.Remove(Normalize(BuildingExpenseCategory.ReserveFund.ToString()));
        lookup.Remove(Normalize(CategoryLabels.ExpenseLabel(BuildingExpenseCategory.ReserveFund)));
        return lookup;
    }

    private static Dictionary<string, BuildingIncomeCategory> BuildIncomeLookup()
    {
        var lookup = new Dictionary<string, BuildingIncomeCategory>();
        foreach (var c in Enum.GetValues<BuildingIncomeCategory>().Where(c => c is not (BuildingIncomeCategory.AccumulatedBalance or BuildingIncomeCategory.OperationalFund)))
        {
            lookup[Normalize(CategoryLabels.IncomeLabel(c))] = c;
            lookup[Normalize(c.ToString())] = c;
        }

        lookup["alquileres"] = BuildingIncomeCategory.CommonAreaRental;
        lookup["alquiler de areas comunes"] = BuildingIncomeCategory.CommonAreaRental;
        lookup["alquiler de area comun"] = BuildingIncomeCategory.CommonAreaRental;   // el texto que muestra la web
        lookup["intereses"] = BuildingIncomeCategory.Interest;
        lookup["otros"] = BuildingIncomeCategory.Other;
        lookup["otros ingresos"] = BuildingIncomeCategory.Other;
        return lookup;
    }
}
