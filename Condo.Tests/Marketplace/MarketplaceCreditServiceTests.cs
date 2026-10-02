using Condo.Api.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condo.Tests.Marketplace;

public class MarketplaceCreditServiceTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly FakeTenantContext _tenant = new();
    private readonly FakeAccessScope _access;

    private readonly Company _company;
    private readonly Building _building;
    private readonly Unit _unit;
    private readonly ApplicationUser _juan;      // propietario principal que publica
    private readonly ApplicationUser _pedro;     // comprador
    private readonly ApplicationUser _superAdmin;
    private readonly ApplicationUser _companyAdmin;
    private readonly MarketplaceListing _listing;
    private int _ref;

    public MarketplaceCreditServiceTests()
    {
        _access = new FakeAccessScope(_tenant);
        _company = _t.AddCompany();
        _companyAdmin = _t.AddUser(_company, UserRole.CompanyAdmin);
        _building = _t.AddBuilding(_company);
        _unit = _t.AddUnit(_building, "302");
        _t.AssignPlan(_building, _companyAdmin, includesMarketplace: true);
        _t.EnableMarketplace(_building);
        _juan = _t.AddUser(_company, name: "Juan");
        _t.AddOwner(_unit, _juan, primary: true);
        _pedro = _t.AddUser(_company, UserRole.Resident, "Pedro");
        _t.AddResident(_t.AddUnit(_building, "101"), _pedro);
        _superAdmin = _t.AddUser(null, UserRole.SuperAdmin, "Super");
        _listing = _t.AddListing(_building, _unit, _juan, 20_000m);
    }

    public void Dispose() => _t.Dispose();

    private MarketplaceCreditService Credits()
    {
        var db = (CondoDbContext)_t.NewContext();
        var audit = new MarketplaceAudit(db, _tenant, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var push = new PushDispatcher(db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance);
        return new MarketplaceCreditService(db, _tenant, audit, push);
    }

    private void LoginAsSuperAdmin()
    {
        _tenant.UserId = _superAdmin.Id;
        _tenant.Role = "SuperAdmin";
        _tenant.CompanyId = null;
    }

    // Reserva ya hecha en la base, sin pasar por el flujo de pago: lo que interesa aca es lo que pasa DESPUES de confirmarla.
    private MarketplaceReservation Seed(
        MarketplaceReservationStatus status, MarketplaceCreditStatus credit, DateTime endsAt,
        ApplicationUser? owner = null, decimal net = 60_000m)
    {
        var reservation = _t.NewReservation(_listing, _pedro, $"MP-{++_ref:D8}", status);
        reservation.OwnerId = (owner ?? _juan).Id;
        reservation.Hours = 3;
        reservation.EndsAtUtc = endsAt;
        reservation.StartsAtUtc = endsAt.AddHours(-3);
        reservation.HourlyPrice = net / 3;
        reservation.BaseAmount = net;
        reservation.OwnerNetAmount = net;
        reservation.CommissionAmount = net / 10;
        reservation.TotalAmount = net + net / 10;
        reservation.CreditStatus = credit;
        reservation.ExpiresAtUtc = null;
        _t.Db.MarketplaceReservations.Add(reservation);
        _t.Db.SaveChanges();
        return reservation;
    }

    private MarketplaceReservation SeedDueForCredit(decimal net = 60_000m, ApplicationUser? owner = null) =>
        Seed(MarketplaceReservationStatus.Completed, MarketplaceCreditStatus.Pending, DateTime.UtcNow.AddHours(-25), owner, net);

    private decimal OwnerBalance(Guid ownerId) =>
        _t.NewContext().OwnerCredits.Where(x => x.OwnerId == ownerId && x.CompanyId == _company.Id).AsEnumerable().Sum(x => x.Amount);

    // ── Reserva terminada -> finalizada ──────────────────────────────────────

    [Fact]
    public async Task Al_terminar_el_horario_la_reserva_confirmada_pasa_a_finalizada_y_queda_auditado()
    {
        var finished = Seed(MarketplaceReservationStatus.Confirmed, MarketplaceCreditStatus.Pending, DateTime.UtcNow.AddHours(-1));
        var running = Seed(MarketplaceReservationStatus.Confirmed, MarketplaceCreditStatus.Pending, DateTime.UtcNow.AddHours(2));
        var inReview = Seed(MarketplaceReservationStatus.InReview, MarketplaceCreditStatus.None, DateTime.UtcNow.AddHours(-5));

        Assert.Equal(1, await Credits().CompleteFinishedAsync(CancellationToken.None));

        var db = _t.NewContext();
        Assert.Equal(MarketplaceReservationStatus.Completed, db.MarketplaceReservations.Single(x => x.Id == finished.Id).Status);
        Assert.Equal(MarketplaceReservationStatus.Confirmed, db.MarketplaceReservations.Single(x => x.Id == running.Id).Status);
        Assert.Equal(MarketplaceReservationStatus.InReview, db.MarketplaceReservations.Single(x => x.Id == inReview.Id).Status);
        var evt = Assert.Single(db.MarketplaceEvents.Where(x => x.Action == "reservation.completed").ToList());
        Assert.Equal(finished.Id, evt.EntityId);
        Assert.Null(evt.UserId);
        Assert.Equal(0, await Credits().CompleteFinishedAsync(CancellationToken.None));
    }

    // ── Acreditacion al saldo ────────────────────────────────────────────────

    [Fact]
    public async Task Pasadas_24_horas_el_neto_del_propietario_se_acredita_a_su_saldo_a_favor()
    {
        var reservation = SeedDueForCredit();

        Assert.Equal(1, await Credits().CreditDueAsync(CancellationToken.None));

        var db = _t.NewContext();
        Assert.Equal(60_000m, OwnerBalance(_juan.Id));
        var lot = db.OwnerCreditMovements.Single();
        Assert.Equal(OwnerCreditMovementKind.Generated, lot.Kind);
        Assert.Equal(60_000m, lot.Amount);
        Assert.Equal(60_000m, lot.RemainingAmount);
        Assert.Equal(reservation.Reference, lot.SourceReference);
        Assert.Equal(reservation.Id, lot.MarketplaceReservationId);
        Assert.Equal(_juan.Id, lot.OwnerId);
        Assert.Contains("Marketplace", lot.Description);

        var saved = db.MarketplaceReservations.Single(x => x.Id == reservation.Id);
        Assert.Equal(MarketplaceCreditStatus.Credited, saved.CreditStatus);
        Assert.NotNull(saved.CreditedAtUtc);
    }

    [Fact]
    public async Task La_acreditacion_registra_la_salida_en_la_cuenta_aparte_el_aviso_y_la_auditoria()
    {
        var reservation = SeedDueForCredit();
        await Credits().CreditDueAsync(CancellationToken.None);

        var db = _t.NewContext();
        var movement = db.MarketplaceAccountMovements.Single();
        Assert.Equal(MarketplaceAccountMovementKind.OwnerCredit, movement.Kind);
        Assert.Equal(-60_000m, movement.Amount);
        Assert.Equal(reservation.Id, movement.ReservationId);
        Assert.Equal(_building.Id, movement.BuildingId);
        Assert.Null(movement.CreatedByUserId);

        var notice = db.Notifications.Single(x => x.Type == NotificationType.MarketplaceCreditApplied);
        Assert.Equal(_juan.Id, notice.RecipientId);
        Assert.Contains("60.000", notice.Body);

        var applied = db.MarketplaceEvents.Single(x => x.Action == "credit.applied");
        Assert.Null(applied.UserId);
        Assert.Equal("Pending", applied.FromStatus);
        Assert.Equal("Credited", applied.ToStatus);
        Assert.Contains("60000", applied.DataJson);
    }

    [Fact]
    public async Task Con_saldo_anterior_el_nuevo_lote_se_suma_como_en_el_ejemplo_80000_mas_60000()
    {
        _t.Db.OwnerCredits.Add(new OwnerCredit { CompanyId = _company.Id, OwnerId = _juan.Id, Amount = 80_000m });
        _t.Db.OwnerCreditMovements.Add(new OwnerCreditMovement
        {
            CompanyId = _company.Id, OwnerId = _juan.Id, Kind = OwnerCreditMovementKind.Generated,
            Amount = 80_000m, RemainingAmount = 80_000m, SourceReference = "PAY-2026-000001", Description = "Saldo anterior"
        });
        _t.Db.SaveChanges();
        SeedDueForCredit();

        await Credits().CreditDueAsync(CancellationToken.None);

        Assert.Equal(140_000m, OwnerBalance(_juan.Id));
        Assert.Equal(2, _t.NewContext().OwnerCreditMovements.Count());
    }

    [Fact]
    public async Task No_se_acredita_antes_de_las_24_horas_del_fin_de_la_reserva()
    {
        var reservation = Seed(MarketplaceReservationStatus.Completed, MarketplaceCreditStatus.Pending, DateTime.UtcNow.AddHours(-23));

        Assert.Equal(0, await Credits().CreditDueAsync(CancellationToken.None));

        Assert.Equal(0m, OwnerBalance(_juan.Id));
        Assert.Equal(MarketplaceCreditStatus.Pending, _t.NewContext().MarketplaceReservations.Single(x => x.Id == reservation.Id).CreditStatus);
    }

    [Fact]
    public async Task Una_reserva_que_termino_hace_mas_de_un_dia_se_finaliza_y_acredita_en_la_misma_pasada()
    {
        Seed(MarketplaceReservationStatus.Confirmed, MarketplaceCreditStatus.Pending, DateTime.UtcNow.AddHours(-30));

        var (completed, credited) = await Credits().ProcessDueAsync(CancellationToken.None);

        Assert.Equal(1, completed);
        Assert.Equal(1, credited);
        Assert.Equal(60_000m, OwnerBalance(_juan.Id));
    }

    [Fact]
    public async Task Acreditar_dos_veces_la_misma_reserva_no_duplica_el_saldo()
    {
        SeedDueForCredit();

        Assert.Equal(1, await Credits().CreditDueAsync(CancellationToken.None));
        Assert.Equal(0, await Credits().CreditDueAsync(CancellationToken.None));
        Assert.Equal((0, 0), await Credits().ProcessDueAsync(CancellationToken.None));

        var db = _t.NewContext();
        Assert.Equal(60_000m, OwnerBalance(_juan.Id));
        Assert.Single(db.OwnerCreditMovements.ToList());
        Assert.Single(db.MarketplaceAccountMovements.ToList());
        Assert.Single(db.Notifications.Where(x => x.Type == NotificationType.MarketplaceCreditApplied).ToList());
    }

    [Fact]
    public async Task Dos_ejecuciones_a_la_vez_sobre_la_misma_reserva_acreditan_una_sola_vez()
    {
        SeedDueForCredit();

        // Cada servicio tiene su propio contexto, como dos procesos distintos que leyeron la reserva pendiente.
        var first = Credits();
        var second = Credits();
        var results = new[] { await first.CreditDueAsync(CancellationToken.None), await second.CreditDueAsync(CancellationToken.None) };

        Assert.Equal(1, results.Sum());
        Assert.Equal(60_000m, OwnerBalance(_juan.Id));
    }

    [Fact]
    public async Task Una_reserva_con_la_acreditacion_retenida_o_cancelada_no_se_acredita()
    {
        Seed(MarketplaceReservationStatus.Completed, MarketplaceCreditStatus.Held, DateTime.UtcNow.AddHours(-30));
        Seed(MarketplaceReservationStatus.Cancelled, MarketplaceCreditStatus.None, DateTime.UtcNow.AddHours(-30));
        Seed(MarketplaceReservationStatus.Expired, MarketplaceCreditStatus.None, DateTime.UtcNow.AddHours(-30));

        Assert.Equal(0, await Credits().CreditDueAsync(CancellationToken.None));
        Assert.Equal(0m, OwnerBalance(_juan.Id));
    }

    [Fact]
    public async Task La_ganancia_va_al_propietario_congelado_en_la_reserva_aunque_el_principal_haya_cambiado()
    {
        var reservation = SeedDueForCredit();
        var newOwner = _t.AddUser(_company, name: "Nuevo");
        _t.Db.UnitOwners.Single(x => x.OwnerId == _juan.Id).IsPrimary = false;
        _t.AddOwner(_unit, newOwner, primary: true);

        await Credits().CreditDueAsync(CancellationToken.None);

        Assert.Equal(60_000m, OwnerBalance(_juan.Id));
        Assert.Equal(0m, OwnerBalance(newOwner.Id));
        Assert.Equal(reservation.OwnerId, _juan.Id);
    }

    [Fact]
    public async Task Varias_reservas_del_mismo_propietario_se_suman_en_un_solo_saldo_con_un_lote_cada_una()
    {
        SeedDueForCredit(60_000m);
        SeedDueForCredit(44_000m);

        Assert.Equal(2, await Credits().CreditDueAsync(CancellationToken.None));

        Assert.Equal(104_000m, OwnerBalance(_juan.Id));
        Assert.Single(_t.NewContext().OwnerCredits.Where(x => x.OwnerId == _juan.Id).ToList());
        Assert.Equal(2, _t.NewContext().OwnerCreditMovements.Count(x => x.Kind == OwnerCreditMovementKind.Generated));
    }

    [Fact]
    public async Task El_saldo_de_un_propietario_no_se_mezcla_con_el_de_otro()
    {
        var otherOwner = _t.AddUser(_company, name: "Otro");
        SeedDueForCredit(60_000m);
        SeedDueForCredit(30_000m, otherOwner);

        await Credits().CreditDueAsync(CancellationToken.None);

        Assert.Equal(60_000m, OwnerBalance(_juan.Id));
        Assert.Equal(30_000m, OwnerBalance(otherOwner.Id));
    }

    // ── El motor de expensas existente usa el saldo ──────────────────────────

    [Fact]
    public async Task El_motor_de_expensas_usa_el_lote_del_marketplace_y_el_saldo_queda_consistente()
    {
        var reservation = SeedDueForCredit();
        await Credits().CreditDueAsync(CancellationToken.None);

        // Es el mismo servicio que usa la aprobacion de pagos de expensas para consumir el saldo (mas antiguo primero).
        var db = _t.NewContext();
        var service = new OwnerCreditService(db);
        var credit = db.OwnerCredits.Single(x => x.OwnerId == _juan.Id);
        var lots = await service.EnsureLotsAsync(_juan.Id, _company.Id, credit.Amount, CancellationToken.None);

        // El lote existente cubre todo el saldo: no hace falta inventar un "saldo anterior".
        var lot = Assert.Single(lots);
        Assert.Equal(reservation.Reference, lot.SourceReference);

        var slices = service.ConsumeLots(lots, 25_000m, _juan.Id, _company.Id, CreditApplyMode.OnPaymentApproval,
            Guid.NewGuid(), null, "Aplicado a expensas");
        credit.Amount -= 25_000m;
        await db.SaveChangesAsync();

        var check = _t.NewContext();
        Assert.Equal(35_000m, check.OwnerCreditMovements.Single(x => x.Kind == OwnerCreditMovementKind.Generated).RemainingAmount);
        var applied = check.OwnerCreditMovements.Single(x => x.Kind == OwnerCreditMovementKind.Applied);
        Assert.Equal(25_000m, applied.Amount);
        Assert.Equal(reservation.Reference, applied.SourceReference);
        Assert.Equal(35_000m, check.OwnerCredits.Single(x => x.OwnerId == _juan.Id).Amount);

        // El texto del comprobante explica de donde salio ese saldo.
        var note = OwnerCreditService.BuildCreditNote(slices, CreditApplyMode.OnPaymentApproval, null);
        Assert.Contains("reserva del Marketplace " + reservation.Reference, note);
        Assert.DoesNotContain("comprobante de pago MP-", note);
    }

    [Fact]
    public void El_texto_de_un_saldo_que_viene_de_un_pago_de_expensas_no_cambia()
    {
        var note = OwnerCreditService.BuildCreditNote([("PAY-2026-000007", 10_000m)], CreditApplyMode.Automatic, null);

        Assert.Contains("comprobante de pago PAY-2026-000007", note);
    }

    // ── Reversa (solo SuperAdmin) ────────────────────────────────────────────

    private async Task<MarketplaceReservation> CreditedReservation()
    {
        var reservation = SeedDueForCredit();
        await Credits().CreditDueAsync(CancellationToken.None);
        return reservation;
    }

    [Fact]
    public async Task El_SuperAdmin_revierte_una_acreditacion_con_el_saldo_intacto()
    {
        var reservation = await CreditedReservation();
        LoginAsSuperAdmin();

        var result = await Credits().ReverseCreditAsync(reservation.Id, "  Reserva mal cargada  ", CancellationToken.None);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(60_000m, result.Value!.Amount);
        Assert.Equal(0m, result.Value.OwnerBalance);

        var db = _t.NewContext();
        Assert.Equal(0m, OwnerBalance(_juan.Id));
        var lot = db.OwnerCreditMovements.Single();
        Assert.Equal(0m, lot.RemainingAmount);
        Assert.Contains("REVERTIDO", lot.Description);
        Assert.Contains("Reserva mal cargada", lot.Description);
        Assert.Equal(MarketplaceCreditStatus.Reversed, db.MarketplaceReservations.Single(x => x.Id == reservation.Id).CreditStatus);

        // El dinero vuelve a la cuenta aparte con un ajuste a nombre del SuperAdmin.
        var adjustment = db.MarketplaceAccountMovements.Single(x => x.Kind == MarketplaceAccountMovementKind.Adjustment);
        Assert.Equal(60_000m, adjustment.Amount);
        Assert.Equal(_superAdmin.Id, adjustment.CreatedByUserId);
        Assert.Contains("Reserva mal cargada", adjustment.Concept);

        var evt = db.MarketplaceEvents.Single(x => x.Action == "credit.reversed");
        Assert.Equal(_superAdmin.Id, evt.UserId);
        Assert.Equal("Credited", evt.FromStatus);
        Assert.Equal("Reversed", evt.ToStatus);
        Assert.Single(db.Notifications.Where(x => x.Type == NotificationType.MarketplaceCreditReversed).ToList());
    }

    [Fact]
    public async Task Una_acreditacion_revertida_no_se_vuelve_a_acreditar()
    {
        var reservation = await CreditedReservation();
        LoginAsSuperAdmin();
        await Credits().ReverseCreditAsync(reservation.Id, "x", CancellationToken.None);

        Assert.Equal(0, await Credits().CreditDueAsync(CancellationToken.None));
        Assert.Equal(0m, OwnerBalance(_juan.Id));
    }

    [Fact]
    public async Task Solo_el_SuperAdmin_puede_revertir()
    {
        var reservation = await CreditedReservation();
        _tenant.UserId = _companyAdmin.Id;
        _tenant.Role = "CompanyAdmin";
        _tenant.CompanyId = _company.Id;

        var result = await Credits().ReverseCreditAsync(reservation.Id, "x", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(403, result.Error!.StatusCode);
        Assert.Equal(60_000m, OwnerBalance(_juan.Id));
    }

    [Fact]
    public async Task La_reversa_exige_un_motivo_razonable()
    {
        var reservation = await CreditedReservation();
        LoginAsSuperAdmin();

        Assert.Equal(400, (await Credits().ReverseCreditAsync(reservation.Id, "   ", CancellationToken.None)).Error!.StatusCode);
        Assert.Equal(400, (await Credits().ReverseCreditAsync(reservation.Id, new string('x', 501), CancellationToken.None)).Error!.StatusCode);
    }

    [Fact]
    public async Task No_se_revierte_dos_veces_ni_algo_que_no_esta_acreditado_ni_una_reserva_inexistente()
    {
        var reservation = await CreditedReservation();
        var pending = Seed(MarketplaceReservationStatus.Completed, MarketplaceCreditStatus.Pending, DateTime.UtcNow.AddHours(-2));
        LoginAsSuperAdmin();

        Assert.True((await Credits().ReverseCreditAsync(reservation.Id, "x", CancellationToken.None)).Ok);
        Assert.Equal(409, (await Credits().ReverseCreditAsync(reservation.Id, "x", CancellationToken.None)).Error!.StatusCode);
        Assert.Equal(409, (await Credits().ReverseCreditAsync(pending.Id, "x", CancellationToken.None)).Error!.StatusCode);
        Assert.Equal(404, (await Credits().ReverseCreditAsync(Guid.NewGuid(), "x", CancellationToken.None)).Error!.StatusCode);
        Assert.Single(_t.NewContext().MarketplaceAccountMovements.Where(x => x.Kind == MarketplaceAccountMovementKind.Adjustment).ToList());
    }

    [Fact]
    public async Task Si_el_saldo_ya_se_uso_en_expensas_la_reversa_se_rechaza_y_no_cambia_nada()
    {
        var reservation = await CreditedReservation();
        var db = _t.NewContext();
        var lot = db.OwnerCreditMovements.Single();
        lot.RemainingAmount = 35_000m;      // se uso una parte
        db.OwnerCredits.Single(x => x.OwnerId == _juan.Id).Amount = 35_000m;
        db.SaveChanges();
        LoginAsSuperAdmin();

        var result = await Credits().ReverseCreditAsync(reservation.Id, "x", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Contains("ya se usó", result.Error.Message);
        Assert.Equal(35_000m, OwnerBalance(_juan.Id));
        Assert.Equal(MarketplaceCreditStatus.Credited, _t.NewContext().MarketplaceReservations.Single(x => x.Id == reservation.Id).CreditStatus);
        Assert.Empty(_t.NewContext().MarketplaceAccountMovements.Where(x => x.Kind == MarketplaceAccountMovementKind.Adjustment).ToList());
    }
}
