using System.Globalization;
using System.Text.Json;
using Condo.Application.Models;
using Condo.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

/// <summary>
/// Factura A4 con el diseno de la plantilla "modelo_factura_A4_preimpreso": las posiciones (en puntos,
/// medidas desde la esquina inferior izquierda como en la plantilla) se respetan tal cual.
/// Todos los conceptos van como exentos (sin IVA). Con el modelo estandar de CONDOPY se dibuja igual
/// pero con los colores de la marca (los del comprobante); sin el, en negro como el formulario preimpreso.
/// Si el timbrado tiene posiciones calibradas (invoice.FieldPositionsJson), cada campo se corre por su
/// offset guardado, para calzar sobre el papel preimpreso real de esa imprenta.
/// </summary>
public sealed class InvoicePdfDocument(InvoiceDto invoice, bool standardTemplate = false, byte[]? backgroundImage = null) : IDocument
{
    private sealed record FieldOffset(float Dx, float Dy, float? FontSize = null, float? Width = null, bool? Hidden = null);

    // Los bloques que se pueden calibrar desde la pantalla de calibracion (las mismas keys estan en
    // invoice-series-calibration-page.component.ts). El controlador descarta cualquier otra.
    public static readonly IReadOnlySet<string> CalibratableKeys = new HashSet<string>
    {
        "edificioLabel", "headerEdificio", "headerEmisor", "headerTimbradoNumero", "vigenciaDesde", "vigenciaHasta",
        "headerActividad", "seriesRuc", "docTitulo", "numeroCondicion", "headerNumero",
        "fechaLabel", "fechaEmision", "condicionLabel", "contadoLabel", "marcaContado", "creditoLabel",
        "nombreLabel", "clienteNombre", "ciLabel", "clienteDocumento", "rucLabel", "clienteRuc", "unidadLabel", "unidad",
        "itemLabel", "conceptoLabel", "valorVentaLabel", "exentasLabel", "pct5Label", "pct10Label",
        "conceptosBloque", "montoExentas", "conceptosNota", "vencimiento",
        "subtotalesLabel", "subtotal", "totalLabel", "totalPagar", "liqLabel", "liq10Label", "totalIvaLabel", "sonLabel", "sonEnLetras",
        "pieOriginal", "pieCopia", "generadoPor"
    };

    // Se guarda en camelCase (como la liquidacion); se acepta tambien PascalCase por lo que ya estaba guardado.
    private static readonly JsonSerializerOptions OffsetsJson = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, FieldOffset> offsets = ParseOffsets(invoice.FieldPositionsJson);

