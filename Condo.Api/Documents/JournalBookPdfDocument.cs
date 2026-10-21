using System.Globalization;
using Condo.Api.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

/// <summary>
/// Libro Diario del ejercicio para imprimir en hojas sueltas con folio correlativo (Ley 1034/83, arts. 78-80 y Ley 4924/2013). Cada hoja lleva
/// el encabezado del titular (razon social, RUC, domicilio), el ejercicio, el libro y la rubrica, su folio, los asientos en orden de numero
/// y al pie la suma de la hoja, la anterior y la acumulada. Sin renglones en blanco. La rubrica del Registro Publico de Comercio es un
/// tramite externo: el sistema solo imprime las hojas.
/// </summary>
public sealed class JournalBookPdfDocument(JournalBookPrint print) : IDocument
{
    public const int RowsPerPage = JournalBookPaginator.DefaultRowsPerPage;
    private const float RowHeight = 13f;
    private const string Ink = "#111111";
    private const string Muted = "#555555";
    private const string Rule = "#999999";

    private static readonly CultureInfo Es = new("es-PY");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Libro Diario {print.FiscalYear} - {print.BuildingName}",
        Author = "EXPENSES"
    };

    public void Compose(IDocumentContainer container)
    {
        if (print.Pages.Count == 0)
        {
            container.Page(page =>
            {
                Setup(page);
                page.Header().Element(c => ComposeHeader(c, print.StartFolio));
                page.Content().PaddingTop(20).Text("No hay asientos asentados en este ejercicio.").FontColor(Muted);
            });
            return;
        }

        foreach (var sheet in print.Pages)
        {
            var current = sheet;
            container.Page(page =>
            {
                Setup(page);
                page.Header().Element(c => ComposeHeader(c, current.Folio));
                page.Content().Element(c => ComposeRows(c, current));
                page.Footer().Element(c => ComposeFooter(c, current));
            });
        }
    }

    private static void Setup(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(28, Unit.Point);
        page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(8).FontColor(Ink));
    }

    private void ComposeHeader(IContainer container, int folio)
    {
        var b = print.Books;
        container.PaddingBottom(6).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("LIBRO DIARIO").FontSize(15).Bold();
                    c.Item().Text(b.Effective.LegalName ?? print.BuildingName).FontSize(10).SemiBold();
                    c.Item().Text($"RUC: {Or(b.Effective.Ruc)}   ·   Domicilio: {Or(b.Effective.Address)}").FontColor(Muted);
                });
                row.ConstantItem(90).AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text("FOLIO").FontSize(7).FontColor(Muted);
                    c.Item().AlignRight().Text(folio.ToString(Es)).FontSize(20).Bold();
                });
            });

            col.Item().PaddingTop(3).Text(
                $"Ejercicio {print.FiscalYear} ({print.From:dd/MM/yyyy} al {print.To:dd/MM/yyyy})   ·   Edificio: {print.BuildingName}" +
                $"   ·   Libro N° {Or(b.BookNumber)}" + (b.RubricDate.HasValue ? $"   ·   Rúbrica: {b.RubricDate:dd/MM/yyyy}" : string.Empty))
                .FontColor(Muted);
            if (!string.IsNullOrWhiteSpace(b.AccountantName))
            {
                col.Item().Text($"Contador: {b.AccountantName}" + (string.IsNullOrWhiteSpace(b.AccountantRegistration) ? string.Empty : $" (matrícula {b.AccountantRegistration})"))
                    .FontColor(Muted);
            }

            col.Item().PaddingTop(4).LineHorizontal(1f).LineColor(Ink);
            col.Item().Row(r =>
            {
                r.ConstantItem(72).Text("N° asiento").Bold();
                r.ConstantItem(50).Text("Fecha").Bold();
                r.RelativeItem().Text("Cuenta / glosa").Bold();
                r.ConstantItem(74).AlignRight().Text("Debe").Bold();
                r.ConstantItem(74).AlignRight().Text("Haber").Bold();
            });
            col.Item().LineHorizontal(0.5f).LineColor(Rule);
        });
    }

    private static void ComposeRows(IContainer container, BookPage sheet)
    {
        container.Column(col =>
        {
            foreach (var row in sheet.Rows)
            {
                if (row.Kind == BookRowKind.Entry)
                {
                    col.Item().Height(RowHeight * 2).PaddingTop(2).Row(r =>
                    {
                        r.ConstantItem(72).Text($"{row.Label}").Bold();
                        r.ConstantItem(50).Text(row.Date.ToString("dd/MM/yyyy"));
                        var glosa = row.Description + (string.IsNullOrWhiteSpace(row.ThirdParty) ? string.Empty : $" · {row.ThirdParty}")
                                    + (row.Continued ? " (continuación)" : string.Empty);
                        r.RelativeItem().Text(glosa).ClampLines(2).Italic();
                        r.ConstantItem(148);
                    });
                }
                else
                {
                    col.Item().Height(RowHeight).Row(r =>
                    {
                        r.ConstantItem(122);
                        var indent = row.Credit > 0m && row.Debit == 0m;
                        r.RelativeItem().PaddingLeft(indent ? 16 : 0).Text($"{row.Code} {row.Name}").ClampLines(1);
                        r.ConstantItem(74).AlignRight().Text(row.Debit != 0m ? Gs(row.Debit) : string.Empty);
                        r.ConstantItem(74).AlignRight().Text(row.Credit != 0m ? Gs(row.Credit) : string.Empty);
                    });
                }
            }
        });
    }

    private void ComposeFooter(IContainer container, BookPage sheet)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(Rule);
            Totals(col, "Suma de esta hoja", sheet.PageDebit, sheet.PageCredit, bold: false);
            if (sheet.PreviousDebit != 0m || sheet.PreviousCredit != 0m) Totals(col, "Suma anterior", sheet.PreviousDebit, sheet.PreviousCredit, bold: false);
            Totals(col, sheet.Folio == print.Pages[^1].Folio ? "Total del ejercicio" : "Suma y sigue", sheet.CumulativeDebit, sheet.CumulativeCredit, bold: true);
            col.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem().Text($"Libro Diario {print.FiscalYear} · {print.EntryCount} asientos · Generado por Expenses el {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(7).FontColor(Muted);
                r.ConstantItem(60).AlignRight().Text($"Folio {sheet.Folio}").FontSize(7).FontColor(Muted);
            });
        });
    }

    private static void Totals(ColumnDescriptor col, string label, decimal debit, decimal credit, bool bold)
    {
        col.Item().Row(r =>
        {
            r.ConstantItem(122);
            var text = r.RelativeItem().AlignRight().PaddingRight(6).Text(label);
            if (bold) text.Bold();
            var d = r.ConstantItem(74).AlignRight().Text(Gs(debit));
            var c = r.ConstantItem(74).AlignRight().Text(Gs(credit));
            if (bold) { d.Bold(); c.Bold(); }
        });
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string Gs(decimal value) => value.ToString("#,##0", Es);
}
