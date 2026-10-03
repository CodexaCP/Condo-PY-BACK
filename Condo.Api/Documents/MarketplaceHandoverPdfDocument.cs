using Condo.Application.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

/// <summary>
/// Nota interna de cambio de propietario principal: que paso, cual era la situacion y como esta hoy cada operacion abierta.
/// Es una constancia interna de gestion, no un documento fiscal.
/// </summary>
public sealed class MarketplaceHandoverPdfDocument(MarketplaceHandoverNoteDto note, DateTime generatedAtUtc) : IDocument
{
    private static readonly TimeZoneInfo Tz = TimeZoneInfo.FindSystemTimeZoneById("America/Asuncion");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Cambio de propietario principal — unidad {note.UnitCode}",
        Author = "CONDOPY"
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32, Unit.Point);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));
            page.Header().PaddingBottom(10).Column(col =>
            {
                col.Item().Text("CONDOPY").FontSize(16).Bold().FontColor(CondoPdfColors.Primary);
                col.Item().Text("Cambio de propietario principal — Marketplace").FontSize(11).FontColor(CondoPdfColors.Accent);
                col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(CondoPdfColors.Primary);
            });
            page.Content().Element(ComposeBody);
            page.Footer().Column(col =>
            {
                col.Item().Element(PdfWatermark.ComposeNonFiscal);
                col.Item().PaddingTop(4).Text($"Generado el {Local(generatedAtUtc)} · CONDOPY")
                    .FontSize(8).FontColor(CondoPdfColors.Gray);
            });
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(12);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                Info(table, "Edificio", note.BuildingName);
                Info(table, "Unidad", note.UnitCode);
                Info(table, "Propietario principal anterior", note.PreviousOwnerName);
                Info(table, "Nuevo propietario principal", note.NewOwnerName ?? "Todavía no hay otro");
                Info(table, "Nota creada", Local(note.CreatedAtUtc));
                Info(table, "Leída", note.ReadAtUtc.HasValue
                    ? $"{Local(note.ReadAtUtc.Value)}{(string.IsNullOrEmpty(note.ReadByName) ? string.Empty : $" · {note.ReadByName}")}"
                    : "Todavía no");
            });

            col.Item().Column(c =>
            {
                c.Item().PaddingBottom(5).BorderBottom(0.5f).BorderColor(CondoPdfColors.Border)
                    .Text("Qué pasó").Bold().FontSize(10).FontColor(CondoPdfColors.Primary);
                c.Item().PaddingTop(4).Text(note.Content).LineHeight(1.45f);
            });

            if (note.Operations.Count > 0)
            {
                col.Item().Column(c =>
                {
                    c.Item().PaddingBottom(5).BorderBottom(0.5f).BorderColor(CondoPdfColors.Border)
                        .Text($"Situación actual de las operaciones ({note.Operations.Count})").Bold().FontSize(10).FontColor(CondoPdfColors.Primary);
                    c.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn(1.2f);
                            cd.RelativeColumn(1.6f);
                            cd.RelativeColumn(1.4f);
                            cd.RelativeColumn(1.5f);
                            cd.RelativeColumn(1.2f);
                        });
                        table.Header(h =>
                        {
                            foreach (var title in new[] { "Reserva", "Espacio", "Horario", "Estado", "Ganancia" })
                            {
                                h.Cell().Background(CondoPdfColors.Primary).Padding(4).Text(title).FontColor(CondoPdfColors.White).Bold().FontSize(8);
                            }
                        });

                        var index = 0;
                        foreach (var op in note.Operations)
                        {
                            var bg = index++ % 2 == 0 ? CondoPdfColors.White : CondoPdfColors.RowAlt;
                            table.Cell().Background(bg).Padding(4).Text(op.Reference).FontFamily("Courier New").FontSize(8);
                            table.Cell().Background(bg).Padding(4).Text(op.Title).FontSize(8);
                            table.Cell().Background(bg).Padding(4).Text($"{Local(op.StartsAtUtc)} – {Local(op.EndsAtUtc, timeOnly: true)}").FontSize(8);
                            table.Cell().Background(bg).Padding(4).Text(StatusText(op)).FontSize(8);
                            table.Cell().Background(bg).Padding(4).AlignRight().Text($"Gs. {op.OwnerNetAmount:N0}").FontSize(8);
                        }
                    });
                });
            }
        });
    }

    private static string StatusText(MarketplaceHandoverOperationDto op)
    {
        var status = op.Status switch
        {
            "InReview" => "Pago en revisión",
            "Confirmed" => "Confirmada",
            "Completed" => "Finalizada",
            "Cancelled" => "Cancelada",
            _ => op.Status
        };
        var credit = op.CreditStatus switch
        {
            "Pending" => "acreditación pendiente",
            "Held" => "acreditación retenida",
            "Credited" => "acreditada",
            "Reversed" => "acreditación revertida",
            _ => null
        };
        var extra = new List<string>();
        if (credit is not null) extra.Add(credit);
        if (op.HasOpenClaim) extra.Add("reclamo abierto");
        if (op.RefundStatus == "Pending") extra.Add("reembolso pendiente");
        if (op.RefundStatus == "Returned") extra.Add("reembolso devuelto");
        return extra.Count == 0 ? status : $"{status} ({string.Join(", ", extra)})";
    }

    private static void Info(TableDescriptor table, string label, string value)
    {
        table.Cell().PaddingVertical(3).Column(c =>
        {
            c.Item().Text(label).FontSize(7.5f).FontColor(CondoPdfColors.Gray);
            c.Item().Text(value).Bold();
        });
    }

    private static string Local(DateTime utc, bool timeOnly = false)
    {
        var value = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(value, Tz).ToString(timeOnly ? "HH:mm" : "dd/MM/yyyy HH:mm");
    }
}
