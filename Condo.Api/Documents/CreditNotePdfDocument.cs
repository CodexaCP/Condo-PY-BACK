using System.Globalization;
using Condo.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public sealed record CreditNotePdfLine(string Concepto, decimal Monto);

public sealed record CreditNotePdfData(
    string BuildingName,
    string? BuildingAddress,
    string? BuildingPhone,
    string? RazonSocial,
    string? Ruc,
    string? Timbrado,
    DateOnly? VigenciaDesde,
    DateOnly? VigenciaHasta,
    string? NumeroFormateado,
    DateTime? FechaEmisionUtc,
    DateTime CreatedAtUtc,
    string? InvoiceNumeroFormateado,
    DateTime? InvoiceFechaEmisionUtc,
    string? ClienteNombre,
    string? ClienteDocumento,
    string UnitCode,
    string Motivo,
    decimal Amount,
    CreditNoteStatus Status,
    string? RejectionReason,
    string? VoidReason,
    string? FiscalCdc,
    string? FiscalEstado,
    List<CreditNotePdfLine> Lines);

/// <summary>
/// Nota de credito A4 con el diseno del modelo "ejemplo_nota_credito_condo_py": encabezado con emisor y
/// timbrado, documento que se ajusta, cliente, items, totales y referencia fiscal. Con el modelo estandar
/// de CONDOPY lleva los colores de la marca (los del comprobante); sin el, sale en blanco y negro.
/// </summary>
public sealed class CreditNotePdfDocument(CreditNotePdfData data, bool standardTemplate = true) : IDocument
{
    private sealed record Palette(
        string Border, string InnerBorder, string SectionFill, string SectionText, string LabelText,
        string Title, string Number, string TotalFill, string TotalText, string Muted);

    private static readonly Palette Classic = new(
        Border: Colors.Black, InnerBorder: "#808080", SectionFill: "#F5F5F5", SectionText: Colors.Black,
        LabelText: Colors.Black, Title: Colors.Black, Number: Colors.Black, TotalFill: "#F5F5F5",
        TotalText: Colors.Black, Muted: "#555555");

    private static readonly Palette Standard = new(
        Border: CondoPdfColors.Primary, InnerBorder: CondoPdfColors.Border, SectionFill: CondoPdfColors.Primary,
        SectionText: CondoPdfColors.White, LabelText: CondoPdfColors.Primary, Title: CondoPdfColors.Primary,
        Number: CondoPdfColors.Accent, TotalFill: CondoPdfColors.Accent, TotalText: CondoPdfColors.White,
        Muted: CondoPdfColors.Gray);

    private readonly Palette p = standardTemplate ? Standard : Classic;