    private static Dictionary<string, FieldOffset> ParseOffsets(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, FieldOffset>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, FieldOffset>>(json, OffsetsJson) ?? new Dictionary<string, FieldOffset>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, FieldOffset>();
        }
    }

    // ─── Media A4 ────────────────────────────────────────────────────────────
    // Todas las coordenadas del diseno estan en puntos de la A4 completa. Con papel de media A4 (la factura
    // ocupa solo la mitad superior de la hoja) cada coordenada pasa por estas tablas: pares (A4 completa ->
    // media A4) medidos sobre el papel real, interpolados linealmente entre ellos. Las mismas tablas estan
    // en invoice-series-calibration-page.component.ts (HALF_X / HALF_Y) — si se cambian aca, cambian alla.
    private static readonly (float From, float To)[] HalfX =
    [
        (34.01575f, 14.2f), (70.86614f, 44f), (328.8189f, 316.4f), (351.4961f, 344.8f),
        (362.8346f, 353.7f), (411.0236f, 405.2f), (493.2283f, 495.5f), (561.2598f, 587.3f)
    ];

    private static readonly (float From, float To)[] HalfY =
    [
        (147.4016f, 443.4f), (172.9134f, 459f), (195.5906f, 479.2f), (218.2677f, 497.9f),
        (243.7795f, 515f), (524.4094f, 641.1f), (542.8346f, 655.4f), (552f, 658.3f),
        (561.2598f, 667.2f), (572.5984f, 671.7f), (663.3071f, 720.2f), (674.6457f, 726.2f),
        (712f, 754f), (718f, 760f), (738f, 775f), (760f, 793f), (773f, 804f), (783f, 812.5f), (795f, 822f),
        (807.874f, 830.7f)
    ];

    private bool HalfPage => invoice.HalfPage;

    // Interpolacion lineal por tramos; fuera de la tabla se sigue con pendiente 1 desde el extremo.
    private static float Interpolate((float From, float To)[] table, float v)
    {
        if (v <= table[0].From) return table[0].To + (v - table[0].From);
        for (var i = 1; i < table.Length; i++)
        {
            if (v > table[i].From) continue;
            var (f0, t0) = table[i - 1];
            var (f1, t1) = table[i];
            return t0 + (v - f0) * (t1 - t0) / (f1 - f0);
        }
        return table[^1].To + (v - table[^1].From);
    }

    private float MX(float x) => HalfPage ? Interpolate(HalfX, x) : x;
    private float MY(float y) => HalfPage ? Interpolate(HalfY, y) : y;
    // Ancho de un elemento que empieza en x (en coordenadas de A4 completa), ya en el papel real.
    private float MW(float x, float width) => HalfPage ? MX(x + width) - MX(x) : width;
    private float MH(float bottomY, float height) => HalfPage ? MY(bottomY + height) - MY(bottomY) : height;

    // x/y vienen en coordenadas de A4 completa (origen abajo-izquierda, y crece hacia arriba); se llevan al
    // papel real (MX/MY) y recien ahi se suma el offset calibrado, tanto para Text() como para los bloques
    // dibujados a mano.
    private (float X, float Y) Offset(string? key, float x, float y)
    {
        x = MX(x);
        y = MY(y);
        if (key is not null && offsets.TryGetValue(key, out var o)) return (x + o.Dx, y + o.Dy);
        return (x, y);
    }

    // Tamano de letra calibrado por campo (independiente de la posicion); si no se calibro, se usa el
    // tamano por defecto del formulario.
    private float FontSizeFor(string? key, float defaultSize) =>
        key is not null && offsets.TryGetValue(key, out var o) && o.FontSize.HasValue ? o.FontSize.Value : defaultSize;

    // "No dibujar": el papel ya trae ese dato impreso. Sin decision guardada, los titulos (label) se ocultan cuando
    // el papel trae su propio marco y el resto se dibuja.
    private bool IsHidden(string? key, bool hiddenByDefault) =>
        key is not null && offsets.TryGetValue(key, out var o) && o.Hidden.HasValue ? o.Hidden.Value : hiddenByDefault;

    // Ancho calibrado (ya en puntos del papel real) o el de fabrica llevado al papel (MW).
    private float WidthFor(string? key, float x, float defaultWidth) =>
        key is not null && offsets.TryGetValue(key, out var o) && o.Width.HasValue ? o.Width.Value : MW(x, defaultWidth);

    private sealed record Palette(
        string Stroke, string Title, string? Label, string BuildingName, string Correlative,
        string TotalFill, string? TotalText, string? HeaderFill);

    private static readonly Palette Classic = new(
        Stroke: Colors.Black, Title: Colors.Black, Label: null, BuildingName: "#14363d", Correlative: "#1f2d3d",
        TotalFill: "#E4E4E4", TotalText: null, HeaderFill: null);

    private static readonly Palette Standard = new(
        Stroke: CondoPdfColors.Primary, Title: CondoPdfColors.Primary, Label: CondoPdfColors.Primary,
        BuildingName: CondoPdfColors.Primary, Correlative: CondoPdfColors.Accent,
        TotalFill: CondoPdfColors.Accent, TotalText: CondoPdfColors.White, HeaderFill: CondoPdfColors.Tint);

    private readonly Palette p = standardTemplate ? Standard : Classic;

    private const float PageWidth = 595.2756f;
    private const float PageHeight = 841.8898f;
    private const float RowTopY = 524.4094f;      // linea bajo el encabezado de la tabla

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
                // Solo en el "PDF de prueba" de calibracion: el escaneo del papel real de fondo, para
                // poder comparar en pantalla sin tener que imprimir. Las facturas reales nunca llevan esto.
                // IMPORTANTE: sin Width()/Height() explicitos antes de Image() — el Layer ya ocupa toda la
                // pagina por si solo, y agregarlos genera un conflicto de restricciones que QuestPDF resuelve
                // descartando el elemento en silencio (sin excepcion), dejando el fondo invisible.
                if (backgroundImage is not null)
                    layers.Layer().Image(backgroundImage).FitArea();
                // Si el cliente ya tiene su propio marco/casillas impresas en el papel, no dibujamos el
                // nuestro encima (pisaria el diseno real de su imprenta).
                if (!invoice.HideFrame)
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

    private void DrawFrame(LayersDescriptor l)
    {
        // Casilla del "TOTAL A PAGAR" (se dibuja primero para que las lineas queden encima).
        FillRect(l, 493.6f, 195.99f, 67.2f, 21.9f, p.TotalFill);

        // Modelo estandar: franja de color bajo los titulos de la tabla (esquinas superiores redondeadas como el marco).
        if (p.HeaderFill is not null)
            FillTopRounded(l, 34.01575f, RowTopY, 527.2441f, 561.2598f - RowTopY, p.HeaderFill);

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
    // Cada titulo y cada valor es un bloque calibrable propio (key): se mueve, cambia de letra y de ancho, o se
    // deja de dibujar ("No dibujar") si el papel ya lo trae impreso. Los titulos (label: true) arrancan sin
    // dibujar cuando el papel trae su propio marco (HideFrame), porque ese papel ya tiene todos los titulos.

    private void DrawHeader(LayersDescriptor l)
    {
        var issued = invoice.Status != InvoiceStatus.Draft;

        // Caja izquierda: el edificio a la izquierda; razon social, direccion y telefono a la derecha.
        Text(l, 48, 793, 7.5f, "Edificio", color: p.Label ?? Colors.Grey.Darken1, key: "edificioLabel", label: true);
        var (edificioX, edificioY) = Offset("headerEdificio", 48, 758f);
        var edificioSize = FontSizeFor("headerEdificio", 15f);
        if (!IsHidden("headerEdificio", false))
            l.Layer().TranslateX(edificioX).TranslateY(PageHeight - edificioY).Width(WidthFor("headerEdificio", 48, 126f)).Column(col =>
            {
                col.Item().Text(invoice.BuildingName.ToUpperInvariant()).FontSize(edificioSize).Bold().FontColor(p.BuildingName);
            });

        var (emisorX, emisorY) = Offset("headerEmisor", 182, 784f);
        // Un solo tamano calibrado se aplica a las 3 lineas (razon social, direccion, telefono) por igual.
        var emisorSize = FontSizeFor("headerEmisor", 10f);
        if (!IsHidden("headerEmisor", false))
            l.Layer().TranslateX(emisorX).TranslateY(PageHeight - emisorY).Width(WidthFor("headerEmisor", 182, 164f)).Column(col =>
            {
                if (!string.IsNullOrWhiteSpace(invoice.SeriesRazonSocial))
                    col.Item().AlignCenter().Text(invoice.SeriesRazonSocial.ToUpperInvariant()).FontSize(emisorSize).Bold();

                // Direccion del establecimiento declarada en el timbrado (la fiscal del edificio); si no la tiene, la del edificio.
                var emisorAddress = !string.IsNullOrWhiteSpace(invoice.SeriesDireccionEstablecimiento)
                    ? invoice.SeriesDireccionEstablecimiento
                    : invoice.BuildingAddress;
                if (!string.IsNullOrWhiteSpace(emisorAddress))
                    col.Item().PaddingTop(8).AlignCenter().Text(emisorAddress).FontSize(emisorSize * 0.8f).Bold();

                if (!string.IsNullOrWhiteSpace(invoice.BuildingPhone))
                    col.Item().PaddingTop(1).AlignCenter().Text($"Tel.: {invoice.BuildingPhone}").FontSize(emisorSize * 0.8f).Bold();
            });

        // Actividad economica del emisor, debajo del telefono. Es un titulo-dato (label): con papel que trae su propio marco
        // arranca sin dibujar porque ese papel ya la imprime; se activa desde la calibracion.
        if (!string.IsNullOrWhiteSpace(invoice.SeriesActividadEconomica))
            Text(l, 182, 712, 7.5f, invoice.SeriesActividadEconomica, width: 164f, align: Align.Center, key: "headerActividad", label: true);

        // Caja derecha: timbrado, vigencia, RUC, tipo de documento y numero.
        const float boxX = 362.8346f, boxW = 198.4252f;
        Text(l, boxX, 795, 9.5f, string.IsNullOrEmpty(invoice.SeriesNumeroTimbrado) ? "TIMBRADO N°" : $"TIMBRADO N°{invoice.SeriesNumeroTimbrado}",
            bold: true, width: boxW, align: Align.Center, color: p.Label, key: "headerTimbradoNumero");
        Text(l, boxX, 783, 7.5f, $"Fecha Inicio Vigencia:{Date(invoice.SeriesVigenciaDesde)}", width: boxW, align: Align.Center, key: "vigenciaDesde");
        Text(l, boxX, 773, 7.5f, $"Fecha Fin Vigencia:{Date(invoice.SeriesVigenciaHasta)}", width: boxW, align: Align.Center, key: "vigenciaHasta");
        Text(l, boxX, 760, 10.5f, string.IsNullOrEmpty(invoice.SeriesRuc) ? "RUC:" : $"RUC:{invoice.SeriesRuc}", bold: true, width: boxW, align: Align.Center, key: "seriesRuc");
        Text(l, boxX, 738, 19, "FACTURA", bold: true, width: boxW, align: Align.Center, color: p.Title, key: "docTitulo");

        // Linea tenue con el numero y la condicion, como en el formulario impreso.
        if (issued && !string.IsNullOrEmpty(invoice.NumeroFormateado))
            Text(l, boxX, 718, 7, $"{invoice.NumeroFormateado}   CONTADO", width: boxW, align: Align.Center, color: Colors.Grey.Medium, key: "numeroCondicion", label: true);
        else
            Text(l, boxX, 718, 7, "CONTADO", width: boxW, align: Align.Center, color: Colors.Grey.Medium, key: "numeroCondicion", label: true);

        // Numero grande: "Nº 001-001-" y el correlativo destacado.
        var (prefix, correlative) = SplitNumber(invoice.NumeroFormateado);
        var (numX, numY) = Offset("headerNumero", boxX, 712f);
        var numeroSize = FontSizeFor("headerNumero", 14f);
        if (!IsHidden("headerNumero", false))
        {
            var numberBox = l.Layer().TranslateX(numX).TranslateY(PageHeight - numY).Width(WidthFor("headerNumero", boxX, boxW)).AlignCenter();
            numberBox.Text(t =>
            {
                if (issued)
                {
                    t.Span("Nº ").FontSize(numeroSize).Bold().FontColor(p.Title);
                    t.Span(prefix).FontSize(numeroSize).Bold().FontColor(p.Title);
                    t.Span(correlative).FontSize(numeroSize + 3).Bold().FontColor(p.Correlative);
                }
                else
                {
                    t.Span("Nº SIN NUMERAR").FontSize(13).Bold().FontColor("#B45309");
                }
            });
        }
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
        // El tipo de documento de la ficha decide la casilla: RUC va en RUC, cedula en C.I. y pasaporte o documento extranjero en
        // la casilla del documento con su propio titulo. Sin tipo guardado (facturas viejas) se adivina: con guion es RUC (8540611-2).
        var type = invoice.ClienteTipoDocumento?.Trim();
        var hasType = !string.IsNullOrEmpty(type);
        // Una cedula nunca lleva guion: un documento con guion cargado como cedula (datos viejos) es en realidad un RUC.
        var isCedulaType = hasType && string.Equals(type, "CedulaParaguaya", StringComparison.OrdinalIgnoreCase);
        var isRuc = hasType
            ? string.Equals(type, "RUC", StringComparison.OrdinalIgnoreCase) || (isCedulaType && document.Contains('-'))
            : document.Contains('-');
        var isOtherDocument = hasType && !isRuc && !isCedulaType;

        Text(l, 45, 645, 8.5f, "FECHA DE EMISION:", color: p.Label, key: "fechaLabel", label: true);
        Text(l, 148, 645, 8.5f, DateInWords(date), bold: true, width: 160, key: "fechaEmision");
        Text(l, 292, 645, 8.5f, "CONDICION DE VENTA:", bold: true, color: p.Label, key: "condicionLabel", label: true);
        Text(l, 404, 645, 8.5f, "CONTADO", bold: true, key: "contadoLabel", label: true);
        CheckBox(l, 452, 642.5f, 11.5f, "marcaContado", checkedBox: true);
        Text(l, 472, 645, 8.5f, "CREDITO", bold: true, key: "creditoLabel", label: true);
        CheckBox(l, 517, 642.5f, 11.5f, null, checkedBox: false);

        Text(l, 45, 619, 8.5f, "NOMBRE O RAZON SOCIAL:", color: p.Label, key: "nombreLabel", label: true);
        Text(l, 172, 619, 9, invoice.ClienteNombre ?? string.Empty, width: 280, key: "clienteNombre");
        Text(l, 470, 619, 8.5f, isOtherDocument ? "DOC. Nº" : "C.I. Nº", color: p.Label, key: "ciLabel", label: true);
        if (!string.IsNullOrEmpty(document) && !isRuc)
            Text(l, isOtherDocument ? 500 : 497, 619, 9, document, width: isOtherDocument ? 61 : 60, key: "clienteDocumento");

        Text(l, 45, 593, 8.5f, "RUC:", color: p.Label, key: "rucLabel", label: true);
        if (!string.IsNullOrEmpty(document) && isRuc)
            Text(l, 74, 593, 9, document, width: 200, key: "clienteRuc");
        Text(l, 300, 593, 8.5f, "UNIDAD", bold: true, color: p.Label, key: "unidadLabel", label: true);
        Text(l, 420, 593, 9, invoice.UnitCode, width: 135, align: Align.Right, key: "unidad");
    }

    // ─── Tabla de conceptos ──────────────────────────────────────────────────

    private void DrawDetail(LayersDescriptor l)
    {
        const float col1 = 70.86614f, col2 = 328.8189f, col3 = 411.0236f, col4 = 493.2283f, right = 561.2598f;

        var bold = standardTemplate;
        Text(l, 34.01575f, 537, 8.5f, "ITEM", bold: bold, width: col1 - 34.01575f, align: Align.Center, color: p.Label, key: "itemLabel", label: true);
        Text(l, col1, 537, 9, "CONCEPTO", bold: bold, width: col2 - col1, align: Align.Center, color: p.Label, key: "conceptoLabel", label: true);
        Text(l, col2, 552, 8.5f, "VALOR DE VENTA", bold: bold, width: right - col2, align: Align.Center, color: p.Label, key: "valorVentaLabel", label: true);
        Text(l, col2, 531, 8.5f, "EXENTAS", bold: bold, width: col3 - col2, align: Align.Center, color: p.Label, key: "exentasLabel", label: true);
        Text(l, col3, 531, 8.5f, "5%", bold: bold, width: col4 - col3, align: Align.Center, color: p.Label, key: "pct5Label", label: true);
        Text(l, col4, 531, 8.5f, "10%", bold: bold, width: right - col4, align: Align.Center, color: p.Label, key: "pct10Label", label: true);

        var lines = BuildConsolidatedLines();

        // Los conceptos se listan de arriba hacia abajo, sin lineas de fila, como en el formulario impreso.
        // La primera linea (expensas) lleva una segunda linea mas chica debajo con el coeficiente.
        const float firstBaseline = 510f;
        const float spacing = 13f;
        const float subNoteSpacing = 10f;

        // Cuanto baja cada renglon (y su nota) respecto de la primera linea; los tres bloques (concepto, monto y
        // nota del coeficiente) comparten estas alturas pero cada uno tiene su propio origen calibrado.
        var rows = new List<(float Down, string Concepto, string? SubNota, decimal Monto)>();
        var down = 0f;
        foreach (var (concepto, subNota, monto) in lines)
        {
            rows.Add((down, concepto, subNota, monto));
            down += spacing;
            if (subNota is not null) down += subNoteSpacing;
        }

        // Concepto
        if (!IsHidden("conceptosBloque", false))
        {
            var (x, y) = Offset("conceptosBloque", 76, firstBaseline);
            var size = FontSizeFor("conceptosBloque", 8.5f);
            var width = WidthFor("conceptosBloque", 76, 248f);
            foreach (var r in rows)
                Text(l, x, y - r.Down, size, r.Concepto, width: width, raw: true);
        }

        // Monto (columna exentas). Si nunca se calibro aparte, sigue al bloque de concepto como antes.
        if (!IsHidden("montoExentas", false))
        {
            var amountKey = offsets.ContainsKey("montoExentas") ? "montoExentas" : "conceptosBloque";
            var (x, y) = Offset(amountKey, col2, firstBaseline);
            var size = FontSizeFor(amountKey, 8.5f);
            var width = WidthFor("montoExentas", col2, col3 - col2 - 6);
            foreach (var r in rows)
                Text(l, x, y - r.Down, size, FormatNumber(r.Monto), width: width, align: Align.Right, raw: true);
        }

        // Nota del coeficiente, debajo de la primera linea.
        if (!IsHidden("conceptosNota", false))
        {
            var noteKey = offsets.ContainsKey("conceptosNota") ? "conceptosNota" : "conceptosBloque";
            var (x, y) = Offset(noteKey, 76, firstBaseline);
            var size = offsets.ContainsKey("conceptosNota") ? FontSizeFor("conceptosNota", 7.5f) : 7.5f;
            var width = WidthFor("conceptosNota", 76, 248f);
            foreach (var r in rows.Where(r => r.SubNota is not null))
                Text(l, x, y - r.Down - spacing, size, r.SubNota!, width: width, color: Colors.Grey.Darken1, raw: true);
        }

        if (invoice.PeriodDueDate.HasValue)
            Text(l, 76, 262, 8.5f, $"Vto. {invoice.PeriodDueDate.Value:dd/MM/yyyy}.", key: "vencimiento");
    }

    // Todo lo que no es Extraordinary se junta en una sola linea "Expensas correspondiente al mes de X"
    // (ordinaria, fondo de reserva, individual, ajustes/mora); lo Extraordinary va aparte, con el % que
    // representa sobre esa expensa. Asi sale el formulario impreso, sin desglosar cada concepto suelto.
    private List<(string Concepto, string? SubNota, decimal Monto)> BuildConsolidatedLines()
    {
        var extraordinario = invoice.Detalle.Where(x => x.ChargeType == ExpenseChargeType.Extraordinary).Sum(x => x.Monto);
        var expensas = invoice.Detalle.Where(x => x.ChargeType != ExpenseChargeType.Extraordinary).Sum(x => x.Monto);

        var result = new List<(string, string?, decimal)>();

        var mes = invoice.PeriodMonth is >= 1 and <= 12 ? Months[invoice.PeriodMonth.Value - 1][..3] : null;
        var periodo = mes is not null && invoice.PeriodYear.HasValue ? $"{mes}/{invoice.PeriodYear.Value}" : null;
        var concepto = periodo is not null
            ? $"EXPENSAS CORRESPONDIENTE AL MES DE {periodo}"
            : "EXPENSAS CORRESPONDIENTE AL PERIODO";

        string? subNota = null;
        if (invoice.BuildingOrdinaryTotal > 0)
        {
            var coefPct = (invoice.UnitCoefficient * 100m).ToString("0.####", CultureInfo.InvariantCulture).Replace(".", ",");
            subNota = $"Coeficiente: {coefPct} % de {FormatNumber(invoice.BuildingOrdinaryTotal)}";
        }

        result.Add((concepto, subNota, expensas));

        if (extraordinario > 0)
        {
            var pctText = expensas > 0
                ? $" {Math.Round(extraordinario / expensas * 100m, MidpointRounding.AwayFromZero):0}%"
                : string.Empty;
            result.Add(($"APORTE EXTRAORDINARIO{pctText}", null, extraordinario));
        }

        return result;
    }

    // ─── Subtotales, total, IVA y letras ─────────────────────────────────────

    private void DrawTotals(LayersDescriptor l)
    {
        var total = invoice.MontoTotal;

        Text(l, 45, 226, 8.5f, "SUBTOTALES", color: p.Label, key: "subtotalesLabel", label: true);
        Text(l, 328.8189f, 226, 8.5f, FormatNumber(total), width: 411.0236f - 328.8189f - 6, align: Align.Right, key: "subtotal");

        Text(l, 45, 203, 8.5f, "TOTAL A PAGAR", bold: standardTemplate, color: p.Label, key: "totalLabel", label: true);
        Text(l, 493.2283f, 203, 9.5f, FormatNumber(total), bold: true, width: 561.2598f - 493.2283f - 6, align: Align.Right, color: p.TotalText, key: "totalPagar");

        Text(l, 45, 180, 8.5f, "LIQUIDACION DEL IVA: (5%)", color: p.Label, key: "liqLabel", label: true);
        Text(l, 332, 180, 8.5f, "(10%)", color: p.Label, key: "liq10Label", label: true);
        Text(l, 470, 180, 8.5f, "TOTAL IVA:", color: p.Label, key: "totalIvaLabel", label: true);

        Text(l, 45, 156, 8.5f, "SON:", bold: true, color: p.Label, key: "sonLabel", label: true);
        Text(l, 82, 156, 8.5f, NumberToWordsEs.Guaranies(total), width: 474, key: "sonEnLetras");
    }

    // ─── Pie: copias, anulacion y aviso de borrador ──────────────────────────

    private void DrawFooter(LayersDescriptor l)
    {
        Text(l, 34.01575f, 131, 6.5f, "Original: Comprador - Copia: Arch. Tributario", bold: true, width: 527.2441f, align: Align.Right, key: "pieOriginal", label: true);
        Text(l, 34.01575f, 122, 6.5f, "2°Copia: Contabilidad(No válido p/ crédito fiscal)", bold: true, width: 527.2441f, align: Align.Right, key: "pieCopia", label: true);
        if (standardTemplate)
            Text(l, 34.01575f, 131, 6.5f, "Generado por CONDOPY", width: 200, color: CondoPdfColors.Gray, key: "generadoPor");

        if (invoice.Status == InvoiceStatus.Voided)
        {
            l.Layer().TranslateX(MX(34.01575f)).TranslateY(PageHeight - MY(105f)).Width(MW(34.01575f, 527.2441f)).Height(34f)
                .Background("#FEE2E2").Border(1).BorderColor("#DC2626").Padding(6)
                .Text($"FACTURA ANULADA — {invoice.MotivoAnulacion}").FontSize(9).Bold().FontColor("#991B1B");
        }
        else if (invoice.Status != InvoiceStatus.Issued)
        {
            l.Layer().TranslateX(MX(34.01575f)).TranslateY(PageHeight - MY(105f)).Width(MW(34.01575f, 527.2441f)).Height(40f)
                .Element(PdfWatermark.ComposeNonFiscal);
        }
    }

    // ─── Utilidades de dibujo (coordenadas de la plantilla, origen abajo-izquierda) ─

    private enum Align { Left, Center, Right }

    private static readonly string[] Months =
        ["ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"];

    // "7 DE AGOSTO DE 2026", como se escribe en el formulario impreso.
    private static string DateInWords(DateTime date) => $"{date.Day} DE {Months[date.Month - 1]} DE {date.Year}";

    // Caja (x, bottomY, ancho, alto) de A4 completa -> (izquierda, arriba desde el borde superior, ancho, alto) en el papel real.
    private (float Left, float Top, float Width, float Height) Box(float x, float bottomY, float width, float height)
    {
        var h = MH(bottomY, height);
        return (MX(x), PageHeight - MY(bottomY) - h, MW(x, width), h);
    }

    private void FillRect(LayersDescriptor l, float x, float bottomY, float width, float height, string color)
    {
        var b = Box(x, bottomY, width, height);
        l.Layer().TranslateX(b.Left).TranslateY(b.Top).Width(b.Width).Height(b.Height).Background(color);
    }

    // Relleno con las esquinas superiores redondeadas (mismo radio que el marco) para no asomar fuera de el.
    private void FillTopRounded(LayersDescriptor l, float x, float bottomY, float width, float height, string color)
    {
        var b = Box(x, bottomY, width, height);
        l.Layer().TranslateX(b.Left).TranslateY(b.Top).Width(b.Width).Height(b.Height)
            .Svg(size =>
            {
                static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
                var w = size.Width;
                var h = size.Height;
                const float r = 9f;
                return $"<svg xmlns='http://www.w3.org/2000/svg' width='{F(w)}' height='{F(h)}' viewBox='0 0 {F(w)} {F(h)}'>" +
                       $"<path d='M0 {F(h)} L0 {F(r)} Q0 0 {F(r)} 0 L{F(w - r)} 0 Q{F(w)} 0 {F(w)} {F(r)} L{F(w)} {F(h)} Z' fill='{color}'/></svg>";
            });
    }

    // Casilla cuadrada (Contado / Credito); marcada lleva una X.
    // La casilla solo se dibuja si el marco es del sistema; la "X" es un bloque calibrable aparte (markKey)
    // porque el papel con casillas propias igual necesita la marca encima.
    private void CheckBox(LayersDescriptor l, float x, float bottomY, float size, string? markKey, bool checkedBox)
    {
        if (!invoice.HideFrame)
        {
            var b = Box(x, bottomY, size, size);
            l.Layer().TranslateX(b.Left).TranslateY(b.Top).Width(b.Width).Height(b.Height).Border(0.9f).BorderColor(Colors.Black);
        }
        if (checkedBox)
            Text(l, x, bottomY + 2.2f, size - 1.5f, "X", bold: true, width: size, align: Align.Center, key: markKey);
    }

    private void Rect(LayersDescriptor l, float x, float bottomY, float width, float height)
    {
        var b = Box(x, bottomY, width, height);
        l.Layer().TranslateX(b.Left).TranslateY(b.Top).Width(b.Width).Height(b.Height)
            .Svg(size =>
            {
                var w = size.Width.ToString("0.###", CultureInfo.InvariantCulture);
                var h = size.Height.ToString("0.###", CultureInfo.InvariantCulture);
                var rw = (size.Width - 0.9f).ToString("0.###", CultureInfo.InvariantCulture);
                var rh = (size.Height - 0.9f).ToString("0.###", CultureInfo.InvariantCulture);
                return $"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}' viewBox='0 0 {w} {h}'>" +
                       $"<rect x='0.45' y='0.45' width='{rw}' height='{rh}' rx='9' ry='9' fill='none' stroke='{p.Stroke}' stroke-width='0.9'/></svg>";
            });
    }

    private void HLine(LayersDescriptor l, float x1, float y, float x2) =>
        l.Layer().TranslateX(MX(x1)).TranslateY(PageHeight - MY(y) - 0.4f).Width(MX(x2) - MX(x1)).Height(0.8f).Background(p.Stroke);

    private void VLine(LayersDescriptor l, float x, float y1, float y2) =>
        l.Layer().TranslateX(MX(x) - 0.4f).TranslateY(PageHeight - MY(y2)).Width(0.8f).Height(MY(y2) - MY(y1)).Background(p.Stroke);

    private void Text(
        LayersDescriptor l, float x, float baselineY, float size, string text,
        bool bold = false, float? width = null, Align align = Align.Left, string? color = null, string? key = null, bool raw = false, bool label = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (!raw && IsHidden(key, label && invoice.HideFrame)) return;

        // raw: x, baselineY y width ya vienen en coordenadas del papel real (bloques con offset ya aplicado).
        if (!raw)
        {
            if (width.HasValue) width = WidthFor(key, x, width.Value);
            (x, baselineY) = Offset(key, x, baselineY);
        }
        size = FontSizeFor(key, size);

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
