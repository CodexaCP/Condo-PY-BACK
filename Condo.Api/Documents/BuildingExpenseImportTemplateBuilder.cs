using ClosedXML.Excel;
using Condo.Api.Services;
using Condo.Domain.Enums;

namespace Condo.Api.Documents;

// Plantilla de la carga masiva de gastos: la hoja "Gastos" con los encabezados y ejemplos, y la hoja
// "Categorias" con los nombres validos.
public static class BuildingExpenseImportTemplateBuilder
{
    public static byte[] Build()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Gastos");

        string[] headers = ["Categoria", "Proveedor", "Descripcion", "Monto"];
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#e8f4f8"));
        }

        object[][] examples =
        [
            ["ANDE", "ANDE", "CONSUMO CICLO 03/26 NIS 1369916", 3155000],
            ["ANDE", "ANDE", "CONSUMO CICLO 03/26 NIS 1369918", 392000],
            ["Limpieza", "TODO BRILLO S.A", "SERVICIO DE LIMPIEZA ABRIL/26", 4500000]
        ];
        for (var r = 0; r < examples.Length; r++)
        {
            for (var c = 0; c < examples[r].Length; c++) sheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(examples[r][c]);
            sheet.Cell(r + 2, 4).Style.NumberFormat.Format = "#,##0";
        }

        sheet.Columns(1, 4).AdjustToContents();

        var categories = workbook.Worksheets.Add("Categorias");
        categories.Cell(1, 1).Value = "Categorias validas";
        categories.Cell(1, 1).Style.Font.SetBold();
        var row = 2;
        foreach (var category in Enum.GetValues<BuildingExpenseCategory>())
        {
            categories.Cell(row++, 1).Value = CategoryLabels.ExpenseLabel(category);
        }

        categories.Column(1).AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
