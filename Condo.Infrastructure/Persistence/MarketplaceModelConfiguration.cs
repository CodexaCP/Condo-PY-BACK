using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Infrastructure.Persistence;

/// <summary>
/// Modelo de datos del marketplace. Las garantias de integridad viven en la base de datos (indices unicos filtrados), no solo
/// en el codigo: un horario no se puede reservar dos veces, un comprador no puede tener dos reservas esperando pago, un pago
/// aprobado es unico por reserva y los movimientos automaticos de la cuenta y del saldo no se pueden duplicar.
/// </summary>
internal static class MarketplaceModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder, bool isSqlServer)
    {
        // ── Publicacion ──────────────────────────────────────────────────────
        modelBuilder.Entity<MarketplaceListing>().Property(x => x.Title).HasMaxLength(200);
        modelBuilder.Entity<MarketplaceListing>().Property(x => x.HourlyPrice).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<MarketplaceListing>().Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<MarketplaceListing>().Property(x => x.StatusReason).HasMaxLength(500);
        modelBuilder.Entity<MarketplaceListing>().HasIndex(x => new { x.BuildingId, x.Status, x.WindowStartUtc });
        modelBuilder.Entity<MarketplaceListing>().HasIndex(x => new { x.UnitId, x.WindowStartUtc });
        modelBuilder.Entity<MarketplaceListing>().HasIndex(x => x.OwnerId);
        modelBuilder.Entity<MarketplaceListing>()
            .HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MarketplaceListing>()
            .HasOne(x => x.Building).WithMany().HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MarketplaceListing>()
            .HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MarketplaceListing>()
            .HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);

        // ── Reserva ──────────────────────────────────────────────────────────
        var reservation = modelBuilder.Entity<MarketplaceReservation>();
        reservation.Property(x => x.Reference).HasMaxLength(30);
        reservation.Property(x => x.HourlyPrice).HasColumnType("decimal(18,2)");
        reservation.Property(x => x.BaseAmount).HasColumnType("decimal(18,2)");
        reservation.Property(x => x.CommissionPercent).HasColumnType("decimal(5,2)");
        reservation.Property(x => x.CommissionAmount).HasColumnType("decimal(18,2)");
        reservation.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");
        reservation.Property(x => x.OwnerNetAmount).HasColumnType("decimal(18,2)");
        reservation.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        reservation.Property(x => x.CreditStatus).HasConversion<string>().HasMaxLength(20);
        reservation.Property(x => x.CancelledBy).HasConversion<string>().HasMaxLength(20);
        reservation.Property(x => x.CancelReason).HasMaxLength(500);
        reservation.Property(x => x.StartResponse).HasConversion<string>().HasMaxLength(20);
        reservation.Property(x => x.StartResponseReason).HasMaxLength(500);
        if (isSqlServer)
        {
            reservation.Property(x => x.RowVersion).IsRowVersion();
        }
        // El proceso de fondo busca las reservas confirmadas que ya empezaron y todavia no recibieron el aviso de inicio.
        reservation.HasIndex(x => new { x.Status, x.StartNoticeSentAtUtc, x.StartsAtUtc });

        reservation.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique().HasFilter("[IsDeleted] = 0");
        reservation.HasIndex(x => new { x.ListingId, x.Status });
        reservation.HasIndex(x => new { x.BuildingId, x.Status, x.StartsAtUtc });
        reservation.HasIndex(x => new { x.Status, x.ExpiresAtUtc });
        reservation.HasIndex(x => new { x.OwnerId, x.CreditStatus });
        // Un comprador solo puede tener una reserva esperando pago a la vez: no puede bloquear varios horarios.
        reservation.HasIndex(x => x.BuyerUserId, "IX_MarketplaceReservations_OnePendingPerBuyer").IsUnique().HasFilter("[Status] = 'PendingPayment' AND [IsDeleted] = 0");
        reservation.HasIndex(x => x.BuyerUserId, "IX_MarketplaceReservations_BuyerUserId");
        reservation.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        reservation.HasOne(x => x.Listing).WithMany(x => x.Reservations).HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Restrict);
        reservation.HasOne(x => x.Building).WithMany().HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        reservation.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        reservation.HasOne(x => x.Buyer).WithMany().HasForeignKey(x => x.BuyerUserId).OnDelete(DeleteBehavior.Restrict);
        reservation.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);

        // ── Bloques de 30 minutos ocupados ───────────────────────────────────
        var slot = modelBuilder.Entity<MarketplaceReservationSlot>();
        slot.HasIndex(x => new { x.ListingId, x.SlotStartUtc }).IsUnique().HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_MarketplaceReservationSlots_NoDoubleBooking");
        slot.HasIndex(x => x.ReservationId);
        slot.HasOne(x => x.Reservation).WithMany(x => x.Slots).HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        slot.HasOne(x => x.Listing).WithMany().HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Restrict);

        // ── Pago de la reserva ───────────────────────────────────────────────
        var payment = modelBuilder.Entity<MarketplacePayment>();
        payment.Property(x => x.ComprobanteUrl).HasMaxLength(500);
        payment.Property(x => x.ExpectedAmount).HasColumnType("decimal(18,2)");
        payment.Property(x => x.ReviewedAmount).HasColumnType("decimal(18,2)");
        payment.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        payment.Property(x => x.RejectionReason).HasMaxLength(500);
        if (isSqlServer)
        {
            payment.Property(x => x.RowVersion).IsRowVersion();
        }

        payment.HasIndex(x => new { x.BuildingId, x.Status, x.SubmittedAtUtc });
        payment.HasIndex(x => x.ReservationId, "IX_MarketplacePayments_ReservationId");
        // Un solo pago aprobado por reserva.
        payment.HasIndex(x => x.ReservationId, "IX_MarketplacePayments_OneApprovedPerReservation").IsUnique().HasFilter("[Status] = 'Approved' AND [IsDeleted] = 0");
        payment.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        payment.HasOne(x => x.Reservation).WithMany(x => x.Payments).HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        payment.HasOne(x => x.Building).WithMany().HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        payment.HasOne(x => x.Buyer).WithMany().HasForeignKey(x => x.BuyerUserId).OnDelete(DeleteBehavior.Restrict);
        payment.HasOne(x => x.ReviewedByUser).WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);

        // ── Extracto de la cuenta aparte ─────────────────────────────────────
        var movement = modelBuilder.Entity<MarketplaceAccountMovement>();
        movement.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        movement.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        movement.Property(x => x.Concept).HasMaxLength(300);
        movement.HasIndex(x => new { x.BuildingId, x.OccurredAtUtc });
        // Un solo movimiento automatico por (reserva, tipo): idempotencia del asiento. Los ajustes manuales pueden repetirse.
        movement.HasIndex(x => new { x.ReservationId, x.Kind }).IsUnique()
            .HasFilter("[ReservationId] IS NOT NULL AND [Kind] <> 'Adjustment' AND [IsDeleted] = 0")
            .HasDatabaseName("IX_MarketplaceAccountMovements_OnePerReservationKind");
        movement.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        movement.HasOne(x => x.Building).WithMany().HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        movement.HasOne(x => x.Reservation).WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);

        // ── Reembolso pendiente al comprador ─────────────────────────────────
        var refund = modelBuilder.Entity<MarketplaceRefund>();
        refund.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        refund.Property(x => x.Origin).HasConversion<string>().HasMaxLength(20);
        refund.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        refund.Property(x => x.Reason).HasMaxLength(500);
        if (isSqlServer)
        {
            refund.Property(x => x.RowVersion).IsRowVersion();
        }

        // Un solo reembolso por reserva: una operacion no se devuelve dos veces.
        refund.HasIndex(x => x.ReservationId).IsUnique().HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_MarketplaceRefunds_OnePerReservation");
        refund.HasIndex(x => new { x.BuildingId, x.Status, x.CreatedAtUtc });
        refund.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        refund.HasOne(x => x.Reservation).WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        refund.HasOne(x => x.Building).WithMany().HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        refund.HasOne(x => x.Recipient).WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        refund.HasOne(x => x.ReturnedByUser).WithMany().HasForeignKey(x => x.ReturnedByUserId).OnDelete(DeleteBehavior.Restrict);

        // ── Reclamo ("Reportar un problema") ─────────────────────────────────
        var claim = modelBuilder.Entity<MarketplaceClaim>();
        claim.Property(x => x.OpenedBy).HasConversion<string>().HasMaxLength(20);
        claim.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        claim.Property(x => x.Resolution).HasConversion<string>().HasMaxLength(20);
        claim.Property(x => x.Reason).HasMaxLength(500);
        claim.Property(x => x.ResolutionNote).HasMaxLength(500);
        if (isSqlServer)
        {
            claim.Property(x => x.RowVersion).IsRowVersion();
        }

        // Un solo reclamo abierto por reserva.
        claim.HasIndex(x => x.ReservationId).IsUnique().HasFilter("[Status] = 'Open' AND [IsDeleted] = 0")
            .HasDatabaseName("IX_MarketplaceClaims_OneOpenPerReservation");
        claim.HasIndex(x => new { x.BuildingId, x.Status, x.CreatedAtUtc });
        claim.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        claim.HasOne(x => x.Reservation).WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        claim.HasOne(x => x.Building).WithMany().HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        claim.HasOne(x => x.OpenedByUser).WithMany().HasForeignKey(x => x.OpenedByUserId).OnDelete(DeleteBehavior.Restrict);
        claim.HasOne(x => x.ResolvedByUser).WithMany().HasForeignKey(x => x.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);

        // ── Deuda por gestion del propietario ────────────────────────────────
        var debt = modelBuilder.Entity<MarketplaceOwnerDebt>();
        debt.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        debt.Property(x => x.PaidAmount).HasColumnType("decimal(18,2)");
        debt.Property(x => x.Reason).HasMaxLength(500);
        // Una deuda por reserva: la comision de una operacion no se cobra dos veces.
        debt.HasIndex(x => x.ReservationId).IsUnique().HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_MarketplaceOwnerDebts_OnePerReservation");
        debt.HasIndex(x => new { x.OwnerId, x.BuildingId, x.SettledAtUtc });
        debt.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        debt.HasOne(x => x.Building).WithMany().HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        debt.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        debt.HasOne(x => x.Reservation).WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);

        // ── Auditoria ────────────────────────────────────────────────────────
        var audit = modelBuilder.Entity<MarketplaceEvent>();
        audit.Property(x => x.Action).HasMaxLength(60);
        audit.Property(x => x.EntityType).HasMaxLength(40);
        audit.Property(x => x.FromStatus).HasMaxLength(40);
        audit.Property(x => x.ToStatus).HasMaxLength(40);
        audit.Property(x => x.IpAddress).HasMaxLength(64);
        audit.Property(x => x.UserAgent).HasMaxLength(300);
        audit.HasIndex(x => new { x.BuildingId, x.TimestampUtc });
        audit.HasIndex(x => new { x.EntityType, x.EntityId, x.TimestampUtc });
        audit.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        audit.HasOne(x => x.Building).WithMany().HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);

        // ── Saldo a favor: un solo lote por reserva ──────────────────────────
        modelBuilder.Entity<OwnerCreditMovement>().HasIndex(x => x.MarketplaceReservationId).IsUnique()
            .HasFilter("[MarketplaceReservationId] IS NOT NULL AND [Kind] = 'Generated' AND [IsDeleted] = 0")
            .HasDatabaseName("IX_OwnerCreditMovements_OneLotPerMarketplaceReservation");
    }
}
