using ClosedXML.Excel;
using Condo.Application.Models;

namespace Condo.Api.Documents;

public static class LibroMovimientosExcelBuilder
{
    private const string CurrencyFormat = "#,##0 \"Gs.\"";

    public static byte[] Build(LibroMovimientosReportDto report)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Libro de movimientos");

        sheet.Cell(1, 1).Value = "CONDOPY - Libro de movimientos";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);

        sheet.Cell(2, 1).Value = report.BuildingName;
        sheet.Cell(2, 1).Style.Font.SetBold();

        sheet.Cell(3, 1).Value = $"Periodo: {report.FromDate:dd/MM/yyyy} - {report.ToDate:dd/MM/yyyy}";
        sheet.Cell(4, 1).Value = $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";

        sheet.Cell(6, 1).Value = "Saldo anterior";
        sheet.Cell(6, 2).Value = report.OpeningBalance;
        sheet.Cell(6, 2).Style.NumberFormat.Format = CurrencyFormat;

        sheet.Cell(7, 1).Value = "Total cobros/ingresos";
        sheet.Cell(7, 2).Value = report.TotalCredits;
        sheet.Cell(7, 2).Style.NumberFormat.Format = CurrencyFormat;

        sheet.Cell(8, 1).Value = "Total gastos";
        sheet.Cell(8, 2).Value = report.TotalDebits;
        sheet.Cell(8, 2).Style.NumberFormat.Format = CurrencyFormat;

        sheet.Cell(9, 1).Value = "Saldo final";
        sheet.Cell(9, 2).Value = report.ClosingBalance;
        sheet.Cell(9, 2).Style.NumberFormat.Format = CurrencyFormat;
        sheet.Range(9, 1, 9, 2).Style.Font.SetBold();

        var headerRow = 11;
        string[] headers = ["Fecha", "Tipo", "Descripcion", "Unidad", "Referencia", "Ingreso", "Gasto", "Saldo"];
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
            sheet.Cell(row, 1).Value = item.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            sheet.Cell(row, 2).Value = TypeLabel(item.Type);
            sheet.Cell(row, 3).Value = item.Description;
            sheet.Cell(row, 4).Value = item.UnitCode ?? string.Empty;
            sheet.Cell(row, 5).Value = item.Reference ?? string.Empty;

            var creditCell = sheet.Cell(row, 6);
            if (item.Credit > 0) creditCell.Value = item.Credit;
            creditCell.Style.NumberFormat.Format = CurrencyFormat;

            var debitCell = sheet.Cell(row, 7);
            if (item.Debit > 0) debitCell.Value = item.Debit;
            debitCell.Style.NumberFormat.Format = CurrencyFormat;

            var balanceCell = sheet.Cell(row, 8);
            balanceCell.Value = item.RunningBalance;
            balanceCell.Style.NumberFormat.Format = CurrencyFormat;
            balanceCell.Style.Font.SetBold();
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

    private static string TypeLabel(LibroMovimientoType type) => type switch
    {
        LibroMovimientoType.Cobro => "Cobro",
        LibroMovimientoType.IngresoEdificio => "Ingreso",
        LibroMovimientoType.GastoEdificio => "Gasto",
        _ => type.ToString()
    };
}
