using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

/// <summary>Para quien se arma el comprobante: define que datos se muestran (cada parte ve solo lo que le corresponde).</summary>
public enum MarketplaceReceiptAudience
{
    // Personal del edificio: ve todo.
    Staff,
    // Comprador: precio, comision por gestion y total; no ve el nombre del propietario ni su ganancia.
    Buyer,
    // Propietario: quien reservo (nombre y unidad), precio por hora y lo que recibe; no ve la comision ni el total.
    Owner
}

public sealed record MarketplaceReceiptData(
    MarketplaceReceiptAudience Audience,
    string Reference,
    string Status,
    string BuildingName,
    string UnitCode,
    string ListingTitle,
    string OwnerName,
    string BuyerName,
    string BuyerUnits,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int Hours,
    decimal HourlyPrice,
    decimal BaseAmount,
    decimal CommissionPercent,
    decimal CommissionAmount,
    decimal TotalAmount,
    decimal OwnerNetAmount,
    DateTime CreatedAtUtc,
    DateTime? PaymentSubmittedAtUtc,
    DateTime? PaymentApprovedAtUtc,
    string? ApprovedByName,
    string? ComprobanteUrl,
    byte[]? ComprobanteImage,
    string CreditStatus,
    DateTime? CreditedAtUtc,
    string? CancelledBy,
    DateTime? CancelledAtUtc,
    string? CancelReason,
    decimal? RefundAmount,
    string? RefundStatus,
    DateTime? RefundReturnedAtUtc,
    DateTime GeneratedAtUtc);

