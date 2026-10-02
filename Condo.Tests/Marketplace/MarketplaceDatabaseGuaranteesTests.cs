using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Marketplace;

/// <summary>
/// Las garantias de integridad del marketplace viven en la base (indices unicos filtrados), no solo en el codigo: aunque dos
/// procesos pasen la validacion a la vez, la base rechaza al segundo.
/// </summary>
public class MarketplaceDatabaseGuaranteesTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly Company _company;
    private readonly Building _building;
    private readonly Unit _unit;
    private readonly ApplicationUser _owner;
    private readonly ApplicationUser _buyerA;
    private readonly ApplicationUser _buyerB;
    private readonly MarketplaceListing _listing;

    public MarketplaceDatabaseGuaranteesTests()
    {
        _company = _t.AddCompany();
        _building = _t.AddBuilding(_company);
        _unit = _t.AddUnit(_building);
        _owner = _t.AddUser(_company);
        _buyerA = _t.AddUser(_company, UserRole.Resident);
        _buyerB = _t.AddUser(_company, UserRole.Resident);
        _listing = _t.AddListing(_building, _unit, _owner);
    }

    public void Dispose() => _t.Dispose();

    private MarketplaceReservation SaveReservation(ApplicationUser buyer, MarketplaceReservationStatus status = MarketplaceReservationStatus.InReview, string? reference = null)
    {
        var reservation = _t.NewReservation(_listing, buyer, reference, status);
        _t.Db.MarketplaceReservations.Add(reservation);
        _t.Db.SaveChanges();
        return reservation;
    }

    // ── Doble reserva ────────────────────────────────────────────────────────

    [Fact]
    public void Dos_reservas_no_pueden_ocupar_el_mismo_bloque_de_la_misma_publicacion()
    {
        var a = SaveReservation(_buyerA);
        var b = SaveReservation(_buyerB);
        var slot = _listing.WindowStartUtc;

        _t.Db.MarketplaceReservationSlots.Add(_t.NewSlot(a, slot));
        _t.Db.SaveChanges();

        _t.Db.MarketplaceReservationSlots.Add(_t.NewSlot(b, slot));
        Assert.Throws<DbUpdateException>(() => _t.Db.SaveChanges());
    }

    [Fact]
    public void Reservas_consecutivas_ocupan_bloques_distintos_y_conviven()
    {
        var a = SaveReservation(_buyerA);
        var b = SaveReservation(_buyerB);

        foreach (var slot in MarketplaceWindowRules.Slots(_listing.WindowStartUtc, _listing.WindowStartUtc.AddHours(3)))
        {
            _t.Db.MarketplaceReservationSlots.Add(_t.NewSlot(a, slot));
        }

        foreach (var slot in MarketplaceWindowRules.Slots(_listing.WindowStartUtc.AddHours(3), _listing.WindowStartUtc.AddHours(4)))
        {
            _t.Db.MarketplaceReservationSlots.Add(_t.NewSlot(b, slot));
        }

        _t.Db.SaveChanges();

        Assert.Equal(8, _t.NewContext().MarketplaceReservationSlots.Count());
    }

    [Fact]
    public void Un_solapamiento_parcial_choca_en_el_bloque_compartido_y_no_guarda_nada_de_la_segunda()
    {
        var a = SaveReservation(_buyerA);
        var b = SaveReservation(_buyerB);

        foreach (var slot in MarketplaceWindowRules.Slots(_listing.WindowStartUtc, _listing.WindowStartUtc.AddHours(3)))
        {
            _t.Db.MarketplaceReservationSlots.Add(_t.NewSlot(a, slot));
        }

        _t.Db.SaveChanges();

        // b pide de 2h a 4h: comparte el tramo 2h-3h con a.
        foreach (var slot in MarketplaceWindowRules.Slots(_listing.WindowStartUtc.AddHours(2), _listing.WindowStartUtc.AddHours(4)))
        {
            _t.Db.MarketplaceReservationSlots.Add(_t.NewSlot(b, slot));
        }

        Assert.Throws<DbUpdateException>(() => _t.Db.SaveChanges());
        Assert.Equal(6, _t.NewContext().MarketplaceReservationSlots.Count());
    }

    [Fact]
    public void Al_liberar_los_bloques_otra_reserva_puede_ocuparlos()
    {
        var a = SaveReservation(_buyerA);
        var b = SaveReservation(_buyerB);
        var slot = _listing.WindowStartUtc;

        var first = _t.NewSlot(a, slot);
        _t.Db.MarketplaceReservationSlots.Add(first);
        _t.Db.SaveChanges();

        // Vence la reserva a: sus bloques se marcan borrados y el horario queda libre.
        first.IsDeleted = true;
        _t.Db.SaveChanges();

        _t.Db.MarketplaceReservationSlots.Add(_t.NewSlot(b, slot));
        _t.Db.SaveChanges();

        Assert.Equal(1, _t.NewContext().MarketplaceReservationSlots.Count(x => !x.IsDeleted));
    }

    [Fact]
    public void El_mismo_horario_en_otra_publicacion_no_choca()
    {
        var otherUnit = _t.AddUnit(_building);
        var otherListing = _t.AddListing(_building, otherUnit, _owner);
        var a = SaveReservation(_buyerA);
        var b = _t.NewReservation(otherListing, _buyerB);
        _t.Db.MarketplaceReservations.Add(b);
        _t.Db.SaveChanges();

        _t.Db.MarketplaceReservationSlots.Add(_t.NewSlot(a, _listing.WindowStartUtc));
        _t.Db.MarketplaceReservationSlots.Add(_t.NewSlot(b, otherListing.WindowStartUtc));

        _t.Db.SaveChanges();
    }

    // ── Una reserva esperando pago por comprador ─────────────────────────────

    [Fact]
    public void Un_comprador_no_puede_tener_dos_reservas_esperando_pago()
    {
        SaveReservation(_buyerA, MarketplaceReservationStatus.PendingPayment);

        _t.Db.MarketplaceReservations.Add(_t.NewReservation(_listing, _buyerA));
        Assert.Throws<DbUpdateException>(() => _t.Db.SaveChanges());
    }

    [Fact]
    public void Dos_compradores_distintos_pueden_tener_cada_uno_su_reserva_esperando_pago()
    {
        SaveReservation(_buyerA, MarketplaceReservationStatus.PendingPayment);
        SaveReservation(_buyerB, MarketplaceReservationStatus.PendingPayment);
    }

    [Fact]
    public void Despues_de_pagar_o_vencer_el_comprador_puede_reservar_de_nuevo()
    {
        var first = SaveReservation(_buyerA, MarketplaceReservationStatus.PendingPayment);
        first.Status = MarketplaceReservationStatus.Expired;
        _t.Db.SaveChanges();

        SaveReservation(_buyerA, MarketplaceReservationStatus.PendingPayment);
    }

    [Fact]
    public void Un_comprador_puede_tener_varias_reservas_si_solo_una_espera_pago()
    {
        SaveReservation(_buyerA, MarketplaceReservationStatus.Confirmed);
        SaveReservation(_buyerA, MarketplaceReservationStatus.InReview);
        SaveReservation(_buyerA, MarketplaceReservationStatus.PendingPayment);
    }

    // ── Numero de operacion ──────────────────────────────────────────────────

    [Fact]
    public void El_numero_de_operacion_es_unico_por_empresa()
    {
        SaveReservation(_buyerA, MarketplaceReservationStatus.Completed, "MP-00000125");

        _t.Db.MarketplaceReservations.Add(_t.NewReservation(_listing, _buyerB, "MP-00000125", MarketplaceReservationStatus.Completed));
        Assert.Throws<DbUpdateException>(() => _t.Db.SaveChanges());
    }

    [Fact]
    public void El_mismo_numero_puede_repetirse_en_otra_empresa()
    {
        SaveReservation(_buyerA, MarketplaceReservationStatus.Completed, "MP-00000125");

        var otherCompany = _t.AddCompany();
        var otherBuilding = _t.AddBuilding(otherCompany);
        var otherUnit = _t.AddUnit(otherBuilding);
        var otherOwner = _t.AddUser(otherCompany);
        var otherBuyer = _t.AddUser(otherCompany, UserRole.Resident);
        var otherListing = _t.AddListing(otherBuilding, otherUnit, otherOwner);

        _t.Db.MarketplaceReservations.Add(_t.NewReservation(otherListing, otherBuyer, "MP-00000125", MarketplaceReservationStatus.Completed));
        _t.Db.SaveChanges();
    }

    // ── Un solo pago aprobado por reserva ────────────────────────────────────

    private MarketplacePayment NewPayment(MarketplaceReservation reservation, MarketplacePaymentStatus status) => new()
    {
        CompanyId = reservation.CompanyId,
        ReservationId = reservation.Id,
        BuildingId = reservation.BuildingId,
        BuyerUserId = reservation.BuyerUserId,
        ComprobanteUrl = "/uploads/x.png",
        ExpectedAmount = reservation.TotalAmount,
        Status = status
    };

    [Fact]
    public void Una_reserva_no_puede_tener_dos_pagos_aprobados()
    {
        var reservation = SaveReservation(_buyerA);
        _t.Db.MarketplacePayments.Add(NewPayment(reservation, MarketplacePaymentStatus.Approved));
        _t.Db.SaveChanges();

        _t.Db.MarketplacePayments.Add(NewPayment(reservation, MarketplacePaymentStatus.Approved));
        Assert.Throws<DbUpdateException>(() => _t.Db.SaveChanges());
    }

    [Fact]
    public void Pagos_rechazados_o_pendientes_pueden_convivir_con_uno_aprobado()
    {
        var reservation = SaveReservation(_buyerA);
        _t.Db.MarketplacePayments.Add(NewPayment(reservation, MarketplacePaymentStatus.Rejected));
        _t.Db.MarketplacePayments.Add(NewPayment(reservation, MarketplacePaymentStatus.Submitted));
        _t.Db.MarketplacePayments.Add(NewPayment(reservation, MarketplacePaymentStatus.Approved));
        _t.Db.SaveChanges();
    }

    // ── Cuenta aparte: un asiento automatico por (reserva, tipo) ─────────────

    private MarketplaceAccountMovement NewMovement(MarketplaceReservation? reservation, MarketplaceAccountMovementKind kind, decimal amount) => new()
    {
        CompanyId = _company.Id,
        BuildingId = _building.Id,
        Kind = kind,
        Amount = amount,
        ReservationId = reservation?.Id,
        Concept = "Prueba"
    };

    [Fact]
    public void El_ingreso_de_una_reserva_no_se_asienta_dos_veces()
    {
        var reservation = SaveReservation(_buyerA);
        _t.Db.MarketplaceAccountMovements.Add(NewMovement(reservation, MarketplaceAccountMovementKind.PaymentIn, 66_000m));
        _t.Db.SaveChanges();

        _t.Db.MarketplaceAccountMovements.Add(NewMovement(reservation, MarketplaceAccountMovementKind.PaymentIn, 66_000m));
        Assert.Throws<DbUpdateException>(() => _t.Db.SaveChanges());
    }

    [Fact]
    public void Una_reserva_puede_tener_su_ingreso_su_acreditacion_y_su_devolucion()
    {
        var reservation = SaveReservation(_buyerA);
        _t.Db.MarketplaceAccountMovements.Add(NewMovement(reservation, MarketplaceAccountMovementKind.PaymentIn, 66_000m));
        _t.Db.MarketplaceAccountMovements.Add(NewMovement(reservation, MarketplaceAccountMovementKind.OwnerCredit, -60_000m));
        _t.Db.MarketplaceAccountMovements.Add(NewMovement(reservation, MarketplaceAccountMovementKind.RefundOut, -6_000m));
        _t.Db.SaveChanges();
    }

    [Fact]
    public void Los_ajustes_manuales_si_pueden_repetirse()
    {
        var reservation = SaveReservation(_buyerA);
        _t.Db.MarketplaceAccountMovements.Add(NewMovement(null, MarketplaceAccountMovementKind.Adjustment, 1_000m));
        _t.Db.MarketplaceAccountMovements.Add(NewMovement(null, MarketplaceAccountMovementKind.Adjustment, -500m));
        _t.Db.MarketplaceAccountMovements.Add(NewMovement(reservation, MarketplaceAccountMovementKind.Adjustment, 200m));
        _t.Db.MarketplaceAccountMovements.Add(NewMovement(reservation, MarketplaceAccountMovementKind.Adjustment, 300m));
        _t.Db.SaveChanges();
    }

    // ── Saldo a favor: un solo lote por reserva ──────────────────────────────

    private OwnerCreditMovement NewCreditMovement(Guid reservationId, OwnerCreditMovementKind kind, decimal amount) => new()
    {
        CompanyId = _company.Id,
        OwnerId = _owner.Id,
        Kind = kind,
        Amount = amount,
        RemainingAmount = kind == OwnerCreditMovementKind.Generated ? amount : 0m,
        MarketplaceReservationId = reservationId,
        Description = "Saldo del marketplace"
    };

    [Fact]
    public void El_saldo_de_una_reserva_no_se_acredita_dos_veces()
    {
        var reservation = SaveReservation(_buyerA);
        _t.Db.OwnerCreditMovements.Add(NewCreditMovement(reservation.Id, OwnerCreditMovementKind.Generated, 60_000m));
        _t.Db.SaveChanges();

        _t.Db.OwnerCreditMovements.Add(NewCreditMovement(reservation.Id, OwnerCreditMovementKind.Generated, 60_000m));
        Assert.Throws<DbUpdateException>(() => _t.Db.SaveChanges());
    }

    [Fact]
    public void Consumir_el_lote_registra_aplicaciones_con_la_misma_reserva_sin_chocar()
    {
        var reservation = SaveReservation(_buyerA);
        _t.Db.OwnerCreditMovements.Add(NewCreditMovement(reservation.Id, OwnerCreditMovementKind.Generated, 60_000m));
        _t.Db.OwnerCreditMovements.Add(NewCreditMovement(reservation.Id, OwnerCreditMovementKind.Applied, 20_000m));
        _t.Db.OwnerCreditMovements.Add(NewCreditMovement(reservation.Id, OwnerCreditMovementKind.Applied, 40_000m));
        _t.Db.SaveChanges();
    }

    [Fact]
    public void Los_lotes_de_otro_origen_no_se_ven_afectados_por_la_restriccion()
    {
        _t.Db.OwnerCreditMovements.Add(new OwnerCreditMovement
        {
            CompanyId = _company.Id, OwnerId = _owner.Id, Kind = OwnerCreditMovementKind.Generated,
            Amount = 5_000m, RemainingAmount = 5_000m, Description = "Saldo anterior"
        });
        _t.Db.OwnerCreditMovements.Add(new OwnerCreditMovement
        {
            CompanyId = _company.Id, OwnerId = _owner.Id, Kind = OwnerCreditMovementKind.Generated,
            Amount = 7_000m, RemainingAmount = 7_000m, Description = "Otro saldo anterior"
        });
        _t.Db.SaveChanges();
    }

    // ── Auditoria de solo insercion ──────────────────────────────────────────

    private MarketplaceEvent SaveEvent()
    {
        var evt = new MarketplaceEvent
        {
            CompanyId = _company.Id,
            BuildingId = _building.Id,
            Action = "reservation.created",
            EntityType = "MarketplaceReservation",
            EntityId = Guid.NewGuid(),
            ToStatus = "PendingPayment"
        };
        _t.Db.MarketplaceEvents.Add(evt);
        _t.Db.SaveChanges();
        return evt;
    }

    [Fact]
    public void Un_evento_de_auditoria_no_se_puede_modificar()
    {
        var evt = SaveEvent();

        evt.ToStatus = "Confirmed";
        Assert.Throws<InvalidOperationException>(() => _t.Db.SaveChanges());
    }

    [Fact]
    public void Un_evento_de_auditoria_no_se_puede_borrar_ni_marcar_como_borrado()
    {
        var evt = SaveEvent();

        _t.Db.MarketplaceEvents.Remove(evt);
        Assert.Throws<InvalidOperationException>(() => _t.Db.SaveChanges());
    }

    [Fact]
    public void Los_eventos_nuevos_se_siguen_pudiendo_insertar_en_la_misma_base()
    {
        SaveEvent();
        SaveEvent();

        Assert.Equal(2, _t.NewContext().MarketplaceEvents.Count());
    }

    // ── Importes congelados ──────────────────────────────────────────────────

    [Fact]
    public void Los_importes_de_la_reserva_quedan_guardados_tal_como_se_congelaron()
    {
        var quote = MarketplacePricing.Quote(20_000m, 3, 10m);
        var reservation = _t.NewReservation(_listing, _buyerA);
        reservation.BaseAmount = quote.BaseAmount;
        reservation.CommissionPercent = quote.CommissionPercent;
        reservation.CommissionAmount = quote.CommissionAmount;
        reservation.TotalAmount = quote.TotalAmount;
        reservation.OwnerNetAmount = quote.OwnerNetAmount;
        _t.Db.MarketplaceReservations.Add(reservation);
        _t.Db.SaveChanges();

        // Aunque la publicacion cambie de precio despues, la reserva conserva lo que se congelo.
        _listing.HourlyPrice = 99_000m;
        _t.Db.SaveChanges();

        var saved = _t.NewContext().MarketplaceReservations.Single(x => x.Id == reservation.Id);
        Assert.Equal(60_000m, saved.BaseAmount);
        Assert.Equal(6_000m, saved.CommissionAmount);
        Assert.Equal(66_000m, saved.TotalAmount);
        Assert.Equal(60_000m, saved.OwnerNetAmount);
        Assert.Equal(10m, saved.CommissionPercent);
    }
}
