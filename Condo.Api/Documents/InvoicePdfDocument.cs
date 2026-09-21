using System.Globalization;
using Condo.Application.Models;
using Condo.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

/// <summary>
/// Factura A4 con el diseno de la plantilla "modelo_factura_A4_preimpreso": las posiciones (en puntos,
/// medidas desde la esquina inferior izquierda como en la plantilla) se respetan tal cual.
/// Todos los conceptos van como exentos (sin IVA).
/// </summary>
public sealed class InvoicePdfDocument(InvoiceDto invoice) : IDocument
{
    private const float PageWidth = 595.2756f;
    private const float PageHeight = 841.8898f;
    private const float RowTopY = 524.4094f;      // linea bajo el encabezado de la tabla
    private const float RowBottomY = 243.7795f;   // base de la tabla de conceptos
    private const float TemplateRowHeight = 48.1890f;
    private const int TemplateRows = 6;
    private const int MaxRows = 26;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = invoice.Status == InvoiceStatus.Issued
            ? $"Factura {invoice.NumeroFormateado}"
            : $"Vista previa de factura - {invoice.UnitCode}",
        Author = "CONDOPY"
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontColor(Colors.Black));
            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Text(string.Empty);
                DrawFrame(layers);
                DrawHeader(layers);
                DrawClientBlock(layers);
                DrawDetail(layers);
                DrawTotals(layers);
                DrawFooter(layers);
            });
        });
    }

    // ─── Marcos y lineas de la plantilla ─────────────────────────────────────

    private static void DrawFrame(LayersDescriptor l)
    {
        Rect(l, 34.01575f, 674.6457f, 317.4803f, 133.2283f);
        Rect(l, 362.8346f, 674.6457f, 198.4252f, 133.2283f);
        Rect(l, 34.01575f, 572.5984f, 527.2441f, 90.70866f);
        HLine(l, 34.01575f, 634.9606f, 561.2598f);

        Rect(l, 34.01575f, 243.7795f, 527.2441f, 317.4803f);
        foreach (var x in new[] { 70.86614f, 328.8189f })
            VLine(l, x, 243.7795f, 561.2598f);
        // Las divisiones de 5% y 10% arrancan bajo el rotulo "VALOR DE VENTA" para no cruzarlo.
        foreach (var x in new[] { 411.0236f, 493.2283f })
            VLine(l, x, 243.7795f, 542.8346f);
        HLine(l, 328.8189f, 542.8346f, 561.2598f);
        HLine(l, 34.01575f, RowTopY, 561.2598f);

        Rect(l, 34.01575f, 147.4016f, 527.2441f, 96.37795f);
        foreach (var y in new[] { 218.2677f, 195.5906f, 172.9134f })
            HLine(l, 34.01575f, y, 561.2598f);
    }

    // ─── Emisor y datos del timbrado ─────────────────────────────────────────

    private void DrawHeader(LayersDescriptor l)
    {
        var issued = invoice.Status != InvoiceStatus.Draft;

        // Caja izquierda: el edificio a la izquierda; razon social, direccion y telefono a la derecha.
        Text(l, 48, 793, 7.5f, "Edificio", color: Colors.Grey.Darken1);
        l.Layer().TranslateX(48).TranslateY(PageHeight - 758f).Width(126f).Column(col =>
        {
            col.Item().Text(invoice.BuildingName.ToUpperInvariant()).FontSize(15).Bold().FontColor("#14363d");
        });

        l.Layer().TranslateX(182).TranslateY(PageHeight - 784f).Width(164f).Column(col =>
        {
            if (!string.IsNullOrWhiteSpace(invoice.SeriesRazonSocial))
                col.Item().AlignCenter().Text(invoice.SeriesRazonSocial.ToUpperInvariant()).FontSize(10).Bold();

            if (!string.IsNullOrWhiteSpace(invoice.BuildingAddress))
                col.Item().PaddingTop(8).AlignCenter().Text(invoice.BuildingAddress).FontSize(8).Bold();

            if (!string.IsNullOrWhiteSpace(invoice.BuildingPhone))
                col.Item().PaddingTop(1).AlignCenter().Text($"Tel.: {invoice.BuildingPhone}").FontSize(8).Bold();
        });

        // Caja derecha: timbrado, vigencia, RUC, tipo de documento y numero.
        const float boxX = 362.8346f, boxW = 198.4252f;
        Text(l, boxX, 795, 9.5f, string.IsNullOrEmpty(invoice.SeriesNumeroTimbrado) ? "TIMBRADO N°" : $"TIMBRADO N°{invoice.SeriesNumeroTimbrado}",
            bold: true, width: boxW, align: Align.Center);
        Text(l, boxX, 783, 7.5f, $"Fecha Inicio Vigencia:{Date(invoice.SeriesVigenciaDesde)}", width: boxW, align: Align.Center);
        Text(l, boxX, 773, 7.5f, $"Fecha Fin Vigencia:{Date(invoice.SeriesVigenciaHasta)}", width: boxW, align: Align.Center);
        Text(l, boxX, 760, 10.5f, string.IsNullOrEmpty(invoice.SeriesRuc) ? "RUC:" : $"RUC:{invoice.SeriesRuc}", bold: true, width: boxW, align: Align.Center);
        Text(l, boxX, 738, 19, "FACTURA", bold: true, width: boxW, align: Align.Center);

        // Linea tenue con el numero y la condicion, como en el formulario impreso.
        if (issued && !string.IsNullOrEmpty(invoice.NumeroFormateado))
            Text(l, boxX, 718, 7, $"{invoice.NumeroFormateado}   CONTADO", width: boxW, align: Align.Center, color: Colors.Grey.Medium);
        else
            Text(l, boxX, 718, 7, "CONTADO", width: boxW, align: Align.Center, color: Colors.Grey.Medium);

        // Numero grande: "Nº 001-001-" y el correlativo destacado.
        var (prefix, correlative) = SplitNumber(invoice.NumeroFormateado);
        var numberBox = l.Layer().TranslateX(boxX).TranslateY(PageHeight - 712f).Width(boxW).AlignCenter();
        numberBox.Text(t =>
        {
            if (issued)
            {
                t.Span("Nº ").FontSize(14).Bold();
                t.Span(prefix).FontSize(14).Bold();
                t.Span(correlative).FontSize(17).Bold().FontColor("#1f2d3d");
            }
            else
            {
                t.Span("Nº SIN NUMERAR").FontSize(13).Bold().FontColor("#B45309");
            }
        });
    }

    // "001-001-0002777" -> ("001-001-", "0002777")
    private static (string Prefix, string Correlative) SplitNumber(string? formatted)
    {
        if (string.IsNullOrEmpty(formatted)) return (string.Empty, string.Empty);
        var index = formatted.LastIndexOf('-');
        return index < 0 ? (string.Empty, formatted) : (formatted[..(index + 1)], formatted[(index + 1)..]);
    }

    // ─── Fecha, condicion, cliente y unidad ──────────────────────────────────

    private void DrawClientBlock(LayersDescriptor l)
    {
        var date = invoice.FechaEmisionUtc?.ToLocalTime() ?? invoice.CreatedAtUtc.ToLocalTime();
        var document = invoice.ClienteDocumento ?? string.Empty;

        Text(l, 45, 645, 7.5f, "FECHA DE EMISIÓN:", bold: true);
        Text(l, 130, 645, 8, date.ToString("dd/MM/yyyy"));
        Text(l, 332, 645, 7.5f, "CONDICIÓN DE VENTA:", bold: true);
        Text(l, 428, 645, 8, "Contado");

        Text(l, 45, 615, 7.5f, "NOMBRE O RAZÓN SOCIAL:", bold: true);
        Text(l, 156, 615, 8, invoice.ClienteNombre ?? string.Empty, width: 208);
        Text(l, 374, 615, 7.5f, "C.I. / RUC:", bold: true);
        Text(l, 445, 615, 8, document, width: 110);

        Text(l, 45, 587, 7.5f, "RUC:", bold: true);
        Text(l, 79, 587, 8, document, width: 250);
        Text(l, 374, 587, 7.5f, "UNIDAD / REFERENCIA:", bold: true);
        Text(l, 473, 587, 8, invoice.UnitCode, width: 85);
    }

    // ─── Tabla de conceptos ──────────────────────────────────────────────────

    private void DrawDetail(LayersDescriptor l)
    {
        Text(l, 328.8189f, 549, 8, "VALOR DE VENTA", bold: true, width: 232.4409f, align: Align.Center);
        Text(l, 43, 537, 8, "ITEM", bold: true);
        Text(l, 146, 537, 8, "CONCEPTO / DESCRIPCIÓN", bold: true);
        Text(l, 352, 530, 7.5f, "EXENTAS", bold: true);
        Text(l, 447, 530, 7.5f, "5%", bold: true);
        Text(l, 520, 530, 7.5f, "10%", bold: true);

        var lines = LateFeeGrouping.Collapse(
            invoice.Detalle,
            l => l.Concepto,
            l => l.Monto,
            (first, sum, label) => new InvoiceLineDto { Concepto = label, ChargeType = first.ChargeType, Monto = sum });
        if (lines.Count > MaxRows)
        {
            var rest = lines.Skip(MaxRows - 1).ToList();
            lines = lines.Take(MaxRows - 1).ToList();
            lines.Add(new InvoiceLineDto { Concepto = $"Otros conceptos ({rest.Count})", Monto = rest.Sum(x => x.Monto) });
        }

        var count = Math.Max(lines.Count, 1);
        var rowHeight = count <= TemplateRows ? TemplateRowHeight : (RowTopY - RowBottomY) / count;
        var fontSize = count <= TemplateRows ? 7.5f : 6.5f;

        // Separadores de fila como en la plantilla (solo cuando entran en las 6 filas del modelo).
        if (count <= TemplateRows)
        {
            for (var i = 1; i < TemplateRows; i++)
                HLine(l, 34.01575f, RowTopY - (i * TemplateRowHeight), 561.2598f);
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var rowTop = RowTopY - (i * rowHeight);
            var baseline = rowTop - Math.Min(19f, rowHeight - 2.5f);
            var line = lines[i];

            Text(l, 40, baseline, fontSize, (i + 1).ToString(CultureInfo.InvariantCulture), width: 28);
            Text(l, 77, baseline, fontSize, line.Concepto, width: 248);
            Text(l, 328.8189f, baseline, fontSize, FormatNumber(line.Monto), width: 77, align: Align.Right);
        }
    }

    // ─── Subtotales, total, IVA y letras ─────────────────────────────────────

    private void DrawTotals(LayersDescriptor l)
    {
        var total = invoice.MontoTotal;

        Text(l, 45, 225, 8, "SUBTOTALES", bold: true);
        Text(l, 329, 225, 7.5f, $"EXENTAS: {FormatNumber(total)}");
        Text(l, 417, 225, 7.5f, "5%: 0");
        Text(l, 499, 225, 7.5f, "10%: 0");

        Text(l, 45, 203, 8, "TOTAL A PAGAR", bold: true);
        Text(l, 411.0236f, 203, 9, FormatNumber(total), bold: true, width: 144, align: Align.Right);

        Text(l, 45, 180, 8, "LIQUIDACIÓN DEL IVA", bold: true);
        Text(l, 218, 180, 7.5f, "5%: 0");
        Text(l, 332, 180, 7.5f, "10%: 0");
        Text(l, 457, 180, 7.5f, "TOTAL IVA: 0");

        Text(l, 45, 156, 8, "SON:", bold: true);
        Text(l, 79, 156, 7.5f, NumberToWordsEs.Guaranies(total), width: 478);
    }

    // ─── Pie: copias, anulacion y aviso de borrador ──────────────────────────

    private void DrawFooter(LayersDescriptor l)
    {
        Text(l, 34.01575f, 130, 6.5f, "Original: Comprador - Copia: Archivo Tributario", width: 527.2441f, align: Align.Right);
        Text(l, 34.01575f, 118, 6.5f, "2° Copia: Contabilidad / Archivo (cuando corresponda)", width: 527.2441f, align: Align.Right);

        if (invoice.Status == InvoiceStatus.Voided)
        {
            l.Layer().TranslateX(34.01575f).TranslateY(PageHeight - 105f).Width(527.2441f).Height(34f)
                .Background("#FEE2E2").Border(1).BorderColor("#DC2626").Padding(6)
                .Text($"FACTURA ANULADA — {invoice.MotivoAnulacion}").FontSize(9).Bold().FontColor("#991B1B");
        }
        else if (invoice.Status != InvoiceStatus.Issued)
        {
            l.Layer().TranslateX(34.01575f).TranslateY(PageHeight - 105f).Width(527.2441f).Height(40f)
                .Element(PdfWatermark.ComposeNonFiscal);
        }
    }

    // ─── Utilidades de dibujo (coordenadas de la plantilla, origen abajo-izquierda) ─

    private enum Align { Left, Center, Right }

    private static float TopOf(float bottomY, float height) => PageHeight - bottomY - height;

    private static void Rect(LayersDescriptor l, float x, float bottomY, float width, float height) =>
        l.Layer().TranslateX(x).TranslateY(TopOf(bottomY, height)).Width(width).Height(height)
            .Svg(size =>
            {
                var w = size.Width.ToString("0.###", CultureInfo.InvariantCulture);
                var h = size.Height.ToString("0.###", CultureInfo.InvariantCulture);
                var rw = (size.Width - 0.9f).ToString("0.###", CultureInfo.InvariantCulture);
                var rh = (size.Height - 0.9f).ToString("0.###", CultureInfo.InvariantCulture);
                return $"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}' viewBox='0 0 {w} {h}'>" +
                       $"<rect x='0.45' y='0.45' width='{rw}' height='{rh}' rx='9' ry='9' fill='none' stroke='black' stroke-width='0.9'/></svg>";
            });

    private static void HLine(LayersDescriptor l, float x1, float y, float x2) =>
        l.Layer().TranslateX(x1).TranslateY(PageHeight - y - 0.4f).Width(x2 - x1).Height(0.8f).Background(Colors.Black);

    private static void VLine(LayersDescriptor l, float x, float y1, float y2) =>
        l.Layer().TranslateX(x - 0.4f).TranslateY(PageHeight - y2).Width(0.8f).Height(y2 - y1).Background(Colors.Black);

    private static void Text(
        LayersDescriptor l, float x, float baselineY, float size, string text,
        bool bold = false, float? width = null, Align align = Align.Left, string? color = null)
    {
        if (string.IsNullOrEmpty(text)) return;

        var box = l.Layer().TranslateX(x).TranslateY(PageHeight - baselineY - (size * 0.95f)).Width(width ?? (PageWidth - x - 20f));
        var aligned = align switch
        {
            Align.Center => box.AlignCenter(),
            Align.Right => box.AlignRight(),
            _ => box.AlignLeft()
        };

        aligned.Text(t =>
        {
            t.ClampLines(1);
            var span = t.Span(text).FontSize(size);
            if (bold) span.Bold();
            if (color is not null) span.FontColor(color);
        });
    }

    private static string FormatNumber(decimal value) =>
        value.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".");

    private static string Date(DateOnly? date) => date?.ToString("dd/MM/yyyy") ?? string.Empty;
}