    private const float FontSize = 8f;
    private const string Dash = "—";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = data.NumeroFormateado is not null
            ? $"Nota de crédito {data.NumeroFormateado}"
            : $"Vista previa de nota de crédito - {data.UnitCode}",
        Author = "CONDOPY"
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(40);
            page.MarginVertical(36);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(FontSize).FontColor(Colors.Black));
            page.Content().Column(col =>
            {
                col.Spacing(10);
                col.Item().Element(ComposeHeader);
                col.Item().Element(ComposeAdjustedDocument);
                col.Item().Element(ComposeClient);
                col.Item().Element(ComposeItems);
                col.Item().Element(ComposeTotals);
                col.Item().Element(ComposeFiscalReference);
                col.Item().Element(ComposeStatusBanner);
            });
            page.Footer().Column(col =>
            {
                col.Item().Text("Original: Comprador - Copia: Archivo Tributario - 2ª Copia: Contabilidad")
                    .FontSize(6.5f).FontColor(p.Muted);
                if (standardTemplate)
                    col.Item().PaddingTop(2).Text("Generado por CONDOPY").FontSize(6.5f).FontColor(CondoPdfColors.Gray);
            });
        });
    }

    // ─── Emisor (izquierda) y timbrado / numero (derecha) ───────────────────

    private void ComposeHeader(IContainer container)
    {
        container.Border(0.8f).BorderColor(p.Border).Row(row =>
        {
            row.RelativeItem(58).Padding(8).Column(col =>
            {
                col.Item().Text($"Edificio {data.BuildingName}").FontSize(10).Bold().FontColor(p.Title);
                if (!string.IsNullOrWhiteSpace(data.BuildingAddress))
                    col.Item().Text(data.BuildingAddress.ToUpperInvariant());
                if (!string.IsNullOrWhiteSpace(data.RazonSocial))
                    col.Item().PaddingTop(4).Text(data.RazonSocial.ToUpperInvariant()).Bold();
                if (!string.IsNullOrWhiteSpace(data.BuildingPhone))
                    col.Item().Text($"Tel.: {data.BuildingPhone}").FontSize(7);
                col.Item().PaddingTop(4).Text(t =>
                {
                    t.Span("RUC: ").Bold().FontColor(p.LabelText);
                    t.Span(string.IsNullOrWhiteSpace(data.Ruc) ? Dash : data.Ruc);
                });
            });

            row.ConstantItem(0.4f).Background(p.Border);

            row.RelativeItem(42).Padding(8).Column(col =>
            {
                col.Item().AlignCenter().Text("NOTA DE CRÉDITO").FontSize(16).Bold().FontColor(p.Title);
                col.Item().AlignCenter().Text(data.NumeroFormateado ?? "SIN NUMERAR").FontSize(10).Bold()
                    .FontColor(data.NumeroFormateado is null ? "#B45309" : p.Number);
                col.Item().PaddingTop(4).AlignCenter().Text(
                    string.IsNullOrWhiteSpace(data.Timbrado) ? "TIMBRADO N°" : $"TIMBRADO N° {data.Timbrado}").Bold().FontColor(p.LabelText);
                col.Item().AlignCenter().Text($"Inicio vigencia: {Date(data.VigenciaDesde)}");
                col.Item().AlignCenter().Text($"Fin vigencia: {Date(data.VigenciaHasta)}");
                col.Item().PaddingTop(4).AlignCenter().Text(t =>
                {
                    t.Span("FECHA DE EMISIÓN: ").Bold().FontColor(p.LabelText);
                    t.Span(DateTimeText(data.FechaEmisionUtc ?? data.CreatedAtUtc));
                });
            });
        });
    }

    // ─── Documento que se ajusta ────────────────────────────────────────────

    private void ComposeAdjustedDocument(IContainer container)
    {
        var (ci, _) = SplitDocument(data.ClienteDocumento);

        container.Border(0.7f).BorderColor(p.Border).Table(t =>
        {
            t.ColumnsDefinition(c => { c.RelativeColumn(136); c.RelativeColumn(198); c.RelativeColumn(153); });

            SectionHeader(t, "DOCUMENTO QUE SE AJUSTA", 3);

            Label(t, "Factura original");
            Value(t, data.InvoiceNumeroFormateado ?? Dash);
            Value(t, $"Fecha: {(data.InvoiceFechaEmisionUtc.HasValue ? DateTimeText(data.InvoiceFechaEmisionUtc.Value) : Dash)}");

            Label(t, "Cliente");
            Value(t, Or(data.ClienteNombre));
            Value(t, $"C.I. N° {ci ?? Dash}");

            Label(t, "Unidad");
            Value(t, data.UnitCode);
            Value(t, "Condición: CONTADO");

            Label(t, "Motivo de la Nota de Crédito");
            t.Cell().ColumnSpan(2).Element(Cell).Text(data.Motivo);
        });
    }

    // ─── Cliente ────────────────────────────────────────────────────────────

    private void ComposeClient(IContainer container)
    {
        var (ci, ruc) = SplitDocument(data.ClienteDocumento);

        container.Border(0.7f).BorderColor(p.Border).Table(t =>
        {
            t.ColumnsDefinition(c => { c.RelativeColumn(108); c.RelativeColumn(198); c.RelativeColumn(71); c.RelativeColumn(110); });

            Label(t, "NOMBRE O RAZÓN SOCIAL", bold: true);
            Value(t, Or(data.ClienteNombre));
            Label(t, "C.I. N°", bold: true);
            Value(t, ci ?? Dash);

            Label(t, "RUC", bold: true);
            Value(t, ruc ?? Dash);
            Label(t, "UNIDAD", bold: true);
            Value(t, data.UnitCode);
        });
    }

    // ─── Items ──────────────────────────────────────────────────────────────

    private void ComposeItems(IContainer container)
    {
        container.Border(0.7f).BorderColor(p.Border).Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.ConstantColumn(34);
                c.RelativeColumn(238);
                c.RelativeColumn(57);
                c.RelativeColumn(51);
                c.RelativeColumn(51);
                c.RelativeColumn(102);
            });

            t.Header(h =>
            {
                foreach (var (title, right) in new[] { ("ITEM", false), ("CONCEPTO", false), ("EXENTAS", true), ("5%", true), ("10%", true), ("VALOR DE AJUSTE", true) })
                {
                    var cell = h.Cell().Background(p.SectionFill).BorderBottom(0.35f).BorderRight(0.35f).BorderColor(p.InnerBorder).PaddingHorizontal(4).PaddingVertical(5);
                    (right ? cell.AlignRight() : cell).Text(title).FontSize(7.2f).Bold().FontColor(p.SectionText);
                }
            });

            var index = 1;
            foreach (var line in data.Lines)
            {
                var bg = standardTemplate && index % 2 == 0 ? CondoPdfColors.RowAlt : CondoPdfColors.White;
                t.Cell().Background(bg).Element(Cell).AlignCenter().Text(index.ToString(CultureInfo.InvariantCulture));
                t.Cell().Background(bg).Element(Cell).Text(line.Concepto);
                t.Cell().Background(bg).Element(Cell).AlignRight().Text(Dash);
                t.Cell().Background(bg).Element(Cell).AlignRight().Text(Dash);
                t.Cell().Background(bg).Element(Cell).AlignRight().Text(Dash);
                t.Cell().Background(bg).Element(Cell).AlignRight().Text(FormatNumber(line.Monto));
                index++;
            }

            t.Cell().Element(Cell).Text(string.Empty);
            t.Cell().Element(Cell).Text("TOTAL NOTA DE CRÉDITO").Bold().FontColor(p.LabelText);
            t.Cell().Element(Cell).Text(string.Empty);
            t.Cell().Element(Cell).Text(string.Empty);
            t.Cell().Element(Cell).Text(string.Empty);
            t.Cell().Background(p.TotalFill).Element(Cell).AlignRight().Text(FormatNumber(data.Amount)).Bold().FontColor(p.TotalText);
        });
    }

    // ─── Totales ────────────────────────────────────────────────────────────

    private void ComposeTotals(IContainer container)
    {
        container.Border(0.7f).BorderColor(p.Border).Table(t =>
        {
            t.ColumnsDefinition(c => { c.RelativeColumn(354); c.RelativeColumn(133); });

            Label(t, "SUBTOTAL", bold: true);
            t.Cell().Element(Cell).AlignRight().Text(FormatNumber(data.Amount));

            Label(t, "TOTAL NOTA DE CRÉDITO", bold: true);
            t.Cell().Background(p.TotalFill).Element(Cell).AlignRight().Text(FormatNumber(data.Amount)).Bold().FontColor(p.TotalText);

            Label(t, "SON", bold: true);
            t.Cell().Element(Cell).AlignRight().Text(NumberToWordsEs.Guaranies(data.Amount)).FontSize(7);
        });
    }

    // ─── Referencia fiscal / trazabilidad ───────────────────────────────────

    private void ComposeFiscalReference(IContainer container)
    {
        var fiscalState = !string.IsNullOrWhiteSpace(data.FiscalEstado)
            ? data.FiscalEstado.ToUpperInvariant()
            : data.NumeroFormateado is not null ? "REGISTRADO" : "PENDIENTE";

        container.Border(0.7f).BorderColor(p.Border).Table(t =>
        {
            t.ColumnsDefinition(c => { c.RelativeColumn(136); c.RelativeColumn(198); c.RelativeColumn(153); });

            SectionHeader(t, "REFERENCIA FISCAL / TRAZABILIDAD", 3);

            Label(t, "Documento original", bold: true);
            Value(t, data.InvoiceNumeroFormateado is null ? Dash : $"Factura {data.InvoiceNumeroFormateado}");
            Value(t, $"CDC: {Dash}");

            Label(t, "Nota de Crédito", bold: true);
            Value(t, data.NumeroFormateado ?? Dash);
            Value(t, $"CDC: {Or(data.FiscalCdc)}");

            Label(t, "Estado interno", bold: true);
            Value(t, StatusLabel(data.Status));
            Value(t, $"Documento fiscal: {fiscalState}");
        });
    }

    // Aviso segun el estado: anulada/rechazada en rojo; borrador sin validez fiscal.
    private void ComposeStatusBanner(IContainer container)
    {
        switch (data.Status)
        {
            case CreditNoteStatus.Voided:
                RedBanner(container, $"NOTA DE CRÉDITO ANULADA — {data.VoidReason}");
                break;
            case CreditNoteStatus.Rejected:
                RedBanner(container, $"NOTA DE CRÉDITO RECHAZADA — {data.RejectionReason}");
                break;
            case CreditNoteStatus.Draft:
                container.Element(PdfWatermark.ComposeNonFiscal);
                break;
        }
    }

    private static void RedBanner(IContainer container, string text) =>
        container.Background("#FEE2E2").Border(1).BorderColor("#DC2626").Padding(6)
            .Text(text).FontSize(9).Bold().FontColor("#991B1B");

    // ─── Utilidades ─────────────────────────────────────────────────────────

    private IContainer Cell(IContainer c) =>
        c.BorderBottom(0.35f).BorderRight(0.35f).BorderColor(p.InnerBorder).PaddingHorizontal(4).PaddingVertical(5);

    private void SectionHeader(TableDescriptor t, string title, int span) =>
        t.Cell().ColumnSpan((uint)span).Background(p.SectionFill).BorderBottom(0.35f).BorderColor(p.InnerBorder)
            .PaddingHorizontal(4).PaddingVertical(5).Text(title).Bold().FontColor(p.SectionText);

    private void Label(TableDescriptor t, string text, bool bold = false)
    {
        var span = t.Cell().Element(Cell).Text(text).FontColor(p.LabelText);
        if (bold || standardTemplate) span.Bold();
    }

    private void Value(TableDescriptor t, string text) => t.Cell().Element(Cell).Text(text);

    // Un documento con guion es RUC (8540611-2); sin guion es cedula. Mismo criterio que la factura.
    private static (string? Ci, string? Ruc) SplitDocument(string? document)
    {
        var value = (document ?? string.Empty).Trim();
        if (value.Length == 0) return (null, null);
        return value.Contains('-') ? (null, value) : (value, null);
    }

    private static string StatusLabel(CreditNoteStatus status) => status switch
    {
        CreditNoteStatus.Draft => "BORRADOR",
        CreditNoteStatus.Approved => "APROBADA",
        CreditNoteStatus.Rejected => "RECHAZADA",
        CreditNoteStatus.Voided => "ANULADA",
        _ => status.ToString().ToUpperInvariant()
    };

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? Dash : value;

    private static string FormatNumber(decimal value) =>
        value.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".");

    private static string Date(DateOnly? date) => date?.ToString("dd/MM/yyyy") ?? string.Empty;

    private static string DateTimeText(DateTime utc) => utc.ToLocalTime().ToString("dd/MM/yyyy");
}
