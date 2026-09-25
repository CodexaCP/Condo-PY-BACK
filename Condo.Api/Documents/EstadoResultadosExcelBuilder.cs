using ClosedXML.Excel;
using Condo.Application.Models;

namespace Condo.Api.Documents;

public static class EstadoResultadosExcelBuilder
{
    private const string CurrencyFormat = "#,##0 \"Gs.\"";

    public static byte[] Build(EstadoResultadosReportDto report)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Estado de resultados");

        sheet.Cell(1, 1).Value = "CONDOPY - Estado de resultados";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);

        sheet.Cell(2, 1).Value = report.BuildingName;
        sheet.Cell(2, 1).Style.Font.SetBold();

        sheet.Cell(3, 1).Value = $"Periodo: {report.FromDate:dd/MM/yyyy} - {report.ToDate:dd/MM/yyyy}";
        sheet.Cell(4, 1).Value = $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";

        var row = 6;
        row = WriteSection(sheet, row, "INGRESOS", report.IncomeLines, report.TotalIncome);
        row += 1;
        row = WriteSection(sheet, row, "GASTOS", report.ExpenseLines, report.TotalExpense);

        row += 2;
        sheet.Cell(row, 1).Value = report.NetResult >= 0 ? "SUPERAVIT DEL PERIODO" : "DEFICIT DEL PERIODO";
        sheet.Cell(row, 1).Style.Font.SetBold();
        sheet.Cell(row, 2).Value = report.NetResult;
        sheet.Cell(row, 2).Style.NumberFormat.Format = CurrencyFormat;
        sheet.Cell(row, 2).Style.Font.SetBold();

        sheet.Columns(1, 2).AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static int WriteSection(IXLWorksheet sheet, int startRow, string title, IReadOnlyList<EstadoResultadosLineDto> lines, decimal total)
    {
        var row = startRow;

        var titleCell = sheet.Cell(row, 1);
        titleCell.Value = title;
        titleCell.Style.Font.SetBold();
        titleCell.Style.Font.FontColor = XLColor.White;
        titleCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1385B6");
        sheet.Cell(row, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#1385B6");
        row++;

        foreach (var line in lines)
        {
            sheet.Cell(row, 1).Value = line.Label;
            var amountCell = sheet.Cell(row, 2);
            amountCell.Value = line.Amount;
            amountCell.Style.NumberFormat.Format = CurrencyFormat;
            row++;
        }

        if (lines.Count == 0)
        {
            sheet.Cell(row, 1).Value = "Sin movimientos en el periodo.";
            sheet.Cell(row, 1).Style.Font.SetItalic();
            row++;
        }

        sheet.Cell(row, 1).Value = $"Total {title.ToLowerInvariant()}";
        sheet.Cell(row, 1).Style.Font.SetBold();
        var totalCell = sheet.Cell(row, 2);
        totalCell.Value = total;
        totalCell.Style.NumberFormat.Format = CurrencyFormat;
        totalCell.Style.Font.SetBold();
        row++;

        return row;
    }
}
