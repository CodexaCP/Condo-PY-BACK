using Condo.Application.Localization;
using Condo.Application.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

/// <summary>
/// Certificado de deudas y créditos de una unidad. El texto usa el vocabulario del país de la empresa (consorcio o condominio, expensas o
/// gastos comunes, unidad funcional o inmueble) y los importes salen en la moneda de la empresa.
/// </summary>
public sealed class DebtCertificatePdfDocument(
    DebtCertificateDto data,
    CurrencyInfo currency,
    IReadOnlyDictionary<string, string> glossary) : IDocument
{
    private const string ColorPrimary = "#1385B6";
    private const string ColorGray = "#637b88";
    private const string ColorBorder = "#d7e5ea";
    private const string ColorDebt = "#c94d3f";
    private const string ColorGreen = "#2f8f3a";
    private const string ColorWhite = "#ffffff";

    private string Term(string key, string fallback) => glossary.TryGetValue(key, out var v) ? v : fallback;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Certificado de deudas y créditos {data.UnitCode} - Expenses",
        Author = "EXPENSES"
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40, Unit.Point);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9.5f));
            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeBody);
            page.Footer().AlignRight().Text(t =>
            {
                t.Span("Expenses · Certificado de deudas y créditos · ").FontColor(ColorGray).FontSize(7.5f);
                t.CurrentPageNumber().FontColor(ColorGray).FontSize(7.5f);
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(10).Column(col =>
        {
            col.Item().Text(data.BuildingLegalName ?? data.BuildingName).FontSize(15).Bold().FontColor(ColorPrimary);
            col.Item().Text("Certificado de deudas y créditos").FontSize(12).FontColor(ColorGray);
            col.Item().PaddingTop(4).LineHorizontal(1.5f).LineColor(ColorPrimary);
        });
    }

    private void ComposeBody(IContainer container)
    {
        var unit = Term("unidad", "Unidad");
        var consortium = Term("consorcio", "Consorcio");
        var expensas = Term("expensas", "Expensas").ToLowerInvariant();
        var issued = data.IssuedAtUtc.ToLocalTime();

        container.Column(col =>
        {
            col.Spacing(8);

            col.Item().Text(t =>
            {
                t.Span("A la fecha de emisión (").FontColor(ColorGray);
                t.Span($"{issued:dd/MM/yyyy HH:mm}").Bold();
                t.Span($"), la administración certifica el estado de cuenta de la {unit.ToLowerInvariant()} ").FontColor(ColorGray);
                t.Span(data.UnitCode).Bold();
                t.Span($" del {consortium.ToLowerInvariant()} ").FontColor(ColorGray);
                t.Span(data.BuildingName).Bold();
                t.Span($", ubicado en {data.BuildingAddress}{(string.IsNullOrWhiteSpace(data.Jurisdiction) ? string.Empty : $" ({data.Jurisdiction})")}.").FontColor(ColorGray);
            });

            if (data.Owners.Count > 0)
                col.Item().Text($"Propietario(s) registrado(s): {string.Join(", ", data.Owners)}.");

            if (!string.IsNullOrWhiteSpace(data.BuildingTaxId))
                col.Item().Text($"Identificación fiscal del {consortium.ToLowerInvariant()}: {data.BuildingTaxId}.");

            col.Item().PaddingTop(4).Element(ComposeLines);

            col.Item().PaddingTop(6).Row(r =>
            {
                r.RelativeItem().Text("Total adeudado").Bold().FontSize(11);
                r.AutoItem().Text(Money.Format(data.TotalDebt, currency)).Bold().FontSize(11)
                    .FontColor(data.HasDebt ? ColorDebt : ColorGreen);
            });

            col.Item().Row(r =>
            {
                r.RelativeItem().Text("Saldo a favor del propietario en la administración").FontColor(ColorGray);
                r.AutoItem().Text(Money.Format(data.OwnerCreditBalance, currency)).FontColor(ColorGray);
            });

            col.Item().PaddingTop(2).Text(data.HasDebt
                ? $"La {unit.ToLowerInvariant()} registra {expensas} y recargos pendientes de pago por el importe indicado."
                : $"La {unit.ToLowerInvariant()} no registra {expensas} ni recargos pendientes de pago a la fecha de emisión.");

            col.Item().Text("El saldo a favor pertenece al propietario y puede incluir importes generados por otras unidades. " +
                            "Este certificado refleja los registros de la administración a la fecha indicada; los pagos acreditados después no figuran.")
                .FontSize(8).FontColor(ColorGray);

            col.Item().PaddingTop(28).Column(sign =>
            {
                sign.Item().Width(220).LineHorizontal(0.7f).LineColor(ColorGray);
                sign.Item().Text(data.AdministratorName ?? data.CompanyName).Bold();
                sign.Item().Text(string.IsNullOrWhiteSpace(data.AdministratorRegistryNumber)
                    ? "Administración"
                    : $"Administración · Registro N.º {data.AdministratorRegistryNumber}").FontColor(ColorGray).FontSize(8.5f);
            });
        });
    }

    private void ComposeLines(IContainer container)
    {
        if (data.Lines.Count == 0)
        {
            container.Text("Sin períodos con saldo pendiente.").FontColor(ColorGray);
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
            });

            table.Header(h =>
            {
                foreach (var title in new[] { "Período", "Vencimiento", "Emitido", "Pagado", "Pendiente" })
                    h.Cell().Background(ColorPrimary).Padding(5).Text(title).FontColor(ColorWhite).Bold();
            });

            foreach (var line in data.Lines)
            {
                table.Cell().BorderBottom(0.5f).BorderColor(ColorBorder).Padding(4).Text(line.PeriodName);
                table.Cell().BorderBottom(0.5f).BorderColor(ColorBorder).Padding(4).Text($"{line.DueDate:dd/MM/yyyy}");
                table.Cell().BorderBottom(0.5f).BorderColor(ColorBorder).Padding(4).AlignRight().Text(Money.Format(line.Charged, currency));
                table.Cell().BorderBottom(0.5f).BorderColor(ColorBorder).Padding(4).AlignRight().Text(Money.Format(line.Paid, currency));
                table.Cell().BorderBottom(0.5f).BorderColor(ColorBorder).Padding(4).AlignRight()
                    .Text(Money.Format(line.Balance, currency)).FontColor(ColorDebt).Bold();
            }
        });
    }
}
