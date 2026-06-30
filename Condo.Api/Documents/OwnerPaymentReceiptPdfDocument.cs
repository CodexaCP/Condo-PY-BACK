using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public record OwnerPaymentReceiptUnitRow(string UnitCode, string BuildingName, decimal AllocatedAmount);

public record OwnerPaymentReceiptSettlementRow(
    string UnitCode, string Concept, int PeriodYear, int PeriodMonth, decimal Amount);

public record OwnerPaymentReceiptData(
    Guid Id,
    string Reference,
    string OwnerFullName,
    DateOnly PaymentDate,
    decimal DeclaredAmount,
    decimal ReviewedAmount,
    string Status,
    DateTime CreatedAtUtc,
    List<OwnerPaymentReceiptUnitRow> Units,
    List<OwnerPaymentReceiptSettlementRow> Settlements,
    decimal RemainingCredit);

public sealed class OwnerPaymentReceiptPdfDocument(OwnerPaymentReceiptData data) : IDocument
{
    private const string ColorPrimary = "#1385B6";
    private const string ColorAccent  = "#1AB7AF";
    private const string ColorGray    = "#637b88";
    private const string ColorBorder  = "#d7e5ea";
    private const string ColorRowAlt  = "#f4f9fc";
    private const string ColorWhite   = "#ffffff";
    private const string ColorCredit  = "#6AC64A";

    private static readonly Dictionary<string, string> StatusColors = new()
    {
        ["Pending"]     = "#f59e0b",
        ["UnderReview"] = "#1385B6",
        ["Approved"]    = "#6AC64A",
        ["Rejected"]    = "#ef4444"
    };

