using Condo.Api.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;

namespace Condo.Tests.Marketplace;

/// <summary>
/// Deuda por gestion del propietario que cancela: se descuenta automaticamente de su proxima acreditacion del marketplace en el
/// mismo edificio, la mas antigua primero, sin tocar las expensas. Cada paso es idempotente.
/// </summary>
public class MarketplaceOwnerDebtTests : IDisposable
{
    private readonly Phase7Harness _h = new();

    public void Dispose() => _h.Dispose();

    private MarketplaceOwnerDebt AddDebt(decimal amount, Building? building = null, DateTime? createdAt = null, decimal paid = 0m)
    {
        // La deuda cuelga de una reserva cualquiera (una por reserva).
        var origin = _h.SeedConfirmed(startsInHours: 50 + _h.T.NewContext().MarketplaceOwnerDebts.Count() * 4);
        var debt = new MarketplaceOwnerDebt
        {
            CompanyId = _h.Company.Id,
            BuildingId = (building ?? _h.Building).Id,
            OwnerId = _h.Juan.Id,
            ReservationId = origin.Id,
            Amount = amount,
            PaidAmount = paid,
            Reason = "Cancelación"
        };
        if (createdAt.HasValue)
        {
            debt.CreatedAtUtc = createdAt.Value;
        }

        _h.T.Db.MarketplaceOwnerDebts.Add(debt);
        _h.T.Db.SaveChanges();
        return debt;
    }

    // Reserva finalizada hace mas de 24 horas, lista para acreditar (neto 60.000).
    private MarketplaceReservation Due() =>
        _h.SeedConfirmed(startsInHours: -30, status: MarketplaceReservationStatus.Completed);

    private MarketplaceOwnerDebt Debt(Guid id) => _h.T.NewContext().MarketplaceOwnerDebts.Single(x => x.Id == id);

    [Fact]
    public async Task La_proxima_acreditacion_descuenta_la_deuda_y_el_saldo_recibe_solo_el_resto()
    {
        var debt = AddDebt(4_000m);
        var reservation = Due();

        var (_, credited) = await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(1, credited);
        Assert.Equal(56_000m, _h.Balance(_h.Juan));                                   // 60.000 - 4.000
        var saved = Debt(debt.Id);
        Assert.Equal(4_000m, saved.PaidAmount);
        Assert.NotNull(saved.SettledAtUtc);

        var db = _h.T.NewContext();
        var lot = db.OwnerCreditMovements.Single(x => x.MarketplaceReservationId == reservation.Id && x.Kind == OwnerCreditMovementKind.Generated);
        Assert.Equal(56_000m, lot.Amount);
        var movement = db.MarketplaceAccountMovements.Single(x => x.ReservationId == reservation.Id && x.Kind == MarketplaceAccountMovementKind.OwnerCredit);
        Assert.Equal(-56_000m, movement.Amount);                                       // lo descontado queda en la cuenta
        Assert.Contains("deuda por gestión", movement.Concept);
        Assert.Equal(1, _h.Events("owner_debt.deducted"));
        Assert.Contains("deuda por gestión", db.Notifications.Single(x => x.RecipientId == _h.Juan.Id && x.Type == NotificationType.MarketplaceCreditApplied).Body);
    }

    [Fact]
    public async Task Si_la_deuda_supera_la_ganancia_no_se_acredita_saldo_y_el_resto_sigue_pendiente()
    {
        var debt = AddDebt(70_000m);
        var reservation = Due();

        await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(0m, _h.Balance(_h.Juan));
        var saved = Debt(debt.Id);
        Assert.Equal(60_000m, saved.PaidAmount);
        Assert.Null(saved.SettledAtUtc);
        Assert.Equal(10_000m, saved.Amount - saved.PaidAmount);

        var db = _h.T.NewContext();
        Assert.Equal(MarketplaceCreditStatus.Credited, db.MarketplaceReservations.Single(x => x.Id == reservation.Id).CreditStatus);
        Assert.DoesNotContain(db.OwnerCreditMovements.ToList(), m => m.MarketplaceReservationId == reservation.Id);
        Assert.DoesNotContain(db.MarketplaceAccountMovements.ToList(), m => m.ReservationId == reservation.Id && m.Kind == MarketplaceAccountMovementKind.OwnerCredit);
        Assert.Contains("no se acreditó saldo", db.Notifications.Single(x => x.RecipientId == _h.Juan.Id && x.Type == NotificationType.MarketplaceCreditApplied).Body);
    }

