using ClosedXML.Excel;
using Condo.Application.Models;

namespace Condo.Api.Documents;

public static class BuildingComparisonExcelBuilder
{
    private const string CurrencyFormat = "#,##0 \"Gs.\"";

    public static byte[] Build(BuildingComparisonReportDto report)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Comparativo de edificios");

        sheet.Cell(1, 1).Value = "CONDOPY - Comparativo de edificios";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);

        sheet.Cell(2, 1).Value = $"Periodo: {report.FromDate:dd/MM/yyyy} - {report.ToDate:dd/MM/yyyy} (morosidad a la fecha de hoy)";
        sheet.Cell(3, 1).Value = $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";

        var headerRow = 5;
        string[] headers = ["Edificio", "Cobrado", "Gastos", "Resultado", "Morosidad actual", "Unidades morosas"];
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(headerRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold();
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1385B6");
        }

        var row = headerRow;
        foreach (var item in report.Items)
        {
            row++;
            sheet.Cell(row, 1).Value = item.BuildingName;

            var collectedCell = sheet.Cell(row, 2);
            collectedCell.Value = item.TotalCollected;
            collectedCell.Style.NumberFormat.Format = CurrencyFormat;

            var expensesCell = sheet.Cell(row, 3);
            expensesCell.Value = item.TotalExpenses;
            expensesCell.Style.NumberFormat.Format = CurrencyFormat;

            var resultCell = sheet.Cell(row, 4);
            resultCell.Value = item.NetResult;
            resultCell.Style.NumberFormat.Format = CurrencyFormat;
            resultCell.Style.Font.SetBold();

            var overdueCell = sheet.Cell(row, 5);
            overdueCell.Value = item.OverdueAmount;
            overdueCell.Style.NumberFormat.Format = CurrencyFormat;
            if (item.OverdueAmount > 0) overdueCell.Style.Font.FontColor = XLColor.FromHtml("#c94d3f");

            sheet.Cell(row, 6).Value = item.UnitsWithOverdueBalance;
        }

        if (report.Items.Count == 0)
        {
            row++;
            sheet.Cell(row, 1).Value = "No hay edificios accesibles para este usuario.";
            sheet.Cell(row, 1).Style.Font.SetItalic();
        }

        var dataRange = sheet.Range(headerRow, 1, row, headers.Length);
        dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#d7e5ea");
        dataRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#d7e5ea");

        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns(1, headers.Length).AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
