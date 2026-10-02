using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Marketplace;

/// <summary>
/// Cancelar una reserva pagada: el comprador solo antes del inicio y sin que se le devuelva la comision; el propietario con motivo,
/// con devolucion total al comprador y la comision a su cargo (saldo a favor y/o deuda por gestion). Siempre libera el horario,
/// no acredita nada y deja un reembolso pendiente para el Encargado.
/// </summary>
public class MarketplaceCancellationServiceTests : IDisposable
{
    private readonly Phase7Harness _h = new();

    public void Dispose() => _h.Dispose();

    private static void AssertError<T>(MarketplaceResult<T> result, int status)
    {
        Assert.False(result.Ok);
        Assert.Equal(status, result.Error!.StatusCode);
    }

    // ── Comprador ────────────────────────────────────────────────────────────

    [Fact]
    public async Task El_comprador_cancela_una_reserva_pagada_antes_del_inicio_y_se_le_devuelve_solo_la_base()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Pedro, "Resident");

        var result = await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, "Cambié de planes", CancellationToken.None);

        Assert.True(result.Ok, result.Error?.Message);
        var dto = result.Value!;
        Assert.Equal("Cancelled", dto.Status);
        Assert.Equal(60_000m, dto.RefundAmount);          // la base: la comision de 6.000 NO se devuelve
        Assert.Equal("Pending", dto.RefundStatus);
        Assert.Equal("Cambié de planes", dto.CancelReason);
        Assert.False(dto.CanCancel);

        var saved = _h.Reload(reservation.Id);
        Assert.Equal(MarketplaceReservationStatus.Cancelled, saved.Status);
        Assert.Equal(MarketplaceCancellationActor.Buyer, saved.CancelledBy);
        Assert.Equal(_h.Pedro.Id, saved.CancelledByUserId);
        Assert.Equal(MarketplaceCreditStatus.None, saved.CreditStatus);     // no se acredita nada al propietario
        Assert.All(saved.Slots, s => Assert.True(s.IsDeleted));              // el horario queda libre

        var db = _h.T.NewContext();
        var refund = Assert.Single(db.MarketplaceRefunds.ToList());
        Assert.Equal(60_000m, refund.Amount);
        Assert.Equal(MarketplaceRefundOrigin.BuyerCancellation, refund.Origin);
        Assert.Equal(MarketplaceRefundStatus.Pending, refund.Status);
        Assert.Equal(_h.Pedro.Id, refund.RecipientUserId);
        Assert.Empty(db.MarketplaceOwnerDebts.ToList());                      // el propietario no asume nada
        Assert.DoesNotContain(db.MarketplaceAccountMovements.ToList(), m => m.Kind == MarketplaceAccountMovementKind.CancellationFee);
        Assert.Equal(66_000m, _h.AccountBalance());                           // el dinero sigue en la cuenta hasta devolverlo
        Assert.Equal(1, _h.Events("reservation.cancelled"));
        Assert.Equal(1, _h.Events("refund.created"));
    }

    [Fact]
    public async Task Al_cancelar_se_avisa_al_propietario_y_a_quienes_devuelven_pero_no_a_otros_edificios()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Pedro, "Resident");

        await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None);

        Assert.Equal(1, _h.Notices(_h.Juan.Id, NotificationType.MarketplaceReservationCancelled));
        Assert.Equal(1, _h.Notices(_h.Manager.Id, NotificationType.MarketplaceRefundPending));
        Assert.Equal(1, _h.Notices(_h.Operator.Id, NotificationType.MarketplaceRefundPending));
        Assert.Equal(1, _h.Notices(_h.CompanyAdmin.Id, NotificationType.MarketplaceRefundPending));
        Assert.Equal(0, _h.Notices(_h.OtherManager.Id, NotificationType.MarketplaceRefundPending));
    }

    [Fact]
    public async Task El_horario_cancelado_queda_libre_en_la_base_para_otra_reserva()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Pedro, "Resident");
        await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None);

        // El indice unico (publicacion, bloque) deja entrar otra reserva en el mismo horario.
        var other = _h.T.NewReservation(_h.Listing, _h.Maria);
        other.StartsAtUtc = reservation.StartsAtUtc;
        other.EndsAtUtc = reservation.EndsAtUtc;
        _h.T.Db.MarketplaceReservations.Add(other);
        _h.T.Db.MarketplaceReservationSlots.Add(_h.T.NewSlot(other, reservation.StartsAtUtc));
        _h.T.Db.SaveChanges();
    }

    [Fact]
    public async Task El_comprador_no_puede_cancelar_una_reserva_ya_empezada()
    {
        var reservation = _h.SeedConfirmed(startsInHours: -1);
        _h.LoginAs(_h.Pedro, "Resident");

        var result = await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None);

        AssertError(result, 409);
        Assert.Contains("Reportar un problema", result.Error!.Message);
        Assert.Equal(MarketplaceReservationStatus.Confirmed, _h.Reload(reservation.Id).Status);
        Assert.Empty(_h.T.NewContext().MarketplaceRefunds.ToList());
    }

    [Fact]
    public async Task Una_reserva_en_revision_no_se_cancela_se_confirma_o_se_rechaza()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5, status: MarketplaceReservationStatus.InReview, credit: MarketplaceCreditStatus.None);
        _h.LoginAs(_h.Pedro, "Resident");

        AssertError(await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None), 409);
    }

    [Fact]
    public async Task Cancelar_dos_veces_no_duplica_el_reembolso()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Pedro, "Resident");

        Assert.True((await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None)).Ok);
        AssertError(await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None), 409);

        Assert.Single(_h.T.NewContext().MarketplaceRefunds.ToList());
        Assert.Equal(1, _h.Events("reservation.cancelled"));
    }

    [Fact]
    public async Task Cancelar_una_reserva_sin_pagar_sigue_siendo_gratis_y_sin_reembolso()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5, status: MarketplaceReservationStatus.PendingPayment, credit: MarketplaceCreditStatus.None);
        _h.LoginAs(_h.Pedro, "Resident");

        var result = await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal("Cancelled", result.Value!.Status);
        Assert.Empty(_h.T.NewContext().MarketplaceRefunds.ToList());
    }

    [Fact]
    public async Task Solo_el_comprador_cancela_por_su_lado()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);

        _h.LoginAs(_h.Maria, "Resident");     // otra vecina
        AssertError(await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None), 404);

        _h.LoginAs(_h.Juan, "Owner");         // el propietario no cancela como comprador
        AssertError(await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None), 404);

        _h.LoginAsStaff(_h.Manager, "BuildingManager");  // ni el personal
        AssertError(await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None), 404);

        Assert.Equal(MarketplaceReservationStatus.Confirmed, _h.Reload(reservation.Id).Status);
    }

    [Fact]
    public async Task El_motivo_del_comprador_tiene_un_maximo()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Pedro, "Resident");

        AssertError(await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, new string('x', 501), CancellationToken.None), 400);
    }

    [Fact]
    public async Task Con_el_modulo_apagado_no_se_cancela()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.T.EnableMarketplace(_h.Building, enabled: false);
        _h.LoginAs(_h.Pedro, "Resident");

        AssertError(await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None), 403);
        Assert.Equal(MarketplaceReservationStatus.Confirmed, _h.Reload(reservation.Id).Status);
    }

    // ── Propietario ──────────────────────────────────────────────────────────

    [Fact]
    public async Task El_propietario_cancela_con_motivo_y_se_le_devuelve_todo_al_comprador()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.GiveCredit(_h.Juan, 10_000m);
        _h.LoginAs(_h.Juan, "Owner");

        var result = await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "Se me complicó", CancellationToken.None);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal("Cancelled", result.Value!.Status);
        Assert.False(result.Value.CanCancel);

        var saved = _h.Reload(reservation.Id);
        Assert.Equal(MarketplaceCancellationActor.Owner, saved.CancelledBy);
        Assert.Equal("Se me complicó", saved.CancelReason);
        Assert.Equal(MarketplaceCreditStatus.None, saved.CreditStatus);
        Assert.All(saved.Slots, s => Assert.True(s.IsDeleted));

        var refund = Assert.Single(_h.T.NewContext().MarketplaceRefunds.ToList());
        Assert.Equal(66_000m, refund.Amount);                     // todo, comision incluida
        Assert.Equal(MarketplaceRefundOrigin.OwnerCancellation, refund.Origin);

        Assert.Equal(1, _h.Notices(_h.Pedro.Id, NotificationType.MarketplaceReservationCancelled));
        Assert.Equal(1, _h.Notices(_h.Juan.Id, NotificationType.MarketplaceReservationCancelled));
        Assert.Equal(1, _h.Notices(_h.Manager.Id, NotificationType.MarketplaceRefundPending));
    }

    [Fact]
    public async Task La_comision_que_asume_el_propietario_se_descuenta_de_su_saldo_a_favor()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.GiveCredit(_h.Juan, 10_000m);
        _h.LoginAs(_h.Juan, "Owner");

        await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "Motivo", CancellationToken.None);

        Assert.Equal(4_000m, _h.Balance(_h.Juan));                       // 10.000 - 6.000
        var db = _h.T.NewContext();
        Assert.Empty(db.MarketplaceOwnerDebts.ToList());                 // alcanzo: no hay deuda
        var lot = Assert.Single(db.OwnerCreditMovements.Where(x => x.Kind == OwnerCreditMovementKind.Generated).ToList());
        Assert.Equal(4_000m, lot.RemainingAmount);                       // el lote se consumio
        var applied = Assert.Single(db.OwnerCreditMovements.Where(x => x.Kind == OwnerCreditMovementKind.Applied).ToList());
        Assert.Equal(6_000m, applied.Amount);
        Assert.Equal(reservation.Id, applied.MarketplaceReservationId);
        // Lo descontado entra a la cuenta aparte: la gestion conserva su comision aunque se devuelva todo.
        var fee = Assert.Single(db.MarketplaceAccountMovements.Where(x => x.Kind == MarketplaceAccountMovementKind.CancellationFee).ToList());
        Assert.Equal(6_000m, fee.Amount);
        Assert.Equal(72_000m, _h.AccountBalance());                      // 66.000 del pago + 6.000 de la comision asumida
    }

    [Fact]
    public async Task Si_el_saldo_no_alcanza_lo_que_falta_queda_como_deuda_por_gestion()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.GiveCredit(_h.Juan, 2_000m);
        _h.LoginAs(_h.Juan, "Owner");

        await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "Motivo", CancellationToken.None);

        Assert.Equal(0m, _h.Balance(_h.Juan));
        var db = _h.T.NewContext();
        var debt = Assert.Single(db.MarketplaceOwnerDebts.ToList());
        Assert.Equal(4_000m, debt.Amount);                                // 6.000 - 2.000 de saldo
        Assert.Equal(0m, debt.PaidAmount);
        Assert.Null(debt.SettledAtUtc);
        Assert.Equal(_h.Juan.Id, debt.OwnerId);
        Assert.Equal(_h.Building.Id, debt.BuildingId);
        Assert.Equal(reservation.Id, debt.ReservationId);
        Assert.Equal(2_000m, db.MarketplaceAccountMovements.Single(x => x.Kind == MarketplaceAccountMovementKind.CancellationFee).Amount);
        Assert.Equal(1, _h.Events("owner_debt.created"));
    }

    [Fact]
    public async Task Sin_saldo_a_favor_toda_la_comision_queda_como_deuda_y_la_cuenta_no_registra_nada()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Juan, "Owner");

        await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "Motivo", CancellationToken.None);

        var db = _h.T.NewContext();
        Assert.Equal(6_000m, Assert.Single(db.MarketplaceOwnerDebts.ToList()).Amount);
        Assert.DoesNotContain(db.MarketplaceAccountMovements.ToList(), m => m.Kind == MarketplaceAccountMovementKind.CancellationFee);
        Assert.Empty(db.OwnerCreditMovements.ToList());
        Assert.Equal(66_000m, _h.AccountBalance());
    }

    [Fact]
    public async Task Un_saldo_anterior_sin_historial_tambien_se_descuenta_con_su_lote()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.GiveCredit(_h.Juan, 10_000m, withLot: false);       // saldo anterior al historial de lotes
        _h.LoginAs(_h.Juan, "Owner");

        await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "Motivo", CancellationToken.None);

        Assert.Equal(4_000m, _h.Balance(_h.Juan));
        var db = _h.T.NewContext();
        var legacy = Assert.Single(db.OwnerCreditMovements.Where(x => x.Kind == OwnerCreditMovementKind.Generated).ToList());
        Assert.Equal(4_000m, legacy.RemainingAmount);
        Assert.Equal(6_000m, Assert.Single(db.OwnerCreditMovements.Where(x => x.Kind == OwnerCreditMovementKind.Applied).ToList()).Amount);
    }

    [Fact]
    public async Task El_propietario_debe_dar_un_motivo()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Juan, "Owner");

        AssertError(await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "  ", CancellationToken.None), 400);
        AssertError(await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, null, CancellationToken.None), 400);
        AssertError(await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, new string('x', 501), CancellationToken.None), 400);
        Assert.Equal(MarketplaceReservationStatus.Confirmed, _h.Reload(reservation.Id).Status);
    }

    [Fact]
    public async Task Solo_el_propietario_de_la_reserva_cancela_por_su_lado()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);

        _h.LoginAs(_h.Pedro, "Resident");      // el comprador no usa el camino del propietario
        AssertError(await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "x", CancellationToken.None), 404);

        _h.LoginAs(_h.Maria, "Resident");
        AssertError(await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "x", CancellationToken.None), 404);

        _h.LoginAsStaff(_h.Manager, "BuildingManager");
        AssertError(await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "x", CancellationToken.None), 404);

        Assert.Equal(MarketplaceReservationStatus.Confirmed, _h.Reload(reservation.Id).Status);
        Assert.Empty(_h.T.NewContext().MarketplaceRefunds.ToList());
    }

    [Fact]
    public async Task El_propietario_no_cancela_una_reserva_ya_empezada_ni_una_sin_pagar()
    {
        var started = _h.SeedConfirmed(startsInHours: -1);
        var unpaid = _h.SeedConfirmed(startsInHours: 8, status: MarketplaceReservationStatus.PendingPayment, credit: MarketplaceCreditStatus.None);
        _h.LoginAs(_h.Juan, "Owner");

        AssertError(await _h.Build().Cancellations.CancelByOwnerAsync(started.Id, "x", CancellationToken.None), 409);
        AssertError(await _h.Build().Cancellations.CancelByOwnerAsync(unpaid.Id, "x", CancellationToken.None), 409);
        Assert.Empty(_h.T.NewContext().MarketplaceOwnerDebts.ToList());
    }

    [Fact]
    public async Task Si_el_propietario_cancela_dos_veces_la_comision_no_se_cobra_dos_veces()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.GiveCredit(_h.Juan, 10_000m);
        _h.LoginAs(_h.Juan, "Owner");

        Assert.True((await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "x", CancellationToken.None)).Ok);
        AssertError(await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "x", CancellationToken.None), 409);

        Assert.Equal(4_000m, _h.Balance(_h.Juan));
        Assert.Single(_h.T.NewContext().MarketplaceAccountMovements.Where(x => x.Kind == MarketplaceAccountMovementKind.CancellationFee).ToList());
    }

    [Fact]
    public async Task La_ganancia_de_una_reserva_cancelada_nunca_se_acredita()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Juan, "Owner");
        await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "x", CancellationToken.None);

        // Aunque el horario ya hubiera pasado, el proceso de fondo no acredita nada.
        var db = _h.T.Db;
        var row = db.MarketplaceReservations.Single(x => x.Id == reservation.Id);
        row.StartsAtUtc = DateTime.UtcNow.AddDays(-3);
        row.EndsAtUtc = DateTime.UtcNow.AddDays(-3).AddHours(3);
        db.SaveChanges();

        var (completed, credited) = await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(0, completed);
        Assert.Equal(0, credited);
        Assert.Equal(0m, _h.Balance(_h.Juan));
    }

    // ── Vista previa (se muestra ANTES de confirmar) ─────────────────────────

    [Fact]
    public async Task La_vista_previa_del_comprador_avisa_que_la_comision_no_se_devuelve()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Pedro, "Resident");

        var preview = (await _h.Build().Cancellations.PreviewAsync(reservation.Id, CancellationToken.None)).Value!;

        Assert.Equal("Buyer", preview.Role);
        Assert.True(preview.CanCancel);
        Assert.False(preview.BeforePayment);
        Assert.False(preview.RequiresReason);
        Assert.Equal(60_000m, preview.RefundAmount);
        Assert.Equal(6_000m, preview.CommissionAmount);
    }

    [Fact]
    public async Task La_vista_previa_del_propietario_muestra_la_devolucion_total_y_la_comision_a_su_cargo()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Juan, "Owner");

        var preview = (await _h.Build().Cancellations.PreviewAsync(reservation.Id, CancellationToken.None)).Value!;

        Assert.Equal("Owner", preview.Role);
        Assert.True(preview.CanCancel);
        Assert.True(preview.RequiresReason);
        Assert.Equal(66_000m, preview.RefundAmount);
        Assert.Equal(6_000m, preview.CommissionAmount);
    }

    [Fact]
    public async Task La_vista_previa_de_una_reserva_sin_pagar_no_cobra_nada_y_la_de_una_empezada_explica_por_que_no()
    {
        var unpaid = _h.SeedConfirmed(startsInHours: 8, status: MarketplaceReservationStatus.PendingPayment, credit: MarketplaceCreditStatus.None);
        var started = _h.SeedConfirmed(startsInHours: -1);
        _h.LoginAs(_h.Pedro, "Resident");

        var before = (await _h.Build().Cancellations.PreviewAsync(unpaid.Id, CancellationToken.None)).Value!;
        Assert.True(before.CanCancel);
        Assert.True(before.BeforePayment);
        Assert.Equal(0m, before.RefundAmount);

        var after = (await _h.Build().Cancellations.PreviewAsync(started.Id, CancellationToken.None)).Value!;
        Assert.False(after.CanCancel);
        Assert.Contains("empezó", after.BlockedReason);
    }

    [Fact]
    public async Task La_vista_previa_no_se_la_da_a_un_extrano()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Maria, "Resident");

        AssertError(await _h.Build().Cancellations.PreviewAsync(reservation.Id, CancellationToken.None), 404);
    }

    // ── Lo que ve cada parte ─────────────────────────────────────────────────

    [Fact]
    public async Task Mis_reservas_dicen_si_se_puede_cancelar_y_como_va_el_reembolso()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Pedro, "Resident");

        var before = (await _h.Build().Reservations.GetMineAsync(_h.Building.Id, CancellationToken.None)).Value!.Single();
        Assert.True(before.CanCancel);
        Assert.Null(before.RefundStatus);

        await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None);

        var after = (await _h.Build().Reservations.GetMineAsync(_h.Building.Id, CancellationToken.None)).Value!.Single();
        Assert.False(after.CanCancel);
        Assert.Equal("Pending", after.RefundStatus);
        Assert.Equal(60_000m, after.RefundAmount);
        Assert.NotNull(after.RefundDueAtUtc);
        Assert.InRange((after.RefundDueAtUtc!.Value - DateTime.UtcNow).TotalHours, 71.9, 72.1);   // "hasta en 72 horas"
    }

    [Fact]
    public async Task El_propietario_ve_quien_reservo_solo_con_nombre_y_unidad_y_lo_que_va_a_recibir()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.SeedConfirmed(startsInHours: 9, status: MarketplaceReservationStatus.PendingPayment, credit: MarketplaceCreditStatus.None);   // aun sin pagar: no se ve
        _h.LoginAs(_h.Juan, "Owner");

        var list = (await _h.Build().Reservations.GetOnMyListingsAsync(_h.Building.Id, CancellationToken.None)).Value!;

        var row = Assert.Single(list);
        Assert.Equal(reservation.Id, row.Id);
        Assert.Contains("Pedro", row.BuyerName);
        Assert.Equal("101", row.BuyerUnits);
        Assert.Equal(60_000m, row.OwnerNetAmount);       // sin comision
        Assert.True(row.CanCancel);
        Assert.Equal("Pending", row.CreditStatus);
    }

    [Fact]
    public async Task El_propietario_no_ve_reservas_de_publicaciones_ajenas_y_las_canceladas_antes_de_pagar_no_aparecen()
    {
        var mine = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Pedro, "Resident");
        var unpaid = _h.SeedConfirmed(startsInHours: 9, status: MarketplaceReservationStatus.PendingPayment, credit: MarketplaceCreditStatus.None);
        await _h.Build().Cancellations.CancelByBuyerAsync(unpaid.Id, null, CancellationToken.None);   // se cancelo sin haber pagado

        _h.LoginAs(_h.Maria, "Resident");                                                              // otra vecina: no es la duena
        Assert.Empty((await _h.Build().Reservations.GetOnMyListingsAsync(_h.Building.Id, CancellationToken.None)).Value!);

        _h.LoginAs(_h.Juan, "Owner");
        Assert.Equal(mine.Id, Assert.Single((await _h.Build().Reservations.GetOnMyListingsAsync(_h.Building.Id, CancellationToken.None)).Value!).Id);
    }
}
