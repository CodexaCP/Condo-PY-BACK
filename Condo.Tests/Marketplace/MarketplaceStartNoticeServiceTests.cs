using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;

namespace Condo.Tests.Marketplace;

/// <summary>
/// Aviso de inicio: al empezar el horario el comprador recibe "¿La vas a usar?". Responder ("Sí, voy" / "No la voy a usar" con
/// motivo) solo queda registrado: no devuelve dinero ni cambia lo que cobra el propietario. Sin respuesta no pasa nada.
/// </summary>
public class MarketplaceStartNoticeServiceTests : IDisposable
{
    private readonly Phase7Harness _h = new();

    public void Dispose() => _h.Dispose();

    private static void AssertError<T>(MarketplaceResult<T> result, int status)
    {
        Assert.False(result.Ok);
        Assert.Equal(status, result.Error!.StatusCode);
    }

    private static MarketplaceStartResponseRequest Yes() => new() { Attending = true };
    private static MarketplaceStartResponseRequest No(string? reason) => new() { Attending = false, Reason = reason };

    // ── Aviso (proceso de fondo) ─────────────────────────────────────────────

    [Fact]
    public async Task Al_empezar_el_horario_el_comprador_recibe_el_aviso_una_sola_vez()
    {
        var reservation = _h.SeedConfirmed(startsInHours: -1);

        Assert.Equal(1, await _h.Build().StartNotices.SendDueAsync(CancellationToken.None));
        Assert.Equal(0, await _h.Build().StartNotices.SendDueAsync(CancellationToken.None));

        Assert.Equal(1, _h.Notices(_h.Pedro.Id, NotificationType.MarketplaceStartNotice));
        Assert.Equal(0, _h.Notices(_h.Juan.Id, NotificationType.MarketplaceStartNotice));          // el propietario no recibe este aviso
        Assert.NotNull(_h.Reload(reservation.Id).StartNoticeSentAtUtc);
        Assert.Equal(1, _h.Events("start_notice.sent"));
    }

    [Fact]
    public async Task No_hay_aviso_antes_de_empezar_despues_de_terminar_ni_para_reservas_que_no_estan_confirmadas()
    {
        _h.SeedConfirmed(startsInHours: 3);                                                         // todavia no empezo
        _h.SeedConfirmed(startsInHours: -10, status: MarketplaceReservationStatus.Completed);        // ya termino
        _h.SeedConfirmed(startsInHours: -1, status: MarketplaceReservationStatus.InReview, credit: MarketplaceCreditStatus.None);
        _h.SeedConfirmed(startsInHours: -20, status: MarketplaceReservationStatus.Cancelled, credit: MarketplaceCreditStatus.None);

        Assert.Equal(0, await _h.Build().StartNotices.SendDueAsync(CancellationToken.None));
        Assert.Equal(0, _h.Notices(_h.Pedro.Id, NotificationType.MarketplaceStartNotice));
    }

    // ── Respuesta ────────────────────────────────────────────────────────────

    [Fact]
    public async Task El_comprador_responde_que_va_y_queda_registrado()
    {
        var reservation = _h.SeedConfirmed(startsInHours: -1);
        await _h.Build().StartNotices.SendDueAsync(CancellationToken.None);
        _h.LoginAs(_h.Pedro, "Resident");

        var pending = (await _h.Build().Reservations.GetMineAsync(_h.Building.Id, CancellationToken.None)).Value!.Single();
        Assert.True(pending.NeedsStartResponse);

        var result = await _h.Build().StartNotices.RespondAsync(reservation.Id, Yes(), CancellationToken.None);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal("Attending", result.Value!.StartResponse);
        Assert.False(result.Value.NeedsStartResponse);
        var saved = _h.Reload(reservation.Id);
        Assert.Equal(MarketplaceStartResponse.Attending, saved.StartResponse);
        Assert.NotNull(saved.StartResponseAtUtc);
        Assert.Equal(1, _h.Events("start_notice.responded"));
    }

