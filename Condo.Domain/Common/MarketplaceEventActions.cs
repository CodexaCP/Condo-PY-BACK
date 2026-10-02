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
    public const string ReservationConfirmed = "reservation.confirmed";
    public const string ReservationRejected = "reservation.rejected";
    public const string ReservationCancelled = "reservation.cancelled";
    public const string ReservationCompleted = "reservation.completed";

    public const string PaymentSubmitted = "payment.submitted";
    public const string PaymentApproved = "payment.approved";
    public const string PaymentRejected = "payment.rejected";

    public const string CreditApplied = "credit.applied";
    public const string CreditHeld = "credit.held";
    public const string CreditReversed = "credit.reversed";

    public const string AccountMovementRecorded = "account.movement";
}
