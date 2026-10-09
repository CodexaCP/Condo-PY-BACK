using ClosedXML.Excel;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Api.Documents;

/// <summary>
/// Excel del modulo "Finanzas del edificio" para el contador. Cada hoja es una tabla plana (una fila por registro, un encabezado,
/// fechas e importes como valores reales, sin celdas combinadas) para que se pueda filtrar, sumar y cargar en otro sistema. Los
/// importes son guaranies enteros; las fechas, dd/mm/aaaa. Donde hay un rubro se incluyen su codigo y el codigo del contador que se
/// cargo en el plan de cuentas. Las columnas y su orden son fijos: no cambian entre exportaciones.
/// </summary>
public static class FinanceExcelExporter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string AmountFormat = "#,##0;[Red]-#,##0";
    private const string DateFormat = "dd/mm/yyyy";
    private const string PercentFormat = "0.0\"%\"";
    private const string CriteriaText = "Caja y saldos: percibido (lo cobrado y pagado). Cuentas por cobrar y morosidad: devengado (lo emitido). Importes en guaraníes.";

    private static readonly string[] MonthNames = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

    public static string MonthLabel(int year, int month) => $"{MonthNames[month - 1]} {year}";

    // Nombre de archivo seguro: sin tildes, espacios ni simbolos.
    public static string FileName(string kind, string buildingName, string period)
    {
        var decomposed = buildingName.Normalize(System.Text.NormalizationForm.FormD);
        var chars = decomposed
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = System.Text.RegularExpressions.Regex.Replace(new string(chars), "-{2,}", "-").Trim('-');
        return $"finanzas-{kind}-{(slug.Length == 0 ? "edificio" : slug)}-{period}.xlsx";
    }

    public static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ── Hojas ─────────────────────────────────────────────────────────────────

    /// <summary>Hoja inicial del paquete para el contador: de que edificio y periodo es y con que criterio se armaron los numeros.</summary>
    public static void AddSummary(XLWorkbook wb, LedgerContext ctx, DateTime generatedAtLocal, string periodText, IEnumerable<string> sheets)
    {
        var ws = wb.Worksheets.Add("Resumen");
        ws.Cell(1, 1).Value = $"Finanzas del edificio — {ctx.BuildingName}";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        var rows = new (string Label, string Value)[]
        {
            ("Edificio", ctx.BuildingName),
            ("Período", periodText),
            ("Fecha de arranque del módulo", ctx.StartDate.ToString("dd/MM/yyyy")),
            ("Generado el", generatedAtLocal.ToString("dd/MM/yyyy HH:mm")),
            ("Criterio", CriteriaText),
            ("Origen de los datos", "Libro derivado de los cobros de propietarios, los gastos y los ingresos cargados en CondoPY; no es una contabilidad."),
            ("Hojas", string.Join(", ", sheets))
        };

        var row = 3;
        foreach (var (label, value) in rows)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 1).Style.Font.SetBold();
            ws.Cell(row, 2).Value = value;
            ws.Cell(row, 2).Style.Alignment.WrapText = true;
            row++;
        }

        ws.Column(1).Width = 30;
        ws.Column(2).Width = 100;
    }

    /// <summary>Plan de cuentas del edificio, con el codigo del contador de cada rubro.</summary>
    public static void AddChart(XLWorkbook wb, LedgerContext ctx)
    {
        var ws = Sheet(wb, "Plan de cuentas", $"Plan de cuentas — {ctx.BuildingName}", "Cuentas del edificio con su código y el código del contador (columna E). El grupo muestra la ruta completa.");
        Header(ws, 4, "Código", "Nombre", "Clase", "Grupo", "Código del contador", "Función", "Estado", "En la liquidación cuenta como", "Nivel", "Tratamiento de IVA");

        var parents = ctx.Categories.Where(c => c.ParentId.HasValue).Select(c => c.ParentId!.Value).ToHashSet();
        var row = 5;
        foreach (var c in ctx.Categories.OrderBy(x => x.Code, StringComparer.Ordinal))
        {
            // Ruta de grupos, de la clase hacia la cuenta (sin la cuenta misma).
            var path = new List<string>();
            var cursor = c;
            var level = 1;
            while (cursor.ParentId.HasValue && ctx.ById.TryGetValue(cursor.ParentId.Value, out var up) && level < 20)
            {
                path.Insert(0, $"{up.Code} · {up.Name}");
                cursor = up;
                level++;
            }

            var isGroup = parents.Contains(c.Id);
            ws.Cell(row, 1).Value = c.Code;
            ws.Cell(row, 2).Value = c.Name;
            ws.Cell(row, 3).Value = TypeLabel(c.Type);
            ws.Cell(row, 4).Value = string.Join(" › ", path);
            ws.Cell(row, 5).Value = c.ExternalCode ?? string.Empty;
            ws.Cell(row, 6).Value = FinanceChartTemplate.FindRole(c.SystemKey)?.Label ?? (isGroup ? "Grupo" : string.Empty);
            ws.Cell(row, 7).Value = c.IsActive ? "Activo" : "Inactivo";
            ws.Cell(row, 8).Value = isGroup ? string.Empty : SettlementCategoryText(c);
            ws.Cell(row, 9).Value = level;
            ws.Cell(row, 10).Value = VatTreatmentLabel(c.VatTreatment);
            if (isGroup) ws.Range(row, 1, row, 10).Style.Font.SetBold();
            row++;
        }

        Finish(ws, 4, row - 1, 10, [12, 42, 18, 46, 20, 38, 10, 30, 8, 20]);
    }

    /// <summary>Saldo de cada cuenta a una fecha.</summary>
    public static void AddBalances(XLWorkbook wb, FinanceBalancesDto balances)
    {
        var ws = Sheet(wb, "Saldos", $"Saldos por cuenta — {balances.BuildingName}",
            $"Al {balances.AsOf:dd/MM/yyyy}. Saldo = saldo inicial + entradas − salidas desde el {balances.FinanceStartDate:dd/MM/yyyy}.");
        Header(ws, 4, "Cuenta", "Tipo", "Estado", "Saldo inicial", "Entradas", "Salidas", "Saldo");

        var row = 5;
        foreach (var a in balances.Accounts)
        {
            ws.Cell(row, 1).Value = a.Name;
            ws.Cell(row, 2).Value = AccountTypeLabel(a.Type);
            ws.Cell(row, 3).Value = a.IsActive ? "Activa" : "Inactiva";
            Amount(ws, row, 4, a.OpeningBalance);
            Amount(ws, row, 5, a.Inflows);
            Amount(ws, row, 6, a.Outflows);
            Amount(ws, row, 7, a.Balance);
            row++;
        }

        if (balances.UnassignedNet != 0m)
        {
            ws.Cell(row, 1).Value = "Sin cuenta asignada";
            ws.Cell(row, 2).Value = "—";
            Amount(ws, row, 7, balances.UnassignedNet);
            row++;
        }

        ws.Cell(row, 1).Value = "Total";
        Amount(ws, row, 4, balances.Accounts.Sum(a => a.OpeningBalance));
        Amount(ws, row, 5, balances.Accounts.Sum(a => a.Inflows));
        Amount(ws, row, 6, balances.Accounts.Sum(a => a.Outflows));
        Amount(ws, row, 7, balances.TotalBalance);
        ws.Range(row, 1, row, 7).Style.Font.SetBold();
        ws.Range(row, 1, row, 7).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        Finish(ws, 4, row, 7, [32, 16, 11, 16, 16, 16, 16], filter: false);
    }

    /// <summary>Un renglon por movimiento del libro, con el codigo del rubro y el del contador.</summary>
    public static void AddMovements(XLWorkbook wb, LedgerContext ctx, FinanceMovementsPageDto page, string? scopeText = null)
    {
        var ws = Sheet(wb, "Movimientos", $"Movimientos — {ctx.BuildingName}",
            $"Del {page.From:dd/MM/yyyy} al {page.To:dd/MM/yyyy}.{(string.IsNullOrWhiteSpace(scopeText) ? string.Empty : " " + scopeText)} Entrada = ingreso de dinero; Salida = egreso.");
        var hasBalance = page.Items.Any(i => i.RunningBalance.HasValue);
        string[] headers = hasBalance
            ? ["Fecha", "Cuenta", "Código rubro", "Rubro", "Grupo", "Código del contador", "Entrada", "Salida", "Saldo", "Descripción", "Unidad / tercero", "Referencia", "Origen"]
            : ["Fecha", "Cuenta", "Código rubro", "Rubro", "Grupo", "Código del contador", "Entrada", "Salida", "Descripción", "Unidad / tercero", "Referencia", "Origen"];
        Header(ws, 4, headers);

        var row = 5;
        foreach (var m in page.Items)
        {
            ctx.ById.TryGetValue(m.CategoryId ?? Guid.Empty, out var category);
            var group = category?.ParentId is Guid pid && ctx.ById.TryGetValue(pid, out var g) ? $"{g.Code} · {g.Name}" : string.Empty;

            var c = 1;
            ws.Cell(row, c++).Value = m.Date.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, c - 1).Style.DateFormat.Format = DateFormat;
            ws.Cell(row, c++).Value = m.AccountName;
            ws.Cell(row, c++).Value = m.CategoryCode;
            ws.Cell(row, c++).Value = m.CategoryName;
            ws.Cell(row, c++).Value = group;
            ws.Cell(row, c++).Value = category?.ExternalCode ?? string.Empty;
            if (m.Direction == LedgerDirection.In) Amount(ws, row, c, m.Amount);
            c++;
            if (m.Direction == LedgerDirection.Out) Amount(ws, row, c, m.Amount);
            c++;
            if (hasBalance)
            {
                if (m.RunningBalance.HasValue) Amount(ws, row, c, m.RunningBalance.Value);
                c++;
            }

            ws.Cell(row, c++).Value = m.Description;
            ws.Cell(row, c++).Value = m.ThirdParty;
            ws.Cell(row, c++).Value = m.Reference;
            ws.Cell(row, c).Value = SourceLabel(m.SourceType);
            row++;
        }

        var totalRow = row;
        ws.Cell(totalRow, 1).Value = "Total";
        Amount(ws, totalRow, 7, page.Items.Where(i => i.Direction == LedgerDirection.In).Sum(i => i.Amount));
        Amount(ws, totalRow, 8, page.Items.Where(i => i.Direction == LedgerDirection.Out).Sum(i => i.Amount));
        ws.Range(totalRow, 1, totalRow, headers.Length).Style.Font.SetBold();
        ws.Range(totalRow, 1, totalRow, headers.Length).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        int[] widths = hasBalance
            ? [12, 22, 12, 32, 30, 18, 15, 15, 16, 48, 22, 22, 22]
            : [12, 22, 12, 32, 30, 18, 15, 15, 48, 22, 22, 22];
        Finish(ws, 4, Math.Max(row - 1, 4), headers.Length, widths);
    }

    /// <summary>Libro de compras: un renglon por factura de proveedor (y por nota de credito, en negativo) con el IVA incluido, y el resumen por tasa.</summary>
    public static void AddVatPurchases(XLWorkbook wb, VatPurchasesBookDto book)
    {
        var ws = Sheet(wb, "Libro de compras", $"Libro de compras — {book.BuildingName}",
            $"Del {book.From:dd/MM/yyyy} al {book.To:dd/MM/yyyy}. Por fecha de la factura del proveedor. El IVA está incluido en el total de cada comprobante; las notas de crédito van en negativo.");
        string[] headers = ["Fecha", "Proveedor", "RUC", "Timbrado", "N° de comprobante", "Tipo", "Descripción", "Código de cuenta", "Cuenta", "Total", "Tasa", "Base gravada / exento", "IVA"];
        Header(ws, 4, headers);

        var row = 5;
        foreach (var r in book.Rows)
        {
            ws.Cell(row, 1).Value = r.Date.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 1).Style.DateFormat.Format = DateFormat;
            ws.Cell(row, 2).Value = r.SupplierName;
            ws.Cell(row, 3).Value = r.SupplierRuc ?? string.Empty;
            ws.Cell(row, 4).Value = r.Timbrado ?? string.Empty;
            ws.Cell(row, 5).Value = r.DocumentNumber ?? string.Empty;
            ws.Cell(row, 6).Value = r.IsCreditNote ? "Nota de crédito" : "Factura";
            ws.Cell(row, 7).Value = r.Description;
            ws.Cell(row, 8).Value = r.LedgerCategoryCode ?? string.Empty;
            ws.Cell(row, 9).Value = r.LedgerCategoryName ?? string.Empty;
            Amount(ws, row, 10, r.Total);
            ws.Cell(row, 11).Value = r.VatRate == 0m ? "Exento" : $"{r.VatRate:0}%";
            Amount(ws, row, 12, r.Base);
            Amount(ws, row, 13, r.Vat);
            row++;
        }

        ws.Cell(row, 1).Value = "Total";
        Amount(ws, row, 10, book.GrandTotal);
        Amount(ws, row, 12, book.GrandBase);
        Amount(ws, row, 13, book.GrandVat);
        ws.Range(row, 1, row, headers.Length).Style.Font.SetBold();
        ws.Range(row, 1, row, headers.Length).Style.Border.TopBorder = XLBorderStyleValues.Thin;
        Finish(ws, 4, Math.Max(row - 1, 4), headers.Length, [12, 28, 14, 12, 20, 15, 40, 14, 32, 16, 9, 18, 14]);

        // Resumen por tasa, debajo del libro.
        var summaryRow = row + 3;
        ws.Cell(summaryRow - 1, 1).Value = "Resumen por tasa";
        ws.Cell(summaryRow - 1, 1).Style.Font.SetBold();
        Header(ws, summaryRow, "Tasa", "Comprobantes", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, "Total", string.Empty, "Base gravada / exento", "IVA");
        var line = summaryRow + 1;
        foreach (var t in book.TotalsByRate)
        {
            ws.Cell(line, 1).Value = t.Rate == 0m ? "Exento" : $"{t.Rate:0}%";
            ws.Cell(line, 2).Value = t.Count;
            Amount(ws, line, 10, t.Total);
            Amount(ws, line, 12, t.Base);
            Amount(ws, line, 13, t.Vat);
            line++;
        }

        if (book.UnclassifiedCount > 0)
        {
            ws.Cell(line + 1, 1).Value =
                $"Atención: {book.UnclassifiedCount} gastos por {book.UnclassifiedTotal:N0} no tienen tasa de IVA y no figuran en este libro. Clasificalos desde el gasto o el tratamiento de IVA de su cuenta.";
            ws.Cell(line + 1, 1).Style.Font.SetFontColor(XLColor.Red);
        }
    }

    /// <summary>Flujo de caja del ejercicio: un renglon por rubro con un importe por mes.</summary>
    public static void AddCashFlow(XLWorkbook wb, LedgerContext ctx, FinanceCashFlowDto flow)
    {
        var ws = Sheet(wb, "Flujo de caja", $"Flujo de caja — {ctx.BuildingName}",
            $"Ejercicio {flow.FiscalYearStart:dd/MM/yyyy} al {flow.FiscalYearEnd:dd/MM/yyyy}, lo percibido hasta el {flow.AsOf:dd/MM/yyyy}.");
        var months = flow.Months.Select(m => MonthLabel(m.Year, m.Month)).ToList();
        var headers = new List<string> { "Tipo", "Grupo", "Código rubro", "Rubro", "Código del contador" };
        headers.AddRange(months);
        headers.Add("Total");
        Header(ws, 4, headers.ToArray());

        var first = 6; // primera columna de importes
        var row = 5;

        void Lines(IEnumerable<FinanceCashFlowLineDto> lines, string type)
        {
            foreach (var l in lines)
            {
                ctx.ById.TryGetValue(l.CategoryId ?? Guid.Empty, out var category);
                ws.Cell(row, 1).Value = type;
                ws.Cell(row, 2).Value = string.IsNullOrEmpty(l.GroupCode) ? string.Empty : $"{l.GroupCode} · {l.GroupName}";
                ws.Cell(row, 3).Value = l.Code;
                ws.Cell(row, 4).Value = l.Name;
                ws.Cell(row, 5).Value = category?.ExternalCode ?? string.Empty;
                for (var i = 0; i < l.Amounts.Count; i++) Amount(ws, row, first + i, l.Amounts[i]);
                Amount(ws, row, first + months.Count, l.Total);
                row++;
            }
        }

        Lines(flow.InLines, "Ingresos");
        Lines(flow.OutLines, "Egresos");

        void Totals(string label, IReadOnlyList<decimal> values)
        {
            ws.Cell(row, 1).Value = label;
            for (var i = 0; i < values.Count; i++) Amount(ws, row, first + i, values[i]);
            Amount(ws, row, first + months.Count, values.Sum());
            ws.Range(row, 1, row, headers.Count).Style.Font.SetBold();
            row++;
        }

        var dataEnd = row - 1;
        Totals("Total ingresos", flow.TotalIn);
        Totals("Total egresos", flow.TotalOut);
        Totals("Neto del mes", flow.Net);

        ws.Cell(row, 1).Value = "Saldo al inicio del ejercicio";
        Amount(ws, row, first, flow.OpeningBalance);
        ws.Range(row, 1, row, headers.Count).Style.Font.SetBold();
        row++;
        ws.Cell(row, 1).Value = "Saldo al cierre de cada mes";
        for (var i = 0; i < flow.ClosingBalance.Count; i++) Amount(ws, row, first + i, flow.ClosingBalance[i]);
        ws.Range(row, 1, row, headers.Count).Style.Font.SetBold();

        var widths = new List<int> { 12, 32, 12, 34, 18 };
        widths.AddRange(Enumerable.Repeat(13, months.Count + 1));
        Finish(ws, 4, dataEnd, headers.Count, widths.ToArray());
    }

    /// <summary>Presupuesto del ejercicio: un renglon por rubro con un importe por mes.</summary>
    public static void AddBudget(XLWorkbook wb, LedgerContext ctx, FinanceBudgetDto budget)
    {
        var ws = Sheet(wb, "Presupuesto", $"Presupuesto — {ctx.BuildingName}",
            $"Ejercicio {budget.FiscalYearStart:dd/MM/yyyy} al {budget.FiscalYearEnd:dd/MM/yyyy}. Importes mensuales en guaraníes.");
        var months = budget.Months.Select(m => MonthLabel(m.Year, m.Month)).ToList();
        var headers = new List<string> { "Tipo", "Grupo", "Código rubro", "Rubro", "Código del contador" };
        headers.AddRange(months);
        headers.Add("Total");
        Header(ws, 4, headers.ToArray());

        var first = 6;
        var row = 5;
        foreach (var r in budget.Rows.OrderBy(x => x.Type == LedgerCategoryType.Income ? 0 : 1).ThenBy(x => x.Code, StringComparer.Ordinal))
        {
            ctx.ById.TryGetValue(r.CategoryId, out var category);
            ws.Cell(row, 1).Value = r.Type == LedgerCategoryType.Income ? "Ingresos" : "Gastos";
            ws.Cell(row, 2).Value = string.IsNullOrEmpty(r.GroupCode) ? string.Empty : $"{r.GroupCode} · {r.GroupName}";
            ws.Cell(row, 3).Value = r.Code;
            ws.Cell(row, 4).Value = r.Name;
            ws.Cell(row, 5).Value = category?.ExternalCode ?? string.Empty;
            for (var i = 0; i < r.Amounts.Count; i++) Amount(ws, row, first + i, r.Amounts[i]);
            Amount(ws, row, first + months.Count, r.Total);
            row++;
        }

        var dataEnd = Math.Max(row - 1, 4);
        void Totals(string label, IReadOnlyList<decimal> values)
        {
            ws.Cell(row, 1).Value = label;
            for (var i = 0; i < values.Count; i++) Amount(ws, row, first + i, values[i]);
            Amount(ws, row, first + months.Count, values.Sum());
            ws.Range(row, 1, row, headers.Count).Style.Font.SetBold();
            row++;
        }

        Totals("Total ingresos", budget.TotalIncome);
        Totals("Total gastos", budget.TotalExpense);

        var widths = new List<int> { 12, 32, 12, 34, 18 };
        widths.AddRange(Enumerable.Repeat(13, months.Count + 1));
        Finish(ws, 4, dataEnd, headers.Count, widths.ToArray());
    }

    /// <summary>Presupuesto contra lo real: el mes elegido y el acumulado del ejercicio, con el semaforo en texto.</summary>
    public static void AddBudgetVsActual(XLWorkbook wb, LedgerContext ctx, FinanceBudgetVsActualDto vs)
    {
        var monthText = MonthLabel(vs.Year, vs.Month);
        var ws = Sheet(wb, "Presupuesto vs real", $"Presupuesto vs. real — {ctx.BuildingName}",
            $"Mes: {monthText}. Acumulado: ejercicio desde el {vs.FiscalYearStart:dd/MM/yyyy} hasta el {vs.AsOf:dd/MM/yyyy}. Gastos: {vs.ExpenseBasis.ToLowerInvariant()}; ingresos: {vs.IncomeBasis.ToLowerInvariant()}. Desvío = real − presupuestado; verde dentro de lo presupuestado, amarillo hasta {vs.AmberThresholdPct:0.#}% de desvío, rojo más allá.");
        Header(ws, 4, "Tipo", "Grupo", "Código rubro", "Rubro", "Código del contador",
            $"Presupuesto {monthText}", $"Real {monthText}", "Desvío mes", "Desvío mes %", "Estado mes",
            "Presupuesto acumulado", "Real acumulado", "Desvío acumulado", "Desvío acumulado %", "Estado acumulado");

        var row = 5;
        void Lines(IEnumerable<FinanceBudgetVsActualLineDto> lines, string type)
        {
            foreach (var l in lines)
            {
                ctx.ById.TryGetValue(l.CategoryId, out var category);
                ws.Cell(row, 1).Value = type;
                ws.Cell(row, 2).Value = string.IsNullOrEmpty(l.GroupCode) ? string.Empty : $"{l.GroupCode} · {l.GroupName}";
                ws.Cell(row, 3).Value = l.Code;
                ws.Cell(row, 4).Value = l.Name;
                ws.Cell(row, 5).Value = category?.ExternalCode ?? string.Empty;
                Amount(ws, row, 6, l.MonthBudget);
                Amount(ws, row, 7, l.MonthActual);
                Amount(ws, row, 8, l.MonthVariance);
                Percent(ws, row, 9, l.MonthVariancePct);
                ws.Cell(row, 10).Value = StatusLabel(l.MonthStatus);
                Amount(ws, row, 11, l.YtdBudget);
                Amount(ws, row, 12, l.YtdActual);
                Amount(ws, row, 13, l.YtdVariance);
                Percent(ws, row, 14, l.YtdVariancePct);
                ws.Cell(row, 15).Value = StatusLabel(l.YtdStatus);
                row++;
            }
        }

        Lines(vs.IncomeLines, "Ingresos");
        Lines(vs.ExpenseLines, "Gastos");
        var dataEnd = Math.Max(row - 1, 4);

        void Totals(string label, FinanceBudgetTotalsDto t)
        {
            ws.Cell(row, 1).Value = label;
            Amount(ws, row, 6, t.MonthBudget);
            Amount(ws, row, 7, t.MonthActual);
            Amount(ws, row, 8, t.MonthActual - t.MonthBudget);
            ws.Cell(row, 10).Value = StatusLabel(t.MonthStatus);
            Amount(ws, row, 11, t.YtdBudget);
            Amount(ws, row, 12, t.YtdActual);
            Amount(ws, row, 13, t.YtdActual - t.YtdBudget);
            ws.Cell(row, 15).Value = StatusLabel(t.YtdStatus);
            ws.Range(row, 1, row, 15).Style.Font.SetBold();
            row++;
        }

        Totals("Total ingresos", vs.IncomeTotals);
        Totals("Total gastos", vs.ExpenseTotals);

        Finish(ws, 4, dataEnd, 15, [10, 30, 12, 32, 18, 17, 16, 15, 13, 12, 18, 17, 17, 16, 14]);
    }

    /// <summary>Libro del fondo de reserva: resumen, mes a mes y movimientos del rango.</summary>
    public static void AddReserveFund(XLWorkbook wb, LedgerContext ctx, FinanceReserveFundDto fund)
    {
        var ws = Sheet(wb, "Fondo de reserva", $"Fondo de reserva — {ctx.BuildingName}",
            fund.HasFundAccount
                ? $"Cuenta «{fund.AccountName}». Desde el {fund.FinanceStartDate:dd/MM/yyyy} hasta el {fund.AsOf:dd/MM/yyyy}. Aportes = cobrado a las unidades para el fondo; usos = gastos pagados por el fondo."
                : "El edificio no tiene una cuenta de tipo «Fondo de reserva»: el libro no puede separar sus movimientos.");
        if (!fund.HasFundAccount)
        {
            ws.Column(1).Width = 40;
            return;
        }

        Header(ws, 4, "Concepto", "Importe");
        var row = 5;
        (string Label, decimal Value)[] summary =
        [
            ("Saldo inicial de la cuenta", fund.OpeningBalance),
            ("Aportes del período", fund.Contributions),
            ("Usos del período", fund.Uses),
            ("Saldo", fund.Balance)
        ];
        foreach (var (label, value) in summary)
        {
            ws.Cell(row, 1).Value = label;
            Amount(ws, row, 2, value);
            row++;
        }

        if (fund.ReserveFundPercentage.HasValue)
        {
            ws.Cell(row, 1).Value = "Porcentaje de aporte del edificio";
            ws.Cell(row, 2).Value = fund.ReserveFundPercentage.Value;
            ws.Cell(row, 2).Style.NumberFormat.Format = "0.##\"%\"";
            row++;
        }

        row += 1;
        Header(ws, row, "Mes", "Apertura", "Aportes", "Usos", "Cierre");
        row++;
        foreach (var m in fund.Months)
        {
            ws.Cell(row, 1).Value = MonthLabel(m.Year, m.Month);
            Amount(ws, row, 2, m.Opening);
            Amount(ws, row, 3, m.Contributions);
            Amount(ws, row, 4, m.Uses);
            Amount(ws, row, 5, m.Closing);
            row++;
        }

        row += 1;
        ws.Cell(row, 1).Value = $"Movimientos del {fund.Movements.From:dd/MM/yyyy} al {fund.Movements.To:dd/MM/yyyy}";
        ws.Cell(row, 1).Style.Font.SetBold();
        row++;
        Header(ws, row, "Fecha", "Entrada", "Salida", "Saldo", "Rubro", "Código rubro", "Código del contador", "Descripción", "Unidad / tercero", "Referencia");
        row++;
        foreach (var m in fund.Movements.Items.OrderBy(i => i.Date))
        {
            ctx.ById.TryGetValue(m.CategoryId ?? Guid.Empty, out var category);
            ws.Cell(row, 1).Value = m.Date.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 1).Style.DateFormat.Format = DateFormat;
            if (m.Direction == LedgerDirection.In) Amount(ws, row, 2, m.Amount);
            if (m.Direction == LedgerDirection.Out) Amount(ws, row, 3, m.Amount);
            ws.Cell(row, 5).Value = m.CategoryName;
            ws.Cell(row, 6).Value = m.CategoryCode;
            ws.Cell(row, 7).Value = category?.ExternalCode ?? string.Empty;
            ws.Cell(row, 8).Value = m.Description;
            ws.Cell(row, 9).Value = m.ThirdParty;
            ws.Cell(row, 10).Value = m.Reference;
            row++;
        }

        int[] widths = [36, 16, 16, 16, 30, 12, 18, 48, 22, 22];
        for (var i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];
    }

    // ── Utilidades ────────────────────────────────────────────────────────────

    // Hoja con titulo y subtitulo; la tabla empieza en la fila 4.
    private static IXLWorksheet Sheet(XLWorkbook wb, string name, string title, string subtitle)
    {
        var ws = wb.Worksheets.Add(name);
        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);
        ws.Cell(2, 1).Value = subtitle;
        ws.Cell(2, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
        return ws;
    }

    private static void Header(IXLWorksheet ws, int row, params string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(row, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#e8f4f8"));
            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }
    }

    private static void Finish(IXLWorksheet ws, int headerRow, int lastRow, int columns, int[] widths, bool filter = true)
    {
        for (var i = 0; i < Math.Min(columns, widths.Length); i++) ws.Column(i + 1).Width = widths[i];
        ws.SheetView.FreezeRows(headerRow);
        if (filter && lastRow > headerRow)
        {
            ws.Range(headerRow, 1, lastRow, columns).SetAutoFilter();
        }
    }

    private static void Amount(IXLWorksheet ws, int row, int column, decimal value)
    {
        var cell = ws.Cell(row, column);
        cell.Value = value;
        cell.Style.NumberFormat.Format = AmountFormat;
    }

    private static void Percent(IXLWorksheet ws, int row, int column, decimal? value)
    {
        if (!value.HasValue) return;
        var cell = ws.Cell(row, column);
        cell.Value = value.Value;
        cell.Style.NumberFormat.Format = PercentFormat;
    }

    private static string VatTreatmentLabel(VatTreatment? treatment) => treatment switch
    {
        VatTreatment.Vat10 => "IVA 10 %",
        VatTreatment.Vat5 => "IVA 5 %",
        VatTreatment.Exempt => "Exento",
        VatTreatment.NotApplicable => "No corresponde",
        _ => string.Empty
    };

    private static string TypeLabel(LedgerCategoryType type) => type switch
    {
        LedgerCategoryType.Asset => "Activo",
        LedgerCategoryType.Liability => "Pasivo",
        LedgerCategoryType.Fund => "Patrimonio / Fondos",
        LedgerCategoryType.Income => "Ingresos",
        _ => "Gastos"
    };

    private static string AccountTypeLabel(FinancialAccountType type) => type switch
    {
        FinancialAccountType.Cash => "Caja",
        FinancialAccountType.Bank => "Banco",
        _ => "Fondo de reserva"
    };

    private static string SourceLabel(LedgerSourceType source) => source switch
    {
        LedgerSourceType.OwnerPayment => "Cobro de propietario",
        LedgerSourceType.BuildingExpense => "Gasto del edificio",
        LedgerSourceType.SupplierCreditNote => "Nota de crédito de proveedor",
        _ => "Ingreso del edificio"
    };

    private static string StatusLabel(BudgetStatus status) => status switch
    {
        BudgetStatus.Green => "Verde",
        BudgetStatus.Amber => "Amarillo",
        BudgetStatus.Red => "Rojo",
        _ => string.Empty
    };

    private static string SettlementCategoryText(LedgerCategory c)
    {
        if (!c.ParentId.HasValue) return string.Empty;
        var expense = FinanceChartTemplate.ExpenseCategoryOf(c);
        if (expense.HasValue) return CategoryLabels.ExpenseLabel(expense.Value);
        var income = FinanceChartTemplate.IncomeCategoryOf(c);
        return income.HasValue ? CategoryLabels.IncomeLabel(income.Value) : string.Empty;
    }
}
