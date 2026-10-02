namespace Condo.Domain.Common;

// Nombres de las acciones que se registran en MarketplaceEvent (auditoria + historial economico).
public static class MarketplaceEventActions
{
    public const string SettingsChanged = "settings.changed";

    public const string ListingCreated = "listing.created";
    public const string ListingUpdated = "listing.updated";
    public const string ListingSuspended = "listing.suspended";
    public const string ListingResumed = "listing.resumed";
    public const string ListingClosed = "listing.closed";

    public const string ReservationCreated = "reservation.created";
    public const string ReservationExpired = "reservation.expired";
    public const string ReservationInReview = "reservation.in_review";
    public const string ReservationConfirmed = "reservation.confirmed";
    public const string ReservationRejected = "reservation.rejected";
    public const string ReservationCancelled = "reservation.cancelled";
    public const string ReservationCompleted = "reservation.completed";

    public const string PaymentSubmitted = "payment.submitted";
    public const string PaymentApproved = "payment.approved";
    public const string PaymentRejected = "payment.rejected";

    public const string CreditApplied = "credit.applied";
    public const string CreditHeld = "credit.held";
    public const string CreditReleased = "credit.released";
    public const string CreditReversed = "credit.reversed";

    public const string AccountMovementRecorded = "account.movement";

    public const string RefundCreated = "refund.created";
    public const string RefundReturned = "refund.returned";
    public const string RefundOverdueAlert = "refund.overdue_alert";

    public const string ClaimOpened = "claim.opened";
    public const string ClaimResolved = "claim.resolved";

    // Comision asumida por el propietario que cancela (o pierde un reclamo) y deuda por gestion.
    public const string OwnerFeeCharged = "owner_fee.charged";
    public const string OwnerDebtCreated = "owner_debt.created";
    public const string OwnerDebtDeducted = "owner_debt.deducted";

    public const string StartNoticeSent = "start_notice.sent";
    public const string StartResponded = "start_notice.responded";
}
