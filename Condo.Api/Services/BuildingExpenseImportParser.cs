using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Condo.Api.Documents;
using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Api.Services;

/// <summary>
/// Lee el Excel de carga masiva de gastos del edificio: columnas obligatorias Proveedor, Descripcion y Monto, mas la Categoria o
/// el Rubro (con Finanzas del edificio la plantilla trae las dos y alcanza con una), una fila por gasto. Solo interpreta el
/// archivo; las validaciones contra el periodo y los rubros las hace el controlador.
/// </summary>
public static class BuildingExpenseImportParser
{
    public const int MaxRows = 500;

    public sealed record ParsedRow(
        int RowNumber, string Category, string Rubro, string Supplier, string Description, decimal? Amount, string? AmountError, bool PaidByReserveFund);

    private static readonly string[] RequiredHeaders = ["proveedor", "descripcion", "monto"];

    private const string CategoryHeader = "categoria";

    // Columna opcional (plantillas de edificios con Finanzas): rubro del plan de cuentas, por codigo o por nombre.
    private const string RubroHeader = "rubro";

    // Columna opcional: si la fila trae monto aca, el gasto se crea marcado como pagado por el fondo de reserva.
    private const string ReserveFundHeader = "monto por fondo de reserva";

    private static readonly Dictionary<string, BuildingExpenseCategory> CategoryLookup = BuildCategoryLookup();

    public static (List<ParsedRow>? Rows, string? Token, string? Error) Parse(Stream stream)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception)
        {
            return (null, null, "El archivo no es un Excel (.xlsx) valido.");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault();
            var lastRow = sheet?.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = sheet?.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (sheet is null || lastRow == 0 || lastColumn == 0)
            {
                return (null, null, "El archivo esta vacio.");
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
                    if ((RequiredHeaders.Contains(header) || header is CategoryHeader or RubroHeader or ReserveFundHeader) && !found.ContainsKey(header)) found[header] = column;
                }

                if (RequiredHeaders.All(found.ContainsKey) && (found.ContainsKey(CategoryHeader) || found.ContainsKey(RubroHeader)))
                {
                    headerRow = row;
                    columns = found;
                }
            }

            if (headerRow == 0)
            {
                return (null, null, "No se encontraron las columnas obligatorias: Categoria, Proveedor, Descripcion y Monto.");
            }

            var rows = new List<ParsedRow>();
            for (var row = headerRow + 1; row <= lastRow; row++)
            {
                var category = columns.TryGetValue(CategoryHeader, out var categoryColumn) ? sheet.Cell(row, categoryColumn).GetString().Trim() : string.Empty;
                var rubro = columns.TryGetValue(RubroHeader, out var rubroColumn) ? sheet.Cell(row, rubroColumn).GetString().Trim() : string.Empty;
                var supplier = sheet.Cell(row, columns["proveedor"]).GetString().Trim();
                var description = sheet.Cell(row, columns["descripcion"]).GetString().Trim();
                var amountCell = sheet.Cell(row, columns["monto"]);
                var fundCell = columns.TryGetValue(ReserveFundHeader, out var fundColumn) ? sheet.Cell(row, fundColumn) : null;
                var hasFundAmount = fundCell is not null && !fundCell.IsEmpty();

                if (category.Length == 0 && rubro.Length == 0 && supplier.Length == 0 && description.Length == 0 && amountCell.IsEmpty() && !hasFundAmount) continue;

                if (rows.Count >= MaxRows)
                {
                    return (null, null, $"El archivo tiene mas de {MaxRows} filas. Dividilo en varios archivos.");
                }

                // El monto va en una de las dos columnas: "Monto" (gasto comun) o "Monto por fondo de reserva".
                decimal? amount;
                string? amountError;
                if (hasFundAmount && !amountCell.IsEmpty())
                {
                    (amount, amountError) = (null, "Completa solo uno: Monto o Monto por fondo de reserva.");
                }
                else
                {
                    (amount, amountError) = ReadAmount(hasFundAmount ? fundCell! : amountCell);
                }

                rows.Add(new ParsedRow(row, category, rubro, supplier, description, amount, amountError, hasFundAmount));
            }

            if (rows.Count == 0)
            {
                return (null, null, "El archivo no tiene filas de gastos debajo de los encabezados.");
            }

            var token = workbook.Worksheets.TryGetWorksheet(BuildingExpenseImportTemplateBuilder.MetaSheetName, out var meta)
                ? meta.Cell(1, 1).GetString()
                : null;
            return (rows, token, null);
        }
    }

    public static bool TryResolveCategory(string text, out BuildingExpenseCategory category) =>
        CategoryLookup.TryGetValue(Normalize(text), out category);

    /// <summary>
    /// Rubro que nombra el texto de la celda entre los rubros de gastos del edificio: acepta lo que ofrece la lista desplegable
    /// ("5.2.03 Aguinaldo"), solo el codigo ("5.2.03") o solo el nombre ("Aguinaldo"). Si el nombre esta en dos rubros pide el codigo.
    /// </summary>
    public static (LedgerCategory? Rubro, string? Error) ResolveRubro(string text, IReadOnlyList<LedgerCategory> options)
    {
        var wanted = Normalize(text);

        var byLabel = options.Where(r => Normalize($"{r.Code} {r.Name}") == wanted).ToList();
        if (byLabel.Count == 1) return (byLabel[0], null);

        var byCode = options.Where(r => Normalize(r.Code) == wanted).ToList();
        if (byCode.Count == 1) return (byCode[0], null);

        var byName = options.Where(r => Normalize(r.Name) == wanted).ToList();
        if (byName.Count == 1) return (byName[0], null);
        if (byName.Count > 1) return (null, $"Hay mas de un rubro llamado \"{text}\": usa su codigo (hoja Rubros).");

        // Codigo seguido de otro texto ("5.2.03 - Aguinaldo", "5.2.03 Aguinaldo viejo"): vale el primer termino.
        var firstToken = wanted.Split(' ', 2)[0];
        var byLeadingCode = options.Where(r => Normalize(r.Code) == firstToken).ToList();
        if (byLeadingCode.Count == 1) return (byLeadingCode[0], null);

        return (null, $"Rubro desconocido: \"{text}\". Ver la hoja Rubros de la plantilla.");
    }

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
