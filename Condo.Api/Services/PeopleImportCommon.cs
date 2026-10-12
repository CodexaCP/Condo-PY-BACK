using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace Condo.Api.Services;

// Edificio y unidad que se pueden elegir en las plantillas de carga masiva de personas (propietarios y residentes).
// Label es lo que muestra la lista desplegable de la plantilla: unico porque el codigo del edificio no se repite dentro de la empresa.
public sealed record ImportBuildingRef(Guid Id, Guid CompanyId, string Name, string Code)
{
    public string Label => $"{Name} ({Code})";
}

public sealed record ImportUnitRef(Guid Id, Guid BuildingId, Guid CompanyId, string Code);

// Piezas comunes de las cargas masivas de personas: lectura de celdas, normalizacion de textos y valores opcionales de las columnas.
public static partial class PeopleImportCommon
{
    public const string BuildingsSheetName = "Edificios";
    public const string UnitsSheetName = "Unidades";
    public const string MetaSheetName = "_condopy";
    public const int HeaderRow = 4;
    public const int FirstDataRow = HeaderRow + 1;
    public const int MaxRows = 2000;
    public const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2 MB

    // Sin tildes ni mayusculas, para comparar encabezados y valores.
    public static string Normalize(string value) =>
        new string(value.Trim().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();

    public static string NormalizeLabel(string label) => WhitespaceRegex().Replace(label.Trim(), " ").ToUpperInvariant();

    // Documento para comparar: mayusculas y sin puntos ni espacios (1.234.567 = 1234567).
    public static string NormalizeDocument(string? value) =>
        new string((value ?? string.Empty).Where(c => c != '.' && !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    public static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return string.Empty;
        return cell.DataType switch
        {
            XLDataType.Number => cell.GetDouble().ToString("0.############", CultureInfo.InvariantCulture),
            XLDataType.Boolean => cell.GetBoolean() ? "true" : "false",
            XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => cell.GetString().Trim()
        };
    }

    // Abre la plantilla y la reconoce por su hoja y su marca oculta. Devuelve la hoja de datos con los encabezados ya comprobados.
    public static (XLWorkbook? Workbook, IXLWorksheet? Sheet, string? Error) Open(
        Stream stream, string sheetName, string marker, string templateLabel, string[] headers)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception)
        {
            return (null, null, "No se pudo leer el archivo: tiene que ser un Excel (.xlsx) generado con la plantilla.");
        }

        var foundMarker = workbook.Worksheets.FirstOrDefault(w => w.Name == MetaSheetName)?.Cell(1, 1).GetString();
        var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name == sheetName);
        if (sheet is null || foundMarker != marker)
        {
            workbook.Dispose();
            return (null, null, $"El archivo no es la plantilla de {templateLabel}. Descargá la plantilla desde esta pantalla y completala sin cambiar sus hojas.");
        }

        for (var i = 0; i < headers.Length; i++)
        {
            if (!string.Equals(Normalize(sheet.Cell(HeaderRow, i + 1).GetString()), Normalize(headers[i]), StringComparison.Ordinal))
            {
                workbook.Dispose();
                return (null, null, $"Los encabezados de la plantilla fueron modificados (columna {i + 1}: debe decir \"{headers[i]}\").");
            }
        }

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? HeaderRow;
        if (lastRow - HeaderRow > MaxRows)
        {
            workbook.Dispose();
            return (null, null, $"El archivo tiene más de {MaxRows} filas. Dividilo en varios archivos.");
        }

