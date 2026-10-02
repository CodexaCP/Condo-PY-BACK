using ClosedXML.Excel;
using Condo.Application.Models;

namespace Condo.Api.Documents;

/// <summary>
/// Excel del extracto de la cuenta aparte del marketplace: es el respaldo para repartir la comision de la gestion entre las partes.
/// Hoja "Resumen" con los totales del periodo y hoja "Movimientos" como tabla plana (una fila por movimiento, importes y fechas
/// como valores reales, sin celdas combinadas) para poder filtrar y sumar. Importes en guaranies; fechas en hora de Paraguay.
/// </summary>
public static class MarketplaceAccountExcelExporter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string AmountFormat = "#,##0;[Red]-#,##0";
    private const string DateTimeFormat = "dd/mm/yyyy hh:mm";

    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(-3);

    public static string KindLabel(string kind) => kind switch
    {
        "PaymentIn" => "Ingreso por reserva",
        "OwnerCredit" => "Acreditado al propietario",
        "RefundOut" => "Devolución al comprador",
        "Adjustment" => "Ajuste manual",
        "CancellationFee" => "Comisión por cancelación del propietario",
        _ => kind
    };

    public static string FileName(MarketplaceStatementDto statement)
    {
        var decomposed = statement.BuildingName.Normalize(System.Text.NormalizationForm.FormD);
        var chars = decomposed
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = System.Text.RegularExpressions.Regex.Replace(new string(chars), "-{2,}", "-").Trim('-');
        return $"marketplace-cuenta-{(slug.Length == 0 ? "edificio" : slug)}-{statement.FromDate:yyyyMMdd}-{statement.ToDate:yyyyMMdd}.xlsx";
    }

    public static byte[] Build(MarketplaceStatementDto statement, DateTime generatedAtUtc)
    {
        using var workbook = new XLWorkbook();
        AddSummary(workbook, statement, generatedAtUtc);
        AddMovements(workbook, statement);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void AddSummary(XLWorkbook wb, MarketplaceStatementDto statement, DateTime generatedAtUtc)
    {
        var ws = wb.Worksheets.Add("Resumen");
        ws.Cell(1, 1).Value = $"Cuenta del Marketplace — {statement.BuildingName}";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);

        var s = statement.Summary;
        var rows = new (string Label, object Value, bool Money)[]
        {
            ("Edificio", statement.BuildingName, false),
            ("Período", $"{statement.FromDate:dd/MM/yyyy} al {statement.ToDate:dd/MM/yyyy}", false),
            ("Generado", (generatedAtUtc + LocalOffset).ToString("dd/MM/yyyy HH:mm"), false),
            ("Saldo inicial", s.OpeningBalance, true),
            ("Ingresos por reservas", s.TotalIn, true),
            ("Acreditado a propietarios", s.TotalCredited, true),
            ("Devuelto a compradores", s.TotalRefunds, true),
            ("Ajustes manuales", s.TotalAdjustments, true),
            ("Comisiones por cancelación de propietarios", s.TotalCancellationFees, true),
            ("Saldo final del período", s.ClosingBalance, true),
            ("Saldo actual (hoy)", s.CurrentBalance, true),
            ("Pendiente de acreditar a propietarios (hoy)", s.PendingToCredit, true),
            ("Reembolsos pendientes de devolver (hoy)", s.PendingRefunds, true),
            ("Deudas por gestión de propietarios por descontar (hoy)", s.OwnerDebtsPending, true),
            ("Ganancia de la gestión (hoy)", s.ManagementGain, true)
        };

        var row = 3;
        foreach (var (label, value, money) in rows)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 1).Style.Font.SetBold();
            var cell = ws.Cell(row, 2);
            if (money)
            {
                cell.Value = (decimal)value;
                cell.Style.NumberFormat.Format = AmountFormat;
            }
            else
            {
                cell.Value = (string)value;
            }

            row++;
        }

        ws.Cell(row + 1, 1).Value =
            "Cuenta contable aparte: no es una cuenta bancaria ni toca la contabilidad del edificio. La ganancia de la gestión es el saldo menos lo que todavía falta acreditar a los propietarios y devolver a los compradores; su reparto se acuerda fuera del sistema.";
        ws.Cell(row + 1, 1).Style.Font.SetItalic();

        ws.Column(1).Width = 46;
        ws.Column(2).Width = 28;
    }

    private static void AddMovements(XLWorkbook wb, MarketplaceStatementDto statement)
    {
        var ws = wb.Worksheets.Add("Movimientos");
        string[] headers = ["Fecha y hora", "Tipo", "Concepto", "Reserva", "Importe", "Registrado por"];
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
        }

        ws.Row(1).Style.Font.SetBold();

        var row = 2;
        foreach (var item in statement.Rows)
        {
            ws.Cell(row, 1).Value = item.OccurredAtUtc + LocalOffset;
            ws.Cell(row, 1).Style.DateFormat.Format = DateTimeFormat;
            ws.Cell(row, 2).Value = KindLabel(item.Kind);
            ws.Cell(row, 3).Value = item.Concept;
            ws.Cell(row, 4).Value = item.Reference ?? string.Empty;
            ws.Cell(row, 5).Value = item.Amount;
            ws.Cell(row, 5).Style.NumberFormat.Format = AmountFormat;
            ws.Cell(row, 6).Value = item.CreatedByName ?? "Automático";
            row++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Column(1).Width = 18;
        ws.Column(2).Width = 28;
        ws.Column(3).Width = 70;
        ws.Column(4).Width = 14;
        ws.Column(5).Width = 16;
        ws.Column(6).Width = 24;
    }
}
