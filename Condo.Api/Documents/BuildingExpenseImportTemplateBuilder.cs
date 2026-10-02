using ClosedXML.Excel;
using Condo.Api.Services;
using Condo.Domain.Enums;

namespace Condo.Api.Documents;

// Plantilla de la carga masiva de gastos, generada para un edificio: la hoja "Gastos" con el edificio, la empresa y los
// encabezados, la hoja "Categorias" con los nombres validos y una hoja oculta con la marca cifrada que ata el archivo a
// ese edificio y empresa. Si el edificio tiene Finanzas habilitado suma la columna "Rubro" y la hoja "Rubros" con los rubros
// activos de gastos de ese edificio (la columna ofrece una lista desplegable con ellos).
public static class BuildingExpenseImportTemplateBuilder
{
    // Hoja oculta (no se puede mostrar desde Excel) con la marca cifrada; la lee BuildingExpenseImportParser.
    public const string MetaSheetName = "_condopy";
    public const string RubrosSheetName = "Rubros";

    // Un rubro de gastos que se puede elegir: "Label" es lo que muestra la lista desplegable ("5.2.03 Aguinaldo").
    public sealed record RubroOption(string Code, string Name, string Group, string Category)
    {
        public string Label => $"{Code} {Name}";
    }

    private const int MaxDataRows = 500;

    public static byte[] Build(string buildingName, string companyName, string token, IReadOnlyList<RubroOption>? rubros = null)
    {
        var withRubros = rubros is { Count: > 0 };

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Gastos");

        sheet.Cell(1, 1).Value = $"Edificio: {buildingName}";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(12);
        sheet.Cell(2, 1).Value = $"Empresa: {companyName}";
        sheet.Cell(2, 1).Style.Font.SetBold();
        var instructions = "Completá una fila por gasto desde la fila 5. Esta plantilla solo sirve para este edificio: no cambies los encabezados ni las hojas. Cada gasto lleva su monto en \"Monto\" o, si lo paga el fondo de reserva, en \"Monto por fondo de reserva\" (uno solo).";
        if (withRubros)
        {
            instructions += " Elegí el \"Rubro\" de la lista (hoja \"Rubros\"): la categoría se toma del rubro y podés dejar \"Categoria\" vacía. Si no completás el rubro, el gasto usa su categoría como siempre.";
        }

        sheet.Cell(3, 1).Value = instructions;
        sheet.Cell(3, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);

        string[] headers = withRubros
            ? ["Categoria", "Rubro", "Proveedor", "Descripcion", "Monto", "Monto por fondo de reserva"]
            : ["Categoria", "Proveedor", "Descripcion", "Monto", "Monto por fondo de reserva"];
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(4, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#e8f4f8"));
        }

        var amountColumn = withRubros ? 5 : 4;
        sheet.Columns(amountColumn, amountColumn + 1).Style.NumberFormat.Format = "#,##0";
        sheet.Column(1).Width = 24;
        if (withRubros)
        {
            sheet.Column(2).Width = 34;
            sheet.Column(3).Width = 32;
            sheet.Column(4).Width = 56;
            sheet.Column(5).Width = 16;
            sheet.Column(6).Width = 28;
        }
        else
        {
            sheet.Column(2).Width = 32;
            sheet.Column(3).Width = 56;
            sheet.Column(4).Width = 16;
            sheet.Column(5).Width = 28;
        }

        var categories = workbook.Worksheets.Add("Categorias");
        categories.Cell(1, 1).Value = "Categorias validas";
        categories.Cell(1, 1).Style.Font.SetBold();
        var row = 2;
        foreach (var category in Enum.GetValues<BuildingExpenseCategory>())
        {
            categories.Cell(row++, 1).Value = CategoryLabels.ExpenseLabel(category);
        }

        categories.Column(1).AdjustToContents();

        if (withRubros)
        {
            var rubroSheet = workbook.Worksheets.Add(RubrosSheetName);
            string[] rubroHeaders = ["Rubro", "Grupo", "Cuenta en la liquidacion como"];
            for (var i = 0; i < rubroHeaders.Length; i++)
            {
                rubroSheet.Cell(1, i + 1).Value = rubroHeaders[i];
                rubroSheet.Cell(1, i + 1).Style.Font.SetBold();
            }

            var rubroRow = 2;
            foreach (var rubro in rubros!)
            {
                rubroSheet.Cell(rubroRow, 1).Value = rubro.Label;
                rubroSheet.Cell(rubroRow, 2).Value = rubro.Group;
                rubroSheet.Cell(rubroRow, 3).Value = rubro.Category;
                rubroRow++;
            }

            rubroSheet.Columns(1, 3).AdjustToContents();

            // Lista desplegable en la columna "Rubro" (no obliga: el parser avisa de los valores desconocidos fila por fila).
            var validation = sheet.Range(5, 2, 4 + MaxDataRows, 2).CreateDataValidation();
            validation.List(rubroSheet.Range(2, 1, rubroRow - 1, 1), true);
            validation.ErrorStyle = XLErrorStyle.Warning;
            validation.IgnoreBlanks = true;
        }

        var meta = workbook.Worksheets.Add(MetaSheetName);
        meta.Cell(1, 1).Value = token;
        meta.Visibility = XLWorksheetVisibility.VeryHidden;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
