using Condo.Application.Models;
using Condo.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public sealed class InvoicePdfDocument(InvoiceDto invoice) : IDocument
{
    private const string ColorPrimary = "#1385B6";
    private const string ColorAccent = "#1AB7AF";
    private const string ColorGray = "#637b88";
    private const string ColorBorder = "#d7e5ea";
    private const string ColorRowAlt = "#f4f9fc";
    private const string ColorWhite = "#ffffff";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = invoice.Status == InvoiceStatus.Issued
            ? $"Factura {invoice.NumeroFormateado}"
            : $"Vista previa de factura - {invoice.UnitCode}",
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
                    c.Item().Text(invoice.SeriesRazonSocial ?? "CONDOPY").FontSize(16).Bold().FontColor(ColorPrimary);
                    c.Item().Text(invoice.Status == InvoiceStatus.Issued ? "Factura" : "Vista previa de factura")
                        .FontSize(11).FontColor(ColorAccent);
                    if (!string.IsNullOrEmpty(invoice.SeriesRuc))
                        c.Item().Text($"RUC: {invoice.SeriesRuc}").FontColor(ColorGray).FontSize(8);
                });
                row.ConstantItem(220).AlignRight().Column(c =>
                {
                    if (invoice.Status == InvoiceStatus.Issued)
                    {
                        c.Item().Text(invoice.NumeroFormateado ?? string.Empty).Bold().FontColor(ColorPrimary).FontSize(13);
                        if (!string.IsNullOrEmpty(invoice.SeriesNumeroTimbrado))
                            c.Item().Text($"Timbrado N° {invoice.SeriesNumeroTimbrado}").FontColor(ColorGray).FontSize(8);
                        c.Item().Text(invoice.FechaEmisionUtc?.ToString("dd/MM/yyyy HH:mm") ?? string.Empty).FontColor(ColorGray).FontSize(8);
                    }
                    else
                    {
                        c.Item().Text("SIN NUMERAR").Bold().FontColor("#B45309").FontSize(13);
                    }
                    c.Item().Text(invoice.BuildingName).FontColor(ColorGray);
                    c.Item().Text($"#{invoice.Id.ToString()[..8].ToUpper()}").FontColor(ColorGray).FontSize(8);
                });
            });
            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(ColorPrimary);

            if (invoice.Status != InvoiceStatus.Issued)
            {
                col.Item().PaddingTop(8).Element(PdfWatermark.ComposeNonFiscal);
            }
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(14);
            col.Item().Element(ComposeInvoiceInfo);
            if (invoice.Detalle.Count > 0)
                col.Item().Element(ComposeDetailTable);
            col.Item().Element(ComposeSummary);

            if (invoice.Status == InvoiceStatus.Voided)
            {
                col.Item().Background("#FEE2E2").Border(1).BorderColor("#DC2626").Padding(8)
                    .Text($"FACTURA ANULADA — {invoice.MotivoAnulacion}").FontColor("#991B1B").Bold();
            }
        });
    }

    private void ComposeInvoiceInfo(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn();
                c.RelativeColumn();
            });

            AddInfoCell(table, "Unidad", invoice.UnitCode);
            AddInfoCell(table, "Edificio", invoice.BuildingName);
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

    private void ComposeDetailTable(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("Detalle").Bold().FontColor(ColorPrimary);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4);
                    c.RelativeColumn(1.5f);
                });

                table.Header(header =>
                {
                    header.Cell().Background(ColorPrimary).Padding(5).Text("Concepto").FontColor(ColorWhite).Bold();
                    header.Cell().Background(ColorPrimary).Padding(5).AlignRight().Text("Monto (Gs.)").FontColor(ColorWhite).Bold();
                });

                var isOdd = true;
                foreach (var line in invoice.Detalle)
                {
                    var bg = isOdd ? ColorWhite : ColorRowAlt;
                    isOdd = !isOdd;
                    table.Cell().Background(bg).Padding(5).Text(line.Concepto).FontColor(ColorPrimary);
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(FormatCurrency(line.Monto)).FontColor(ColorPrimary);
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
                c.RelativeColumn(4);
                c.RelativeColumn(1.5f);
            });

            table.Cell().Background(ColorAccent).Padding(5).Text(t => t.Span("Total").FontColor(ColorWhite).Bold());
            table.Cell().Background(ColorAccent).Padding(5).AlignRight()
                .Text(t => t.Span(FormatCurrency(invoice.MontoTotal)).FontColor(ColorWhite).Bold());
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