    [Fact]
    public async Task Las_deudas_se_descuentan_de_la_mas_antigua_a_la_mas_nueva()
    {
        var older = AddDebt(5_000m, createdAt: DateTime.UtcNow.AddDays(-10));
        var newer = AddDebt(5_000m, createdAt: DateTime.UtcNow.AddDays(-2));
        Due();

        // Con una ganancia mas chica que las dos deudas juntas (neto 7.000), se paga la antigua entera y parte de la nueva.
        var row = _h.T.Db.MarketplaceReservations.Single(x => x.Status == MarketplaceReservationStatus.Completed);
        row.OwnerNetAmount = 7_000m;
        _h.T.Db.SaveChanges();

        await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(5_000m, Debt(older.Id).PaidAmount);
        Assert.NotNull(Debt(older.Id).SettledAtUtc);
        Assert.Equal(2_000m, Debt(newer.Id).PaidAmount);
        Assert.Null(Debt(newer.Id).SettledAtUtc);
        Assert.Equal(0m, _h.Balance(_h.Juan));
    }

    [Fact]
    public async Task Una_deuda_de_otro_edificio_no_se_descuenta_aca()
    {
        var debt = AddDebt(4_000m, building: _h.OtherBuilding);
        Due();

        await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(60_000m, _h.Balance(_h.Juan));
        Assert.Equal(0m, Debt(debt.Id).PaidAmount);
    }

    [Fact]
    public async Task Una_deuda_ya_saldada_no_se_vuelve_a_descontar()
    {
        var debt = AddDebt(4_000m, paid: 4_000m);
        debt.SettledAtUtc = DateTime.UtcNow.AddDays(-1);
        _h.T.Db.SaveChanges();
        Due();

        await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(60_000m, _h.Balance(_h.Juan));
    }

    [Fact]
    public async Task Procesar_dos_veces_no_descuenta_dos_veces()
    {
        var debt = AddDebt(4_000m);
        Due();

        await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);
        var (_, second) = await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(0, second);
        Assert.Equal(56_000m, _h.Balance(_h.Juan));
        Assert.Equal(4_000m, Debt(debt.Id).PaidAmount);
    }

    [Fact]
    public async Task La_deuda_de_un_propietario_no_se_descuenta_de_otro()
    {
        var debt = AddDebt(4_000m);
        var other = _h.T.AddUser(_h.Company, name: "Ana");
        _h.T.AddOwner(_h.T.AddUnit(_h.Building, "401"), other, primary: true);
        var reservation = Due();
        reservation.OwnerId = other.Id;       // la reserva es de OTRO propietario
        _h.T.Db.SaveChanges();

        await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(60_000m, _h.Balance(other));
        Assert.Equal(0m, Debt(debt.Id).PaidAmount);
    }

    [Fact]
    public async Task De_punta_a_punta_el_propietario_cancela_sin_saldo_y_la_siguiente_reserva_paga_la_comision()
    {
        var cancelled = _h.SeedConfirmed(startsInHours: 5);
        _h.LoginAs(_h.Juan, "Owner");
        Assert.True((await _h.Build().Cancellations.CancelByOwnerAsync(cancelled.Id, "Motivo", CancellationToken.None)).Ok);
        Assert.Equal(6_000m, Assert.Single(_h.T.NewContext().MarketplaceOwnerDebts.ToList()).Amount);

        Due();
        await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(54_000m, _h.Balance(_h.Juan));                  // 60.000 - 6.000 de comision asumida
        var debt = Assert.Single(_h.T.NewContext().MarketplaceOwnerDebts.ToList());
        Assert.NotNull(debt.SettledAtUtc);
    }

    [Fact]
    public async Task Una_acreditacion_que_se_aplico_a_la_deuda_no_se_puede_revertir_porque_no_genero_saldo()
    {
        AddDebt(70_000m);
        var reservation = Due();
        await _h.Build().Credits.ProcessDueAsync(CancellationToken.None);
        _h.LoginAs(_h.SuperAdmin, "SuperAdmin");
        _h.Tenant.CompanyId = null;

        var result = await _h.Build().Credits.ReverseCreditAsync(reservation.Id, "Prueba", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Contains("deuda por gestión", result.Error.Message);
    }
}
