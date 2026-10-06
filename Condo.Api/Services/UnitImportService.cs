using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace Condo.Api.Services;

// Carga masiva de unidades: plantilla de Excel (una columna por dato de la unidad, el edificio como lista desplegable) y su lectura
// y validacion. La logica es pura (sin base de datos) para poder probarla; el controlador arma la lista de edificios y guarda.
public static partial class UnitImportService
{
    public const string SheetName = "Unidades";
    public const string BuildingsSheetName = "Edificios";
    public const string MetaSheetName = "_condopy";
    public const string TemplateMarker = "condopy-unidades-v1";
    public const int HeaderRow = 4;
    public const int FirstDataRow = HeaderRow + 1;
    public const int MaxRows = 2000;

    public static readonly string[] Headers = ["Edificio", "Codigo", "Piso", "Coeficiente", "Activa"];

    // Edificio que se puede elegir en la plantilla. Label es lo que muestra la lista desplegable: unico porque el codigo del
    // edificio no se repite dentro de la empresa.
    public sealed record BuildingRef(Guid Id, Guid CompanyId, string Name, string Code, string CompanyName)
    {
        public string Label => $"{Name} ({Code}) · {CompanyName}";
    }

    public sealed record ParsedRow(int RowNumber, string Building, string Code, string Floor, string Coefficient, string Active);

    public sealed record ValidatedRow(
        ParsedRow Row, BuildingRef? Building, string Code, string Floor, decimal Coefficient, bool IsActive, string? Error);

    // ─── Plantilla ───────────────────────────────────────────────────────────