/// <summary>
/// "Comprobante interno de reserva" del Marketplace. Se genera al pedirlo, desde los importes congelados en la reserva (nunca se
/// recalcula con la publicacion ni con la comision vigente). NO es un documento fiscal: lleva la franja de aviso en cada hoja.
/// </summary>
public sealed class MarketplaceReceiptPdfDocument(MarketplaceReceiptData data) : IDocument
{
    private static readonly TimeZoneInfo Tz = TimeZoneInfo.FindSystemTimeZoneById("America/Asuncion");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Comprobante interno de reserva {data.Reference}",
        Author = "CONDOPY"
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32, Unit.Point);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));
            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeBody);
            page.Footer().Column(col =>
            {
                col.Item().Element(PdfWatermark.ComposeNonFiscal);
                col.Item().PaddingTop(4).Text($"Generado el {Local(data.GeneratedAtUtc)} · CONDOPY")
                    .FontSize(8).FontColor(CondoPdfColors.Gray);
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(10).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("CONDOPY").FontSize(16).Bold().FontColor(CondoPdfColors.Primary);
                    c.Item().Text("Comprobante interno de reserva").FontSize(11).FontColor(CondoPdfColors.Accent);
                    c.Item().Text("Marketplace de espacios").FontSize(9).FontColor(CondoPdfColors.Gray);
                });
                row.ConstantItem(200).AlignRight().Column(c =>
                {
                    c.Item().Text(data.Reference).Bold().FontFamily("Courier New").FontSize(12).FontColor(CondoPdfColors.Primary);
                    c.Item().PaddingTop(3).Text(StatusLabel(data.Status)).Bold().FontColor(StatusColor(data.Status));
                });
            });
            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(CondoPdfColors.Primary);
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(14);
            col.Item().Element(ComposeParties);
            col.Item().Element(ComposeSchedule);
            col.Item().Element(ComposeAmounts);
            col.Item().Element(ComposePayment);
            if (data.Audience != MarketplaceReceiptAudience.Buyer)
            {
                col.Item().Element(ComposeCredit);
            }

            if (data.CancelledAtUtc.HasValue)
            {
                col.Item().Element(ComposeCancellation);
            }

            if (data.Audience == MarketplaceReceiptAudience.Staff && data.ComprobanteImage is { Length: > 0 })
            {
                col.Item().Element(ComposeTransferImage);
            }
        });
    }

    // ── Partes ───────────────────────────────────────────────────────────────

    private void ComposeParties(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Element(c => Section(c, "Datos de la operación"));
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                Info(table, "Edificio", data.BuildingName);
                Info(table, "Espacio publicado", data.ListingTitle);
                Info(table, "Unidad del espacio", data.UnitCode);
                switch (data.Audience)
                {
                    case MarketplaceReceiptAudience.Staff:
                        Info(table, "Propietario que publicó", data.OwnerName);
                        Info(table, "Comprador", BuyerLine());
                        break;
                    case MarketplaceReceiptAudience.Buyer:
                        Info(table, "Comprador", BuyerLine());
                        break;
                    default:
                        Info(table, "Reservado por", BuyerLine());
                        break;
                }

                Info(table, "Reserva creada", Local(data.CreatedAtUtc));
            });
        });
    }

    private string BuyerLine() => string.IsNullOrWhiteSpace(data.BuyerUnits) ? data.BuyerName : $"{data.BuyerName} · Unidad {data.BuyerUnits}";

    private void ComposeSchedule(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Element(c => Section(c, "Horario reservado"));
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                Info(table, "Desde", Local(data.StartsAtUtc));
                Info(table, "Hasta", Local(data.EndsAtUtc));
                Info(table, "Duración", data.Hours == 1 ? "1 hora" : $"{data.Hours} horas");
            });
        });
    }

    // ── Importes (congelados al crear la reserva) ────────────────────────────

    private void ComposeAmounts(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Element(c => Section(c, "Importes"));
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(1.4f); });
                Money(table, "Precio por hora", data.HourlyPrice);

                switch (data.Audience)
                {
                    case MarketplaceReceiptAudience.Owner:
                        Money(table, $"Precio del espacio ({data.Hours} h)", data.BaseAmount);
                        Money(table, "Lo que recibís (se acredita a tu saldo a favor)", data.OwnerNetAmount, strong: true);
                        break;
                    case MarketplaceReceiptAudience.Buyer:
                        Money(table, $"Precio del espacio ({data.Hours} h)", data.BaseAmount);
                        Money(table, $"Comisión por gestión ({data.CommissionPercent:0.##} %)", data.CommissionAmount);
                        Money(table, "Total pagado", data.TotalAmount, strong: true);
                        break;
                    default:
                        Money(table, $"Precio del espacio ({data.Hours} h) — neto del propietario", data.OwnerNetAmount);
                        Money(table, $"Comisión por gestión ({data.CommissionPercent:0.##} %)", data.CommissionAmount);
                        Money(table, "Total pagado por el comprador", data.TotalAmount, strong: true);
                        break;
                }
            });
            col.Item().PaddingTop(4).Text("Importes tomados de la reserva al momento de crearla; no se recalculan.")
                .FontSize(8).Italic().FontColor(CondoPdfColors.Gray);
        });
    }

    // ── Pago ─────────────────────────────────────────────────────────────────

    private void ComposePayment(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Element(c => Section(c, "Pago por transferencia"));
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                Info(table, "Comprobante enviado", data.PaymentSubmittedAtUtc.HasValue ? Local(data.PaymentSubmittedAtUtc.Value) : "—");
                Info(table, "Pago confirmado", data.PaymentApprovedAtUtc.HasValue ? Local(data.PaymentApprovedAtUtc.Value) : "—");
                if (data.Audience == MarketplaceReceiptAudience.Staff)
                {
                    Info(table, "Confirmado por", data.ApprovedByName ?? "—");
                    Info(table, "Archivo del comprobante", data.ComprobanteUrl ?? "—");
                }
            });
        });
    }

    // ── Acreditacion al saldo del propietario ────────────────────────────────

    private void ComposeCredit(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Element(c => Section(c, "Acreditación al propietario"));
            col.Item().Text(CreditText()).LineHeight(1.4f);
        });
    }

    private string CreditText() => data.CreditStatus switch
    {
        "Credited" => $"Acreditada al saldo a favor el {(data.CreditedAtUtc.HasValue ? Local(data.CreditedAtUtc.Value) : "—")}.",
        "Pending" => "Pendiente: se acredita 24 horas después de que termine la reserva, si no hay reclamos.",
        "Held" => "Retenida por un reclamo abierto.",
        "Reversed" => "La acreditación fue revertida.",
        _ => "No corresponde acreditar (la reserva no se concretó)."
    };

    // ── Cancelacion y reembolso ──────────────────────────────────────────────

    private void ComposeCancellation(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Element(c => Section(c, "Cancelación"));
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                Info(table, "Cancelada el", Local(data.CancelledAtUtc!.Value));
                Info(table, "Cancelada por", CancelledByLabel(data.CancelledBy));
                if (!string.IsNullOrWhiteSpace(data.CancelReason))
                {
                    Info(table, "Motivo", data.CancelReason!);
                }

                if (data.Audience != MarketplaceReceiptAudience.Owner && data.RefundAmount.HasValue)
                {
                    Info(table, "Reembolso al comprador", Gs(data.RefundAmount.Value));
                    Info(table, "Estado del reembolso", data.RefundStatus == "Returned"
                        ? $"Devuelto el {(data.RefundReturnedAtUtc.HasValue ? Local(data.RefundReturnedAtUtc.Value) : "—")}"
                        : "Pendiente (hasta 72 horas)");
                }
            });
        });
    }

    private void ComposeTransferImage(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Element(c => Section(c, "Comprobante de transferencia adjunto"));
            col.Item().MaxHeight(380).Image(data.ComprobanteImage!).FitArea();
        });
    }

    // ── Piezas ───────────────────────────────────────────────────────────────

    private static void Section(IContainer container, string title) =>
        container.PaddingBottom(5).BorderBottom(0.5f).BorderColor(CondoPdfColors.Border)
            .Text(title).Bold().FontSize(10).FontColor(CondoPdfColors.Primary);

    private static void Info(TableDescriptor table, string label, string value)
    {
        table.Cell().PaddingVertical(3).Column(c =>
        {
            c.Item().Text(label).FontSize(7.5f).FontColor(CondoPdfColors.Gray);
            c.Item().Text(value).Bold();
        });
    }

    private static void Money(TableDescriptor table, string label, decimal amount, bool strong = false)
    {
        var background = strong ? CondoPdfColors.Tint : CondoPdfColors.White;
        var labelCell = table.Cell().Background(background).BorderBottom(0.5f).BorderColor(CondoPdfColors.Border).PaddingVertical(4).PaddingHorizontal(4);
        var amountCell = table.Cell().Background(background).BorderBottom(0.5f).BorderColor(CondoPdfColors.Border).PaddingVertical(4).PaddingHorizontal(4);
        if (strong)
        {
            labelCell.Text(label).Bold();
            amountCell.AlignRight().Text(Gs(amount)).Bold();
        }
        else
        {
            labelCell.Text(label);
            amountCell.AlignRight().Text(Gs(amount));
        }
    }

    private static string Gs(decimal amount) => $"Gs. {amount:N0}";

    private static string Local(DateTime utc)
    {
        var value = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(value, Tz).ToString("dd/MM/yyyy HH:mm");
    }

    private static string StatusLabel(string status) => status switch
    {
        "Confirmed" => "Confirmada",
        "Completed" => "Finalizada",
        "Cancelled" => "Cancelada",
        "InReview" => "En revisión",
        "PendingPayment" => "Esperando pago",
        "Expired" => "Vencida",
        _ => "Cancelada"
    };

    private static string StatusColor(string status) => status switch
    {
        "Confirmed" => "#6AC64A",
        "Completed" => CondoPdfColors.Gray,
        "InReview" => CondoPdfColors.Primary,
        "PendingPayment" => "#f59e0b",
        _ => "#ef4444"
    };

    private static string CancelledByLabel(string? by) => by switch
    {
        "Buyer" => "el comprador",
        "Owner" => "el propietario",
        "Staff" => "la administración",
        _ => "el sistema"
    };
}
