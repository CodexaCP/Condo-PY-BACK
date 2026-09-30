using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Enums;
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
    // Width: ancho del bloque; RowHeight: alto de cada fila del cuerpo (solo en la key "filas"); Hidden: no
    // dibujar el bloque (el papel ya lo trae impreso).
    private sealed record FieldOffset(
        float Dx, float Dy, float? FontSize = null, float? Width = null, float? RowHeight = null, bool? Hidden = null);

    // Posicion base de cada bloque sobre el papel propio (puntos PDF, desde la esquina superior izquierda).
    // Las keys, x, top, tamanos y anchos tienen que coincidir con FIELDS de settlement-calibration-page.component.ts:
    // si se agrega un bloque calibrable aca, hay que agregarlo alla tambien para poder arrastrarlo.
    // Align: 'L' izquierda, 'C' centrado, 'R' derecha (dentro del ancho del bloque).
    private sealed record FieldDefault(float X, float Top, float Size, float Width = 0, char Align = 'L', bool HiddenByDefault = false);

    // Cada dato es un bloque propio (etiqueta y valor por separado) para que se pueda ubicar, ensanchar u ocultar
    // solo, segun lo que el papel ya traiga impreso. Las columnas del cuerpo van una por bloque.
    // Categorias de gasto: cada una es un titulo, una descripcion y un valor (como las de ingreso). Las filas
    // por defecto siguen el orden de la planilla, una debajo de la otra.
    private static readonly (BuildingExpenseCategory Category, string Text)[] ExpenseBlocks =
    [
        (BuildingExpenseCategory.Ande, "ANDE"),
        (BuildingExpenseCategory.Essap, "ESSAP S.A."),
        (BuildingExpenseCategory.Utilities, "SERVICIOS"),
        (BuildingExpenseCategory.InternetPhone, "INTERNET Y TELEFONÍA"),
        (BuildingExpenseCategory.Cleaning, "LIMPIEZA"),
        (BuildingExpenseCategory.Security, "SEGURIDAD"),
        (BuildingExpenseCategory.Maintenance, "MANTENIMIENTO"),
        (BuildingExpenseCategory.Elevator, "ASCENSOR"),
        (BuildingExpenseCategory.Insurance, "SEGURO"),
        (BuildingExpenseCategory.Supplies, "INSUMOS"),
        (BuildingExpenseCategory.Payroll, "SALARIOS"),
        (BuildingExpenseCategory.Taxes, "IMPUESTOS"),
        (BuildingExpenseCategory.Administration, "ADMINISTRACIÓN"),
        (BuildingExpenseCategory.Extraordinary, "EXTRAORDINARIO"),
        (BuildingExpenseCategory.Other, "OTROS GASTOS"),
        (BuildingExpenseCategory.ReserveFund, "FONDO DE RESERVA")
    ];

    private static string ExpensePrefix(BuildingExpenseCategory category) => "gasto" + category;

    // Categorias que se imprimen con un renglon por descripcion distinta (hasta MaxCategoryLines), cada uno con su
    // monto; las demas van en una sola linea con el total.
    private static readonly HashSet<BuildingExpenseCategory> MultiLineCategories = [BuildingExpenseCategory.Ande];
    private const int MaxCategoryLines = 5;

    // Agrupa los gastos de la categoria por descripcion (o proveedor si no tienen) y suma sus montos. Si hay mas de
    // MaxCategoryLines, lo que sobra se junta en el ultimo renglon como "OTROS".
    private static List<(string Description, decimal Amount)> CategoryLines(List<SettlementExpenseLineDto> lines)
    {
        var groups = lines
            .GroupBy(x => x.Description.Trim().Length > 0 ? x.Description.Trim() : x.Supplier.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Description: g.Key, Amount: g.Sum(x => x.Amount)))
            .ToList();
        if (groups.Count <= MaxCategoryLines) return groups;

        var result = groups.Take(MaxCategoryLines - 1).ToList();
        result.Add(("OTROS", groups.Skip(MaxCategoryLines - 1).Sum(x => x.Amount)));
        return result;
    }

    // Perezoso: BaseDefaults se declara mas abajo y los campos estaticos se inicializan en orden de aparicion.
    private static IReadOnlyDictionary<string, FieldDefault>? fieldDefaults;
    private static IReadOnlyDictionary<string, FieldDefault> FieldDefaults => fieldDefaults ??= BuildFieldDefaults();

    private static Dictionary<string, FieldDefault> BuildFieldDefaults()
    {
        var defaults = new Dictionary<string, FieldDefault>(BaseDefaults);
        for (var i = 0; i < ExpenseBlocks.Length; i++)
        {
            var top = 145f + i * 15f;
            var prefix = ExpensePrefix(ExpenseBlocks[i].Category);
            defaults[prefix + "Label"] = new(50, top, 8, 135);
            defaults[prefix + "Descripcion"] = new(185, top, 8, 220);
            defaults[prefix + "Valor"] = new(475, top, 8, 70, 'R');
        }
        return defaults;
    }

    private static readonly Dictionary<string, FieldDefault> BaseDefaults = new()
    {
        // Encabezado: titulo (etiqueta) y luego cada valor por separado.
        ["titulo"] = new(195, 86, 11),
        ["edificio"] = new(66, 100, 8),
        ["mes"] = new(215, 112, 10),
        ["anio"] = new(330, 112, 10),

        // Saldo acumulado: titulo y valor aparte de los conceptos (solo en la primera hoja).
        ["saldoLabel"] = new(50, 130, 8, 200),
        ["saldoDescripcion"] = new(255, 130, 8, 110),
        ["saldoValor"] = new(475, 130, 8, 70, 'R'),

        // Fondo operativo: igual que el saldo acumulado, titulo y valor aparte (solo en la primera hoja).
        ["fondoOperativoLabel"] = new(50, 118, 8, 200),
        ["fondoOperativoDescripcion"] = new(255, 118, 8, 110),
        ["fondoOperativoValor"] = new(475, 118, 8, 70, 'R'),

        // El resto de las categorias de ingreso, cada una con su titulo y su valor.
        ["alquilerLabel"] = new(370, 106, 8, 105),
        ["alquilerDescripcion"] = new(250, 106, 8, 115),
        ["alquilerValor"] = new(475, 106, 8, 70, 'R'),
        ["interesLabel"] = new(370, 94, 8, 105),
        ["interesDescripcion"] = new(250, 94, 8, 115),
        ["interesValor"] = new(475, 94, 8, 70, 'R'),
        ["ajusteLabel"] = new(370, 82, 8, 105),
        ["ajusteDescripcion"] = new(250, 82, 8, 115),
        ["ajusteValor"] = new(475, 82, 8, 70, 'R'),
        ["aporteExtraLabel"] = new(370, 70, 8, 105),
        ["aporteExtraDescripcion"] = new(250, 70, 8, 115),
        ["aporteExtraValor"] = new(475, 70, 8, 70, 'R'),
        ["otroLabel"] = new(370, 58, 8, 105),
        ["otroDescripcion"] = new(250, 58, 8, 115),
        ["otroValor"] = new(475, 58, 8, 70, 'R'),

        // Cuerpo: una columna por bloque, con el alto de fila comun ("filas").
        // (arrancan ocultos: los gastos se imprimen por categoria, cada una con su titulo, descripcion y valor;
        // el cuerpo con una fila por gasto sigue disponible destildando "No dibujar")
        ["colConcepto"] = new(50, 145, 8, 135, HiddenByDefault: true),
        ["colDescripcion"] = new(185, 145, 8, 220, HiddenByDefault: true),
        ["colReserva"] = new(405, 145, 8, 70, 'R', true),
        ["colMonto"] = new(475, 145, 8, 70, 'R', true),

        // Totales: cada titulo y cada valor por separado (solo salen en la ultima hoja).
        // El mes se usa en dos lugares del papel: arriba (mes) y en la franja de totales (mesTotales).
        ["mesTotales"] = new(250, 598, 8, 100, 'C'),
        ["totIngresosLabel"] = new(50, 585, 8, 200),
        // Aportes calculados: el de reserva es un % de los gastos comunes (en las dos columnas) y el
        // extraordinario un % del sub total. Solo salen si el edificio tiene el porcentaje configurado.
        ["reservaPctLabel"] = new(50, 637, 8, 250),
        ["reservaPctValorReserva"] = new(405, 637, 8, 70, 'R'),
        ["reservaPctValorComunes"] = new(475, 637, 8, 70, 'R'),
        ["extraPctLabel"] = new(50, 650, 8, 250),
        ["extraPctValor"] = new(475, 650, 8, 70, 'R'),
        ["totIngresosValor"] = new(475, 585, 8, 70, 'R'),
        ["totGastosLabel"] = new(50, 598, 8, 200),
        // Suma de los valores de todos los conceptos (todas las categorias de gasto), en un solo valor.
        ["totGastosValor"] = new(335, 598, 8, 70, 'R'),
        ["totGastosReserva"] = new(405, 598, 8, 70, 'R'),
        ["totGastosComunes"] = new(475, 598, 8, 70, 'R'),
        ["subTotalValor"] = new(475, 611, 8, 70, 'R'),
        ["totalValor"] = new(475, 624, 8, 70, 'R'),

        // Fechas: el papel ya trae "Fecha de emision"; vigencia y vencimiento con su etiqueta aparte.
        ["fechaEmision"] = new(165, 705, 8),
        ["vigenciaLabel"] = new(52, 745, 8),
        ["vigenciaDesde"] = new(110, 745, 8),
        ["vigenciaHasta"] = new(165, 745, 8),
        ["vencimientoLabel"] = new(52, 757, 8),
        ["vencimiento"] = new(120, 757, 8),

        // Firmas: por cada una, la imagen, el nombre y el cargo por separado.
        // Autorizado = presidente del consorcio (imagen, nombre y cargo); Verificacion = encargado de edificio
        // (building manager), solo la imagen.
        ["firmaAutorizado"] = new(250, 655, 8, 110),
        ["firmaAutorizadoNombre"] = new(215, 700, 8, 175, 'C'),
        ["firmaAutorizadoCargo"] = new(215, 711, 8, 175, 'C'),
        ["firmaVerificacion"] = new(420, 655, 8, 110),

    };

    // "filas" no es un bloque: guarda el alto de fila comun de las cuatro columnas.
    public const string RowsKey = "filas";
    private const float DefaultRowHeight = 15f;

    public static IReadOnlyCollection<string> CalibratableKeys => FieldDefaults.Keys.Append(RowsKey).ToList();

    private const float PageWidth = 595.2756f;
    private const float PageHeight = 841.8898f;
    private const float BlockWidth = 495f;

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
    private (float X, float Top, float Size, float Width) Place(string key)
    {
        var d = FieldDefaults[key];
        offsets.TryGetValue(key, out var o);
        return (d.X + (o?.Dx ?? 0), d.Top - (o?.Dy ?? 0),
            o?.FontSize is > 0 ? o.FontSize.Value : d.Size,
            o?.Width is > 0 ? o.Width.Value : d.Width);
    }

    private bool IsHidden(string key) =>
        offsets.TryGetValue(key, out var o) && o.Hidden.HasValue ? o.Hidden.Value : FieldDefaults[key].HiddenByDefault;

    private float RowHeight =>
        offsets.TryGetValue(RowsKey, out var o) && o.RowHeight is >= 6 and <= 80 ? o.RowHeight.Value : DefaultRowHeight;

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

    // Sobre el papel propio del edificio la liquidacion sale como planilla y cada dato es un bloque independiente
    // con su posicion, ancho y letra (y se puede ocultar si el papel ya lo trae impreso): las cuatro columnas del
    // cuerpo (concepto, descripcion y los dos montos) apoyadas en un alto de fila comun, y aparte cada titulo y
    // cada valor de los totales, de las fechas, de las firmas y del pie. Un renglon del cuerpo nunca pasa a dos
    // lineas (se corta con "..."): asi las columnas siempre quedan alineadas. Si las filas no entran en la hoja
    // siguen en la siguiente, en la misma zona del papel; los totales salen solo en la ultima hoja.
    private sealed record BodyRow(string Concept, string Description, string Reserve, string Amount);

    // Todo lo que va abajo de las filas: si el bloque no esta oculto, las filas terminan antes de el.
    private static readonly string[] BottomKeys =
    [
        "reservaPctLabel", "reservaPctValorReserva", "reservaPctValorComunes", "extraPctLabel", "extraPctValor",
        "totIngresosLabel", "totIngresosValor", "totGastosLabel", "totGastosValor", "totGastosReserva", "totGastosComunes",
        "subTotalValor", "totalValor",
        "fechaEmision", "vigenciaLabel", "vigenciaDesde", "vigenciaHasta", "vencimientoLabel", "vencimiento",
        "firmaAutorizado", "firmaAutorizadoNombre", "firmaAutorizadoCargo",
        "firmaVerificacion"
    ];

    // Una categoria de ingreso = un titulo y un valor, cada uno su bloque calibrable.
    private static readonly (BuildingIncomeCategory Category, string Prefix, string Text)[] IncomeBlocks =
    [
        (BuildingIncomeCategory.AccumulatedBalance, "saldo", "SALDO ACUMULADO"),
        (BuildingIncomeCategory.OperationalFund, "fondoOperativo", "FONDO OPERATIVO"),
        (BuildingIncomeCategory.CommonAreaRental, "alquiler", "ALQUILER/USO DE SALÓN"),
        (BuildingIncomeCategory.Interest, "interes", "INTERÉS"),
        (BuildingIncomeCategory.CreditAdjustment, "ajuste", "AJUSTE A FAVOR"),
        (BuildingIncomeCategory.ExtraordinaryContribution, "aporteExtra", "APORTE EXTRAORDINARIO"),
        (BuildingIncomeCategory.Other, "otro", "OTROS INGRESOS")
    ];

    private IReadOnlyList<BodyRow> BuildBodyRows()
    {
        var rows = new List<BodyRow>();

        // Un gasto por linea; el del fondo de reserva va en su columna. Los ingresos no van en el cuerpo: cada
        // categoria tiene su propio titulo y valor.
        foreach (var expense in ExpenseRows())
            rows.Add(new BodyRow(
                expense.Supplier.ToUpperInvariant(),
                expense.Description.ToUpperInvariant(),
                expense.IsReserveFund ? FormatNumber(expense.Amount) : string.Empty,
                expense.IsReserveFund ? string.Empty : FormatNumber(expense.Amount)));

        return rows;
    }

    private void ComposeOnPaper(IDocumentContainer container)
    {
        // Sin columnas del cuerpo a la vista (por defecto), no hay filas: una sola hoja.
        var bodyVisible = new[] { "colConcepto", "colDescripcion", "colReserva", "colMonto" }.Any(key => !IsHidden(key));
        var rows = bodyVisible ? BuildBodyRows() : Array.Empty<BodyRow>();
        var rowHeight = RowHeight;

        // Las filas terminan antes del primer bloque que este DEBAJO de ellas y sobre su misma franja horizontal.
        // Un bloque arrastrado por encima del inicio de las filas, o a un costado (el papel trae ahi su propia
        // caja), no las limita: si no, quedaria casi sin espacio y saldria una fila por hoja.
        var firstTop = Place("colConcepto").Top;
        var bodyLeft = float.MaxValue;
        var bodyRight = 0f;
        foreach (var key in new[] { "colConcepto", "colDescripcion", "colReserva", "colMonto" })
        {
            if (IsHidden(key)) continue;
            var col = Place(key);
            bodyLeft = Math.Min(bodyLeft, col.X);
            bodyRight = Math.Max(bodyRight, col.X + col.Width);
        }

        var lowest = PageHeight - 20f;
        foreach (var key in BottomKeys)
        {
            if (IsHidden(key)) continue;
            var block = Place(key);
            var blockRight = block.X + (block.Width > 0 ? block.Width : 60f);
            var overlapsBody = block.X < bodyRight && blockRight > bodyLeft;
            if (block.Top > firstTop + rowHeight && overlapsBody) lowest = Math.Min(lowest, block.Top);
        }

        var capacity = Math.Max(3, (int)Math.Floor((lowest - 6f - firstTop) / rowHeight));

        var chunks = rows.Count == 0 ? new List<BodyRow[]> { Array.Empty<BodyRow>() } : rows.Chunk(capacity).ToList();
        for (var pageIndex = 0; pageIndex < chunks.Count; pageIndex++)
        {
            var pageRows = chunks[pageIndex];
            var isLastPage = pageIndex == chunks.Count - 1;

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(8).FontColor(p.Primary));
                page.Background().Image(backgroundImage!).FitArea();

                page.Content().Layers(layers =>
                {
                    layers.PrimaryLayer().Text(string.Empty);
                    ComposePaperRows(layers, pageRows, rowHeight);
                });

                page.Foreground().Layers(fg =>
                {
                    fg.PrimaryLayer().Text(string.Empty);
                    ComposePaperFixedBlocks(fg, isFirstPage: pageIndex == 0, isLastPage);
                });
            });
        }
    }

    private void ComposePaperRows(LayersDescriptor layers, BodyRow[] pageRows, float rowHeight)
    {
        var concepto = Place("colConcepto");
        var descripcion = Place("colDescripcion");
        var reserva = Place("colReserva");
        var monto = Place("colMonto");

        // Papel liso (sin marco impreso): una linea fina al pie de cada fila, del primer al ultimo renglon.
        if (!hideFrame)
        {
            var left = Math.Min(concepto.X, Math.Min(descripcion.X, Math.Min(reserva.X, monto.X)));
            var right = Math.Max(concepto.X + concepto.Width, Math.Max(descripcion.X + descripcion.Width, Math.Max(reserva.X + reserva.Width, monto.X + monto.Width)));
            for (var i = 0; i < pageRows.Length; i++)
                layers.Layer().TranslateX(left).TranslateY(concepto.Top + (i + 1) * rowHeight - 1f)
                    .Width(Math.Max(10f, right - left)).Height(0.5f).Background("#000000");
        }

        for (var i = 0; i < pageRows.Length; i++)
        {
            var row = pageRows[i];
            if (!IsHidden("colConcepto")) PaperCell(layers, concepto, i, rowHeight, row.Concept, right: false);
            if (!IsHidden("colDescripcion")) PaperCell(layers, descripcion, i, rowHeight, row.Description, right: false);
            if (!IsHidden("colReserva")) PaperCell(layers, reserva, i, rowHeight, row.Reserve, right: true);
            if (!IsHidden("colMonto")) PaperCell(layers, monto, i, rowHeight, row.Amount, right: true);
        }
    }

    private static void PaperCell(
        LayersDescriptor layers, (float X, float Top, float Size, float Width) col, int index, float rowHeight,
        string text, bool right)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        layers.Layer().TranslateX(col.X).TranslateY(col.Top + index * rowHeight).Width(Math.Max(10f, col.Width))
            .Text(t =>
            {
                t.ClampLines(1);
                if (right) t.AlignRight();
                t.Span(text).FontSize(col.Size);
            });
    }

    private void ComposePaperFixedBlocks(LayersDescriptor fg, bool isFirstPage, bool isLastPage)
    {
        var titleStyle = true;

        // Encabezado
        PaperText(fg, "titulo", "LIQUIDACIÓN EXPENSAS COMUNES", bold: titleStyle);
        PaperText(fg, "edificio", summary.BuildingName.ToUpperInvariant(), bold: true);
        PaperText(fg, "mes", MonthText(), bold: true);
        PaperText(fg, "anio", summary.PeriodYear > 0 ? summary.PeriodYear.ToString() : string.Empty, bold: true);

        // Ingresos: cada categoria con su titulo, su descripcion y su valor por separado, solo en la primera hoja y solo las que
        // tienen ingresos en el periodo.
        if (isFirstPage)
        {
            foreach (var (category, prefix, text) in IncomeBlocks)
            {
                if (!summary.IncomeTotals.TryGetValue(category.ToString(), out var amount)) continue;
                summary.IncomeDescriptions.TryGetValue(category.ToString(), out var description);
                PaperText(fg, prefix + "Label", text, bold: true);
                PaperText(fg, prefix + "Descripcion", description?.ToUpperInvariant());
                PaperText(fg, prefix + "Valor", FormatNumber(amount), bold: true);
            }
        }

        // Gastos: cada categoria con su titulo, su descripcion y su valor por separado, solo en la primera hoja
        // y solo las que tienen gastos en el periodo. El valor es el total de la categoria y la descripcion junta
        // las de sus gastos (o los proveedores si no tienen descripcion).
        if (isFirstPage)
        {
            var byCategory = ExpenseRows().Where(x => x.Category.Length > 0).GroupBy(x => x.Category).ToDictionary(g => g.Key, g => g.ToList());
            // Lo que baja cada categoria siguiente por los renglones de mas que uso una anterior (ANDE con varias
            // descripciones): asi nunca se pisan y no hace falta recalibrar segun cuantas haya.
            var pushDown = 0f;
            foreach (var (category, text) in ExpenseBlocks)
            {
                if (!byCategory.TryGetValue(category.ToString(), out var lines)) continue;
                var prefix = ExpensePrefix(category);
                PaperText(fg, prefix + "Label", text, bold: true, extraTop: pushDown);

                // ANDE: un renglon por descripcion distinta (titulo una sola vez), con su propio monto. Con una
                // sola descripcion sale una linea, sin renglones vacios.
                if (MultiLineCategories.Contains(category))
                {
                    var rowHeight = RowHeight;
                    var categoryLines = CategoryLines(lines);
                    for (var i = 0; i < categoryLines.Count; i++)
                    {
                        PaperText(fg, prefix + "Descripcion", categoryLines[i].Description.ToUpperInvariant(), extraTop: pushDown + i * rowHeight);
                        PaperText(fg, prefix + "Valor", FormatNumber(categoryLines[i].Amount), bold: true, extraTop: pushDown + i * rowHeight);
                    }
                    pushDown += (categoryLines.Count - 1) * rowHeight;
                    continue;
                }

                var descriptions = lines.Select(x => x.Description.Trim()).Where(x => x.Length > 0).Distinct().ToList();
                if (descriptions.Count == 0)
                    descriptions = lines.Select(x => x.Supplier.Trim()).Where(x => x.Length > 0).Distinct().ToList();

                PaperText(fg, prefix + "Descripcion", string.Join(" / ", descriptions).ToUpperInvariant(), extraTop: pushDown);
                PaperText(fg, prefix + "Valor", FormatNumber(lines.Sum(x => x.Amount)), bold: true, extraTop: pushDown);
            }
        }

        // Totales: solo en la ultima hoja, cada titulo y cada valor por separado.
        if (isLastPage)
        {
            var expenses = ExpenseRows();
            var reserveTotal = expenses.Where(x => x.IsReserveFund).Sum(x => x.Amount);
            var commonTotal = expenses.Where(x => !x.IsReserveFund).Sum(x => x.Amount);

            PaperText(fg, "mesTotales", MonthText(), bold: true);
            PaperText(fg, "totIngresosLabel", "TOTAL PARA GASTOS", bold: true);
            PaperText(fg, "totIngresosValor", FormatNumber(summary.TotalBuildingIncomes), bold: true);
            PaperText(fg, "totGastosLabel", "TOTAL GASTOS DEL MES", bold: true);
            PaperText(fg, "totGastosValor", FormatNumber(expenses.Sum(x => x.Amount)), bold: true);
            PaperText(fg, "totGastosReserva", reserveTotal > 0 ? FormatNumber(reserveTotal) : string.Empty, bold: true);
            PaperText(fg, "totGastosComunes", FormatNumber(commonTotal), bold: true);

            // Aportes calculados (redondeo al guarani): reserva = % de los gastos comunes; sub total = gastos
            // comunes + reserva; extraordinario = % del sub total; total general = sub total + extraordinario.
            var reservePct = summary.ReservePercentage ?? 0m;
            var extraPct = summary.ExtraordinaryPercentage ?? 0m;
            // Mismo calculo que usa el reparto de cargos: base = gastos comunes (menos los ingresos si el edificio
            // los acredita a los propietarios).
            var calc = SettlementContributions.Compute(
                commonTotal, summary.IncomeTreatment == IncomeTreatment.CreditToOwners ? summary.TotalBuildingIncomes : 0m,
                summary.ReservePercentage, summary.ExtraordinaryPercentage);
            var reserveContribution = calc.ReserveContribution;
            var subTotal = calc.SubTotal;
            var extraContribution = calc.ExtraordinaryContribution;

            if (reservePct > 0)
            {
                PaperText(fg, "reservaPctLabel", $"APORTE DE FONDO DE RESERVA {reservePct:0.##}%", bold: true);
                PaperText(fg, "reservaPctValorReserva", FormatNumber(reserveContribution), bold: true);
                PaperText(fg, "reservaPctValorComunes", FormatNumber(reserveContribution), bold: true);
            }

            PaperText(fg, "subTotalValor", FormatNumber(subTotal), bold: true);

            if (extraPct > 0)
            {
                PaperText(fg, "extraPctLabel", $"APORTE EXTRAORDINARIO {extraPct:0.##}%", bold: true);
                PaperText(fg, "extraPctValor", FormatNumber(extraContribution), bold: true);
            }

            PaperText(fg, "totalValor", FormatNumber(subTotal + extraContribution), bold: true);
        }

        // Fechas: el papel ya trae "Fecha de emision", asi que solo el valor; vigencia y vencimiento con su etiqueta.
        var emitted = (summary.GeneratedAtUtc ?? DateTime.UtcNow).ToLocalTime();
        PaperText(fg, "fechaEmision", emitted.ToString("dd/MM/yyyy"));
        PaperText(fg, "vigenciaLabel", "VIGENCIA");
        PaperText(fg, "vigenciaDesde", periodStartDate);
        PaperText(fg, "vigenciaHasta", periodEndDate);
        PaperText(fg, "vencimientoLabel", "VENCIMIENTO");
        PaperText(fg, "vencimiento", periodDueDate);

        // Firmas: Autorizado = presidente (imagen, nombre y cargo por separado) y Verificacion = encargado de
        // edificio (solo la imagen). Si todavia no firmo nadie en ese lugar, queda vacio.
        PaperSignature(fg, "firmaAutorizado", president, withNameAndTitle: true);
        PaperSignature(fg, "firmaVerificacion", approver, withNameAndTitle: false);
    }

    // Un dato suelto en su posicion. Los que tienen ancho se alinean segun su Align (montos a la derecha,
    // nombres centrados); los demas van en una linea desde su posicion.
    private void PaperText(LayersDescriptor fg, string key, string? text, bool bold = false, float extraTop = 0f)
    {
        if (string.IsNullOrWhiteSpace(text) || IsHidden(key)) return;

        var place = Place(key);
        var align = FieldDefaults[key].Align;
        var layer = fg.Layer().TranslateX(place.X).TranslateY(place.Top + extraTop);
        var box = place.Width > 0 ? layer.Width(BlockWidthAt(place.X, place.Width)) : layer;
        box.Text(t =>
        {
            // Con ancho propio, una sola linea (se corta con "..."): asi un texto largo no pisa al de abajo.
            if (place.Width > 0) t.ClampLines(1);
            if (align == 'R') t.AlignRight();
            else if (align == 'C') t.AlignCenter();
            var span = t.Span(text).FontSize(place.Size);
            if (bold) span.Bold();
        });
    }

    // Firma de un cajetin: imagen, nombre y cargo son tres bloques independientes.
    private void PaperSignature(LayersDescriptor fg, string key, SettlementSignature? signature, bool withNameAndTitle)
    {
        if (signature is null) return;

        if (!IsHidden(key) && signature.Image is { Length: > 0 })
        {
            var place = Place(key);
            fg.Layer().TranslateX(place.X).TranslateY(place.Top).Width(Math.Max(10f, place.Width)).Height(40)
                .AlignCenter().AlignBottom()
                .Element(img => img.Image(signature.Image).FitArea());
        }

        if (!withNameAndTitle) return;
        PaperText(fg, key + "Nombre", signature.Name, bold: true);
        PaperText(fg, key + "Cargo", signature.Title);
    }

    private string MonthText()
    {
        if (summary.PeriodMonth is >= 1 and <= 12)
            return System.Globalization.CultureInfo.GetCultureInfo("es-PY").DateTimeFormat.GetMonthName(summary.PeriodMonth).ToUpperInvariant();
        return summary.ExpensePeriodName.ToUpperInvariant();
    }

    // Gastos linea por linea; si no vinieron (p. ej. una liquidacion sin detalle) se arman con los totales
    // por categoria.
    private IReadOnlyList<SettlementExpenseLineDto> ExpenseRows() =>
        summary.ExpenseLines.Count > 0
            ? summary.ExpenseLines
            : summary.CategoryTotals.SelectMany(c => c.Items.Select(i => new SettlementExpenseLineDto
            {
                Description = i.Description,
                Amount = i.Amount,
                IsReserveFund = c.Category == "ReserveFund"
            })).ToList();

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

    // En el diseno estandar no se repite a la misma persona si aprobo y publico.
    private SettlementSignature? StandardPublisher =>
        publisher is not null && approver is not null && publisher.Name == approver.Name && publisher.Title == approver.Title
            ? null
            : publisher;

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(c => ComposeSignatureBlock(c, approver));
            row.ConstantItem(24);
            row.RelativeItem().Element(c => ComposeSignatureBlock(c, president));
            row.ConstantItem(24);
            row.RelativeItem().Element(c => ComposeSignatureBlock(c, StandardPublisher));
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
