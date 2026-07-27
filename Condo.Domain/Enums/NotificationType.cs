namespace Condo.Domain.Enums;

public enum NotificationType
{
    OwnerPaymentSubmitted = 1,
    PaymentUnderReview = 2,
    PaymentApproved = 3,
    PaymentRejected = 4,
    LateFeeConfigChanged = 5,
    AmenityReservationUpdated = 6,
    PlanExpiringSoon = 7,
    PlanExpired = 8,
    PlanSuspended = 9,
    AnnouncementPublished = 10,
    ClaimCreated = 11,
    ClaimStatusUpdated = 12,
    AmenityReservationCreated = 13,
    VoteOpened = 14
}