    [Fact]
    public async Task Decir_que_no_la_va_a_usar_pide_motivo_y_no_cambia_nada_para_el_propietario_ni_devuelve_dinero()
    {
        var reservation = _h.SeedConfirmed(startsInHours: -1);
        _h.LoginAs(_h.Pedro, "Resident");

        AssertError(await _h.Build().StartNotices.RespondAsync(reservation.Id, No(null), CancellationToken.None), 400);
        AssertError(await _h.Build().StartNotices.RespondAsync(reservation.Id, No("   "), CancellationToken.None), 400);
        AssertError(await _h.Build().StartNotices.RespondAsync(reservation.Id, No(new string('x', 501)), CancellationToken.None), 400);

        var result = await _h.Build().StartNotices.RespondAsync(reservation.Id, No("Me surgió un viaje"), CancellationToken.None);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal("NotUsing", result.Value!.StartResponse);
        var saved = _h.Reload(reservation.Id);
        Assert.Equal("Me surgió un viaje", saved.StartResponseReason);
        // Sin devolucion automatica: la reserva sigue confirmada y la ganancia del propietario sigue pendiente.
        Assert.Equal(MarketplaceReservationStatus.Confirmed, saved.Status);
        Assert.Equal(MarketplaceCreditStatus.Pending, saved.CreditStatus);
        var db = _h.T.NewContext();
        Assert.Empty(db.MarketplaceRefunds.ToList());
        Assert.Empty(db.MarketplaceOwnerDebts.ToList());
        Assert.Equal(66_000m, _h.AccountBalance());
    }

    [Fact]
    public async Task Despues_de_decir_que_no_la_usa_el_comprador_igual_puede_reportar_un_problema_para_pedir_su_dinero()
    {
        var reservation = _h.SeedConfirmed(startsInHours: -1);
        _h.LoginAs(_h.Pedro, "Resident");
        await _h.Build().StartNotices.RespondAsync(reservation.Id, No("No pude ir"), CancellationToken.None);

        var claim = await _h.Build().Claims.OpenAsync(reservation.Id, "Quiero mi dinero", CancellationToken.None);

        Assert.True(claim.Ok, claim.Error?.Message);
    }

    [Fact]
    public async Task Solo_se_responde_una_vez()
    {
        var reservation = _h.SeedConfirmed(startsInHours: -1);
        _h.LoginAs(_h.Pedro, "Resident");

        Assert.True((await _h.Build().StartNotices.RespondAsync(reservation.Id, Yes(), CancellationToken.None)).Ok);
        AssertError(await _h.Build().StartNotices.RespondAsync(reservation.Id, No("cambié de idea"), CancellationToken.None), 409);
        Assert.Equal(MarketplaceStartResponse.Attending, _h.Reload(reservation.Id).StartResponse);
    }

    [Fact]
    public async Task Solo_se_responde_con_la_reserva_en_curso()
    {
        var notStarted = _h.SeedConfirmed(startsInHours: 3);
        var finished = _h.SeedConfirmed(startsInHours: -10, status: MarketplaceReservationStatus.Completed);
        var unpaid = _h.SeedConfirmed(startsInHours: -1, status: MarketplaceReservationStatus.InReview, credit: MarketplaceCreditStatus.None);
        _h.LoginAs(_h.Pedro, "Resident");

        AssertError(await _h.Build().StartNotices.RespondAsync(notStarted.Id, Yes(), CancellationToken.None), 409);
        AssertError(await _h.Build().StartNotices.RespondAsync(finished.Id, Yes(), CancellationToken.None), 409);
        AssertError(await _h.Build().StartNotices.RespondAsync(unpaid.Id, Yes(), CancellationToken.None), 409);
    }

    [Fact]
    public async Task Solo_el_comprador_responde_el_aviso_de_su_reserva()
    {
        var reservation = _h.SeedConfirmed(startsInHours: -1);

        _h.LoginAs(_h.Maria, "Resident");
        AssertError(await _h.Build().StartNotices.RespondAsync(reservation.Id, Yes(), CancellationToken.None), 404);

        _h.LoginAs(_h.Juan, "Owner");
        AssertError(await _h.Build().StartNotices.RespondAsync(reservation.Id, Yes(), CancellationToken.None), 404);

        Assert.Null(_h.Reload(reservation.Id).StartResponse);
    }

    [Fact]
    public async Task Con_el_modulo_apagado_no_se_responde()
    {
        var reservation = _h.SeedConfirmed(startsInHours: -1);
        _h.T.EnableMarketplace(_h.Building, enabled: false);
        _h.LoginAs(_h.Pedro, "Resident");

        AssertError(await _h.Build().StartNotices.RespondAsync(reservation.Id, Yes(), CancellationToken.None), 403);
    }
}
