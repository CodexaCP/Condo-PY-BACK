using Condo.Application.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public sealed class CollectionReportPdfDocument(CollectionReportDto report, string filterDescription) : IDocument
{
    private const string ColorPrimary  = "#1385B6";
    private const string ColorDark     = "#0d2a38";
    private const string ColorAccent   = "#1AB7AF";
    private const string ColorGreen    = "#6AC64A";
    private const string ColorGray     = "#637b88";
    private const string ColorBorder   = "#d7e5ea";
    private const string ColorRowAlt   = "#f4f9fc";
    private const string ColorWhite    = "#ffffff";
    private const string ColorWarn     = "#c94d3f";
    private const string ColorWarnBg   = "#fdf1f0";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = "Reporte de Cobranza - CONDOPY",
        Author = "CONDOPY"
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(28, Unit.Point);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(8.5f));
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
                    c.Item().Text("CONDOPY").FontSize(18).Bold().FontColor(ColorPrimary);
                    c.Item().Text("Reporte de cobranza").FontSize(11).FontColor(ColorAccent);
                    c.Item().PaddingTop(2).Text(filterDescription).FontSize(8).FontColor(ColorGray);
                });
                row.AutoItem().AlignRight().AlignBottom()
                    .Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}").FontColor(ColorGray).FontSize(8);
            });
            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(ColorPrimary);
            col.Item().PaddingTop(8).Element(ComposeSummaryBadges);
        });
    }

    private void ComposeSummaryBadges(IContainer container)
    {
        var s = report.Summary;
        container.Row(r =>
        {
            r.RelativeItem().Element(c => SummaryBadge(c, "Emitido",    FormatGs(s.TotalChargedAmount),          ColorPrimary));
            r.ConstantItem(8);
            r.RelativeItem().Element(c => SummaryBadge(c, "Cobrado",    FormatGs(s.TotalCollectedAmount),        ColorAccent));
            r.ConstantItem(8);
            r.RelativeItem().Element(c => SummaryBadge(c, "Pendiente",  FormatGs(s.TotalPendingAmount),          ColorWarn));
            r.ConstantItem(8);
            r.RelativeItem().Element(c => SummaryBadge(c, "Recuperación", $"{s.CollectionRatePercentage:N1}%",   ColorGreen));
        });
    }

    private static void SummaryBadge(IContainer c, string label, string value, string color)
    {
        c.Border(0.5f).BorderColor(color).Padding(7).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(label).FontSize(7.5f).FontColor(color);
                col.Item().Text(value).Bold().FontSize(11).FontColor(color);
            });
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(12);
            col.Item().Element(ComposeTable);
            if (report.Summary.TotalCreditBalanceAmount > 0 || HasTypeBreakdown())
                col.Item().Element(ComposeBreakdownRow);
        });
    }

    private void ComposeTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(1.6f); // Periodo
                c.RelativeColumn(1.8f); // Edificio
                c.RelativeColumn(0.9f); // Estado
                c.RelativeColumn(1.3f); // Emitido
                c.RelativeColumn(1.3f); // Cobrado
                c.RelativeColumn(1.3f); // Pendiente
                c.RelativeColumn(1.0f); // Recuperac.
                c.RelativeColumn(0.7f); // Variac.
            });

            // Header
            table.Header(h =>
            {
                void Head(string text, bool right = false)
                {
                    var cell = h.Cell().Background(ColorPrimary).Padding(6);
                    var txt = cell.Text(text).FontColor(ColorWhite).Bold().FontSize(8);
                    if (right) txt.AlignRight();
                }
                Head("Periodo");
                Head("Edificio");
                Head("Estado");
                Head("Emitido",    right: true);
                Head("Cobrado",    right: true);
                Head("Pendiente",  right: true);
                Head("Recuperac.", right: true);
                Head("Variación",  right: true);
            });

            var isOdd = true;
            foreach (var item in report.Items)
            {
                var bg = isOdd ? ColorWhite : ColorRowAlt;
                isOdd = !isOdd;

                var hasPending = item.PendingAmount > 0;

                // Periodo
                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .Text(item.ExpensePeriodName).Bold().FontColor(ColorDark);

                // Edificio
                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .Text(item.BuildingName).FontColor(ColorGray);

                // Estado
                var statusColor = item.Status == "Published" ? ColorAccent : item.Status == "Closed" ? ColorGreen : ColorGray;
                var statusLabel = item.Status == "Published" ? "Publicado" : item.Status == "Closed" ? "Cerrado" : "Borrador";
                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .Text(statusLabel).FontColor(statusColor).Bold();

                // Emitido
                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .AlignRight().Text(FormatGs(item.TotalChargedAmount)).FontColor(ColorDark);

                // Cobrado
                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .AlignRight().Text(FormatGs(item.TotalCollectedAmount)).FontColor(ColorDark);

                // Pendiente
                var pendingCell = table.Cell().Background(hasPending ? ColorWarnBg : bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5).AlignRight();
                var pendingTxt = pendingCell.Text(FormatGs(item.PendingAmount)).FontColor(hasPending ? ColorWarn : ColorDark);
                if (hasPending) pendingTxt.Bold();

                // Recuperación
                var rateColor = item.CollectionRatePercentage >= 80 ? ColorGreen
                              : item.CollectionRatePercentage >= 50 ? ColorPrimary
                              : ColorWarn;
                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .AlignRight().Text($"{item.CollectionRatePercentage:N1}%").FontColor(rateColor).Bold();

                // Variación
                if (item.PreviousPeriodCollectionRatePercentage.HasValue)
                {
                    var delta = item.CollectionRatePercentage - item.PreviousPeriodCollectionRatePercentage.Value;
                    var deltaColor = delta >= 0 ? ColorGreen : ColorWarn;
                    var sign = delta >= 0 ? "+" : "";
                    table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                        .AlignRight().Text($"{sign}{delta:N1}%").FontColor(deltaColor);
                }
                else
                {
                    table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                        .AlignRight().Text("—").FontColor(ColorGray);
                }
            }
        });
    }

    private void ComposeBreakdownRow(IContainer container)
    {
        var s = report.Summary;
        container.Row(row =>
        {
            // Desglose por tipo
            row.RelativeItem().Column(col =>
            {
                col.Item().PaddingBottom(4).Text("Desglose por tipo de cargo").Bold().FontColor(ColorDark);
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(1); });

                    void Row(string label, decimal amount)
                    {
                        if (amount <= 0) return;
                        table.Cell().BorderBottom(0.3f).BorderColor(ColorBorder).Padding(4)
                            .Text(label).FontColor(ColorGray);
                        table.Cell().BorderBottom(0.3f).BorderColor(ColorBorder).Padding(4)
                            .AlignRight().Text(FormatGs(amount)).FontColor(ColorDark);
                    }

                    Row("Ordinaria",       s.OrdinaryChargedAmount);
                    Row("Fondo de reserva",s.ReserveFundChargedAmount);
                    Row("Extraordinario",  s.ExtraordinaryChargedAmount);
                    Row("Individual",      s.IndividualChargedAmount);
                    Row("Ajuste",          s.AdjustmentChargedAmount);
                });
            });

            row.ConstantItem(20);

            // Desglose por responsabilidad
            row.RelativeItem().Column(col =>
            {
                col.Item().PaddingBottom(4).Text("Por responsabilidad").Bold().FontColor(ColorDark);
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(1); });

                    table.Header(h =>
                    {
                        h.Cell().Background(ColorPrimary).Padding(4).Text("Tipo").FontColor(ColorWhite).Bold();
                        h.Cell().Background(ColorPrimary).Padding(4).AlignRight().Text("Emitido").FontColor(ColorWhite).Bold();
                        h.Cell().Background(ColorPrimary).Padding(4).AlignRight().Text("Pendiente").FontColor(ColorWhite).Bold();
                    });

                    void RespRow(string label, decimal charged, decimal pending)
                    {
                        table.Cell().BorderBottom(0.3f).BorderColor(ColorBorder).Padding(4).Text(label).FontColor(ColorGray);
                        table.Cell().BorderBottom(0.3f).BorderColor(ColorBorder).Padding(4).AlignRight().Text(FormatGs(charged)).FontColor(ColorDark);
                        var hp = pending > 0;
                        table.Cell().Background(hp ? ColorWarnBg : ColorWhite).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(4)
                            .AlignRight().Text(FormatGs(pending)).FontColor(hp ? ColorWarn : ColorDark);
                    }

                    RespRow("Residentes",   s.ResidentChargedAmount,  s.ResidentPendingAmount);
                    RespRow("Propietarios", s.OwnerChargedAmount,     s.OwnerPendingAmount);

                    if (s.TotalCreditBalanceAmount > 0)
                    {
                        table.Cell().Padding(4).Text("Créditos a favor").FontColor(ColorAccent).Bold();
                        table.Cell().Padding(4).AlignRight().Text(FormatGs(s.TotalCreditBalanceAmount)).FontColor(ColorAccent).Bold();
                        table.Cell().Padding(4).Text("").FontColor(ColorWhite);
                    }
                });
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text("Generado por CONDOPY · Reporte de Cobranza").FontColor(ColorGray).FontSize(7.5f);
            row.ConstantItem(80).AlignRight().Text(t =>
            {
                t.CurrentPageNumber().FontColor(ColorGray).FontSize(7.5f);
                t.Span(" / ").FontColor(ColorGray).FontSize(7.5f);
                t.TotalPages().FontColor(ColorGray).FontSize(7.5f);
            });
        });
    }

    private bool HasTypeBreakdown()
    {
        var s = report.Summary;
        return s.OrdinaryChargedAmount > 0 || s.ReserveFundChargedAmount > 0
            || s.ExtraordinaryChargedAmount > 0 || s.IndividualChargedAmount > 0
            || s.AdjustmentChargedAmount > 0;
    }

    private static string FormatGs(decimal value) => $"Gs. {value:N0}";
}
