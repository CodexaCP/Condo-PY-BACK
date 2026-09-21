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

        Text(l, 45, 791, 7, "EDIFICIO / EMISOR", color: Colors.Grey.Darken2);
        Text(l, 45, 765, 14, invoice.BuildingName, bold: true, width: 295);
        Text(l, 45, 748, 9, invoice.SeriesRazonSocial ?? string.Empty, width: 295);
        Text(l, 45, 731, 8, string.IsNullOrEmpty(invoice.SeriesRuc) ? string.Empty : $"RUC: {invoice.SeriesRuc}", width: 295);
        Text(l, 45, 714, 8, invoice.BuildingAddress ?? string.Empty, width: 295);
        Text(l, 45, 697, 8, string.IsNullOrEmpty(invoice.BuildingPhone) ? string.Empty : $"Tel.: {invoice.BuildingPhone}", width: 295);

        const float boxX = 362.8346f, boxW = 198.4252f;
        Text(l, boxX, 791, 8, string.IsNullOrEmpty(invoice.SeriesNumeroTimbrado) ? "TIMBRADO N°" : $"TIMBRADO N° {invoice.SeriesNumeroTimbrado}",
            bold: true, width: boxW, align: Align.Center);
        Text(l, boxX, 777, 7, $"Fecha Inicio: {Date(invoice.SeriesVigenciaDesde)}", width: boxW, align: Align.Center);
        Text(l, boxX, 763, 7, $"Fecha Fin: {Date(invoice.SeriesVigenciaHasta)}", width: boxW, align: Align.Center);
        Text(l, boxX, 746, 9, string.IsNullOrEmpty(invoice.SeriesRuc) ? "RUC:" : $"RUC: {invoice.SeriesRuc}", bold: true, width: boxW, align: Align.Center);
        Text(l, boxX, 723, 17, "FACTURA", bold: true, width: boxW, align: Align.Center);
        Text(l, boxX, 703, 12, issued ? invoice.NumeroFormateado ?? string.Empty : "SIN NUMERAR", bold: true, width: boxW, align: Align.Center,
            color: issued ? Colors.Black : "#B45309");
        Text(l, boxX, 686, 7, "CONDICIÓN: CONTADO", width: boxW, align: Align.Center);
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

        var lines = invoice.Detalle.ToList();
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
        l.Layer().TranslateX(x).TranslateY(TopOf(bottomY, height)).Width(width).Height(height).Border(0.8f).BorderColor(Colors.Black);

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
