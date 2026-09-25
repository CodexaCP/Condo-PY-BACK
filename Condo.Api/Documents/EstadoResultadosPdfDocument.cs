using Condo.Application.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public sealed class EstadoResultadosPdfDocument(EstadoResultadosReportDto report) : IDocument
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
        Title = $"Estado de Resultados {report.BuildingName} - CONDOPY",
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
                    c.Item().Text("CONDOPY").FontSize(18).Bold().FontColor(ColorPrimary);
                    c.Item().Text("Estado de resultados").FontSize(11).FontColor(ColorAccent);
                    c.Item().PaddingTop(2).Text($"{report.BuildingName}  ·  {report.FromDate:dd/MM/yyyy} – {report.ToDate:dd/MM/yyyy}")
                        .FontSize(8).FontColor(ColorGray);
                });
                row.AutoItem().AlignRight().AlignBottom()
                    .Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}").FontColor(ColorGray).FontSize(8);
            });

            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(ColorPrimary);

            col.Item().PaddingTop(8).Row(r =>
            {
                r.RelativeItem().Element(c => Badge(c, "Total ingresos", FormatGs(report.TotalIncome), ColorAccent));
                r.ConstantItem(8);
                r.RelativeItem().Element(c => Badge(c, "Total gastos", FormatGs(report.TotalExpense), ColorDebt));
                r.ConstantItem(8);
                r.RelativeItem().Element(c => Badge(c,
                    report.NetResult >= 0 ? "Superavit del periodo" : "Deficit del periodo",
                    FormatGs(report.NetResult), report.NetResult >= 0 ? ColorGreen : ColorDebt));
            });
        });
    }

    private static void Badge(IContainer c, string label, string value, string color)
    {
        c.Border(0.5f).BorderColor(color).Padding(7).Column(col =>
        {
            col.Item().Text(label).FontSize(7.5f).FontColor(color);
            col.Item().Text(value).Bold().FontSize(11).FontColor(color);
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.PaddingTop(12).Column(col =>
        {
            col.Item().Element(c => ComposeSection(c, "INGRESOS", report.IncomeLines, report.TotalIncome, ColorAccent));
            col.Item().PaddingTop(16).Element(c => ComposeSection(c, "GASTOS", report.ExpenseLines, report.TotalExpense, ColorDebt));
        });
    }

    private void ComposeSection(IContainer container, string title, IReadOnlyList<EstadoResultadosLineDto> lines, decimal total, string accentColor)
    {
        container.Column(col =>
        {
            col.Item().Background(accentColor).Padding(6).Text(title).Bold().FontColor(ColorWhite).FontSize(10);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3f);
                    c.RelativeColumn(1.2f);
                });

                var isOdd = true;
                foreach (var line in lines)
                {
                    var bg = isOdd ? ColorWhite : ColorRowAlt;
                    isOdd = !isOdd;

                    table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(6)
                        .Text(line.Label).FontColor(ColorDark);
                    table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(6)
                        .AlignRight().Text(FormatGs(line.Amount)).FontColor(ColorDark);
                }

                if (lines.Count == 0)
                {
                    table.Cell().ColumnSpan(2).Padding(8).Text("Sin movimientos en el periodo.").FontColor(ColorGray).Italic();
                }

                table.Cell().Background(accentColor).Padding(6).Text($"Total {title.ToLowerInvariant()}").Bold().FontColor(ColorWhite);
                table.Cell().Background(accentColor).Padding(6).AlignRight().Text(FormatGs(total)).Bold().FontColor(ColorWhite);
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text($"CONDOPY · Estado de resultados: {report.BuildingName}").FontColor(ColorGray).FontSize(7.5f);
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
