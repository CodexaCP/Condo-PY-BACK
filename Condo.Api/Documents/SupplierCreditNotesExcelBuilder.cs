using ClosedXML.Excel;
using Condo.Application.Models;
using Condo.Domain.Enums;

namespace Condo.Api.Documents;

// Excel de las notas de credito que los proveedores emitieron sobre gastos del edificio: para el contador y como anexo de la liquidacion.
public static class SupplierCreditNotesExcelBuilder
{
    private const string CurrencyFormat = "#,##0 \"Gs.\"";

    // baseUrl: origen de la API ("https://api...") para que el enlace al documento del proveedor se abra desde el Excel.
    public static byte[] Build(string buildingName, string? periodName, IReadOnlyList<PeriodSupplierCreditNoteDto> rows, string baseUrl)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("NC de proveedor");

        sheet.Cell(1, 1).Value = "CONDOPY - Notas de crédito de proveedor";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Cell(2, 1).Value = buildingName;
        sheet.Cell(2, 1).Style.Font.SetBold();
        sheet.Cell(3, 1).Value = string.IsNullOrWhiteSpace(periodName) ? "Todos los períodos" : $"Período: {periodName}";
        sheet.Cell(4, 1).Value = $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";

        var applied = rows.Where(r => r.Status == BuildingExpenseCreditNoteStatus.Applied).ToList();
        sheet.Cell(6, 1).Value = "Total aplicado (sin anuladas)";
        sheet.Cell(6, 2).Value = applied.Sum(r => r.Amount);
        sheet.Cell(6, 2).Style.NumberFormat.Format = CurrencyFormat;
        sheet.Range(6, 1, 6, 2).Style.Font.SetBold();

        var headerRow = 8;
        string[] headers =
        [
            "Fecha", "Período", "Proveedor", "Gasto", "Categoría", "Rubro", "Nº de nota", "Timbrado", "Monto", "Tratamiento",
            "Estado", "Motivo", "Documento", "Motivo de anulación"
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(headerRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold();
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1385B6");
        }

        var row = headerRow;
        foreach (var r in rows)
        {
            row++;
            sheet.Cell(row, 1).Value = r.IssueDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            sheet.Cell(row, 2).Value = r.ExpensePeriodName;
            sheet.Cell(row, 3).Value = r.SupplierName;
            sheet.Cell(row, 4).Value = r.ExpenseDescription;
            sheet.Cell(row, 5).Value = r.Category;
            sheet.Cell(row, 6).Value = r.Rubro ?? string.Empty;
            sheet.Cell(row, 7).Value = r.Numero;
            sheet.Cell(row, 8).Value = r.Timbrado ?? string.Empty;
            sheet.Cell(row, 9).Value = r.Amount;
            sheet.Cell(row, 9).Style.NumberFormat.Format = CurrencyFormat;
            sheet.Cell(row, 10).Value = r.Treatment;
            sheet.Cell(row, 11).Value = r.Status == BuildingExpenseCreditNoteStatus.Applied ? "Aplicada" : "Anulada";
            sheet.Cell(row, 12).Value = r.Reason;
            if (!string.IsNullOrWhiteSpace(r.DocumentUrl))
            {
                var link = r.DocumentUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? r.DocumentUrl : baseUrl.TrimEnd('/') + r.DocumentUrl;
                sheet.Cell(row, 13).Value = "Ver documento";
                if (Uri.TryCreate(link, UriKind.Absolute, out var uri))
                    sheet.Cell(row, 13).SetHyperlink(new XLHyperlink(uri));
            }
            sheet.Cell(row, 14).Value = r.VoidReason;

            if (r.Status == BuildingExpenseCreditNoteStatus.Voided)
                sheet.Range(row, 1, row, headers.Length).Style.Font.FontColor = XLColor.Gray;
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
