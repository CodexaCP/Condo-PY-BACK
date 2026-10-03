using System.Text;
using System.Text.Json;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Marketplace;

/// <summary>
/// Documentos de la fase 8: el comprobante interno de reserva en PDF (cada parte lo pide con su version, solo si el pago esta
/// confirmado) y el historial economico de la operacion, reconstruido desde los eventos de auditoria y solo para el personal.
/// </summary>
public class MarketplaceDocumentServiceTests : IDisposable
{
    private readonly Phase7Harness _h = new();

    public MarketplaceDocumentServiceTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    public void Dispose() => _h.Dispose();

    private static void AssertError<T>(MarketplaceResult<T> result, int status)
    {
        Assert.False(result.Ok);
        Assert.Equal(status, result.Error!.StatusCode);
    }

    private static bool IsPdf(byte[] bytes) => bytes.Length > 1000 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";

    private string ReceiptAudiences() =>
        string.Join(",", _h.T.NewContext().MarketplaceEvents.Where(x => x.Action == "receipt.generated").AsEnumerable()
            .Select(x => JsonDocument.Parse(x.DataJson!).RootElement.GetProperty("Audience").GetString()));

    // ── Comprobante interno de reserva ───────────────────────────────────────

    [Fact]
    public async Task El_personal_descarga_el_comprobante_y_queda_registrado_quien_lo_pidio()
    {
        var reservation = _h.SeedConfirmed();
        _h.LoginAsStaff(_h.Manager, "BuildingManager");

        var result = await _h.BuildDocuments().Documents.GetReceiptAsync(reservation.Id, CancellationToken.None);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.True(IsPdf(result.Value!.Bytes));
        Assert.Equal($"comprobante-reserva-{reservation.Reference}.pdf", result.Value.FileName);
        var evt = Assert.Single(_h.T.NewContext().MarketplaceEvents.Where(x => x.Action == "receipt.generated").ToList());
        Assert.Equal(_h.Manager.Id, evt.UserId);
        Assert.Equal(reservation.Id, evt.EntityId);
        Assert.Equal("Staff", ReceiptAudiences());
    }

    [Fact]
    public async Task Cada_parte_recibe_su_version_del_comprobante()
    {
        var reservation = _h.SeedConfirmed();

        _h.LoginAs(_h.Pedro, "Resident");
        Assert.True(IsPdf((await _h.BuildDocuments().Documents.GetReceiptAsync(reservation.Id, CancellationToken.None)).Value!.Bytes));

        _h.LoginAs(_h.Juan, "Owner");
        Assert.True(IsPdf((await _h.BuildDocuments().Documents.GetReceiptAsync(reservation.Id, CancellationToken.None)).Value!.Bytes));

        _h.LoginAsStaff(_h.Operator, "CompanyOperator");
        Assert.True(IsPdf((await _h.BuildDocuments().Documents.GetReceiptAsync(reservation.Id, CancellationToken.None)).Value!.Bytes));

        Assert.Equal("Buyer,Owner,Staff", ReceiptAudiences());
    }

    [Fact]
    public async Task Un_extrano_o_personal_de_otro_edificio_no_obtiene_el_comprobante()
    {
        var reservation = _h.SeedConfirmed();

        _h.LoginAs(_h.Maria, "Resident");     // otra vecina del mismo edificio
        AssertError(await _h.BuildDocuments().Documents.GetReceiptAsync(reservation.Id, CancellationToken.None), 404);

        _h.LoginAsStaff(_h.OtherManager, "BuildingManager");
        AssertError(await _h.BuildDocuments().Documents.GetReceiptAsync(reservation.Id, CancellationToken.None), 404);

        AssertError(await _h.BuildDocuments().Documents.GetReceiptAsync(Guid.NewGuid(), CancellationToken.None), 404);
        Assert.Empty(_h.T.NewContext().MarketplaceEvents.Where(x => x.Action == "receipt.generated").ToList());
    }

    [Theory]
    [InlineData(MarketplaceReservationStatus.PendingPayment)]
    [InlineData(MarketplaceReservationStatus.InReview)]
    public async Task Sin_pago_confirmado_no_hay_comprobante(MarketplaceReservationStatus status)
    {
        var reservation = _h.SeedConfirmed(status: status, credit: MarketplaceCreditStatus.None);
        _h.LoginAs(_h.Pedro, "Resident");

        var result = await _h.BuildDocuments().Documents.GetReceiptAsync(reservation.Id, CancellationToken.None);

        AssertError(result, 409);
        Assert.Contains("pago confirmado", result.Error!.Message);
    }

