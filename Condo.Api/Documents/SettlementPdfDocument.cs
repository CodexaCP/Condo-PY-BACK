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
    byte[]? backgroundImage = null,
    string? fieldPositionsJson = null,
    bool hideFrame = true) : IDocument
{
    private sealed record FieldOffset(float Dx, float Dy, float? FontSize = null);

    // Posicion base de cada bloque sobre el papel propio (puntos PDF, desde la esquina superior izquierda).
    // Las keys, x, top y tamanos tienen que coincidir con FIELDS de settlement-calibration-page.component.ts:
    // si se agrega un bloque calibrable aca, hay que agregarlo alla tambien para poder arrastrarlo.
    private sealed record FieldDefault(float X, float Top, float Size);

    private static readonly IReadOnlyDictionary<string, FieldDefault> FieldDefaults = new Dictionary<string, FieldDefault>
    {
        ["titulo"] = new(195, 86, 11),
        ["edificio"] = new(66, 100, 8),
        ["periodo"] = new(215, 112, 10),
        ["columnas"] = new(385, 128, 6.5f),
        ["cuerpo"] = new(50, 145, 8),
        ["control"] = new(60, 668, 8),
        ["firmas"] = new(50, 725, 9),
        ["pie"] = new(50, 815, 7)
    };

    public static IReadOnlyCollection<string> CalibratableKeys => FieldDefaults.Keys.ToList();

    private const float PageWidth = 595.2756f;
    private const float PageHeight = 841.8898f;
    private const float BlockWidth = 495f;
    private const float PaperBodyWidth = 495f;   // proveedor 120 + concepto 215 + dos columnas de monto de 80
    private const float PaperAmountWidth = 80f;

    private readonly Dictionary<string, FieldOffset> offsets = ParseOffsets(fieldPositionsJson);

    private static readonly System.Text.Json.JsonSerializerOptions OffsetJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static Dictionary<string, FieldOffset> ParseOffsets(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, FieldOffset>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, FieldOffset>>(json, OffsetJsonOptions)
                   ?? new Dictionary<string, FieldOffset>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new Dictionary<string, FieldOffset>();
        }
    }

    // dy positivo = hacia arriba (mismo criterio que la calibracion de la factura).
    private (float X, float Top, float Size) Place(string key)
    {
        var d = FieldDefaults[key];
        offsets.TryGetValue(key, out var o);
        return (d.X + (o?.Dx ?? 0), d.Top - (o?.Dy ?? 0), o?.FontSize is > 0 ? o.FontSize.Value : d.Size);
    }

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
        if (backgroundImage is not null)
        {
            ComposeOnPaper(container);
            return;
        }

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

    // Sobre el papel propio del edificio la liquidacion sale como planilla (proveedor, concepto y dos columnas
    // de monto: fondos de reservas y gastos comunes). Cada bloque va en una posicion absoluta calibrable sobre
    // una hoja A4; solo el cuerpo (ingresos, gastos y totales) crece y, si no entra, sigue en la hoja siguiente
    // en la misma zona del papel. El papel ya trae su marco: por defecto solo se dibuja el texto.
    private void ComposeOnPaper(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(8).FontColor(p.Primary));
            page.Background().Image(backgroundImage!).FitArea();

            page.Content().Layers(layers =>
            {
                var (bodyX, bodyTop, bodySize) = Place("cuerpo");
                var lowest = Place("pie").Top;
                if (hasSignatures) lowest = Math.Min(lowest, Place("firmas").Top);
                lowest = Math.Min(lowest, Place("control").Top);
                var bottomReserve = Math.Max(20f, PageHeight - lowest + 6f);
                var rightPadding = Math.Max(0f, PageWidth - bodyX - PaperBodyWidth);

                layers.PrimaryLayer()
                    .PaddingLeft(bodyX).PaddingTop(bodyTop).PaddingRight(rightPadding).PaddingBottom(bottomReserve)
                    .DefaultTextStyle(x => x.FontSize(bodySize))
                    .Element(ComposePaperBody);
            });

            // Todo lo demas, cada bloque en su posicion y repetido en cada hoja (las capas del contenido
            // solo se dibujan en la primera).
            page.Foreground().Layers(fg =>
            {
                fg.PrimaryLayer().Text(string.Empty);

                var titulo = Place("titulo");
                fg.Layer().TranslateX(titulo.X).TranslateY(titulo.Top)
                    .Text("LIQUIDACIÓN EXPENSAS COMUNES").FontSize(titulo.Size).Bold();

                var edificio = Place("edificio");
                fg.Layer().TranslateX(edificio.X).TranslateY(edificio.Top)
                    .Text(summary.BuildingName.ToUpperInvariant()).FontSize(edificio.Size).Bold();

                var periodo = Place("periodo");
                fg.Layer().TranslateX(periodo.X).TranslateY(periodo.Top)
                    .Text($"MES: {summary.ExpensePeriodName.ToUpperInvariant()}").FontSize(periodo.Size).Bold();

                var columnas = Place("columnas");
                fg.Layer().TranslateX(columnas.X).TranslateY(columnas.Top)
                    .Width(PaperAmountWidth * 2)
                    .DefaultTextStyle(x => x.FontSize(columnas.Size).Bold())
                    .Row(row =>
                    {
                        row.ConstantItem(PaperAmountWidth).AlignCenter().Text("FONDOS DE RESERVA");
                        row.ConstantItem(PaperAmountWidth).AlignCenter().Text("GASTOS COMUNES");
                    });

                var control = Place("control");
                fg.Layer().TranslateX(control.X).TranslateY(control.Top)
                    .Width(BlockWidthAt(control.X, 260))
                    .DefaultTextStyle(x => x.FontSize(control.Size))
                    .Element(ComposePaperControl);

                if (hasSignatures)
                {
                    var firmas = Place("firmas");
                    fg.Layer().TranslateX(firmas.X).TranslateY(firmas.Top)
                        .Width(BlockWidthAt(firmas.X))
                        .DefaultTextStyle(x => x.FontSize(firmas.Size))
                        .Element(ComposeSignatures);
                }

                var pie = Place("pie");
                fg.Layer().TranslateX(pie.X).TranslateY(pie.Top)
                    .Width(BlockWidthAt(pie.X))
                    .DefaultTextStyle(x => x.FontSize(pie.Size))
                    .Element(ComposeFooter);
            });
        });
    }

    // Gastos y ingresos linea por linea; si no vinieron (p. ej. una liquidacion sin detalle) se arman con los
    // totales por categoria.
    private IReadOnlyList<SettlementExpenseLineDto> ExpenseRows() =>
        summary.ExpenseLines.Count > 0
            ? summary.ExpenseLines
            : summary.CategoryTotals.SelectMany(c => c.Items.Select(i => new SettlementExpenseLineDto
            {
                Description = i.Description,
                Amount = i.Amount,
                IsReserveFund = c.Category == "ReserveFund"
            })).ToList();

    private void ComposePaperBody(IContainer container)
    {
        var expenses = ExpenseRows();
        var reserveTotal = expenses.Where(x => x.IsReserveFund).Sum(x => x.Amount);
        var commonTotal = expenses.Where(x => !x.IsReserveFund).Sum(x => x.Amount);
        var lineColor = "#000000";

        IContainer Cell(TableDescriptor t, uint span = 1) => span > 1
            ? Styled(t.Cell().ColumnSpan(span))
            : Styled(t.Cell());

        // Con marco propio del sistema (papel liso) cada celda lleva su linea fina; sobre papel con marco, nada.
        IContainer Styled(IContainer c) => hideFrame
            ? c.PaddingVertical(2).PaddingHorizontal(3)
            : c.BorderBottom(0.5f).BorderColor(lineColor).PaddingVertical(2).PaddingHorizontal(3);

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(120);
                c.RelativeColumn(215);
                c.ConstantColumn(PaperAmountWidth);
                c.ConstantColumn(PaperAmountWidth);
            });

            // Ingresos del periodo (saldo acumulado, alquileres, intereses...) y el total disponible.
            foreach (var income in summary.IncomeLines)
            {
                Cell(table).Text(income.Label.ToUpperInvariant());
                Cell(table).Text(income.Description);
                Cell(table).Text(string.Empty);
                Cell(table).AlignRight().Text(FormatNumber(income.Amount));
            }

            if (summary.IncomeLines.Count > 0 || summary.TotalBuildingIncomes > 0)
            {
                Cell(table, 3).Text("TOTAL PARA GASTOS").Bold();
                Cell(table).AlignRight().Text(FormatNumber(summary.TotalBuildingIncomes)).Bold();
                table.Cell().ColumnSpan(4).PaddingTop(8).Text(" ").FontSize(1);
            }

            // Un gasto por linea: el del fondo de reserva va en su columna.
            foreach (var expense in expenses)
            {
                Cell(table).Text(expense.Supplier.ToUpperInvariant());
                Cell(table).Text(expense.Description.ToUpperInvariant());
                Cell(table).AlignRight().Text(expense.IsReserveFund ? FormatNumber(expense.Amount) : string.Empty);
                Cell(table).AlignRight().Text(expense.IsReserveFund ? string.Empty : FormatNumber(expense.Amount));
            }

            table.Cell().ColumnSpan(4).PaddingTop(8).Text(" ").FontSize(1);

            Cell(table, 2).Text("TOTAL GASTOS DEL MES").Bold();
            Cell(table).AlignRight().Text(reserveTotal > 0 ? FormatNumber(reserveTotal) : string.Empty).Bold();
            Cell(table).AlignRight().Text(FormatNumber(commonTotal)).Bold();

            Cell(table, 3).Text("MONTO NETO A DISTRIBUIR").Bold();
            Cell(table).AlignRight().Text(FormatNumber(summary.NetCommonAmount)).Bold();
        });
    }

    private void ComposePaperControl(IContainer container)
    {
        var emitted = (summary.GeneratedAtUtc ?? DateTime.UtcNow).ToLocalTime();
        container.Column(col =>
        {
            col.Item().Text($"Fecha de emisión: {emitted:dd/MM/yyyy}");
            col.Item().Text($"Vigencia: {periodStartDate} al {periodEndDate}");
            col.Item().Text($"Vencimiento: {periodDueDate}");
        });
    }

    private static string FormatNumber(decimal value) => value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-PY"));

    // Ancho de los bloques: el estandar, recortado para que nunca se salgan de la hoja si se arrastran a la derecha.
    private static float BlockWidthAt(float x, float width = BlockWidth) => Math.Max(40f, Math.Min(width, PageWidth - x - 4));

    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(10).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("CONDOPY").FontSize(16).Bold().FontColor(p.Primary);
                    c.Item().Text("Liquidacion de Expensas").FontSize(11).FontColor(p.Accent);
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
