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
    private const int MaxRows = 18;

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
        // Casilla gris del "TOTAL A PAGAR" (se dibuja primero para que las lineas queden encima).
        FillRect(l, 493.6f, 195.99f, 67.2f, 21.9f, "#E4E4E4");

        Rect(l, 34.01575f, 674.6457f, 317.4803f, 133.2283f);   // edificio / emisor
        Rect(l, 362.8346f, 674.6457f, 198.4252f, 133.2283f);   // timbrado y numero
        Rect(l, 34.01575f, 572.5984f, 527.2441f, 90.70866f);   // fecha, condicion, cliente

        // Tabla de conceptos: sin filas, solo columnas (item | concepto | exentas | 5% | 10%).
        Rect(l, 34.01575f, 243.7795f, 527.2441f, 317.4803f);
        VLine(l, 70.86614f, 243.7795f, 561.2598f);
        VLine(l, 328.8189f, 243.7795f, 561.2598f);
        VLine(l, 411.0236f, 243.7795f, 542.8346f);
        VLine(l, 493.2283f, 243.7795f, 542.8346f);
        HLine(l, 328.8189f, 542.8346f, 561.2598f);
        HLine(l, 34.01575f, RowTopY, 561.2598f);

        // Totales: la fila de subtotales sigue las columnas y el total tiene su casilla.
        Rect(l, 34.01575f, 147.4016f, 527.2441f, 96.37795f);
        foreach (var y in new[] { 218.2677f, 195.5906f, 172.9134f })
            HLine(l, 34.01575f, y, 561.2598f);
        foreach (var x in new[] { 328.8189f, 411.0236f, 493.2283f })
            VLine(l, x, 218.2677f, 243.7795f);
        VLine(l, 493.2283f, 195.5906f, 218.2677f);
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
        var date = (invoice.FechaEmisionUtc ?? invoice.CreatedAtUtc).ToLocalTime();
        var document = (invoice.ClienteDocumento ?? string.Empty).Trim();
        // Un documento con guion es RUC (8540611-2); sin guion es cedula.
        var isRuc = document.Contains('-');

        Text(l, 45, 645, 8.5f, "FECHA DE EMISION:");
        Text(l, 148, 645, 8.5f, DateInWords(date), bold: true, width: 160);
        Text(l, 292, 645, 8.5f, "CONDICION DE VENTA:", bold: true);
        Text(l, 404, 645, 8.5f, "CONTADO", bold: true);
        CheckBox(l, 452, 642.5f, 11.5f, checkedBox: true);
        Text(l, 472, 645, 8.5f, "CREDITO", bold: true);
        CheckBox(l, 517, 642.5f, 11.5f, checkedBox: false);

        Text(l, 45, 619, 8.5f, "NOMBRE O RAZON SOCIAL:");
        Text(l, 172, 619, 9, invoice.ClienteNombre ?? string.Empty, width: 280);
        Text(l, 470, 619, 8.5f, "C.I. Nº");
        if (!string.IsNullOrEmpty(document) && !isRuc)
            Text(l, 497, 619, 9, document, width: 60);

        Text(l, 45, 593, 8.5f, "RUC:");
        if (!string.IsNullOrEmpty(document) && isRuc)
            Text(l, 74, 593, 9, document, width: 200);
        Text(l, 300, 593, 8.5f, "UNIDAD", bold: true);
        Text(l, 420, 593, 9, invoice.UnitCode, width: 135, align: Align.Right);
    }

    // ─── Tabla de conceptos ──────────────────────────────────────────────────

    private void DrawDetail(LayersDescriptor l)
    {
        const float col1 = 70.86614f, col2 = 328.8189f, col3 = 411.0236f, col4 = 493.2283f, right = 561.2598f;

        Text(l, 34.01575f, 537, 8.5f, "ITEM", width: col1 - 34.01575f, align: Align.Center);
        Text(l, col1, 537, 9, "CONCEPTO", width: col2 - col1, align: Align.Center);
        Text(l, col2, 552, 8.5f, "VALOR DE VENTA", width: right - col2, align: Align.Center);
        Text(l, col2, 531, 8.5f, "EXENTAS", width: col3 - col2, align: Align.Center);
        Text(l, col3, 531, 8.5f, "5%", width: col4 - col3, align: Align.Center);
        Text(l, col4, 531, 8.5f, "10%", width: right - col4, align: Align.Center);

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

        // Los conceptos se listan de arriba hacia abajo, sin lineas de fila, como en el formulario impreso.
        const float firstBaseline = 510f;
        var spacing = lines.Count <= 12 ? 13f : 12f;
        var fontSize = lines.Count <= 12 ? 8.5f : 8f;

        for (var i = 0; i < lines.Count; i++)
        {
            var baseline = firstBaseline - (i * spacing);
            Text(l, 76, baseline, fontSize, lines[i].Concepto, width: 248);
            Text(l, col2, baseline, fontSize, FormatNumber(lines[i].Monto), width: col3 - col2 - 6, align: Align.Right);
        }

        if (invoice.PeriodDueDate.HasValue)
            Text(l, 76, 262, 8.5f, $"Vto. {invoice.PeriodDueDate.Value:dd/MM/yyyy}.");
    }

    // ─── Subtotales, total, IVA y letras ─────────────────────────────────────

    private void DrawTotals(LayersDescriptor l)
    {
        var total = invoice.MontoTotal;

        Text(l, 45, 226, 8.5f, "SUBTOTALES");
        Text(l, 328.8189f, 226, 8.5f, FormatNumber(total), width: 411.0236f - 328.8189f - 6, align: Align.Right);

        Text(l, 45, 203, 8.5f, "TOTAL A PAGAR");
        Text(l, 493.2283f, 203, 9.5f, FormatNumber(total), bold: true, width: 561.2598f - 493.2283f - 6, align: Align.Right);

        Text(l, 45, 180, 8.5f, "LIQUIDACION DEL IVA: (5%)");
        Text(l, 332, 180, 8.5f, "(10%)");
        Text(l, 470, 180, 8.5f, "TOTAL IVA:");

        Text(l, 45, 156, 8.5f, "SON:", bold: true);
        Text(l, 82, 156, 8.5f, NumberToWordsEs.Guaranies(total), width: 474);
    }

    // ─── Pie: copias, anulacion y aviso de borrador ──────────────────────────

    private void DrawFooter(LayersDescriptor l)
    {
        Text(l, 34.01575f, 131, 6.5f, "Original: Comprador - Copia: Arch. Tributario", bold: true, width: 527.2441f, align: Align.Right);
        Text(l, 34.01575f, 122, 6.5f, "2°Copia: Contabilidad(No válido p/ crédito fiscal)", bold: true, width: 527.2441f, align: Align.Right);

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

    private static readonly string[] Months =
        ["ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"];

    // "7 DE AGOSTO DE 2026", como se escribe en el formulario impreso.
    private static string DateInWords(DateTime date) => $"{date.Day} DE {Months[date.Month - 1]} DE {date.Year}";

    private static void FillRect(LayersDescriptor l, float x, float bottomY, float width, float height, string color) =>
        l.Layer().TranslateX(x).TranslateY(TopOf(bottomY, height)).Width(width).Height(height).Background(color);

    // Casilla cuadrada (Contado / Credito); marcada lleva una X.
    private static void CheckBox(LayersDescriptor l, float x, float bottomY, float size, bool checkedBox)
    {
        l.Layer().TranslateX(x).TranslateY(TopOf(bottomY, size)).Width(size).Height(size).Border(0.9f).BorderColor(Colors.Black);
        if (checkedBox)
            Text(l, x, bottomY + 2.2f, size - 1.5f, "X", bold: true, width: size, align: Align.Center);
    }

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