    public static byte[] BuildTemplate(IReadOnlyList<BuildingRef> buildings)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);

        sheet.Cell(1, 1).Value = "Carga de unidades — CONDOPY";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(12);
        sheet.Cell(2, 1).Value =
            "Completá una fila por unidad desde la fila 5. No cambies los encabezados ni las hojas. Edificio: elegilo de la lista. " +
            "Codigo y Piso: texto (ej. 101, A-01 / 1, PB). Coeficiente: de 0 a 1 (ej. 0.007000); vacío = 0. Activa: Si o No (vacío = Si).";
        sheet.Cell(2, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
        sheet.Cell(3, 1).Value = "El archivo se valida antes de guardar: si alguna fila tiene errores no se crea ninguna unidad.";
        sheet.Cell(3, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);

        for (var i = 0; i < Headers.Length; i++)
        {
            var cell = sheet.Cell(HeaderRow, i + 1);
            cell.Value = Headers[i];
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#e8f4f8"));
        }

        var lastRow = HeaderRow + MaxRows;
        // Codigo y piso como texto: Excel no convierte "101" ni "01" en numeros.
        sheet.Range(FirstDataRow, 2, lastRow, 3).Style.NumberFormat.Format = "@";
        sheet.Range(FirstDataRow, 4, lastRow, 4).Style.NumberFormat.Format = "0.000000";
        sheet.Column(1).Width = 46;
        sheet.Column(2).Width = 16;
        sheet.Column(3).Width = 12;
        sheet.Column(4).Width = 16;
        sheet.Column(5).Width = 12;

        // Hoja con los edificios disponibles (es la fuente de la lista desplegable).
        var buildingSheet = workbook.Worksheets.Add(BuildingsSheetName);
        string[] buildingHeaders = ["Edificio (el valor que se elige en la lista)", "Empresa", "Codigo"];
        for (var i = 0; i < buildingHeaders.Length; i++)
        {
            buildingSheet.Cell(1, i + 1).Value = buildingHeaders[i];
            buildingSheet.Cell(1, i + 1).Style.Font.SetBold();
        }

        var row = 2;
        foreach (var b in buildings.OrderBy(x => x.CompanyName).ThenBy(x => x.Name))
        {
            buildingSheet.Cell(row, 1).Value = b.Label;
            buildingSheet.Cell(row, 2).Value = b.CompanyName;
            buildingSheet.Cell(row, 3).Value = b.Code;
            row++;
        }

        buildingSheet.Columns(1, 3).AdjustToContents();

        if (row > 2)
        {
            var building = sheet.Range(FirstDataRow, 1, lastRow, 1).CreateDataValidation();
            building.List(buildingSheet.Range(2, 1, row - 1, 1), true);
            building.ErrorStyle = XLErrorStyle.Stop;
            building.ErrorTitle = "Edificio";
            building.ErrorMessage = "Elegí un edificio de la lista (hoja Edificios).";
            building.IgnoreBlanks = true;
        }

        var coefficient = sheet.Range(FirstDataRow, 4, lastRow, 4).CreateDataValidation();
        coefficient.Decimal.Between(0, 1);
        coefficient.ErrorStyle = XLErrorStyle.Stop;
        coefficient.ErrorTitle = "Coeficiente";
        coefficient.ErrorMessage = "El coeficiente va de 0 a 1 (ej. 0.007000).";
        coefficient.IgnoreBlanks = true;

        var active = sheet.Range(FirstDataRow, 5, lastRow, 5).CreateDataValidation();
        active.List("\"Si,No\"", true);
        active.ErrorStyle = XLErrorStyle.Stop;
        active.ErrorTitle = "Activa";
        active.ErrorMessage = "Elegí Si o No.";
        active.IgnoreBlanks = true;

        sheet.SheetView.FreezeRows(HeaderRow);

        var meta = workbook.Worksheets.Add(MetaSheetName);
        meta.Cell(1, 1).Value = TemplateMarker;
        meta.Visibility = XLWorksheetVisibility.VeryHidden;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ─── Lectura ─────────────────────────────────────────────────────────────

    public static (List<ParsedRow>? Rows, string? Error) Parse(Stream stream)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception)
        {
            return (null, "No se pudo leer el archivo: tiene que ser un Excel (.xlsx) generado con la plantilla.");
        }

        using (workbook)
        {
            var marker = workbook.Worksheets.FirstOrDefault(w => w.Name == MetaSheetName)?.Cell(1, 1).GetString();
            var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name == SheetName);
            if (sheet is null || marker != TemplateMarker)
                return (null, "El archivo no es la plantilla de unidades. Descargá la plantilla desde esta pantalla y completala sin cambiar sus hojas.");

            for (var i = 0; i < Headers.Length; i++)
            {
                if (!string.Equals(Normalize(sheet.Cell(HeaderRow, i + 1).GetString()), Normalize(Headers[i]), StringComparison.Ordinal))
                    return (null, $"Los encabezados de la plantilla fueron modificados (columna {i + 1}: debe decir \"{Headers[i]}\").");
            }

            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? HeaderRow;
            if (lastRow - HeaderRow > MaxRows)
                return (null, $"El archivo tiene más de {MaxRows} filas. Dividilo en varios archivos.");

            var rows = new List<ParsedRow>();
            for (var r = FirstDataRow; r <= lastRow; r++)
            {
                var building = CellText(sheet.Cell(r, 1));
                var code = CellText(sheet.Cell(r, 2));
                var floor = CellText(sheet.Cell(r, 3));
                var coefficient = CellText(sheet.Cell(r, 4));
                var active = CellText(sheet.Cell(r, 5));

                // Las filas totalmente vacias (formato sin datos) se ignoran.
                if (building.Length == 0 && code.Length == 0 && floor.Length == 0 && coefficient.Length == 0 && active.Length == 0) continue;

                rows.Add(new ParsedRow(r, building, code, floor, coefficient, active));
            }

            return (rows, null);
        }
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return string.Empty;
        return cell.DataType switch
        {
            XLDataType.Number => cell.GetDouble().ToString("0.############", CultureInfo.InvariantCulture),
            XLDataType.Boolean => cell.GetBoolean() ? "true" : "false",
            _ => cell.GetString().Trim()
        };
    }

    // ─── Validacion ──────────────────────────────────────────────────────────

    // existingCodes: unidades que ya estan cargadas (edificio + codigo en mayusculas).
    public static List<ValidatedRow> Validate(
        IReadOnlyList<ParsedRow> rows, IReadOnlyDictionary<string, BuildingRef> buildingsByLabel, ISet<(Guid BuildingId, string Code)> existingCodes)
    {
        var result = new List<ValidatedRow>();
        var firstRowOfCode = new Dictionary<(Guid, string), int>();

        foreach (var row in rows)
        {
            BuildingRef? building = null;
            var code = row.Code.Trim().ToUpperInvariant();
            var floor = row.Floor.Trim();
            decimal coefficient = 0m;
            var isActive = true;
            string? error = null;

            if (row.Building.Length == 0)
                error = "Elegí el edificio de la lista.";
            else if (!buildingsByLabel.TryGetValue(NormalizeLabel(row.Building), out building))
                error = "El edificio no está en la lista: elegilo del desplegable (hoja Edificios).";

            if (error is null)
            {
                if (code.Length == 0) error = "El código de la unidad es obligatorio.";
                else if (code.Length > 20) error = "El código no puede superar los 20 caracteres.";
                else if (!CodeRegex().IsMatch(code)) error = "El código solo puede contener letras, números y guiones medios.";
            }

            if (error is null)
            {
                if (floor.Length == 0) error = "El piso es obligatorio.";
                else if (floor.Length > 20) error = "El piso no puede superar los 20 caracteres.";
            }

            if (error is null)
            {
                var (value, coefficientError) = ParseCoefficient(row.Coefficient);
                coefficient = value;
                error = coefficientError;
            }

            if (error is null)
            {
                var (active, activeError) = ParseActive(row.Active);
                isActive = active;
                error = activeError;
            }

            if (error is null)
            {
                var key = (building!.Id, code);
                if (existingCodes.Contains(key))
                    error = "Ya existe una unidad con ese código en este edificio.";
                else if (firstRowOfCode.TryGetValue(key, out var firstRow))
                    error = $"Código repetido en el archivo: ya está en la fila {firstRow}.";
                else
                    firstRowOfCode[key] = row.RowNumber;
            }

            result.Add(new ValidatedRow(row, building, code, floor, coefficient, isActive, error));
        }

        return result;
    }

    // Vacio = 0. Acepta coma o punto decimal y "0.7%" (= 0.007). De 0 a 1, con hasta 6 decimales.
    public static (decimal Value, string? Error) ParseCoefficient(string text)
    {
        var value = text.Trim();
        if (value.Length == 0) return (0m, null);

        var percent = value.EndsWith('%');
        if (percent) value = value[..^1].Trim();
        value = value.Replace(',', '.');

        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return (0m, "El coeficiente no es un número (ej. 0.007000).");

        if (percent) number /= 100m;
        if (number < 0m || number > 1m) return (0m, "El coeficiente va de 0 a 1 (ej. 0.007000).");

        return (decimal.Round(number, 6), null);
    }

    // Vacio = activa. Acepta Si/Sí/No, true/false, 1/0, activa/inactiva.
    public static (bool Value, string? Error) ParseActive(string text)
    {
        switch (Normalize(text))
        {
            case "":
            case "si":
            case "true":
            case "1":
            case "activa":
            case "activo":
                return (true, null);
            case "no":
            case "false":
            case "0":
            case "inactiva":
            case "inactivo":
                return (false, null);
            default:
                return (true, "Activa debe ser Si o No.");
        }
    }

    public static string NormalizeLabel(string label) => WhitespaceRegex().Replace(label.Trim(), " ").ToUpperInvariant();

    // Sin tildes ni mayusculas, para comparar encabezados y valores.
    private static string Normalize(string value) =>
        new string(value.Trim().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();

    [GeneratedRegex("^[A-Z0-9]+(?:-[A-Z0-9]+)*$")]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
