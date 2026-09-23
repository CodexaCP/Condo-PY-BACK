using Condo.Application.Models;
using Condo.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public sealed class ReceiptPdfDocument(ExpenseReceiptDto receipt) : IDocument
{
    private const string ColorPrimary = "#1385B6";
    private const string ColorAccent = "#1AB7AF";
    private const string ColorGray = "#637b88";
    private const string ColorBorder = "#d7e5ea";
    private const string ColorRowAlt = "#f4f9fc";
    private const string ColorWhite = "#ffffff";
    private const string ColorDebt = "#c94d3f";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Comprobante {receipt.UnitCode} - {receipt.ExpensePeriodName}",
        Author = "CONDOPY"
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32, Unit.Point);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));
            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeBody);
            page.Footer().Column(col =>
            {
                col.Item().Element(PdfWatermark.ComposeNonFiscal);
                col.Item().Element(ComposeFooter);
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(10).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("CONDOPY").FontSize(16).Bold().FontColor(ColorPrimary);
                    c.Item().Text("Comprobante individual de expensas").FontSize(11).FontColor(ColorAccent);
                });
                row.ConstantItem(200).AlignRight().Column(c =>
                {
                    c.Item().Text(receipt.BuildingName).Bold().FontColor(ColorPrimary);
                    c.Item().Text(receipt.ExpensePeriodName).FontColor(ColorGray);
                });
            });

            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(ColorPrimary);
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(14);
            col.Item().Element(ComposeUnitInfo);
            col.Item().Element(ComposeChargesTable);
            col.Item().Element(ComposeSummary);
            if (receipt.Payments.Count > 0)
                col.Item().Element(ComposePaymentsTable);
        });
    }

    private void ComposeUnitInfo(IContainer container)
    {
        container.Column(col =>
        {
            // ── Propietario ──────────────────────────────────────
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(); c.RelativeColumn(); });
                t.Cell().ColumnSpan(3).Background(ColorPrimary).PaddingHorizontal(6).PaddingVertical(3)
                    .Text("Propietario").FontColor(ColorWhite).Bold().FontSize(8);
                AddInfoCell(t, "Nombre", receipt.OwnerName);
                AddInfoCell(t, "Tipo doc.", receipt.OwnerDocumentType ?? "—");
                AddInfoCell(t, "Documento", receipt.OwnerDocumentNumber ?? "—");
            });

            // ── Residente ─────────────────────────────────────────
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(); c.RelativeColumn(); });
                t.Cell().ColumnSpan(3).Background(ColorAccent).PaddingHorizontal(6).PaddingVertical(3)
                    .Text("Residente").FontColor(ColorWhite).Bold().FontSize(8);
                AddInfoCell(t, "Nombre", receipt.ResidentName);
                AddInfoCell(t, "Tipo doc.", receipt.ResidentDocumentType ?? "—");
                AddInfoCell(t, "Documento", receipt.ResidentDocumentNumber ?? "—");
            });

            // ── Datos de la unidad ────────────────────────────────
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                AddInfoCell(t, "Unidad", receipt.UnitCode);
                AddInfoCell(t, "Coeficiente", receipt.UnitCoefficient.ToString("F6"));
                AddInfoCell(t, "Vencimiento", receipt.DueDate.ToString("dd/MM/yyyy"));
                AddInfoCell(t, "Periodo", $"{receipt.Month:D2}/{receipt.Year}");
            });
        });
    }

    private static void AddInfoCell(TableDescriptor table, string label, string value)
    {
        table.Cell().Border(0.5f).BorderColor(ColorBorder).Padding(6).Column(c =>
        {
            c.Item().Text(label).FontColor(ColorGray).FontSize(8);
            c.Item().Text(value).Bold().FontColor(ColorPrimary);
        });
    }

    // Todo lo que no es Extraordinary se junta en una sola linea "Expensas correspondiente al mes de X"
    // (ordinaria, fondo de reserva, individual, ajustes/mora); lo Extraordinary va aparte, con el % que
    // representa sobre esa expensa. Mismo criterio que la factura (InvoicePdfDocument), sin desglosar
    // cada concepto suelto (honorarios, limpieza, ascensores, etc.).
    private List<(string Concepto, string? SubNota, decimal Monto)> BuildConsolidatedLines()
    {
        var expensas = receipt.TotalAmount - receipt.ExtraordinaryAmount;
        var result = new List<(string, string?, decimal)>
        {
            ($"EXPENSAS CORRESPONDIENTE AL MES DE {MesAbrev(receipt.Month)}/{receipt.Year}",
             receipt.BuildingOrdinaryTotal > 0
                ? $"Coeficiente: {CoefPct(receipt.UnitCoefficient)} % de {FormatCurrency(receipt.BuildingOrdinaryTotal)}"
                : null,
             expensas)
        };

        if (receipt.ExtraordinaryAmount > 0)
        {
            var pctText = expensas > 0
                ? $" {Math.Round(receipt.ExtraordinaryAmount / expensas * 100m, MidpointRounding.AwayFromZero):0}%"
                : string.Empty;
            result.Add(($"APORTE EXTRAORDINARIO{pctText}", null, receipt.ExtraordinaryAmount));
        }

        return result;
    }

    private static string CoefPct(decimal coefficient) =>
        (coefficient * 100m).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture).Replace(".", ",");

    private static readonly string[] MonthsAbbrev =
        ["ENE", "FEB", "MAR", "ABR", "MAY", "JUN", "JUL", "AGO", "SEP", "OCT", "NOV", "DIC"];

    private static string MesAbrev(int month) => month is >= 1 and <= 12 ? MonthsAbbrev[month - 1] : month.ToString();

    private void ComposeChargesTable(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("Cargos del periodo").Bold().FontColor(ColorPrimary);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(1.5f);
                });

                table.Header(header =>
                {
                    header.Cell().Background(ColorPrimary).Padding(5).Text("Concepto").FontColor(ColorWhite).Bold();
                    header.Cell().Background(ColorPrimary).Padding(5).AlignRight().Text("Monto (Gs.)").FontColor(ColorWhite).Bold();
                });

                var isOdd = true;
                foreach (var (concepto, subNota, monto) in BuildConsolidatedLines())
                {
                    var bg = isOdd ? ColorWhite : ColorRowAlt;
                    isOdd = !isOdd;

                    table.Cell().Background(bg).Padding(5).Column(c =>
                    {
                        c.Item().Text(concepto).FontColor(ColorPrimary);
                        if (subNota is not null)
                            c.Item().Text(subNota).FontSize(7.5f).FontColor(ColorGray);
                    });
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(FormatCurrency(monto)).FontColor(ColorPrimary);
                }
            });
        });
    }

    private void ComposeSummary(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                c.RelativeColumn(1.5f);
            });

            table.Header(header =>
            {
                header.Cell().Background(ColorPrimary).Padding(5).Text("Resumen").FontColor(ColorWhite).Bold();
                header.Cell().Background(ColorPrimary).Padding(5).AlignRight().Text("Monto (Gs.)").FontColor(ColorWhite).Bold();
            });

            AddSummaryRow(table, "Total cargado", receipt.TotalAmount, true);
        });
    }

    private static void AddSummaryRow(TableDescriptor table, string label, decimal amount, bool isTotal)
    {
        var bg = isTotal ? ColorAccent : ColorWhite;
        var textColor = isTotal ? ColorWhite : ColorPrimary;

        table.Cell().Background(bg).Padding(5).Text(t =>
        {
            var span = t.Span(label).FontColor(textColor);
            if (isTotal) span.Bold();
        });
        table.Cell().Background(bg).Padding(5).AlignRight().Text(t =>
        {
            var span = t.Span(FormatCurrency(amount)).FontColor(textColor);
            if (isTotal) span.Bold();
        });
    }

    private void ComposePaymentsTable(IContainer container)
    {
        var totalPaid = receipt.Payments.Sum(p => p.Amount);
        var balance = receipt.TotalAmount - totalPaid;

        container.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("Pagos registrados").Bold().FontColor(ColorPrimary);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1);
                    c.RelativeColumn(2);
                    c.RelativeColumn(1.5f);
                });

                table.Header(header =>
                {
                    header.Cell().Background(ColorPrimary).Padding(5).Text("Fecha").FontColor(ColorWhite).Bold();
                    header.Cell().Background(ColorPrimary).Padding(5).Text("Método / Referencia").FontColor(ColorWhite).Bold();
                    header.Cell().Background(ColorPrimary).Padding(5).AlignRight().Text("Importe (Gs.)").FontColor(ColorWhite).Bold();
                });

                var isOdd = true;
                foreach (var payment in receipt.Payments)
                {
                    var bg = isOdd ? ColorWhite : ColorRowAlt;
                    isOdd = !isOdd;
                    var methodLabel = payment.Method switch
                    {
                        PaymentMethod.Cash => "Efectivo",
                        PaymentMethod.BankTransfer => "Transferencia",
                        PaymentMethod.Card => "Tarjeta",
                        PaymentMethod.Check => "Cheque",
                        _ => "Otro"
                    };
                    var refText = string.IsNullOrEmpty(payment.Reference) ? methodLabel : $"{methodLabel} — {payment.Reference}";

                    table.Cell().Background(bg).Padding(5).Text(payment.PaymentDate.ToString("dd/MM/yyyy")).FontColor(ColorPrimary);
                    table.Cell().Background(bg).Padding(5).Column(refCol =>
                    {
                        refCol.Item().Text(refText).FontColor(ColorGray);
                        if (!string.IsNullOrWhiteSpace(payment.Notes))
                            refCol.Item().PaddingTop(2).Text(payment.Notes).FontSize(7.5f).Italic().FontColor(ColorGray);
                    });
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(FormatCurrency(payment.Amount)).FontColor(ColorAccent);
                }
            });

            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.Span("Total pagado: ").FontColor(ColorGray);
                    t.Span(FormatCurrency(totalPaid)).Bold().FontColor(ColorAccent);
                    t.Span("   Saldo: ").FontColor(ColorGray);
                    t.Span(FormatCurrency(balance)).Bold().FontColor(balance > 0 ? ColorDebt : ColorAccent);
                });
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text("Generado por CONDOPY").FontColor(ColorGray);
            row.ConstantItem(80).AlignRight().Text(t =>
            {
                t.CurrentPageNumber().FontColor(ColorGray);
                t.Span(" / ").FontColor(ColorGray);
                t.TotalPages().FontColor(ColorGray);
            });
        });
    }

    private static string FormatCurrency(decimal value) => $"Gs. {value:N0}";
}
