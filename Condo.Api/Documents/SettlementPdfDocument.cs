using Condo.Application.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public sealed record SettlementSignature(string Name, string Title, byte[]? Image);

public sealed class SettlementPdfDocument(
    ExpenseSettlementSummaryDto summary,
    string periodStartDate,
    string periodEndDate,
    string periodDueDate,
    SettlementSignature? approver = null,
    SettlementSignature? publisher = null) : IDocument
{
    private const string ColorPrimary = "#1385B6";
    private const string ColorAccent = "#1AB7AF";
    private const string ColorGray = "#637b88";
    private const string ColorBorder = "#d7e5ea";
    private const string ColorRowAlt = "#f4f9fc";
    private const string ColorWhite = "#ffffff";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Liquidacion {summary.ExpensePeriodName} - {summary.BuildingName}",
        Author = "CONDOPY"
    };

    private bool hasSignatures => approver is not null || publisher is not null;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32, Unit.Point);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));

            page.Header().Element(ComposeHeader);
            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().PaddingBottom(hasSignatures ? 110 : 0).Element(ComposeBody);
                if (hasSignatures)
                {
                    layers.Layer().AlignBottom().Element(ComposeSignatures);
                }
            });
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

            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(ColorPrimary);
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(14);
            col.Item().Element(ComposeSummary);
            if (summary.CategoryTotals.Count > 0)
            {
                col.Item().Element(ComposeCategoryTable);
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

    private static readonly IReadOnlyDictionary<string, string> CategoryLabels = new Dictionary<string, string>
    {
        ["Utilities"] = "Servicios",
        ["Cleaning"] = "Limpieza",
        ["Security"] = "Seguridad",
        ["Maintenance"] = "Mantenimiento",
        ["Elevator"] = "Ascensor",
        ["Insurance"] = "Seguro",
        ["Payroll"] = "Salarios",
        ["Taxes"] = "Impuestos",
        ["Administration"] = "Administracion",
        ["ReserveFund"] = "Fondo de reserva",
        ["Extraordinary"] = "Extraordinario",
        ["Supplies"] = "Insumos",
        ["Other"] = "Otro"
    };

    private const string ColorCatHeader = "#e8f4f8";

    private void ComposeCategoryTable(IContainer container)
    {
        var totals = summary.CategoryTotals;
        var expenseCount = totals.Sum(x => x.ExpenseCount);
        var total = totals.Sum(x => x.Amount);

        container.Column(col =>
        {
            col.Item().PaddingBottom(4).Text("Gastos comunes por categoria").Bold().FontColor(ColorPrimary);
            col.Item().PaddingBottom(8)
                .Text($"{expenseCount} gastos · {totals.Count} categorias · {FormatCurrency(total)}")
                .FontColor(ColorGray);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.ConstantColumn(110);
                });

                table.Header(header =>
                {
                    header.Cell().Background(ColorPrimary).Padding(4).Text("Concepto").FontColor(ColorWhite).Bold();
                    header.Cell().Background(ColorPrimary).Padding(4).AlignRight().Text("Monto (Gs.)").FontColor(ColorWhite).Bold();
                });

                var itemAlt = true;
                foreach (var cat in totals)
                {
                    var catLabel = CategoryLabels.GetValueOrDefault(cat.Category, cat.Category);

                    // Fila cabecera de categoría
                    table.Cell().Background(ColorCatHeader).Padding(4)
                        .Text(catLabel).Bold().FontColor(ColorPrimary);
                    table.Cell().Background(ColorCatHeader).Padding(4).AlignRight()
                        .Text(FormatCurrency(cat.Amount)).Bold().FontColor(ColorPrimary);

                    // Sub-filas: un gasto por fila
                    foreach (var item in cat.Items)
                    {
                        var bg = itemAlt ? ColorWhite : ColorRowAlt;
                        itemAlt = !itemAlt;

                        table.Cell().Background(bg).PaddingLeft(14).PaddingVertical(3)
                            .Text(t =>
                            {
                                t.Span("· ").FontColor(ColorAccent);
                                t.Span(item.Description).FontColor(ColorGray);
                            });
                        table.Cell().Background(bg).PaddingRight(4).PaddingVertical(3).AlignRight()
                            .Text(FormatCurrency(item.Amount)).FontColor(ColorGray);
                    }
                }

                // Fila total
                table.Cell().Background(ColorAccent).Padding(4)
                    .Text("Total gastos comunes").FontColor(ColorWhite).Bold();
                table.Cell().Background(ColorAccent).Padding(4).AlignRight()
                    .Text(FormatCurrency(total)).FontColor(ColorWhite).Bold();
            });
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(c => ComposeSignatureBlock(c, approver));
            row.ConstantItem(40);
            row.RelativeItem().Element(c => ComposeSignatureBlock(c, publisher));
        });
    }

    private static void ComposeSignatureBlock(IContainer container, SettlementSignature? signature)
    {
        if (signature is null) return;

        container.AlignCenter().Column(col =>
        {
            col.Item().Height(50).AlignCenter().AlignBottom().Element(img =>
            {
                if (signature.Image is { Length: > 0 }) img.Image(signature.Image).FitArea();
            });
            col.Item().PaddingTop(2).LineHorizontal(0.75f).LineColor(ColorGray);
            col.Item().PaddingTop(3).AlignCenter().Text(signature.Name).Bold().FontColor(ColorPrimary);
            col.Item().AlignCenter().Text(signature.Title).FontColor(ColorGray);
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
