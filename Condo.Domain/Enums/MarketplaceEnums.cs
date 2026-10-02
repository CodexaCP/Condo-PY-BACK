namespace Condo.Domain.Enums;

// Estado de la publicacion. Active <-> Suspended (automatica si el dueño deja de ser el principal, o manual); Closed es final.
public enum MarketplaceListingStatus
{
    Active = 1,
    Suspended = 2,
    Closed = 3
}

// Estado de la reserva (la operacion comercial). No mezcla el estado del pago ni el de la acreditacion del saldo.
public enum MarketplaceReservationStatus
{
    PendingPayment = 1,
    InReview = 2,
    Confirmed = 3,
    Completed = 4,
    Cancelled = 5,
    Expired = 6,
    Rejected = 7
}

public enum MarketplacePaymentStatus
{
    Submitted = 1,
    Approved = 2,
    Rejected = 3
}

// Acreditacion de la ganancia del propietario a su saldo a favor.
// None: todavia no corresponde · Pending: corresponde, espera el fin de la reserva + 24 h sin reclamos ·
// Held: retenida por un reclamo · Credited: ya sumada al saldo · Reversed: se revirtio la acreditacion.
public enum MarketplaceCreditStatus
{
    None = 1,
    Pending = 2,
    Held = 3,
    Credited = 4,
    Reversed = 5
}

public enum MarketplaceCancellationActor
{
    Buyer = 1,
    Owner = 2,
    Staff = 3,
    System = 4
}

// Movimientos del extracto de la cuenta aparte (contable, por edificio). El importe va con signo:
// PaymentIn suma, OwnerCredit y RefundOut restan, Adjustment lo carga el SuperAdmin con el signo que corresponda.
public enum MarketplaceAccountMovementKind
{
    PaymentIn = 1,
    OwnerCredit = 2,
    RefundOut = 3,
    Adjustment = 4
}
