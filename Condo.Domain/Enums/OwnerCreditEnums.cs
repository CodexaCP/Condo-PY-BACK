namespace Condo.Domain.Enums;

public enum OwnerCreditMovementKind
{
    Generated = 1,
    Applied = 2,
    CreditNoteExcess = 3
}

public enum CreditApplyMode
{
    Automatic = 1,
    ManualApp = 2,
    ManualManager = 3,
    OnPaymentApproval = 4
}
