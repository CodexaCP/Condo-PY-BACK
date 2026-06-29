using Condo.Application.Models;
using Condo.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public sealed class AccountStatementPdfDocument(
    string unitCode,
    string buildingName,
    IReadOnlyList<AccountStatementPeriodDto> statements) : IDocument
{
    private const string ColorPrimary = "#1385B6";
    private const string ColorAccent  = "#1AB7AF";
    private const string ColorGreen   = "#6AC64A";
    private const string ColorGray    = "#637b88";
    private const string ColorBorder  = "#d7e5ea";
    private const string ColorRowAlt  = "#f4f9fc";
    private const string ColorWhite   = "#ffffff";
    private const string ColorDebt    = "#c94d3f";
    private const string ColorDebtBg  = "#fdf1f0";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Estado de Cuenta {unitCode} - CONDOPY",
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
        var totalCharged  = statements.Sum(x => x.TotalCharges);
        var totalPaid     = statements.Sum(x => x.TotalPayments);
        var runningBalance = statements.FirstOrDefault()?.RunningBalance ?? 0m;

        container.PaddingBottom(10).Column(col =>
        {
            // Logo + unidad
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("CONDOPY").FontSize(18).Bold().FontColor(ColorPrimary);
                    c.Item().Text("Estado de cuenta por unidad").FontSize(11).FontColor(ColorAccent);
                    c.Item().PaddingTop(2).Text($"{unitCode}  ·  {buildingName}").FontSize(8.5f).FontColor(ColorGray);
                });
                row.AutoItem().AlignRight().AlignBottom()
                    .Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}").FontColor(ColorGray).FontSize(8);
            });

            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(ColorPrimary);

            // Badges resumen
            col.Item().PaddingTop(8).Row(r =>
            {
                r.RelativeItem().Element(c => Badge(c, "Saldo acumulado", FormatGs(runningBalance), runningBalance > 0 ? ColorDebt : ColorGreen));
                r.ConstantItem(8);
                r.RelativeItem().Element(c => Badge(c, "Total cargado",   FormatGs(totalCharged),   ColorPrimary));
                r.ConstantItem(8);
                r.RelativeItem().Element(c => Badge(c, "Total pagado",    FormatGs(totalPaid),      ColorAccent));
                r.ConstantItem(8);
                r.RelativeItem().Element(c => Badge(c, "Periodos",        statements.Count.ToString(), ColorGray));
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
                c.RelativeColumn(2.0f); // Periodo
                c.RelativeColumn(1.2f); // Estado
                c.RelativeColumn(1.3f); // Cargado
                c.RelativeColumn(1.3f); // Pagado
                c.RelativeColumn(1.2f); // Saldo periodo
                c.RelativeColumn(1.2f); // Saldo anterior
                c.RelativeColumn(1.2f); // Saldo acumulado
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
                Head("Estado");
                Head("Cargado",       right: true);
                Head("Pagado",        right: true);
                Head("Saldo periodo", right: true);
                Head("Saldo anterior",right: true);
                Head("Acumulado",     right: true);
            });

            // Rows — statements already newest-first; show in chronological order for reading flow
            var ordered = statements.OrderBy(x => x.Year).ThenBy(x => x.Month).ToList();
            var isOdd = true;
            foreach (var s in ordered)
            {
                var bg = isOdd ? ColorWhite : ColorRowAlt;
                isOdd = !isOdd;

                var hasDebt      = s.Balance > 0;
                var hasAccumDebt = s.RunningBalance > 0;
                var statusLabel  = s.Status == ExpensePeriodStatus.Published ? "Publicado"
                                 : s.Status == ExpensePeriodStatus.Closed    ? "Cerrado"
                                 : "Borrador";
                var statusColor  = s.Status == ExpensePeriodStatus.Published ? ColorAccent
                                 : s.Status == ExpensePeriodStatus.Closed    ? ColorGreen
                                 : ColorGray;

                // Periodo
                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .Text(s.ExpensePeriodName).Bold().FontColor(ColorPrimary);

                // Estado
                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .Text(statusLabel).FontColor(statusColor).Bold();

                // Cargado
                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .AlignRight().Text(FormatGs(s.TotalCharges)).FontColor("#0d2a38");

                // Pagado
                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .AlignRight().Text(FormatGs(s.TotalPayments)).FontColor(ColorAccent);

                // Saldo periodo
                var periodCell = table.Cell().Background(hasDebt ? ColorDebtBg : bg)
                    .BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5).AlignRight();
                var periodTxt = periodCell.Text(FormatGs(s.Balance)).FontColor(hasDebt ? ColorDebt : ColorGreen);
                if (hasDebt) periodTxt.Bold();

                // Saldo anterior
                table.Cell().Background(bg).BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5)
                    .AlignRight().Text(FormatGs(s.PreviousBalance)).FontColor(ColorGray);

                // Acumulado
                var accumCell = table.Cell().Background(hasAccumDebt ? ColorDebtBg : bg)
                    .BorderBottom(0.3f).BorderColor(ColorBorder).Padding(5).AlignRight();
                var accumTxt = accumCell.Text(FormatGs(s.RunningBalance)).FontColor(hasAccumDebt ? ColorDebt : ColorGreen);
                if (hasAccumDebt) accumTxt.Bold();
            }
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text($"CONDOPY · Estado de cuenta: {unitCode} · {buildingName}").FontColor(ColorGray).FontSize(7.5f);
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
