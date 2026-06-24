using Condo.Application.Models;
using Condo.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public sealed class PaymentReceiptPdfDocument(PaymentDto payment) : IDocument
{
    private const string ColorPrimary = "#14363d";
    private const string ColorAccent = "#1a8c5b";
    private const string ColorGray = "#6b878d";
    private const string ColorBorder = "#dbe7e3";
    private const string ColorRowAlt = "#f5faf9";
    private const string ColorWhite = "#ffffff";
    private const string ColorCredit = "#1a7f37";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Comprobante de pago {payment.UnitCode} - {payment.ExpensePeriodName}",
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
            page.Footer().Element(ComposeFooter);
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
                    c.Item().Text("Comprobante de pago").FontSize(11).FontColor(ColorAccent);
                });
                row.ConstantItem(200).AlignRight().Column(c =>
                {
                    c.Item().Text(payment.BuildingName).Bold().FontColor(ColorPrimary);
                    c.Item().Text(payment.ExpensePeriodName).FontColor(ColorGray);
                    c.Item().Text($"#{payment.Id.ToString()[..8].ToUpper()}").FontColor(ColorGray).FontSize(8);
                });
            });
            col.Item().PaddingTop(8).LineHorizontal(1).LineColor(ColorBorder);
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(14);
            col.Item().Element(ComposePaymentInfo);
            if (payment.Allocations.Count > 0)
                col.Item().Element(ComposeAllocationsTable);
            col.Item().Element(ComposeSummary);
        });
    }

    private void ComposePaymentInfo(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn();
                c.RelativeColumn();
                c.RelativeColumn();
            });

            AddInfoCell(table, "Unidad", payment.UnitCode);
            AddInfoCell(table, "Edificio", payment.BuildingName);
            AddInfoCell(table, "Periodo", payment.ExpensePeriodName);
            AddInfoCell(table, "Fecha de pago", payment.PaymentDate.ToString("dd/MM/yyyy"));
            AddInfoCell(table, "Método", MethodLabel(payment.Method));
            AddInfoCell(table, "Referencia", string.IsNullOrEmpty(payment.Reference) ? "—" : payment.Reference);
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

    private void ComposeAllocationsTable(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("Cargos cubiertos").Bold().FontColor(ColorPrimary);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(1.5f);
                    c.RelativeColumn(1.5f);
                });

                table.Header(header =>
                {
                    header.Cell().Background(ColorPrimary).Padding(5).Text("Concepto").FontColor(ColorWhite).Bold();
                    header.Cell().Background(ColorPrimary).Padding(5).Text("Tipo").FontColor(ColorWhite).Bold();
                    header.Cell().Background(ColorPrimary).Padding(5).AlignRight().Text("Imputado (Gs.)").FontColor(ColorWhite).Bold();
                });

                var isOdd = true;
                foreach (var alloc in payment.Allocations)
                {
                    var bg = isOdd ? ColorWhite : ColorRowAlt;
                    isOdd = !isOdd;
                    table.Cell().Background(bg).Padding(5).Text(alloc.ChargeConcept).FontColor(ColorPrimary);
                    table.Cell().Background(bg).Padding(5).Text(ChargeTypeLabel(alloc.ChargeType)).FontColor(ColorGray);
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(FormatCurrency(alloc.AllocatedAmount)).FontColor(ColorPrimary);
                }
            });
        });
    }

    private void ComposeSummary(IContainer container)
    {
        var credit = payment.Amount - payment.AllocatedAmount;

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

            if (payment.Allocations.Count > 0)
                AddSummaryRow(table, "Total imputado a cargos", payment.AllocatedAmount, false);

            if (credit > 0.01m)
                AddSummaryRow(table, "Crédito a favor", credit, false, ColorCredit);

            AddSummaryRow(table, "Total cobrado", payment.Amount, true);
        });

        if (!string.IsNullOrEmpty(payment.Notes))
        {
            container.PaddingTop(10).Column(c =>
            {
                c.Item().Text("Notas").FontColor(ColorGray).FontSize(8);
                c.Item().Text(payment.Notes).FontColor(ColorPrimary).Italic();
            });
        }
    }

    private static void AddSummaryRow(TableDescriptor table, string label, decimal amount, bool isTotal, string? overrideColor = null)
    {
        var bg = isTotal ? ColorAccent : ColorWhite;
        var textColor = isTotal ? ColorWhite : (overrideColor ?? ColorPrimary);

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

    private static string MethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Efectivo",
        PaymentMethod.BankTransfer => "Transferencia bancaria",
        PaymentMethod.Card => "Tarjeta",
        PaymentMethod.Check => "Cheque",
        _ => "Otro"
    };

    private static string ChargeTypeLabel(ExpenseChargeType type) => type switch
    {
        ExpenseChargeType.Ordinary => "Ordinaria",
        ExpenseChargeType.ReserveFund => "Fondo reserva",
        ExpenseChargeType.Extraordinary => "Extraordinario",
        ExpenseChargeType.Individual => "Individual",
        _ => "Ajuste"
    };
}
