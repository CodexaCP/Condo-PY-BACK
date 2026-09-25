using Condo.Application.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public sealed class BuildingComparisonPdfDocument(BuildingComparisonReportDto report) : IDocument
{
    private const string ColorPrimary = "#1385B6";
    private const string ColorAccent  = "#1AB7AF";
    private const string ColorGreen   = "#6AC64A";
    private const string ColorGray    = "#637b88";
    private const string ColorDark    = "#0d2a38";
    private const string ColorBorder  = "#d7e5ea";
    private const string ColorRowAlt  = "#f4f9fc";
    private const string ColorWhite   = "#ffffff";
    private const string ColorDebt    = "#c94d3f";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = "Comparativo de edificios - CONDOPY",
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
                    c.Item().Text("Comparativo de edificios").FontSize(11).FontColor(ColorAccent);
                    c.Item().PaddingTop(2).Text($"{report.FromDate:dd/MM/yyyy} – {report.ToDate:dd/MM/yyyy}  ·  morosidad a la fecha de hoy")
                        .FontSize(8).FontColor(ColorGray);
                });
                row.AutoItem().AlignRight().AlignBottom()
                    .Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}").FontColor(ColorGray).FontSize(8);
            });
            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(ColorPrimary);
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.PaddingTop(12).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(2.2f); // Edificio
                c.RelativeColumn(1.3f); // Cobrado
                c.RelativeColumn(1.3f); // Gastos
                c.RelativeColumn(1.3f); // Resultado
                c.RelativeColumn(1.3f); // Morosidad
                c.RelativeColumn(1.1f); // Unidades morosas
            });

            table.Header(h =>
            {
                void Head(string text, bool right = false)
                {
                    var cell = h.Cell().Background(ColorPrimary).Padding(6);
                    var txt = cell.Text(text).FontColor(ColorWhite).Bold().FontSize(8);
                    if (right) txt.AlignRight();
                }
                Head("Edificio");
                Head("Cobrado", right: true);
                Head("Gastos", right: true);
                Head("Resultado", right: true);
                Head("Morosidad actual", right: true);
                Head("Unid. morosas", right: true);
            });

            var isOdd = true;
            foreach (var item in report.Items)
            {
                var bg = isOdd ? ColorWhite : ColorRowAlt;
                isOdd = !isOdd;

                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(6)
                    .Text(item.BuildingName).Bold().FontColor(ColorPrimary);

                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(6)
                    .AlignRight().Text(FormatGs(item.TotalCollected)).FontColor(ColorAccent);

                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(6)
                    .AlignRight().Text(FormatGs(item.TotalExpenses)).FontColor(ColorDebt);

                var resultColor = item.NetResult >= 0 ? ColorGreen : ColorDebt;
                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(6)
                    .AlignRight().Text(FormatGs(item.NetResult)).FontColor(resultColor).Bold();

                var overdueBg = item.OverdueAmount > 0 ? "#fdf1f0" : bg;
                table.Cell().Background(overdueBg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(6)
                    .AlignRight().Text(FormatGs(item.OverdueAmount)).FontColor(item.OverdueAmount > 0 ? ColorDebt : ColorGray).Bold();

                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(6)
                    .AlignRight().Text(item.UnitsWithOverdueBalance.ToString()).FontColor(ColorGray);
            }

            if (report.Items.Count == 0)
            {
                table.Cell().ColumnSpan(6).Padding(10).Text("No hay edificios accesibles para este usuario.").FontColor(ColorGray).Italic();
            }
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text("CONDOPY · Comparativo de edificios").FontColor(ColorGray).FontSize(7.5f);
            row.ConstantItem(80).AlignRight().Text(t =>
            {
                t.CurrentPageNumber().FontColor(ColorGray).FontSize(7.5f);
                t.Span(" / ").FontColor(ColorGray).FontSize(7.5f);
                t.TotalPages().FontColor(ColorGray).FontSize(7.5f);
            });
        });
    }

    private static string FormatGs(decimal value) => $"Gs. {value:N0}";
}
