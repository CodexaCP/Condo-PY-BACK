using Condo.Application.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public sealed class LibroMovimientosPdfDocument(LibroMovimientosReportDto report) : IDocument
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
        Title = $"Libro de Movimientos {report.BuildingName} - CONDOPY",
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
                    c.Item().Text("Libro de movimientos").FontSize(11).FontColor(ColorAccent);
                    c.Item().PaddingTop(2).Text($"{report.BuildingName}  ·  {report.FromDate:dd/MM/yyyy} – {report.ToDate:dd/MM/yyyy}")
                        .FontSize(8).FontColor(ColorGray);
                });
                row.AutoItem().AlignRight().AlignBottom()
                    .Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}").FontColor(ColorGray).FontSize(8);
            });

            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(ColorPrimary);

            col.Item().PaddingTop(8).Row(r =>
            {
                r.RelativeItem().Element(c => Badge(c, "Saldo anterior", FormatGs(report.OpeningBalance), ColorGray));
                r.ConstantItem(8);
                r.RelativeItem().Element(c => Badge(c, "Total cobros/ingresos", FormatGs(report.TotalCredits), ColorAccent));
                r.ConstantItem(8);
                r.RelativeItem().Element(c => Badge(c, "Total gastos", FormatGs(report.TotalDebits), ColorDebt));
                r.ConstantItem(8);
                r.RelativeItem().Element(c => Badge(c, "Saldo final", FormatGs(report.ClosingBalance), report.ClosingBalance >= 0 ? ColorGreen : ColorDebt));
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
        container.PaddingTop(12).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(1.0f); // Fecha
                c.RelativeColumn(1.0f); // Tipo
                c.RelativeColumn(3.2f); // Descripcion
                c.RelativeColumn(0.9f); // Unidad
                c.RelativeColumn(1.3f); // Referencia
                c.RelativeColumn(1.2f); // Ingreso
                c.RelativeColumn(1.2f); // Gasto
                c.RelativeColumn(1.3f); // Saldo
            });

            table.Header(h =>
            {
                void Head(string text, bool right = false)
                {
                    var cell = h.Cell().Background(ColorPrimary).Padding(6);
                    var txt = cell.Text(text).FontColor(ColorWhite).Bold().FontSize(8);
                    if (right) txt.AlignRight();
                }
                Head("Fecha");
                Head("Tipo");
                Head("Descripcion");
                Head("Unidad");
                Head("Referencia");
                Head("Ingreso", right: true);
                Head("Gasto", right: true);
                Head("Saldo", right: true);
            });

            var isOdd = true;
            foreach (var item in report.Items)
            {
                var bg = isOdd ? ColorWhite : ColorRowAlt;
                isOdd = !isOdd;

                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .Text(item.Date.ToString("dd/MM/yyyy")).FontColor(ColorDark);

                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .Text(TypeLabel(item.Type)).FontColor(TypeColor(item.Type)).Bold();

                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .Text(item.Description).FontColor(ColorDark);

                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .Text(item.UnitCode ?? "-").FontColor(ColorGray);

                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .Text(item.Reference ?? "-").FontColor(ColorGray);

                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .AlignRight().Text(item.Credit > 0 ? FormatGs(item.Credit) : "-").FontColor(ColorAccent);

                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .AlignRight().Text(item.Debit > 0 ? FormatGs(item.Debit) : "-").FontColor(ColorDebt);

                var balanceColor = item.RunningBalance >= 0 ? ColorGreen : ColorDebt;
                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .AlignRight().Text(FormatGs(item.RunningBalance)).FontColor(balanceColor).Bold();
            }
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text($"CONDOPY · Libro de movimientos: {report.BuildingName}").FontColor(ColorGray).FontSize(7.5f);
            row.ConstantItem(80).AlignRight().Text(t =>
            {
                t.CurrentPageNumber().FontColor(ColorGray).FontSize(7.5f);
                t.Span(" / ").FontColor(ColorGray).FontSize(7.5f);
                t.TotalPages().FontColor(ColorGray).FontSize(7.5f);
            });
        });
    }

    private static string TypeLabel(LibroMovimientoType type) => type switch
    {
        LibroMovimientoType.Cobro => "Cobro",
        LibroMovimientoType.IngresoEdificio => "Ingreso",
        LibroMovimientoType.GastoEdificio => "Gasto",
        _ => type.ToString()
    };

    private static string TypeColor(LibroMovimientoType type) => type switch
    {
        LibroMovimientoType.Cobro => ColorAccent,
        LibroMovimientoType.IngresoEdificio => ColorGreen,
        LibroMovimientoType.GastoEdificio => ColorDebt,
        _ => ColorGray
    };

    private static string FormatGs(decimal value) => $"Gs. {value:N0}";
}
