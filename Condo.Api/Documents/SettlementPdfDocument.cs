using Condo.Application.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public sealed class SettlementPdfDocument(
    ExpenseSettlementSummaryDto summary,
    IReadOnlyList<SettlementPdfChargeRow> charges,
    string periodStartDate,
    string periodEndDate,
    string periodDueDate) : IDocument
{
    private const string ColorPrimary = "#14363d";
    private const string ColorAccent = "#1a8c5b";
    private const string ColorGray = "#6b878d";
    private const string ColorBorder = "#dbe7e3";
    private const string ColorRowAlt = "#f5faf9";
    private const string ColorWhite = "#ffffff";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Liquidacion {summary.ExpensePeriodName} - {summary.BuildingName}",
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
                    c.Item().Text("Liquidacion de Expensas").FontSize(11).FontColor(ColorAccent);
                });
                row.ConstantItem(200).AlignRight().Column(c =>
                {
                    c.Item().Text(summary.BuildingName).Bold().FontColor(ColorPrimary);
                    c.Item().Text(summary.ExpensePeriodName).FontColor(ColorGray);
                });
            });

            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text($"Vigencia: {periodStartDate} al {periodEndDate}").FontColor(ColorGray);
                row.RelativeItem().AlignRight().Text($"Vencimiento: {periodDueDate}").FontColor(ColorGray);
            });

            col.Item().PaddingTop(8).LineHorizontal(1).LineColor(ColorBorder);
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(14);
            col.Item().Element(ComposeSummary);
            if (charges.Count > 0)
            {
                col.Item().Element(ComposeChargesTable);
            }
        });
    }

    private void ComposeSummary(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("Resumen de liquidacion").Bold().FontColor(ColorPrimary);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    header.Cell().Background(ColorPrimary).Padding(5)
                        .Text("Concepto").FontColor(ColorWhite).Bold();
                    header.Cell().Background(ColorPrimary).Padding(5)
                        .AlignRight().Text("Monto (Gs.)").FontColor(ColorWhite).Bold();
                });

                AddSummaryRow(table, "Total gastos del edificio", summary.TotalBuildingExpenses, false);
                AddSummaryRow(table, "Total ingresos del edificio", summary.TotalBuildingIncomes, false);
                AddSummaryRow(table, "Monto neto a distribuir", summary.NetCommonAmount, true);

                if (summary.ReserveFundAmount > 0)
                {
                    AddSummaryRow(table, "  Fondo de reserva", summary.ReserveFundAmount, false, isSubrow: true);
                }

                if (summary.ExtraordinaryAmount > 0)
                {
                    AddSummaryRow(table, "  Gastos extraordinarios", summary.ExtraordinaryAmount, false, isSubrow: true);
                }
            });
        });
    }

    private static void AddSummaryRow(TableDescriptor table, string label, decimal amount, bool isTotal, bool isSubrow = false)
    {
        var bg = isTotal ? ColorAccent : (isSubrow ? ColorRowAlt : ColorWhite);
        var textColor = isTotal ? ColorWhite : ColorPrimary;

        table.Cell().Background(bg).Padding(5)
            .Text(t =>
            {
                var span = t.Span(label).FontColor(textColor);
                if (isTotal) span.Bold();
                if (isSubrow) span.Italic();
            });
        table.Cell().Background(bg).Padding(5).AlignRight()
            .Text(t =>
            {
                var span = t.Span(FormatCurrency(amount)).FontColor(textColor);
                if (isTotal) span.Bold();
            });
    }

    private void ComposeChargesTable(IContainer container)
    {
        var unitCount = charges.Select(x => x.UnitCode).Distinct().Count();
        var total = charges.Sum(x => x.Amount);

        container.Column(col =>
        {
            col.Item().PaddingBottom(4).Text("Distribucion por unidad").Bold().FontColor(ColorPrimary);
            col.Item().PaddingBottom(8)
                .Text($"{charges.Count} cargos · {unitCount} unidades · {FormatCurrency(total)}")
                .FontColor(ColorGray);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(55);
                    c.RelativeColumn(4);
                    c.ConstantColumn(75);
                    c.ConstantColumn(90);
                });

                table.Header(header =>
                {
                    header.Cell().Background(ColorPrimary).Padding(4).Text("Unidad").FontColor(ColorWhite).Bold();
                    header.Cell().Background(ColorPrimary).Padding(4).Text("Concepto").FontColor(ColorWhite).Bold();
                    header.Cell().Background(ColorPrimary).Padding(4).Text("Tipo").FontColor(ColorWhite).Bold();
                    header.Cell().Background(ColorPrimary).Padding(4).AlignRight().Text("Monto (Gs.)").FontColor(ColorWhite).Bold();
                });

                var isOdd = true;
                foreach (var charge in charges.OrderBy(x => x.UnitCode).ThenBy(x => x.Concept))
                {
                    var bg = isOdd ? ColorWhite : ColorRowAlt;
                    isOdd = !isOdd;

                    table.Cell().Background(bg).Padding(4).Text(charge.UnitCode).FontColor(ColorPrimary);
                    table.Cell().Background(bg).Padding(4).Text(charge.Concept).FontColor(ColorPrimary);
                    table.Cell().Background(bg).Padding(4).Text(charge.ChargeType).FontColor(ColorGray);
                    table.Cell().Background(bg).Padding(4).AlignRight().Text(FormatCurrency(charge.Amount)).FontColor(ColorPrimary);
                }
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.Span("Generado por CONDOPY").FontColor(ColorGray);
                if (summary.GeneratedAtUtc.HasValue)
                {
                    t.Span($" · {summary.GeneratedAtUtc.Value:dd/MM/yyyy HH:mm} UTC").FontColor(ColorGray);
                }
            });
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

public sealed record SettlementPdfChargeRow(string UnitCode, string Concept, string ChargeType, decimal Amount);
