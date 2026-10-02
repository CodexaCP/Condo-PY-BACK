using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;

namespace Condo.Tests.Marketplace;

/// <summary>
/// "Reportar un problema": lo abren el comprador o el propietario desde que empieza la reserva hasta 24 horas despues de su fin,
/// retiene la acreditacion y lo resuelve el Encargado: a favor del propietario (se acredita) o a favor del comprador (devolucion
/// total y el propietario asume la comision, como si hubiera cancelado).
/// </summary>
public class MarketplaceClaimServiceTests : IDisposable
{
    private readonly Phase7Harness _h = new();

    public void Dispose() => _h.Dispose();

    private static void AssertError<T>(MarketplaceResult<T> result, int status)
    {
        Assert.False(result.Ok);
        Assert.Equal(status, result.Error!.StatusCode);
    }

    // Reserva ya terminada hace una hora (3 h de duracion): finalizada y con la acreditacion pendiente.
    private MarketplaceReservation Finished() =>
        _h.SeedConfirmed(startsInHours: -4, status: MarketplaceReservationStatus.Completed);

    // Reserva en curso: confirmada, empezo hace una hora.
    private MarketplaceReservation Running() => _h.SeedConfirmed(startsInHours: -1);

    private async Task<MarketplaceClaimDto> OpenAsBuyer(MarketplaceReservation reservation, string reason = "No pude usar el espacio")
    {
        _h.LoginAs(_h.Pedro, "Resident");
        var result = await _h.Build().Claims.OpenAsync(reservation.Id, reason, CancellationToken.None);
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value!;
    }

    private async Task<MarketplaceResult<MarketplaceClaimDto>> Resolve(Guid claimId, string outcome, string note = "Revisado por la administración")
    {
        _h.LoginAsStaff(_h.Manager, "BuildingManager");
        return await _h.Build().Claims.ResolveAsync(claimId, new MarketplaceClaimResolveRequest { Outcome = outcome, Note = note }, CancellationToken.None);
    }

    // ── Abrir ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task El_comprador_reporta_un_problema_y_la_acreditacion_queda_retenida()
    {
        var reservation = Finished();

        var claim = await OpenAsBuyer(reservation);

        Assert.Equal("Open", claim.Status);
        Assert.Equal("Buyer", claim.OpenedBy);
        Assert.Equal("No pude usar el espacio", claim.Reason);
        Assert.Equal(66_000m, claim.TotalAmount);
        Assert.Equal(MarketplaceCreditStatus.Held, _h.Reload(reservation.Id).CreditStatus);
        Assert.Equal(1, _h.Events("claim.opened"));
        Assert.Equal(1, _h.Events("credit.held"));
    }

    [Fact]
    public async Task Al_reportar_se_avisa_al_encargado_y_a_la_otra_parte_pero_no_a_otros_edificios()
    {
        var reservation = Finished();

        await OpenAsBuyer(reservation);

        Assert.Equal(1, _h.Notices(_h.Manager.Id, NotificationType.MarketplaceClaimOpened));
        Assert.Equal(1, _h.Notices(_h.Operator.Id, NotificationType.MarketplaceClaimOpened));
        Assert.Equal(1, _h.Notices(_h.Juan.Id, NotificationType.MarketplaceClaimOpened));          // la otra parte
        Assert.Equal(0, _h.Notices(_h.Pedro.Id, NotificationType.MarketplaceClaimOpened));         // quien reporta no se avisa a si mismo
        Assert.Equal(0, _h.Notices(_h.OtherManager.Id, NotificationType.MarketplaceClaimOpened));
    }

    [Fact]
    public async Task El_propietario_tambien_puede_reportar_un_problema()
    {
        var reservation = Finished();
        _h.LoginAs(_h.Juan, "Owner");

        var result = await _h.Build().Claims.OpenAsync(reservation.Id, "Dejaron el espacio sucio", CancellationToken.None);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal("Owner", result.Value!.OpenedBy);
        Assert.Equal(1, _h.Notices(_h.Pedro.Id, NotificationType.MarketplaceClaimOpened));
        Assert.Equal(MarketplaceCreditStatus.Held, _h.Reload(reservation.Id).CreditStatus);
    }

    [Fact]
    public async Task Se_puede_reportar_con_la_reserva_en_curso()
    {
        var reservation = Running();

        var claim = await OpenAsBuyer(reservation, "El propietario no abrió");

        Assert.Equal("Open", claim.Status);
        Assert.Equal(MarketplaceCreditStatus.Held, _h.Reload(reservation.Id).CreditStatus);
    }

