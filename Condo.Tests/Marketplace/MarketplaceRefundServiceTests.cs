using Condo.Api.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;

namespace Condo.Tests.Marketplace;

/// <summary>
/// Reembolsos pendientes: el Encargado devuelve el dinero fuera del sistema y lo marca "devuelto" (una sola vez); la cuenta aparte
/// registra la salida recien ahi. Hay una alerta a las 72 horas y una lista de deudas por gestion visible solo para el personal.
/// </summary>
public class MarketplaceRefundServiceTests : IDisposable
{
    private readonly Phase7Harness _h = new();

    public void Dispose() => _h.Dispose();

    private static void AssertError<T>(MarketplaceResult<T> result, int status)
    {
        Assert.False(result.Ok);
        Assert.Equal(status, result.Error!.StatusCode);
    }

    // Pedro cancela una reserva pagada y devuelve el reembolso pendiente que quedo (base: 60.000).
    private async Task<MarketplaceRefund> BuyerCancels()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Pedro, "Resident");
        Assert.True((await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None)).Ok);
        return _h.T.NewContext().MarketplaceRefunds.Single(x => x.ReservationId == reservation.Id);
    }

    // ── Marcar como devuelto ─────────────────────────────────────────────────

    [Fact]
    public async Task El_encargado_marca_el_reembolso_como_devuelto_y_la_cuenta_registra_la_salida()
    {
        var refund = await BuyerCancels();
        _h.LoginAsStaff(_h.Manager, "BuildingManager");

        var result = await _h.Build().Refunds.MarkReturnedAsync(refund.Id, CancellationToken.None);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal("Returned", result.Value!.Status);
        Assert.Equal(_h.Manager.FullName, result.Value.ReturnedByName);
        Assert.NotNull(result.Value.ReturnedAtUtc);

        var db = _h.T.NewContext();
        var saved = db.MarketplaceRefunds.Single(x => x.Id == refund.Id);
        Assert.Equal(MarketplaceRefundStatus.Returned, saved.Status);
        Assert.Equal(_h.Manager.Id, saved.ReturnedByUserId);

        var movement = Assert.Single(db.MarketplaceAccountMovements.Where(x => x.Kind == MarketplaceAccountMovementKind.RefundOut).ToList());
        Assert.Equal(-60_000m, movement.Amount);
        Assert.Equal(refund.ReservationId, movement.ReservationId);
        Assert.Equal(_h.Manager.Id, movement.CreatedByUserId);
        Assert.Equal(6_000m, _h.AccountBalance());              // queda la comision de la gestion

        Assert.Equal(1, _h.Notices(_h.Pedro.Id, NotificationType.MarketplaceRefundReturned));
        Assert.Equal(1, _h.Events("refund.returned"));
    }

    [Fact]
    public async Task Marcar_dos_veces_no_duplica_la_salida_de_la_cuenta()
    {
        var refund = await BuyerCancels();
        _h.LoginAsStaff(_h.Manager, "BuildingManager");

        Assert.True((await _h.Build().Refunds.MarkReturnedAsync(refund.Id, CancellationToken.None)).Ok);
        AssertError(await _h.Build().Refunds.MarkReturnedAsync(refund.Id, CancellationToken.None), 409);

        Assert.Single(_h.T.NewContext().MarketplaceAccountMovements.Where(x => x.Kind == MarketplaceAccountMovementKind.RefundOut).ToList());
        Assert.Equal(1, _h.Notices(_h.Pedro.Id, NotificationType.MarketplaceRefundReturned));
    }

    [Fact]
    public async Task El_operador_y_el_administrador_de_empresa_tambien_devuelven()
    {
        var first = await BuyerCancels();
        _h.LoginAsStaff(_h.Operator, "CompanyOperator");
        Assert.True((await _h.Build().Refunds.MarkReturnedAsync(first.Id, CancellationToken.None)).Ok);

        var second = await BuyerCancels();
        _h.LoginAsStaff(_h.CompanyAdmin, "CompanyAdmin");
        _h.Access.Buildings.Add(_h.Building.Id);
        Assert.True((await _h.Build().Refunds.MarkReturnedAsync(second.Id, CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task Solo_el_personal_del_edificio_marca_devuelto()
    {
        var refund = await BuyerCancels();

        _h.LoginAs(_h.Pedro, "Resident");            // el propio comprador no se marca su devolucion
        AssertError(await _h.Build().Refunds.MarkReturnedAsync(refund.Id, CancellationToken.None), 404);

        _h.LoginAs(_h.Juan, "Owner");
        AssertError(await _h.Build().Refunds.MarkReturnedAsync(refund.Id, CancellationToken.None), 404);

        _h.LoginAsStaff(_h.OtherManager, "BuildingManager");   // Encargado de otro edificio
        AssertError(await _h.Build().Refunds.MarkReturnedAsync(refund.Id, CancellationToken.None), 404);

        Assert.Equal(MarketplaceRefundStatus.Pending, _h.T.NewContext().MarketplaceRefunds.Single(x => x.Id == refund.Id).Status);
        Assert.Empty(_h.T.NewContext().MarketplaceAccountMovements.Where(x => x.Kind == MarketplaceAccountMovementKind.RefundOut).ToList());
    }

    [Fact]
    public async Task Un_reembolso_inexistente_responde_404()
    {
        _h.LoginAsStaff(_h.Manager, "BuildingManager");

        AssertError(await _h.Build().Refunds.MarkReturnedAsync(Guid.NewGuid(), CancellationToken.None), 404);
    }

    // ── Lista del Encargado ──────────────────────────────────────────────────

    [Fact]
    public async Task La_lista_muestra_primero_los_pendientes_del_mas_antiguo_al_mas_nuevo_y_marca_los_vencidos()
    {
        var first = await BuyerCancels();
        var second = await BuyerCancels();
        // El primero se creo hace 80 horas: ya paso el maximo de 72.
        var db = _h.T.Db;
        db.MarketplaceRefunds.Single(x => x.Id == first.Id).CreatedAtUtc = DateTime.UtcNow.AddHours(-80);
        db.SaveChanges();
        _h.LoginAsStaff(_h.Manager, "BuildingManager");

        var list = (await _h.Build().Refunds.GetRefundsAsync(_h.Building.Id, includeReturned: false, CancellationToken.None)).Value!;

        Assert.Equal([first.Id, second.Id], list.Select(x => x.Id).ToArray());
        Assert.True(list[0].Overdue);
        Assert.False(list[1].Overdue);
        Assert.Equal("Pedro Prueba", list[0].BuyerName);
        Assert.Equal(60_000m, list[0].Amount);
        Assert.Equal("BuyerCancellation", list[0].Origin);
    }

    [Fact]
    public async Task La_lista_incluye_los_devueltos_solo_si_se_pide()
    {
        var pending = await BuyerCancels();
        var returned = await BuyerCancels();
        _h.LoginAsStaff(_h.Manager, "BuildingManager");
        await _h.Build().Refunds.MarkReturnedAsync(returned.Id, CancellationToken.None);

        var only = (await _h.Build().Refunds.GetRefundsAsync(_h.Building.Id, false, CancellationToken.None)).Value!;
        var all = (await _h.Build().Refunds.GetRefundsAsync(_h.Building.Id, true, CancellationToken.None)).Value!;

        Assert.Equal(pending.Id, Assert.Single(only).Id);
        Assert.Equal([pending.Id, returned.Id], all.Select(x => x.Id).ToArray());
        Assert.Equal("Returned", all[1].Status);
    }

    [Fact]
    public async Task La_lista_de_reembolsos_no_se_ve_desde_otro_edificio_ni_como_usuario_final()
    {
        await BuyerCancels();

        _h.LoginAsStaff(_h.OtherManager, "BuildingManager");
        AssertError(await _h.Build().Refunds.GetRefundsAsync(_h.Building.Id, false, CancellationToken.None), 404);

        _h.LoginAs(_h.Pedro, "Resident");
        AssertError(await _h.Build().Refunds.GetRefundsAsync(_h.Building.Id, false, CancellationToken.None), 404);

        _h.LoginAs(_h.Juan, "Owner");
        AssertError(await _h.Build().Refunds.GetRefundsAsync(_h.Building.Id, false, CancellationToken.None), 404);
    }

    // ── Alerta a las 72 horas ────────────────────────────────────────────────

    [Fact]
    public async Task Pasadas_72_horas_sin_devolver_el_encargado_recibe_una_sola_alerta()
    {
        var refund = await BuyerCancels();
        var db = _h.T.Db;
        db.MarketplaceRefunds.Single(x => x.Id == refund.Id).CreatedAtUtc = DateTime.UtcNow.AddHours(-73);
        db.SaveChanges();

        Assert.Equal(1, await _h.Build().Refunds.SendOverdueAlertsAsync(CancellationToken.None));
        Assert.Equal(0, await _h.Build().Refunds.SendOverdueAlertsAsync(CancellationToken.None));     // una sola vez

        Assert.Equal(1, _h.Notices(_h.Manager.Id, NotificationType.MarketplaceRefundOverdue));
        Assert.Equal(1, _h.Notices(_h.Operator.Id, NotificationType.MarketplaceRefundOverdue));
        Assert.Equal(0, _h.Notices(_h.OtherManager.Id, NotificationType.MarketplaceRefundOverdue));
        Assert.NotNull(_h.T.NewContext().MarketplaceRefunds.Single(x => x.Id == refund.Id).OverdueAlertSentAtUtc);
        Assert.Equal(1, _h.Events("refund.overdue_alert"));
    }

    [Fact]
    public async Task No_hay_alerta_antes_de_las_72_horas_ni_si_ya_se_devolvio()
    {
        var fresh = await BuyerCancels();
        var returned = await BuyerCancels();
        var db = _h.T.Db;
        db.MarketplaceRefunds.Single(x => x.Id == fresh.Id).CreatedAtUtc = DateTime.UtcNow.AddHours(-71);
        db.MarketplaceRefunds.Single(x => x.Id == returned.Id).CreatedAtUtc = DateTime.UtcNow.AddHours(-100);
        db.SaveChanges();
        _h.LoginAsStaff(_h.Manager, "BuildingManager");
        await _h.Build().Refunds.MarkReturnedAsync(returned.Id, CancellationToken.None);

        Assert.Equal(0, await _h.Build().Refunds.SendOverdueAlertsAsync(CancellationToken.None));
        Assert.Equal(0, _h.Notices(_h.Manager.Id, NotificationType.MarketplaceRefundOverdue));
    }

    // ── Deudas por gestion ───────────────────────────────────────────────────

    [Fact]
    public async Task El_encargado_ve_las_deudas_por_gestion_pendientes_de_su_edificio()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Juan, "Owner");
        await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "Motivo", CancellationToken.None);
        _h.LoginAsStaff(_h.Manager, "BuildingManager");

        var debts = (await _h.Build().Refunds.GetOwnerDebtsAsync(_h.Building.Id, CancellationToken.None)).Value!;

        var debt = Assert.Single(debts);
        Assert.Equal("Juan Prueba", debt.OwnerName);
        Assert.Equal(6_000m, debt.Amount);
        Assert.Equal(6_000m, debt.Remaining);
        Assert.Equal(reservation.Reference, debt.Reference);
    }

    [Fact]
    public async Task Las_deudas_por_gestion_no_las_ve_un_usuario_final_ni_otro_edificio()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Juan, "Owner");
        await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "Motivo", CancellationToken.None);

        AssertError(await _h.Build().Refunds.GetOwnerDebtsAsync(_h.Building.Id, CancellationToken.None), 404);   // ni el propio deudor

        _h.LoginAsStaff(_h.OtherManager, "BuildingManager");
        AssertError(await _h.Build().Refunds.GetOwnerDebtsAsync(_h.Building.Id, CancellationToken.None), 404);
    }

    // ── La cuenta aparte ─────────────────────────────────────────────────────

    [Fact]
    public async Task Lo_que_falta_devolver_a_compradores_no_cuenta_como_ganancia_de_la_gestion()
    {
        var refund = await BuyerCancels();
        _h.LoginAsStaff(_h.Manager, "BuildingManager");

        var before = (await _h.Build().Account.GetStatementAsync(_h.Building.Id, null, null, CancellationToken.None)).Value!.Summary;
        Assert.Equal(66_000m, before.CurrentBalance);
        Assert.Equal(60_000m, before.PendingRefunds);
        Assert.Equal(6_000m, before.ManagementGain);                 // solo la comision que no se devuelve

        await _h.Build().Refunds.MarkReturnedAsync(refund.Id, CancellationToken.None);

        var after = (await _h.Build().Account.GetStatementAsync(_h.Building.Id, null, null, CancellationToken.None)).Value!.Summary;
        Assert.Equal(6_000m, after.CurrentBalance);
        Assert.Equal(0m, after.PendingRefunds);
        Assert.Equal(6_000m, after.ManagementGain);
        Assert.Equal(60_000m, after.TotalRefunds);
    }

    [Fact]
    public async Task Si_cancela_el_propietario_la_gestion_conserva_su_comision_porque_la_asume_el_propietario()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.GiveCredit(_h.Juan, 10_000m);
        _h.LoginAs(_h.Juan, "Owner");
        await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "Motivo", CancellationToken.None);
        var refund = _h.T.NewContext().MarketplaceRefunds.Single();
        _h.LoginAsStaff(_h.Manager, "BuildingManager");
        await _h.Build().Refunds.MarkReturnedAsync(refund.Id, CancellationToken.None);

        var summary = (await _h.Build().Account.GetStatementAsync(_h.Building.Id, null, null, CancellationToken.None)).Value!.Summary;

        Assert.Equal(6_000m, summary.CurrentBalance);                // 66.000 + 6.000 (comision asumida) - 66.000 (devuelto)
        Assert.Equal(6_000m, summary.TotalCancellationFees);
        Assert.Equal(66_000m, summary.TotalRefunds);
        Assert.Equal(6_000m, summary.ManagementGain);
    }

    [Fact]
    public async Task La_deuda_por_gestion_pendiente_se_informa_en_el_resumen_de_la_cuenta()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Juan, "Owner");
        await _h.Build().Cancellations.CancelByOwnerAsync(reservation.Id, "Motivo", CancellationToken.None);
        _h.LoginAsStaff(_h.Manager, "BuildingManager");

        var summary = (await _h.Build().Account.GetStatementAsync(_h.Building.Id, null, null, CancellationToken.None)).Value!.Summary;

        Assert.Equal(6_000m, summary.OwnerDebtsPending);
    }
}
