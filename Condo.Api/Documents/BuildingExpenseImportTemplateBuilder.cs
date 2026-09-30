using ClosedXML.Excel;
using Condo.Api.Services;
using Condo.Domain.Enums;

namespace Condo.Api.Documents;

// Plantilla de la carga masiva de gastos, generada para un edificio: la hoja "Gastos" con el edificio, la empresa y los
// encabezados, la hoja "Categorias" con los nombres validos y una hoja oculta con la marca cifrada que ata el archivo a
// ese edificio y empresa.
public static class BuildingExpenseImportTemplateBuilder
{
    // Hoja oculta (no se puede mostrar desde Excel) con la marca cifrada; la lee BuildingExpenseImportParser.
    public const string MetaSheetName = "_condopy";

    public static byte[] Build(string buildingName, string companyName, string token)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Gastos");

        sheet.Cell(1, 1).Value = $"Edificio: {buildingName}";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(12);
        sheet.Cell(2, 1).Value = $"Empresa: {companyName}";
        sheet.Cell(2, 1).Style.Font.SetBold();
        sheet.Cell(3, 1).Value = "Completá una fila por gasto desde la fila 5. Esta plantilla solo sirve para este edificio: no cambies los encabezados ni las hojas. Cada gasto lleva su monto en \"Monto\" o, si lo paga el fondo de reserva, en \"Monto por fondo de reserva\" (uno solo).";
        sheet.Cell(3, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);

        string[] headers = ["Categoria", "Proveedor", "Descripcion", "Monto", "Monto por fondo de reserva"];
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(4, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#e8f4f8"));
        }

        sheet.Columns(4, 5).Style.NumberFormat.Format = "#,##0";
        sheet.Column(1).Width = 24;
        sheet.Column(2).Width = 32;
        sheet.Column(3).Width = 56;
        sheet.Column(4).Width = 16;
        sheet.Column(5).Width = 28;

        var categories = workbook.Worksheets.Add("Categorias");
        categories.Cell(1, 1).Value = "Categorias validas";
        categories.Cell(1, 1).Style.Font.SetBold();
        var row = 2;
        foreach (var category in Enum.GetValues<BuildingExpenseCategory>())
        {
            categories.Cell(row++, 1).Value = CategoryLabels.ExpenseLabel(category);
        }

        categories.Column(1).AdjustToContents();

        var meta = workbook.Worksheets.Add(MetaSheetName);
        meta.Cell(1, 1).Value = token;
        meta.Visibility = XLWorksheetVisibility.VeryHidden;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