    [Fact]
    public async Task Antes_de_que_empiece_no_hay_nada_que_reportar()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 3);
        _h.LoginAs(_h.Pedro, "Resident");

        var result = await _h.Build().Claims.OpenAsync(reservation.Id, "x", CancellationToken.None);

        AssertError(result, 409);
        Assert.Contains("todavía no empezó", result.Error!.Message);
        Assert.Empty(_h.T.NewContext().MarketplaceClaims.ToList());
    }

    [Fact]
    public async Task Pasadas_24_horas_del_fin_ya_no_se_puede_reportar()
    {
        var reservation = _h.SeedConfirmed(startsInHours: -30, status: MarketplaceReservationStatus.Completed);   // termino hace 27 h
        _h.LoginAs(_h.Pedro, "Resident");

        var result = await _h.Build().Claims.OpenAsync(reservation.Id, "x", CancellationToken.None);

        AssertError(result, 409);
        Assert.Contains("24 horas", result.Error!.Message);
    }

    [Fact]
    public async Task Si_la_ganancia_ya_se_acredito_ya_no_se_puede_reportar()
    {
        var reservation = _h.SeedConfirmed(startsInHours: -4, status: MarketplaceReservationStatus.Completed, credit: MarketplaceCreditStatus.Credited);
        _h.LoginAs(_h.Pedro, "Resident");

        var result = await _h.Build().Claims.OpenAsync(reservation.Id, "x", CancellationToken.None);

        AssertError(result, 409);
        Assert.Contains("acreditó", result.Error!.Message);
    }

    [Fact]
    public async Task Hay_un_solo_reclamo_abierto_por_reserva()
    {
        var reservation = Finished();
        await OpenAsBuyer(reservation);

        _h.LoginAs(_h.Juan, "Owner");
        var second = await _h.Build().Claims.OpenAsync(reservation.Id, "Yo también", CancellationToken.None);

        AssertError(second, 409);
        Assert.Contains("reclamo abierto", second.Error!.Message);
        Assert.Single(_h.T.NewContext().MarketplaceClaims.ToList());
    }

    [Fact]
    public async Task El_motivo_es_obligatorio_y_tiene_un_maximo()
    {
        var reservation = Finished();
        _h.LoginAs(_h.Pedro, "Resident");

        AssertError(await _h.Build().Claims.OpenAsync(reservation.Id, " ", CancellationToken.None), 400);
        AssertError(await _h.Build().Claims.OpenAsync(reservation.Id, null, CancellationToken.None), 400);
        AssertError(await _h.Build().Claims.OpenAsync(reservation.Id, new string('x', 501), CancellationToken.None), 400);
        Assert.Empty(_h.T.NewContext().MarketplaceClaims.ToList());
    }

    [Fact]
    public async Task Solo_las_partes_de_la_reserva_reportan()
    {
        var reservation = Finished();

        _h.LoginAs(_h.Maria, "Resident");
        AssertError(await _h.Build().Claims.OpenAsync(reservation.Id, "x", CancellationToken.None), 404);

        _h.LoginAsStaff(_h.Manager, "BuildingManager");
        AssertError(await _h.Build().Claims.OpenAsync(reservation.Id, "x", CancellationToken.None), 404);

        Assert.Equal(MarketplaceCreditStatus.Pending, _h.Reload(reservation.Id).CreditStatus);
    }

    [Fact]
    public async Task Con_el_modulo_apagado_no_se_reporta()
    {
        var reservation = Finished();
        _h.T.EnableMarketplace(_h.Building, enabled: false);
        _h.LoginAs(_h.Pedro, "Resident");

        AssertError(await _h.Build().Claims.OpenAsync(reservation.Id, "x", CancellationToken.None), 403);
    }

    // ── Un reclamo abierto retiene la acreditacion ───────────────────────────

    [Fact]
    public async Task Un_reclamo_abierto_frena_la_acreditacion_aunque_pase_la_ventana_de_24_horas()
    {
        var reservation = Finished();
        await OpenAsBuyer(reservation);
        var db = _h.T.Db;
        var row = db.MarketplaceReservations.Single(x => x.Id == reservation.Id);
        row.StartsAtUtc = DateTime.UtcNow.AddHours(-30);
        row.EndsAtUtc = DateTime.UtcNow.AddHours(-27);
        db.SaveChanges();

        var (_, credited) = await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(0, credited);
        Assert.Equal(0m, _h.Balance(_h.Juan));
        Assert.Equal(MarketplaceCreditStatus.Held, _h.Reload(reservation.Id).CreditStatus);
    }

    // ── Resolver ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_favor_del_propietario_se_libera_la_acreditacion_y_se_acredita_al_vencer_la_ventana()
    {
        var reservation = Finished();
        var claim = await OpenAsBuyer(reservation);

        var resolved = await Resolve(claim.Id, "InFavorOfOwner", "El espacio estaba disponible");

        Assert.True(resolved.Ok, resolved.Error?.Message);
        Assert.Equal("Resolved", resolved.Value!.Status);
        Assert.Equal("InFavorOfOwner", resolved.Value.Resolution);
        Assert.Equal("El espacio estaba disponible", resolved.Value.ResolutionNote);
        Assert.Equal(MarketplaceCreditStatus.Pending, _h.Reload(reservation.Id).CreditStatus);
        Assert.Empty(_h.T.NewContext().MarketplaceRefunds.ToList());
        Assert.Equal(1, _h.Notices(_h.Juan.Id, NotificationType.MarketplaceClaimResolved));
        Assert.Equal(1, _h.Notices(_h.Pedro.Id, NotificationType.MarketplaceClaimResolved));

        // Pasada la ventana de 24 horas, el proceso de fondo acredita normalmente.
        var db = _h.T.Db;
        var row = db.MarketplaceReservations.Single(x => x.Id == reservation.Id);
        row.StartsAtUtc = DateTime.UtcNow.AddHours(-30);
        row.EndsAtUtc = DateTime.UtcNow.AddHours(-27);
        db.SaveChanges();
        var (_, credited) = await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);
        Assert.Equal(1, credited);
        Assert.Equal(60_000m, _h.Balance(_h.Juan));
    }

    [Fact]
    public async Task A_favor_del_comprador_se_le_devuelve_todo_y_el_propietario_asume_la_comision()
    {
        var reservation = Finished();
        _h.GiveCredit(_h.Juan, 10_000m);
        var claim = await OpenAsBuyer(reservation);

        var resolved = await Resolve(claim.Id, "InFavorOfBuyer", "El propietario no entregó el espacio");

        Assert.True(resolved.Ok, resolved.Error?.Message);
        Assert.Equal("InFavorOfBuyer", resolved.Value!.Resolution);

        var saved = _h.Reload(reservation.Id);
        Assert.Equal(MarketplaceCreditStatus.None, saved.CreditStatus);          // el propietario no cobra
        var db = _h.T.NewContext();
        var refund = Assert.Single(db.MarketplaceRefunds.ToList());
        Assert.Equal(66_000m, refund.Amount);                                    // todo, comision incluida
        Assert.Equal(MarketplaceRefundOrigin.ClaimResolution, refund.Origin);
        Assert.Equal(4_000m, _h.Balance(_h.Juan));                               // 10.000 - 6.000 de comision
        Assert.Equal(6_000m, db.MarketplaceAccountMovements.Single(x => x.Kind == MarketplaceAccountMovementKind.CancellationFee).Amount);
        Assert.Empty(db.MarketplaceOwnerDebts.ToList());
        Assert.Equal(1, _h.Notices(_h.Manager.Id, NotificationType.MarketplaceRefundPending));
        Assert.Equal(1, _h.Notices(_h.Pedro.Id, NotificationType.MarketplaceClaimResolved));
        Assert.Equal(1, _h.Notices(_h.Juan.Id, NotificationType.MarketplaceClaimResolved));
        Assert.Equal(1, _h.Events("credit.released"));
    }

    [Fact]
    public async Task A_favor_del_comprador_sin_saldo_la_comision_queda_como_deuda_por_gestion()
    {
        var reservation = Finished();
        var claim = await OpenAsBuyer(reservation);

        await Resolve(claim.Id, "InFavorOfBuyer");

        var debt = Assert.Single(_h.T.NewContext().MarketplaceOwnerDebts.ToList());
        Assert.Equal(6_000m, debt.Amount);
        Assert.Equal(_h.Juan.Id, debt.OwnerId);
    }

    [Fact]
    public async Task Si_la_reserva_seguia_en_curso_y_se_resuelve_a_favor_del_comprador_pasa_a_cancelada()
    {
        var reservation = Running();
        var claim = await OpenAsBuyer(reservation);

        await Resolve(claim.Id, "InFavorOfBuyer", "No se pudo usar");

        var saved = _h.Reload(reservation.Id);
        Assert.Equal(MarketplaceReservationStatus.Cancelled, saved.Status);
        Assert.Equal(MarketplaceCancellationActor.Staff, saved.CancelledBy);
        Assert.Equal("No se pudo usar", saved.CancelReason);
    }

    [Fact]
    public async Task Una_reserva_resuelta_a_favor_del_comprador_nunca_se_acredita()
    {
        var reservation = Finished();
        var claim = await OpenAsBuyer(reservation);
        await Resolve(claim.Id, "InFavorOfBuyer");
        var db = _h.T.Db;
        var row = db.MarketplaceReservations.Single(x => x.Id == reservation.Id);
        row.StartsAtUtc = DateTime.UtcNow.AddHours(-30);
        row.EndsAtUtc = DateTime.UtcNow.AddHours(-27);
        db.SaveChanges();

        var (_, credited) = await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(0, credited);
        Assert.Equal(0m, _h.Balance(_h.Juan));
    }

    [Fact]
    public async Task Resolver_dos_veces_no_duplica_el_reembolso_ni_la_comision()
    {
        var reservation = Finished();
        _h.GiveCredit(_h.Juan, 10_000m);
        var claim = await OpenAsBuyer(reservation);

        Assert.True((await Resolve(claim.Id, "InFavorOfBuyer")).Ok);
        AssertError(await Resolve(claim.Id, "InFavorOfBuyer"), 409);
        AssertError(await Resolve(claim.Id, "InFavorOfOwner"), 409);

        var db = _h.T.NewContext();
        Assert.Single(db.MarketplaceRefunds.ToList());
        Assert.Single(db.MarketplaceAccountMovements.Where(x => x.Kind == MarketplaceAccountMovementKind.CancellationFee).ToList());
        Assert.Equal(4_000m, _h.Balance(_h.Juan));
    }

    [Fact]
    public async Task Al_resolver_hay_que_elegir_un_resultado_valido_y_explicarlo()
    {
        var reservation = Finished();
        var claim = await OpenAsBuyer(reservation);

        AssertError(await Resolve(claim.Id, ""), 400);
        AssertError(await Resolve(claim.Id, "Otra"), 400);
        AssertError(await Resolve(claim.Id, "99"), 400);
        AssertError(await Resolve(claim.Id, "InFavorOfOwner", "  "), 400);
        AssertError(await Resolve(claim.Id, "InFavorOfOwner", new string('x', 501)), 400);
        Assert.Equal("Open", _h.T.NewContext().MarketplaceClaims.Single().Status.ToString());
    }

    [Fact]
    public async Task Solo_el_personal_del_edificio_resuelve()
    {
        var reservation = Finished();
        var claim = await OpenAsBuyer(reservation);
        var request = new MarketplaceClaimResolveRequest { Outcome = "InFavorOfBuyer", Note = "x" };

        _h.LoginAs(_h.Pedro, "Resident");
        AssertError(await _h.Build().Claims.ResolveAsync(claim.Id, request, CancellationToken.None), 404);

        _h.LoginAs(_h.Juan, "Owner");
        AssertError(await _h.Build().Claims.ResolveAsync(claim.Id, request, CancellationToken.None), 404);

        _h.LoginAsStaff(_h.OtherManager, "BuildingManager");
        AssertError(await _h.Build().Claims.ResolveAsync(claim.Id, request, CancellationToken.None), 404);

        AssertError(await _h.Build().Claims.ResolveAsync(Guid.NewGuid(), request, CancellationToken.None), 404);
        Assert.Equal(MarketplaceClaimStatus.Open, _h.T.NewContext().MarketplaceClaims.Single().Status);
        Assert.Empty(_h.T.NewContext().MarketplaceRefunds.ToList());
    }

    [Fact]
    public async Task Resuelto_a_favor_del_propietario_dentro_de_la_ventana_se_puede_reportar_de_nuevo()
    {
        var reservation = Finished();
        var claim = await OpenAsBuyer(reservation);
        await Resolve(claim.Id, "InFavorOfOwner");

        _h.LoginAs(_h.Pedro, "Resident");
        var again = await _h.Build().Claims.OpenAsync(reservation.Id, "Sigue el problema", CancellationToken.None);

        Assert.True(again.Ok, again.Error?.Message);
        Assert.Equal(2, _h.T.NewContext().MarketplaceClaims.Count());
    }

    // ── Lista del Encargado ──────────────────────────────────────────────────

    [Fact]
    public async Task El_encargado_ve_el_reclamo_con_lo_necesario_para_decidir()
    {
        var reservation = Finished();
        _h.LoginAs(_h.Pedro, "Resident");
        var db = _h.T.Db;
        var row = db.MarketplaceReservations.Single(x => x.Id == reservation.Id);
        row.StartResponse = MarketplaceStartResponse.NotUsing;
        row.StartResponseReason = "Me enfermé";
        db.SaveChanges();
        await OpenAsBuyer(reservation);
        _h.LoginAsStaff(_h.Manager, "BuildingManager");

        var list = (await _h.Build().Claims.GetClaimsAsync(_h.Building.Id, includeResolved: false, CancellationToken.None)).Value!;

        var item = Assert.Single(list);
        Assert.Equal("Pedro Prueba", item.BuyerName);
        Assert.Equal("101", item.BuyerUnits);
        Assert.Equal("Juan Prueba", item.OwnerName);
        Assert.Equal("302", item.UnitCode);
        Assert.Equal(60_000m, item.BaseAmount);
        Assert.Equal(6_000m, item.CommissionAmount);
        Assert.Equal(66_000m, item.TotalAmount);
        Assert.Equal("NotUsing", item.BuyerStartResponse);
        Assert.Equal("Me enfermé", item.BuyerStartResponseReason);
        Assert.Equal("Pedro Prueba", item.OpenedByName);
    }

    [Fact]
    public async Task La_lista_de_reclamos_trae_los_abiertos_primero_y_los_resueltos_solo_si_se_piden()
    {
        var first = Finished();
        var firstClaim = await OpenAsBuyer(first);
        await Resolve(firstClaim.Id, "InFavorOfOwner");
        var second = _h.SeedConfirmed(startsInHours: -10, status: MarketplaceReservationStatus.Completed, buyer: _h.Maria);
        _h.LoginAs(_h.Maria, "Resident");
        var open = (await _h.Build().Claims.OpenAsync(second.Id, "Problema", CancellationToken.None)).Value!;
        _h.LoginAsStaff(_h.Manager, "BuildingManager");

        var onlyOpen = (await _h.Build().Claims.GetClaimsAsync(_h.Building.Id, false, CancellationToken.None)).Value!;
        var all = (await _h.Build().Claims.GetClaimsAsync(_h.Building.Id, true, CancellationToken.None)).Value!;

        Assert.Equal(open.Id, Assert.Single(onlyOpen).Id);
        Assert.Equal([open.Id, firstClaim.Id], all.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task La_lista_de_reclamos_no_se_ve_desde_otro_edificio_ni_como_usuario_final()
    {
        var reservation = Finished();
        await OpenAsBuyer(reservation);

        _h.LoginAsStaff(_h.OtherManager, "BuildingManager");
        AssertError(await _h.Build().Claims.GetClaimsAsync(_h.Building.Id, false, CancellationToken.None), 404);

        _h.LoginAs(_h.Pedro, "Resident");
        AssertError(await _h.Build().Claims.GetClaimsAsync(_h.Building.Id, false, CancellationToken.None), 404);
    }

    // ── Lo que ve cada parte ─────────────────────────────────────────────────

    [Fact]
    public async Task Mis_reservas_dicen_cuando_se_puede_reportar_y_como_se_resolvio()
    {
        var reservation = Finished();
        _h.LoginAs(_h.Pedro, "Resident");

        var before = (await _h.Build().Reservations.GetMineAsync(_h.Building.Id, CancellationToken.None)).Value!.Single();
        Assert.True(before.CanReportProblem);
        Assert.Null(before.ClaimStatus);

        var claim = await OpenAsBuyer(reservation);
        var open = (await _h.Build().Reservations.GetMineAsync(_h.Building.Id, CancellationToken.None)).Value!.Single();
        Assert.False(open.CanReportProblem);                 // ya hay uno abierto
        Assert.Equal("Open", open.ClaimStatus);

        await Resolve(claim.Id, "InFavorOfBuyer", "Se devuelve todo");
        _h.LoginAs(_h.Pedro, "Resident");
        var resolved = (await _h.Build().Reservations.GetMineAsync(_h.Building.Id, CancellationToken.None)).Value!.Single();
        Assert.Equal("Resolved", resolved.ClaimStatus);
        Assert.Equal("InFavorOfBuyer", resolved.ClaimResolution);
        Assert.Equal("Se devuelve todo", resolved.ClaimResolutionNote);
        Assert.Equal(66_000m, resolved.RefundAmount);
        Assert.False(resolved.CanReportProblem);
    }
}
