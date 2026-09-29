using Condo.Application.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public sealed record SettlementSignature(string Name, string Title, byte[]? Image);

/// <summary>
/// Liquidacion de expensas A4. Con el modelo estandar sale con los colores de CONDOPY; con el modelo propio
/// del edificio (backgroundImage, cargado por el superadmin) el papel ya trae su diseno, asi que se imprime
/// sobre el a pagina completa y el texto va en negro/gris, sin marca ni colores de CONDOPY.
/// </summary>
public sealed class SettlementPdfDocument(
    ExpenseSettlementSummaryDto summary,
    string periodStartDate,
    string periodEndDate,
    string periodDueDate,
    SettlementSignature? approver = null,
    SettlementSignature? president = null,
    SettlementSignature? publisher = null,
    bool standardTemplate = true,
    byte[]? backgroundImage = null) : IDocument
{
    private sealed record Palette(
        string Primary, string Accent, string Gray, string RowAlt, string CatHeader,
        string HeaderFill, string HeaderText, string TotalFill, string TotalText);

    private static readonly Palette Standard = new(
        Primary: "#1385B6", Accent: "#1AB7AF", Gray: "#637b88", RowAlt: "#f4f9fc", CatHeader: "#e8f4f8",
        HeaderFill: "#1385B6", HeaderText: "#ffffff", TotalFill: "#1AB7AF", TotalText: "#ffffff");

    private static readonly Palette OwnPaper = new(
        Primary: "#000000", Accent: "#404040", Gray: "#404040", RowAlt: "#f5f5f5", CatHeader: "#ededed",
        HeaderFill: "#e4e4e4", HeaderText: "#000000", TotalFill: "#cfcfcf", TotalText: "#000000");

    private const string ColorWhite = "#ffffff";

    private readonly Palette p = standardTemplate ? Standard : OwnPaper;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Liquidacion {summary.ExpensePeriodName} - {summary.BuildingName}",
        Author = "CONDOPY"
    };

    private bool hasSignatures => approver is not null || president is not null || publisher is not null;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32, Unit.Point);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));

            // El fondo va fuera del margen: el modelo del edificio cubre toda la hoja y el contenido
            // se acomoda dentro del margen, pagina por pagina.
            if (backgroundImage is not null)
                page.Background().Image(backgroundImage).FitArea();

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
                    if (standardTemplate)
                    {
                        c.Item().Text("CONDOPY").FontSize(16).Bold().FontColor(p.Primary);
                        c.Item().Text("Liquidacion de Expensas").FontSize(11).FontColor(p.Accent);
                    }
                    else
                    {
                        c.Item().Text("Liquidacion de Expensas").FontSize(14).Bold().FontColor(p.Primary);
                    }
                });
                row.ConstantItem(200).AlignRight().Column(c =>
                {
                    c.Item().Text(summary.BuildingName).Bold().FontColor(p.Primary);
                    c.Item().Text(summary.ExpensePeriodName).FontColor(p.Gray);
                });
            });

            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text($"Vigencia: {periodStartDate} al {periodEndDate}").FontColor(p.Gray);
                row.RelativeItem().AlignRight().Text($"Vencimiento: {periodDueDate}").FontColor(p.Gray);
            });

            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(p.Primary);
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
            col.Item().PaddingBottom(6).Text("Resumen de liquidacion").Bold().FontColor(p.Primary);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    header.Cell().Background(p.HeaderFill).Padding(5)
                        .Text("Concepto").FontColor(p.HeaderText).Bold();
                    header.Cell().Background(p.HeaderFill).Padding(5)
                        .AlignRight().Text("Monto (Gs.)").FontColor(p.HeaderText).Bold();
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

    private void AddSummaryRow(TableDescriptor table, string label, decimal amount, bool isTotal, bool isSubrow = false)
    {
        var bg = isTotal ? p.TotalFill : (isSubrow ? p.RowAlt : ColorWhite);
        var textColor = isTotal ? p.TotalText : p.Primary;

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
        ["Ande"] = "ANDE",
        ["Essap"] = "ESSAP",
        ["InternetPhone"] = "Internet y telefonia",
        ["Other"] = "Otro"
    };

    private void ComposeCategoryTable(IContainer container)
    {
        var totals = summary.CategoryTotals;
        var expenseCount = totals.Sum(x => x.ExpenseCount);
        var total = totals.Sum(x => x.Amount);

        container.Column(col =>
        {
            col.Item().PaddingBottom(4).Text("Gastos comunes por categoria").Bold().FontColor(p.Primary);
            col.Item().PaddingBottom(8)
                .Text($"{expenseCount} gastos · {totals.Count} categorias · {FormatCurrency(total)}")
                .FontColor(p.Gray);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.ConstantColumn(110);
                });

                table.Header(header =>
                {
                    header.Cell().Background(p.HeaderFill).Padding(4).Text("Concepto").FontColor(p.HeaderText).Bold();
                    header.Cell().Background(p.HeaderFill).Padding(4).AlignRight().Text("Monto (Gs.)").FontColor(p.HeaderText).Bold();
                });

                var itemAlt = true;
                foreach (var cat in totals)
                {
                    var catLabel = CategoryLabels.GetValueOrDefault(cat.Category, cat.Category);

                    // Fila cabecera de categoría
                    table.Cell().Background(p.CatHeader).Padding(4)
                        .Text(catLabel).Bold().FontColor(p.Primary);
                    table.Cell().Background(p.CatHeader).Padding(4).AlignRight()
                        .Text(FormatCurrency(cat.Amount)).Bold().FontColor(p.Primary);

                    // Sub-filas: un gasto por fila
                    foreach (var item in cat.Items)
                    {
                        var bg = itemAlt ? ColorWhite : p.RowAlt;
                        itemAlt = !itemAlt;

                        table.Cell().Background(bg).PaddingLeft(14).PaddingVertical(3)
                            .Text(t =>
                            {
                                t.Span("· ").FontColor(p.Accent);
                                t.Span(item.Description).FontColor(p.Gray);
                            });
                        table.Cell().Background(bg).PaddingRight(4).PaddingVertical(3).AlignRight()
                            .Text(FormatCurrency(item.Amount)).FontColor(p.Gray);
                    }
                }

                // Fila total
                table.Cell().Background(p.TotalFill).Padding(4)
                    .Text("Total gastos comunes").FontColor(p.TotalText).Bold();
                table.Cell().Background(p.TotalFill).Padding(4).AlignRight()
                    .Text(FormatCurrency(total)).FontColor(p.TotalText).Bold();
            });
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(c => ComposeSignatureBlock(c, approver));
            row.ConstantItem(24);
            row.RelativeItem().Element(c => ComposeSignatureBlock(c, president));
            row.ConstantItem(24);
            row.RelativeItem().Element(c => ComposeSignatureBlock(c, publisher));
        });
    }

    private void ComposeSignatureBlock(IContainer container, SettlementSignature? signature)
    {
        if (signature is null) return;

        container.AlignCenter().Column(col =>
        {
            col.Item().Height(50).AlignCenter().AlignBottom().Element(img =>
            {
                if (signature.Image is { Length: > 0 }) img.Image(signature.Image).FitArea();
            });
            col.Item().PaddingTop(2).LineHorizontal(0.75f).LineColor(p.Gray);
            col.Item().PaddingTop(3).AlignCenter().Text(signature.Name).Bold().FontColor(p.Primary);
            col.Item().AlignCenter().Text(signature.Title).FontColor(p.Gray);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                if (standardTemplate)
                    t.Span("Generado por CONDOPY").FontColor(p.Gray);
                if (summary.GeneratedAtUtc.HasValue)
                {
                    var stamp = summary.GeneratedAtUtc.Value.ToString("dd/MM/yyyy HH:mm");
                    t.Span(standardTemplate ? $" · {stamp} UTC" : $"Generado el {stamp} UTC").FontColor(p.Gray);
                }
            });
            row.ConstantItem(80).AlignRight().Text(t =>
            {
                t.CurrentPageNumber().FontColor(p.Gray);
                t.Span(" / ").FontColor(p.Gray);
                t.TotalPages().FontColor(p.Gray);
            });
        });
    }

    private static string FormatCurrency(decimal value) => $"Gs. {value:N0}";
}