    [Fact]
    public async Task Con_el_modulo_apagado_no_se_genera_el_comprobante()
    {
        var reservation = _h.SeedConfirmed();
        _h.T.EnableMarketplace(_h.Building, enabled: false);
        _h.LoginAs(_h.Pedro, "Resident");

        AssertError(await _h.BuildDocuments().Documents.GetReceiptAsync(reservation.Id, CancellationToken.None), 403);
    }

    [Fact]
    public async Task El_comprobante_de_una_reserva_cancelada_y_reembolsada_tambien_se_genera()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Juan, "Owner");
        await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "Motivo", CancellationToken.None);
        var refund = _h.T.NewContext().MarketplaceRefunds.Single();
        _h.LoginAsStaff(_h.Manager, "BuildingManager");
        await _h.Build().Refunds.MarkReturnedAsync(refund.Id, CancellationToken.None);

        foreach (var login in new Action[]
                 {
                     () => _h.LoginAsStaff(_h.Manager, "BuildingManager"),
                     () => _h.LoginAs(_h.Pedro, "Resident"),
                     () => _h.LoginAs(_h.Juan, "Owner")
                 })
        {
            login();
            var result = await _h.BuildDocuments().Documents.GetReceiptAsync(reservation.Id, CancellationToken.None);
            Assert.True(result.Ok, result.Error?.Message);
            Assert.True(IsPdf(result.Value!.Bytes));
        }
    }

    [Fact]
    public async Task El_personal_ve_adjunto_el_comprobante_de_transferencia_cuando_es_una_imagen()
    {
        var reservation = _h.SeedConfirmed();
        var uploads = Path.Combine(_h.WebRoot, "uploads");
        Directory.CreateDirectory(uploads);
        // PNG de 1x1 pixel.
        File.WriteAllBytes(Path.Combine(uploads, "transferencia.png"), Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        var payment = _h.T.Db.MarketplacePayments.Single(x => x.ReservationId == reservation.Id);
        payment.ComprobanteUrl = "/uploads/transferencia.png";
        _h.T.Db.SaveChanges();
        _h.LoginAsStaff(_h.Manager, "BuildingManager");

        var withImage = await _h.BuildDocuments().Documents.GetReceiptAsync(reservation.Id, CancellationToken.None);

        Assert.True(withImage.Ok, withImage.Error?.Message);
        Assert.True(IsPdf(withImage.Value!.Bytes));

        // Un comprobante que ya no esta en disco no rompe el documento.
        File.Delete(Path.Combine(uploads, "transferencia.png"));
        Assert.True((await _h.BuildDocuments().Documents.GetReceiptAsync(reservation.Id, CancellationToken.None)).Ok);
    }

    // ── Historial economico ──────────────────────────────────────────────────

    [Fact]
    public async Task El_historial_cuenta_la_operacion_de_punta_a_punta_con_importes_y_responsables()
    {
        // Flujo real: reserva -> comprobante -> el Encargado confirma.
        var listing = _h.Listing;
        DateTime At(int hour) => listing.WindowStartUtc.Date.AddHours(hour);
        _h.LoginAs(_h.Pedro, "Resident");
        var reserved = (await _h.Build().Reservations.ReserveAsync(
            new MarketplaceQuoteRequest { ListingId = listing.Id, StartsAtUtc = At(19), EndsAtUtc = At(22) }, CancellationToken.None)).Value!;
        await _h.Build().Payments.SubmitPaymentAsync(reserved.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = "/uploads/abc.png" }, CancellationToken.None);
        var paymentId = _h.T.NewContext().MarketplacePayments.Single(x => x.ReservationId == reserved.Id).Id;
        _h.LoginAsStaff(_h.Manager, "BuildingManager");
        Assert.True((await _h.Build().Payments.ApproveAsync(paymentId, new MarketplaceApproveRequest { ReviewedAmount = 66_000m }, CancellationToken.None)).Ok);

        var history = (await _h.BuildDocuments().Documents.GetHistoryAsync(reserved.Id, CancellationToken.None)).Value!;

        Assert.Equal(reserved.Reference, history.Reference);
        Assert.Equal(60_000m, history.BaseAmount);
        Assert.Equal(6_000m, history.CommissionAmount);
        Assert.Equal(66_000m, history.TotalAmount);
        Assert.Equal(60_000m, history.OwnerNetAmount);

        var actions = history.Items.Select(x => x.Action).ToList();
        Assert.Contains("reservation.created", actions);
        Assert.Contains("payment.submitted", actions);
        Assert.Contains("payment.approved", actions);
        Assert.Contains("reservation.confirmed", actions);
        Assert.Contains("account.movement", actions);
        Assert.True(actions.IndexOf("reservation.created") < actions.IndexOf("payment.approved"));   // en orden

        var created = history.Items.Single(x => x.Action == "reservation.created");
        Assert.Equal("Reserva creada", created.Title);
        Assert.Equal(66_000m, created.Amount);
        Assert.Contains("Ganancia del propietario Gs. 60.000", created.Detail!.Replace(",", "."));
        Assert.Equal(_h.Pedro.FullName, created.ActorName);

        var approved = history.Items.Single(x => x.Action == "payment.approved");
        Assert.Equal(_h.Manager.FullName, approved.ActorName);
        Assert.Equal(66_000m, approved.Amount);
        Assert.Equal(66_000m, history.Items.Single(x => x.Action == "account.movement").Amount);
        Assert.Null(history.Items.Single(x => x.Action == "account.movement").ActorName);               // lo hizo el sistema
    }

    [Fact]
    public async Task El_historial_incluye_la_cancelacion_el_reembolso_y_la_comision_asumida()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.GiveCredit(_h.Juan, 10_000m);
        _h.LoginAs(_h.Juan, "Owner");
        await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "Se me complicó", CancellationToken.None);
        var refund = _h.T.NewContext().MarketplaceRefunds.Single();
        _h.LoginAsStaff(_h.Manager, "BuildingManager");
        await _h.Build().Refunds.MarkReturnedAsync(refund.Id, CancellationToken.None);

        var history = (await _h.BuildDocuments().Documents.GetHistoryAsync(reservation.Id, CancellationToken.None)).Value!;

        var cancelled = history.Items.Single(x => x.Action == "reservation.cancelled");
        Assert.Equal(66_000m, cancelled.Amount);
        Assert.Contains("Por el propietario", cancelled.Detail);
        Assert.Contains("Se me complicó", cancelled.Detail);
        Assert.Equal(66_000m, history.Items.Single(x => x.Action == "refund.created").Amount);
        Assert.Equal(66_000m, history.Items.Single(x => x.Action == "refund.returned").Amount);
        var fee = history.Items.Single(x => x.Action == "owner_fee.charged");
        Assert.Equal(6_000m, fee.Amount);
        Assert.Contains("6.000", fee.Detail!.Replace(",", "."));
        // El historial se arma solo con lo de ESTA operacion.
        Assert.All(history.Items, i => Assert.NotEqual("listing.suspended", i.Action));
    }

    [Fact]
    public async Task El_historial_no_mezcla_otras_operaciones_y_es_solo_del_personal_del_edificio()
    {
        var first = _h.SeedConfirmed(startsInHours: 5);
        var second = _h.SeedConfirmed(startsInHours: 20);
        _h.LoginAs(_h.Pedro, "Resident");
        await _h.Build().Cancellations.CancelByBuyerAsync(first.Id, null, CancellationToken.None);

        _h.LoginAsStaff(_h.Manager, "BuildingManager");
        var historySecond = (await _h.BuildDocuments().Documents.GetHistoryAsync(second.Id, CancellationToken.None)).Value!;
        Assert.DoesNotContain(historySecond.Items, i => i.Action is "reservation.cancelled" or "refund.created");

        // Ni el comprador ni el propietario ni el Encargado de otro edificio.
        _h.LoginAs(_h.Pedro, "Resident");
        AssertError(await _h.BuildDocuments().Documents.GetHistoryAsync(first.Id, CancellationToken.None), 404);
        _h.LoginAs(_h.Juan, "Owner");
        AssertError(await _h.BuildDocuments().Documents.GetHistoryAsync(first.Id, CancellationToken.None), 404);
        _h.LoginAsStaff(_h.OtherManager, "BuildingManager");
        AssertError(await _h.BuildDocuments().Documents.GetHistoryAsync(first.Id, CancellationToken.None), 404);
        AssertError(await _h.BuildDocuments().Documents.GetHistoryAsync(Guid.NewGuid(), CancellationToken.None), 404);
    }

    [Fact]
    public void Un_evento_con_datos_ilegibles_igual_se_muestra()
    {
        var evt = new MarketplaceEvent
        {
            Action = "payment.approved", EntityType = "MarketplacePayment", EntityId = Guid.NewGuid(), DataJson = "{no es json"
        };

        var item = MarketplaceDocumentService.ToItem(evt, "Encargado");

        Assert.Equal("Pago confirmado", item.Title);
        Assert.Null(item.Amount);
    }

    [Fact]
    public void Una_accion_desconocida_se_muestra_con_su_codigo()
    {
        var item = MarketplaceDocumentService.ToItem(new MarketplaceEvent { Action = "algo.nuevo", EntityId = Guid.NewGuid() }, null);

        Assert.Equal("algo.nuevo", item.Title);
        Assert.Null(item.ActorName);
    }
}
