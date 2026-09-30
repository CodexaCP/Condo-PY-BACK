using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Condo.Domain.Enums;

namespace Condo.Api.Services;

/// <summary>
/// Lee el Excel de carga masiva de gastos del edificio: cuatro columnas obligatorias (Categoria, Proveedor,
/// Descripcion y Monto), una fila por gasto. Solo interpreta el archivo; las validaciones contra el periodo las
/// hace el controlador.
/// </summary>
public static class BuildingExpenseImportParser
{
    public const int MaxRows = 500;

    public sealed record ParsedRow(int RowNumber, string Category, string Supplier, string Description, decimal? Amount, string? AmountError);

    private static readonly string[] RequiredHeaders = ["categoria", "proveedor", "descripcion", "monto"];

    private static readonly Dictionary<string, BuildingExpenseCategory> CategoryLookup = BuildCategoryLookup();

    public static (List<ParsedRow>? Rows, string? Error) Parse(Stream stream)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception)
        {
            return (null, "El archivo no es un Excel (.xlsx) valido.");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault();
            var lastRow = sheet?.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = sheet?.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (sheet is null || lastRow == 0 || lastColumn == 0)
            {
                return (null, "El archivo esta vacio.");
            }

            // Los encabezados se buscan en las primeras filas (por si la planilla trae un titulo arriba).
            var headerRow = 0;
            var columns = new Dictionary<string, int>();
            for (var row = 1; row <= Math.Min(10, lastRow) && headerRow == 0; row++)
            {
                var found = new Dictionary<string, int>();
                for (var column = 1; column <= lastColumn; column++)
                {
                    var header = Normalize(sheet.Cell(row, column).GetString());
                    if (RequiredHeaders.Contains(header) && !found.ContainsKey(header)) found[header] = column;
                }

                if (found.Count == RequiredHeaders.Length)
                {
                    headerRow = row;
                    columns = found;
                }
            }

            if (headerRow == 0)
            {
                return (null, "No se encontraron las columnas obligatorias: Categoria, Proveedor, Descripcion y Monto.");
            }

            var rows = new List<ParsedRow>();
            for (var row = headerRow + 1; row <= lastRow; row++)
            {
                var category = sheet.Cell(row, columns["categoria"]).GetString().Trim();
                var supplier = sheet.Cell(row, columns["proveedor"]).GetString().Trim();
                var description = sheet.Cell(row, columns["descripcion"]).GetString().Trim();
                var amountCell = sheet.Cell(row, columns["monto"]);

                if (category.Length == 0 && supplier.Length == 0 && description.Length == 0 && amountCell.IsEmpty()) continue;

                if (rows.Count >= MaxRows)
                {
                    return (null, $"El archivo tiene mas de {MaxRows} filas. Dividilo en varios archivos.");
                }

                var (amount, amountError) = ReadAmount(amountCell);
                rows.Add(new ParsedRow(row, category, supplier, description, amount, amountError));
            }

            if (rows.Count == 0)
            {
                return (null, "El archivo no tiene filas de gastos debajo de los encabezados.");
            }

            return (rows, null);
        }
    }

    public static bool TryResolveCategory(string text, out BuildingExpenseCategory category) =>
        CategoryLookup.TryGetValue(Normalize(text), out category);

    private static (decimal? Amount, string? Error) ReadAmount(IXLCell cell)
    {
        if (cell.IsEmpty()) return (null, "El monto esta vacio.");

        if (cell.DataType == XLDataType.Number)
        {
            return (decimal.Round((decimal)cell.GetDouble(), 2), null);
        }

        var text = cell.GetString().Trim();
        text = Regex.Replace(text, @"(?i)gs\.?|₲|\s| ", string.Empty);

        // "3.155.000" o "3,155,000": separadores de miles; "1.234,50": punto de miles y coma decimal.
        if (Regex.IsMatch(text, @"^\d{1,3}([.,]\d{3})+$")) text = text.Replace(".", string.Empty).Replace(",", string.Empty);
        else if (text.Contains(',') && text.Contains('.')) text = text.Replace(".", string.Empty).Replace(",", ".");
        else text = text.Replace(",", ".");

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? (decimal.Round(value, 2), null)
            : (null, "El monto no es un numero valido.");
    }

    // Sin mayusculas, tildes ni espacios repetidos: "Administración" == "administracion".
    private static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) builder.Append(character);
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ");
    }

    // Nombre en espanol (el mismo de los reportes), nombre interno y algunos sinonimos usuales.
    private static Dictionary<string, BuildingExpenseCategory> BuildCategoryLookup()
    {
        var lookup = new Dictionary<string, BuildingExpenseCategory>();
        foreach (var category in Enum.GetValues<BuildingExpenseCategory>())
        {
            lookup[Normalize(CategoryLabels.ExpenseLabel(category))] = category;
            lookup[Normalize(category.ToString())] = category;
        }

        lookup["otros"] = BuildingExpenseCategory.Other;
        lookup["otros gastos"] = BuildingExpenseCategory.Other;
        lookup["sueldos"] = BuildingExpenseCategory.Payroll;
        lookup["reserva"] = BuildingExpenseCategory.ReserveFund;
        lookup["extraordinarios"] = BuildingExpenseCategory.Extraordinary;
        lookup["internet"] = BuildingExpenseCategory.InternetPhone;
        lookup["telefonia"] = BuildingExpenseCategory.InternetPhone;
        lookup["internet y telefonia"] = BuildingExpenseCategory.InternetPhone;
        lookup["essap s.a."] = BuildingExpenseCategory.Essap;
        return lookup;
    }
}
