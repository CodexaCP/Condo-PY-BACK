using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

/// <summary>
/// Informe para la asamblea de propietarios (PDF): saldos, estado de resultados por rubro, ejecucion presupuestaria, fondo de reserva,
/// morosidad por antiguedad, cuentas por pagar y las notas del administrador. Es el mismo informe que las pantallas de Finanzas: solo
/// presenta los datos de <see cref="AssemblyReportDto"/>. No lista unidades ni propietarios.
/// </summary>
public sealed class AssemblyReportPdfDocument(AssemblyReportDto report) : IDocument
{
    private const string ColorPrimary = CondoPdfColors.Primary;
    private const string ColorAccent = CondoPdfColors.Accent;
    private const string ColorGray = CondoPdfColors.Gray;
    private const string ColorBorder = CondoPdfColors.Border;
    private const string ColorRowAlt = CondoPdfColors.RowAlt;
    private const string ColorWhite = CondoPdfColors.White;
    private const string ColorDark = "#0d2a38";
    private const string ColorGreen = "#2f8f2f";
    private const string ColorAmber = "#b7791f";
    private const string ColorRed = "#c94d3f";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Informe de asamblea {report.BuildingName} - CONDOPY",
        Author = "CONDOPY"
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32, Unit.Point);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9).FontColor(ColorDark));
            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeBody);
            page.Footer().Element(ComposeFooter);
        });
    }

    // ── Encabezado y pie ──────────────────────────────────────────────────────

    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(8).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("CONDOPY").FontSize(18).Bold().FontColor(ColorPrimary);
                    c.Item().Text("Informe para la asamblea").FontSize(11).FontColor(ColorAccent);
                    c.Item().PaddingTop(2).Text($"{report.BuildingName}  ·  {report.From:dd/MM/yyyy} – {report.To:dd/MM/yyyy}")
                        .FontSize(8).FontColor(ColorGray);
                });
                row.AutoItem().AlignRight().AlignBottom()
                    .Text($"Generado: {report.GeneratedAt:dd/MM/yyyy HH:mm}").FontColor(ColorGray).FontSize(8);
            });
            col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(ColorPrimary);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text($"CONDOPY · Informe de asamblea: {report.BuildingName}").FontColor(ColorGray).FontSize(7.5f);
            row.ConstantItem(80).AlignRight().Text(t =>
            {
                t.CurrentPageNumber().FontColor(ColorGray).FontSize(7.5f);
                t.Span(" / ").FontColor(ColorGray).FontSize(7.5f);
                t.TotalPages().FontColor(ColorGray).FontSize(7.5f);
            });
        });
    }

    // ── Cuerpo ────────────────────────────────────────────────────────────────

    private void ComposeBody(IContainer container)
    {
        container.PaddingTop(4).Column(col =>
        {
            col.Spacing(14);
            col.Item().Element(ComposeSummary);
            col.Item().Element(ComposeAccounts);
            col.Item().Element(ComposeResults);
            if (report.Budget is not null) col.Item().Element(ComposeBudget);
            if (report.Reserve is not null) col.Item().Element(ComposeReserve);
            col.Item().Element(ComposeReceivables);
            col.Item().Element(ComposePayables);
            if (report.Warnings.Count > 0) col.Item().Element(ComposeWarnings);
            if (!string.IsNullOrWhiteSpace(report.Notes)) col.Item().Element(ComposeNotes);
        });
    }

    private void ComposeSummary(IContainer container)
    {
        container.Row(r =>
        {
            r.RelativeItem().Element(c => Badge(c, "Saldo inicial", Gs(report.OpeningBalance), ColorGray));
            r.ConstantItem(6);
            r.RelativeItem().Element(c => Badge(c, "Ingresos", Gs(report.TotalIncome), ColorAccent));
            r.ConstantItem(6);
            r.RelativeItem().Element(c => Badge(c, "Egresos", Gs(report.TotalExpense), ColorRed));
            r.ConstantItem(6);
            r.RelativeItem().Element(c => Badge(c, "Saldo final", Gs(report.ClosingBalance), ColorPrimary));
        });
    }

    private void ComposeAccounts(IContainer container)
    {
        Section(container, "Saldos por cuenta", $"Del {report.From:dd/MM/yyyy} al {report.To:dd/MM/yyyy}.", body =>
        {
            body.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2.6f);
                    c.RelativeColumn(1.3f);
                    c.RelativeColumn(1.3f);
                    c.RelativeColumn(1.3f);
                    c.RelativeColumn(1.3f);
                });
                HeaderRow(table, "Cuenta", "Saldo inicial", "Entradas", "Salidas", "Saldo final");
                var alt = false;
                foreach (var a in report.Accounts)
                {
                    var bg = Background(ref alt);
                    Cell(table, bg, a.Name);
                    Money(table, bg, a.Opening);
                    Money(table, bg, a.Inflows);
                    Money(table, bg, a.Outflows);
                    Money(table, bg, a.Closing);
                }

                TotalRow(table, "Total", report.OpeningBalance, report.Accounts.Sum(a => a.Inflows), report.Accounts.Sum(a => a.Outflows), report.ClosingBalance);
            });
        });
    }

    private void ComposeResults(IContainer container)
    {
        Section(container, "Estado de resultados", "Criterio de caja: lo efectivamente cobrado y pagado en el rango.", body =>
        {
            body.Column(col =>
            {
                col.Spacing(10);
                col.Item().Element(c => RubroTable(c, "Ingresos", report.IncomeLines, report.TotalIncome, ColorAccent));
                col.Item().Element(c => RubroTable(c, "Egresos", report.ExpenseLines, report.TotalExpense, ColorRed));
                col.Item().Background(report.NetResult >= 0 ? ColorGreen : ColorRed).Padding(6).Row(r =>
                {
                    r.RelativeItem().Text(report.NetResult >= 0 ? "Superávit del período" : "Déficit del período").Bold().FontColor(ColorWhite);
                    r.AutoItem().Text(Gs(report.NetResult)).Bold().FontColor(ColorWhite);
                });
            });
        });
    }

    private void RubroTable(IContainer container, string title, IReadOnlyList<AssemblyReportRubroDto> lines, decimal total, string color)
    {
        container.Column(col =>
        {
            col.Item().Background(color).Padding(5).Text(title.ToUpperInvariant()).Bold().FontColor(ColorWhite).FontSize(9);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(60);
                    c.RelativeColumn(3f);
                    c.RelativeColumn(1.2f);
                });
                var alt = false;
                foreach (var l in lines)
                {
                    var bg = Background(ref alt);
                    Cell(table, bg, l.Code);
                    Cell(table, bg, l.Name);
                    Money(table, bg, l.Amount);
                }

                if (lines.Count == 0)
                {
                    table.Cell().ColumnSpan(3).Padding(6).Text("Sin movimientos en el rango.").FontColor(ColorGray).Italic();
                }

                table.Cell().ColumnSpan(2).Background(color).Padding(5).Text($"Total {title.ToLowerInvariant()}").Bold().FontColor(ColorWhite);
                table.Cell().Background(color).Padding(5).AlignRight().Text(Gs(total)).Bold().FontColor(ColorWhite);
            });
        });
    }

    private void ComposeBudget(IContainer container)
    {
        var budget = report.Budget!;
        var through = budget.AsOf;
        Section(container, "Ejecución presupuestaria",
            $"Acumulado del ejercicio {budget.FiscalYear} ({budget.FiscalYearStart:dd/MM/yyyy} al {through:dd/MM/yyyy}). Ingresos: {budget.IncomeBasis.ToLowerInvariant()}. Gastos: {budget.ExpenseBasis.ToLowerInvariant()}.", body =>
        {
            body.Column(col =>
            {
                col.Spacing(10);
                col.Item().Element(c => BudgetTable(c, "Ingresos", budget.IncomeLines, budget.IncomeTotals, ColorAccent));
                col.Item().Element(c => BudgetTable(c, "Gastos", budget.ExpenseLines, budget.ExpenseTotals, ColorRed));
            });
        });
    }

    private void BudgetTable(IContainer container, string title, IReadOnlyList<FinanceBudgetVsActualLineDto> lines, FinanceBudgetTotalsDto totals, string color)
    {
        var shown = lines.Where(l => l.YtdBudget != 0m || l.YtdActual != 0m).ToList();
        container.Column(col =>
        {
            col.Item().Background(color).Padding(5).Text(title.ToUpperInvariant()).Bold().FontColor(ColorWhite).FontSize(9);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2.8f);
                    c.RelativeColumn(1.3f);
                    c.RelativeColumn(1.3f);
                    c.RelativeColumn(1.3f);
                    c.RelativeColumn(0.8f);
                });
                HeaderRow(table, "Cuenta", "Presupuestado", "Real", "Desvío", "%");
                var alt = false;
                foreach (var l in shown)
                {
                    var bg = Background(ref alt);
                    Cell(table, bg, $"{l.Code} {l.Name}".Trim());
                    Money(table, bg, l.YtdBudget);
                    Money(table, bg, l.YtdActual);
                    Money(table, bg, l.YtdVariance, StatusColor(l.YtdStatus));
                    Cell(table, bg, l.YtdVariancePct is { } p ? $"{p:0.#}%" : "—", right: true, color: StatusColor(l.YtdStatus));
                }

                if (shown.Count == 0)
                {
                    table.Cell().ColumnSpan(5).Padding(6).Text("Sin renglones presupuestados ni movimientos.").FontColor(ColorGray).Italic();
                }

                TotalRow(table, "Total", totals.YtdBudget, totals.YtdActual, totals.YtdActual - totals.YtdBudget, null, color);
            });
        });
    }

    private void ComposeReserve(IContainer container)
    {
        var fund = report.Reserve!;
        Section(container, "Fondo de reserva",
            fund.ReserveFundPercentage is { } pct ? $"Cuenta «{fund.AccountName}». Aporte previsto: {pct:0.#}% de las expensas." : $"Cuenta «{fund.AccountName}».", body =>
        {
            body.Row(r =>
            {
                r.RelativeItem().Element(c => Badge(c, "Saldo inicial", Gs(fund.Opening), ColorGray));
                r.ConstantItem(6);
                r.RelativeItem().Element(c => Badge(c, "Aportes", Gs(fund.Contributions), ColorAccent));
                r.ConstantItem(6);
                r.RelativeItem().Element(c => Badge(c, "Usos", Gs(fund.Uses), ColorRed));
                r.ConstantItem(6);
                r.RelativeItem().Element(c => Badge(c, "Saldo final", Gs(fund.Closing), ColorPrimary));
            });
        });
    }

    private void ComposeReceivables(IContainer container)
    {
        var r = report.Receivables;
        Section(container, "Morosidad por antigüedad",
            $"Situación al {report.SnapshotDate:dd/MM/yyyy} (fecha de emisión), sobre las expensas publicadas. Resumen agregado: no se identifican unidades ni propietarios.", body =>
        {
            body.Column(col =>
            {
                col.Spacing(8);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => Badge(c, "Cobrado", $"{Gs(r.TotalCollected)}  ({r.CollectionRatePercentage:0.#}%)", ColorGreen));
                    row.ConstantItem(6);
                    row.RelativeItem().Element(c => Badge(c, "Pendiente", Gs(r.TotalPending), ColorAmber));
                    row.ConstantItem(6);
                    row.RelativeItem().Element(c => Badge(c, "Vencido", $"{Gs(r.OverdueAmount)}  ·  {(r.OverdueUnits == 1 ? "1 unidad" : $"{r.OverdueUnits} unidades")}", ColorRed));
                });
                col.Item().Element(c => AgingTable(c, r.Aging, "Unidades-período", r.OverdueAmount));
            });
        });
    }

    private void ComposePayables(IContainer container)
    {
        var p = report.Payables;
        Section(container, "Cuentas por pagar",
            $"Facturas de proveedores con vencimiento y sin pago registrado, al {report.SnapshotDate:dd/MM/yyyy} (fecha de emisión).", body =>
        {
            body.Column(col =>
            {
                col.Spacing(8);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => Badge(c, "A pagar", $"{Gs(p.PendingTotal)}  ·  {Facturas(p.PendingCount)}", ColorAmber));
                    row.ConstantItem(6);
                    row.RelativeItem().Element(c => Badge(c, "Vencido", $"{Gs(p.OverdueTotal)}  ·  {Facturas(p.OverdueCount)}", ColorRed));
                    row.ConstantItem(6);
                    row.RelativeItem().Element(c => Badge(c, "Vence en 7 días", Gs(p.DueNext7DaysTotal), ColorPrimary));
                });
                col.Item().Element(c => AgingTable(c, p.Aging, "Facturas", p.OverdueTotal));
            });
        });
    }

    private void AgingTable(IContainer container, IReadOnlyList<PayableAgingBucketDto> aging, string countLabel, decimal total)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(2f);
                c.RelativeColumn(1f);
                c.RelativeColumn(1.4f);
            });
            HeaderRow(table, "Antigüedad", countLabel, "Monto");
            var alt = false;
            foreach (var b in aging)
            {
                var bg = Background(ref alt);
                Cell(table, bg, b.Label);
                Cell(table, bg, b.Count.ToString(), right: true);
                Money(table, bg, b.Total);
            }

            table.Cell().ColumnSpan(2).Background(ColorPrimary).Padding(5).Text("Total vencido").Bold().FontColor(ColorWhite);
            table.Cell().Background(ColorPrimary).Padding(5).AlignRight().Text(Gs(total)).Bold().FontColor(ColorWhite);
        });
    }

    private void ComposeWarnings(IContainer container)
    {
        container.Border(0.5f).BorderColor(ColorAmber).Padding(7).Column(col =>
        {
            col.Item().Text("Observaciones").Bold().FontColor(ColorAmber);
            foreach (var w in report.Warnings) col.Item().Text($"• {w}").FontSize(8.5f);
        });
    }

    private void ComposeNotes(IContainer container)
    {
        Section(container, "Notas del administrador", null, body =>
        {
            body.Border(0.5f).BorderColor(ColorBorder).Padding(8).Text(report.Notes!).FontSize(9).LineHeight(1.3f);
        });
    }

    // ── Piezas comunes ────────────────────────────────────────────────────────

    private static void Section(IContainer container, string title, string? subtitle, Action<IContainer> body)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(4).Column(head =>
            {
                head.Item().Text(title).Bold().FontSize(11).FontColor(ColorPrimary);
                if (!string.IsNullOrWhiteSpace(subtitle)) head.Item().Text(subtitle).FontSize(7.5f).FontColor(ColorGray);
            });
            col.Item().Element(body);
        });
    }

    private static void Badge(IContainer c, string label, string value, string color)
    {
        c.Border(0.5f).BorderColor(color).Padding(6).Column(col =>
        {
            col.Item().Text(label).FontSize(7.5f).FontColor(color);
            col.Item().Text(value).Bold().FontSize(10).FontColor(color);
        });
    }

    private static string Background(ref bool alt)
    {
        var bg = alt ? ColorRowAlt : ColorWhite;
        alt = !alt;
        return bg;
    }

    private static void HeaderRow(TableDescriptor table, params string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = table.Cell().Background(CondoPdfColors.Tint).BorderBottom(0.5f).BorderColor(ColorBorder).Padding(5);
            (i == 0 ? cell : cell.AlignRight()).Text(headers[i]).Bold().FontSize(8).FontColor(ColorPrimary);
        }
    }

    private static void Cell(TableDescriptor table, string bg, string text, bool right = false, string? color = null)
    {
        var cell = table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5);
        (right ? cell.AlignRight() : cell).Text(text).FontColor(color ?? ColorDark);
    }

    private static void Money(TableDescriptor table, string bg, decimal value, string? color = null) =>
        Cell(table, bg, Gs(value), right: true, color: color);

    private static void TotalRow(TableDescriptor table, string label, decimal a, decimal b, decimal c, decimal? d, string color = ColorPrimary)
    {
        table.Cell().Background(color).Padding(5).Text(label).Bold().FontColor(ColorWhite);
        foreach (var value in new[] { a, b, c }.Concat(d.HasValue ? [d.Value] : []))
        {
            table.Cell().Background(color).Padding(5).AlignRight().Text(Gs(value)).Bold().FontColor(ColorWhite);
        }

        // Sin cuarto importe (la tabla del presupuesto), la columna del porcentaje queda vacia en el total.
        if (!d.HasValue) table.Cell().Background(color).Padding(5).Text(string.Empty);
    }

    private static string StatusColor(BudgetStatus status) => status switch
    {
        BudgetStatus.Red => ColorRed,
        BudgetStatus.Amber => ColorAmber,
        BudgetStatus.Green => ColorGreen,
        _ => ColorDark
    };


    private static string Facturas(int count) => count == 1 ? "1 factura" : $"{count} facturas";

    private static string Gs(decimal value) => $"Gs. {value:N0}";
}
