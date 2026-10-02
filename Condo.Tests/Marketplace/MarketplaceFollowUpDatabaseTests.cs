using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Marketplace;

/// <summary>
/// Garantias de la fase 7 que viven en la base de datos (indices unicos filtrados), no solo en el codigo: un reembolso y una deuda
/// por reserva, un reclamo abierto por reserva y una comision por cancelacion por reserva en la cuenta.
/// </summary>
public class MarketplaceFollowUpDatabaseTests : IDisposable
{
    private readonly Phase7Harness _h = new();
    private readonly MarketplaceReservation _reservation;

    public MarketplaceFollowUpDatabaseTests()
    {
        _reservation = _h.SeedConfirmed();
    }

    public void Dispose() => _h.Dispose();

    private MarketplaceRefund NewRefund() => new()
    {
        CompanyId = _h.Company.Id,
        ReservationId = _reservation.Id,
        BuildingId = _h.Building.Id,
        RecipientUserId = _h.Pedro.Id,
        Amount = 60_000m,
        Origin = MarketplaceRefundOrigin.BuyerCancellation,
        Reason = "x"
    };

    private MarketplaceClaim NewClaim(MarketplaceClaimStatus status = MarketplaceClaimStatus.Open) => new()
    {
        CompanyId = _h.Company.Id,
        ReservationId = _reservation.Id,
        BuildingId = _h.Building.Id,
        OpenedByUserId = _h.Pedro.Id,
        OpenedBy = MarketplaceClaimParty.Buyer,
        Reason = "x",
        Status = status
    };

    private MarketplaceOwnerDebt NewDebt() => new()
    {
        CompanyId = _h.Company.Id,
        BuildingId = _h.Building.Id,
        OwnerId = _h.Juan.Id,
        ReservationId = _reservation.Id,
        Amount = 6_000m,
        Reason = "x"
    };

    [Fact]
    public void Una_reserva_no_puede_tener_dos_reembolsos()
    {
        _h.T.Db.MarketplaceRefunds.Add(NewRefund());
        _h.T.Db.SaveChanges();

        using var other = _h.T.NewContext();
        other.MarketplaceRefunds.Add(NewRefund());
        Assert.Throws<DbUpdateException>(() => other.SaveChanges());
    }

    [Fact]
    public void Una_reserva_no_puede_tener_dos_reclamos_abiertos_pero_si_otro_despues_de_resolver_el_primero()
    {
        _h.T.Db.MarketplaceClaims.Add(NewClaim());
        _h.T.Db.SaveChanges();

        using (var other = _h.T.NewContext())
        {
            other.MarketplaceClaims.Add(NewClaim());
            Assert.Throws<DbUpdateException>(() => other.SaveChanges());
        }

        var first = _h.T.Db.MarketplaceClaims.Single();
        first.Status = MarketplaceClaimStatus.Resolved;
        _h.T.Db.SaveChanges();

        using var again = _h.T.NewContext();
        again.MarketplaceClaims.Add(NewClaim());
        again.SaveChanges();   // ya no hay uno abierto
    }

    [Fact]
    public void Una_reserva_no_puede_tener_dos_deudas_por_gestion()
    {
        _h.T.Db.MarketplaceOwnerDebts.Add(NewDebt());
        _h.T.Db.SaveChanges();

        using var other = _h.T.NewContext();
        other.MarketplaceOwnerDebts.Add(NewDebt());
        Assert.Throws<DbUpdateException>(() => other.SaveChanges());
    }

    [Theory]
    [InlineData(MarketplaceAccountMovementKind.CancellationFee)]
    [InlineData(MarketplaceAccountMovementKind.RefundOut)]
    public void La_cuenta_no_asienta_dos_veces_la_comision_por_cancelacion_ni_la_devolucion_de_una_reserva(MarketplaceAccountMovementKind kind)
    {
        MarketplaceAccountMovement Movement() => new()
        {
            CompanyId = _h.Company.Id,
            BuildingId = _h.Building.Id,
            Kind = kind,
            Amount = kind == MarketplaceAccountMovementKind.RefundOut ? -60_000m : 6_000m,
            ReservationId = _reservation.Id,
            Concept = "x"
        };

        _h.T.Db.MarketplaceAccountMovements.Add(Movement());
        _h.T.Db.SaveChanges();

        using var other = _h.T.NewContext();
        other.MarketplaceAccountMovements.Add(Movement());
        Assert.Throws<DbUpdateException>(() => other.SaveChanges());
    }

    [Fact]
    public void Una_reserva_guarda_la_respuesta_al_aviso_de_inicio()
    {
        var row = _h.T.Db.MarketplaceReservations.Single(x => x.Id == _reservation.Id);
        row.StartNoticeSentAtUtc = DateTime.UtcNow;
        row.StartResponse = MarketplaceStartResponse.NotUsing;
        row.StartResponseReason = "Viaje";
        row.StartResponseAtUtc = DateTime.UtcNow;
        _h.T.Db.SaveChanges();

        var saved = _h.T.NewContext().MarketplaceReservations.Single(x => x.Id == _reservation.Id);
        Assert.Equal(MarketplaceStartResponse.NotUsing, saved.StartResponse);
        Assert.Equal("Viaje", saved.StartResponseReason);
    }
}