        return (workbook, sheet, null);
    }

    // ─── Valores opcionales de las columnas ───────────────────────────────────

    // Vacio = null (el que llama decide el valor por defecto). Acepta Si/Sí/No, true/false, 1/0.
    public static (bool? Value, string? Error) ParseYesNo(string text, string label)
    {
        switch (Normalize(text))
        {
            case "": return (null, null);
            case "si": case "true": case "1": case "x": return (true, null);
            case "no": case "false": case "0": return (false, null);
            default: return (null, $"{label} debe ser Si o No.");
        }
    }

    // Acepta aaaa-mm-dd (como lo deja Excel) y dd/mm/aaaa. Vacio = null.
    public static (DateOnly? Value, string? Error) ParseDate(string text, string label)
    {
        var value = text.Trim();
        if (value.Length == 0) return (null, null);

        string[] formats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy"];
        if (DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && date >= new DateOnly(1900, 1, 1))
            return (date, null);

        return (null, $"{label} no es una fecha válida (usá dd/mm/aaaa).");
    }

    // Porcentaje de 0 a 100 con hasta 2 decimales. Acepta coma o punto y el signo %. Vacio = null.
    public static (decimal? Value, string? Error) ParsePercentage(string text)
    {
        var value = text.Trim();
        if (value.Length == 0) return (null, null);

        if (value.EndsWith('%')) value = value[..^1].Trim();
        value = value.Replace(',', '.');

        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return (null, "El % de titularidad no es un número (ej. 50).");
        if (number <= 0m || number > 100m)
            return (null, "El % de titularidad debe ser mayor que 0 y hasta 100.");

        return (decimal.Round(number, 2), null);
    }

    // Tipo de documento: acepta el texto de la lista o el codigo interno. Vacio = null.
    public static (string? Value, string? Error) ParseDocumentType(string text)
    {
        switch (Normalize(text))
        {
            case "": return (null, null);
            case "cedula paraguaya": case "cedulaparaguaya": case "cedula": case "ci": return (PersonRules.Cedula, null);
            case "ruc": return (PersonRules.Ruc, null);
            case "pasaporte": return ("Pasaporte", null);
            case "documento extranjero": case "documentoextranjero": return ("DocumentoExtranjero", null);
            default: return (null, "El tipo de documento no es válido: elegilo de la lista (Cédula paraguaya, RUC, Pasaporte o Documento extranjero).");
        }
    }

    public const string DocumentTypeList = "\"Cédula paraguaya,RUC,Pasaporte,Documento extranjero\"";

    // ─── Plantilla: partes comunes ────────────────────────────────────────────

    public static void WriteTitle(IXLWorksheet sheet, string title, string instructions)
    {
        sheet.Cell(1, 1).Value = title;
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(12);
        sheet.Cell(2, 1).Value = instructions;
        sheet.Cell(2, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
        sheet.Cell(3, 1).Value = "El archivo se valida antes de guardar: si alguna fila tiene errores no se crea nada. Las columnas con * son obligatorias.";
        sheet.Cell(3, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
    }

    public static void WriteHeaders(IXLWorksheet sheet, string[] headers, int[] widths)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(HeaderRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#e8f4f8"));
            sheet.Column(i + 1).Width = widths[i];
        }

        sheet.SheetView.FreezeRows(HeaderRow);
    }

    // Hojas de referencia con los edificios y las unidades existentes; el edificio se elige de una lista desplegable.
    // Devuelve el rango de edificios (null si no hay) para armar la validacion de la columna.
    public static void AddReferenceSheets(
        XLWorkbook workbook, IXLWorksheet sheet, int buildingColumn, IReadOnlyList<ImportBuildingRef> buildings, IReadOnlyList<ImportUnitRef> units)
    {
        var buildingSheet = workbook.Worksheets.Add(BuildingsSheetName);
        buildingSheet.Cell(1, 1).Value = "Edificio (el valor que se elige en la lista)";
        buildingSheet.Cell(1, 2).Value = "Codigo";
        buildingSheet.Row(1).Style.Font.SetBold();

        var row = 2;
        foreach (var b in buildings.OrderBy(x => x.Name))
        {
            buildingSheet.Cell(row, 1).Value = b.Label;
            buildingSheet.Cell(row, 2).Value = b.Code;
            row++;
        }

        buildingSheet.Columns(1, 2).AdjustToContents();

        if (row > 2)
        {
            var validation = sheet.Range(FirstDataRow, buildingColumn, HeaderRow + MaxRows, buildingColumn).CreateDataValidation();
            validation.List(buildingSheet.Range(2, 1, row - 1, 1), true);
            validation.ErrorStyle = XLErrorStyle.Stop;
            validation.ErrorTitle = "Edificio";
            validation.ErrorMessage = "Elegí un edificio de la lista (hoja Edificios).";
            validation.IgnoreBlanks = true;
        }

        // Para consultar los codigos exactos de las unidades mientras se completa la plantilla.
        var unitSheet = workbook.Worksheets.Add(UnitsSheetName);
        unitSheet.Cell(1, 1).Value = "Edificio";
        unitSheet.Cell(1, 2).Value = "Codigo de la unidad";
        unitSheet.Row(1).Style.Font.SetBold();
        var buildingById = buildings.ToDictionary(b => b.Id);
        var unitRow = 2;
        foreach (var u in units.Where(u => buildingById.ContainsKey(u.BuildingId))
                     .OrderBy(u => buildingById[u.BuildingId].Name).ThenBy(u => u.Code))
        {
            unitSheet.Cell(unitRow, 1).Value = buildingById[u.BuildingId].Label;
            unitSheet.Cell(unitRow, 2).Value = u.Code;
            unitRow++;
        }

        unitSheet.Columns(1, 2).AdjustToContents();
    }

    public static void AddMarker(XLWorkbook workbook, string marker)
    {
        var meta = workbook.Worksheets.Add(MetaSheetName);
        meta.Cell(1, 1).Value = marker;
        meta.Visibility = XLWorksheetVisibility.VeryHidden;
    }

    public static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // Codigo de unidad como lo guarda el sistema: mayusculas y sin espacios sobrantes.
    public static string NormalizeUnitCode(string code) => code.Trim().ToUpperInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