    public DocumentMetadata GetMetadata() => new()
    {
        Title  = $"Comprobante de propietario {data.Reference}",
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
                    c.Item().Text("Comprobante de pago del propietario").FontSize(11).FontColor(ColorAccent);
                });
                row.ConstantItem(200).AlignRight().Column(c =>
                {
                    c.Item().Text(data.Reference).Bold().FontFamily("Courier New").FontColor(ColorPrimary);
                    c.Item().Text($"#{data.Id.ToString()[..8].ToUpper()}").FontColor(ColorGray).FontSize(8);
                    c.Item().PaddingTop(3)
                        .Text(StatusLabel(data.Status)).Bold().FontSize(9)
                        .FontColor(StatusColors.GetValueOrDefault(data.Status, ColorGray));
                });
            });
            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(ColorPrimary);
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(14);
            col.Item().Element(ComposePaymentInfo);
            if (data.Units.Count > 0)
                col.Item().Element(ComposeUnitsTable);
            if (data.Settlements.Count > 0)
                col.Item().Element(ComposeSettlementsTable);
            col.Item().Element(ComposeSummary);
        });
    }

    private void ComposePaymentInfo(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn();
                c.RelativeColumn();
                c.RelativeColumn();
            });

            AddInfoCell(table, "Propietario",   data.OwnerFullName);
            AddInfoCell(table, "Fecha de pago", data.PaymentDate.ToString("dd/MM/yyyy"));
            AddInfoCell(table, "Enviado el",    data.CreatedAtUtc.ToString("dd/MM/yyyy HH:mm"));
            AddInfoCell(table, "Monto declarado",  FormatGs(data.DeclaredAmount));
            AddInfoCell(table, "Monto revisado",   data.ReviewedAmount > 0 ? FormatGs(data.ReviewedAmount) : "—");
            AddInfoCell(table, "Estado",           StatusLabel(data.Status));
        });
    }

    private void ComposeUnitsTable(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("Unidades incluidas").Bold().FontColor(ColorPrimary);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1);
                    c.RelativeColumn(2.5f);
                    c.RelativeColumn(1.5f);
                });
                table.Header(h =>
                {
                    h.Cell().Background(ColorPrimary).Padding(5).Text("Unidad").FontColor(ColorWhite).Bold();
                    h.Cell().Background(ColorPrimary).Padding(5).Text("Edificio").FontColor(ColorWhite).Bold();
                    h.Cell().Background(ColorPrimary).Padding(5).AlignRight().Text("Monto asignado (Gs.)").FontColor(ColorWhite).Bold();
                });
                var odd = true;
                foreach (var u in data.Units)
                {
                    var bg = odd ? ColorWhite : ColorRowAlt; odd = !odd;
                    table.Cell().Background(bg).Padding(5).Text(u.UnitCode).FontFamily("Courier New").FontColor(ColorPrimary);
                    table.Cell().Background(bg).Padding(5).Text(u.BuildingName).FontColor(ColorGray);
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(FormatGs(u.AllocatedAmount)).FontColor(ColorPrimary);
                }
            });
        });
    }

    private void ComposeSettlementsTable(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("Cargos liquidados").Bold().FontColor(ColorPrimary);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(0.8f);
                    c.RelativeColumn(0.6f);
                    c.RelativeColumn(2.5f);
                    c.RelativeColumn(1.5f);
                });
                table.Header(h =>
                {
                    h.Cell().Background(ColorPrimary).Padding(5).Text("Unidad").FontColor(ColorWhite).Bold();
                    h.Cell().Background(ColorPrimary).Padding(5).Text("Período").FontColor(ColorWhite).Bold();
                    h.Cell().Background(ColorPrimary).Padding(5).Text("Concepto").FontColor(ColorWhite).Bold();
                    h.Cell().Background(ColorPrimary).Padding(5).AlignRight().Text("Imputado (Gs.)").FontColor(ColorWhite).Bold();
                });
                var odd = true;
                foreach (var s in data.Settlements)
                {
                    var bg = odd ? ColorWhite : ColorRowAlt; odd = !odd;
                    var period = $"{s.PeriodMonth:D2}/{s.PeriodYear}";
                    table.Cell().Background(bg).Padding(5).Text(s.UnitCode).FontFamily("Courier New").FontColor(ColorPrimary);
                    table.Cell().Background(bg).Padding(5).Text(period).FontColor(ColorGray);
                    table.Cell().Background(bg).Padding(5).Text(s.Concept).FontColor(ColorPrimary);
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(FormatGs(s.Amount)).FontColor(ColorPrimary);
                }
            });
        });
    }

    private void ComposeSummary(IContainer container)
    {
        var totalSettled = data.Settlements.Sum(s => s.Amount);

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                c.RelativeColumn(1.5f);
            });
            table.Header(h =>
            {
                h.Cell().Background(ColorPrimary).Padding(5).Text("Resumen").FontColor(ColorWhite).Bold();
                h.Cell().Background(ColorPrimary).Padding(5).AlignRight().Text("Monto (Gs.)").FontColor(ColorWhite).Bold();
            });

            AddSummaryRow(table, "Monto declarado", data.DeclaredAmount, false);
            if (data.ReviewedAmount > 0)
                AddSummaryRow(table, "Monto revisado/aprobado", data.ReviewedAmount, false);
            if (totalSettled > 0)
                AddSummaryRow(table, "Total liquidado a cargos", totalSettled, false);
            if (data.RemainingCredit > 0)
                AddSummaryRow(table, "Saldo a favor restante", data.RemainingCredit, false, ColorCredit);
            AddSummaryRow(table, "Total cobrado", data.ReviewedAmount > 0 ? data.ReviewedAmount : data.DeclaredAmount, true);
        });
    }

    private static void AddInfoCell(TableDescriptor table, string label, string value)
    {
        table.Cell().Border(0.5f).BorderColor(ColorBorder).Padding(6).Column(c =>
        {
            c.Item().Text(label).FontColor(ColorGray).FontSize(8);
            c.Item().Text(value).Bold().FontColor(ColorPrimary);
        });
    }

    private static void AddSummaryRow(TableDescriptor table, string label, decimal amount, bool isTotal, string? overrideColor = null)
    {
        var bg = isTotal ? ColorAccent : ColorWhite;
        var textColor = isTotal ? ColorWhite : (overrideColor ?? ColorPrimary);
        table.Cell().Background(bg).Padding(5).Text(t =>
        {
            var span = t.Span(label).FontColor(textColor);
            if (isTotal) span.Bold();
        });
        table.Cell().Background(bg).Padding(5).AlignRight().Text(t =>
        {
            var span = t.Span(FormatGs(amount)).FontColor(textColor);
            if (isTotal) span.Bold();
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text("Generado por CONDOPY").FontColor(ColorGray);
            row.ConstantItem(80).AlignRight().Text(t =>
            {
                t.CurrentPageNumber().FontColor(ColorGray);
                t.Span(" / ").FontColor(ColorGray);
                t.TotalPages().FontColor(ColorGray);
            });
        });
    }

    private static string FormatGs(decimal value) => $"Gs. {value:N0}";

    private static string StatusLabel(string status) => status switch
    {
        "Pending"     => "Pendiente",
        "UnderReview" => "En Revisión",
        "Approved"    => "Aprobado",
        "Rejected"    => "Rechazado",
        _             => status
    };
}
