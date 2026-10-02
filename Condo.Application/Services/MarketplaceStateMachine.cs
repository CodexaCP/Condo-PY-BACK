using Condo.Domain.Enums;

namespace Condo.Application.Services;

public sealed class MarketplaceTransitionException(string message) : InvalidOperationException(message);

/// <summary>
/// Transiciones permitidas de cada maquina de estados del marketplace. Cualquier otra se rechaza: nadie cambia un estado
/// "a mano" a un valor arbitrario. Publicacion, reserva, pago y acreditacion del saldo son maquinas separadas.
/// </summary>
public static class MarketplaceStateMachine
{
    private static readonly IReadOnlyDictionary<MarketplaceListingStatus, MarketplaceListingStatus[]> ListingMap =
        new Dictionary<MarketplaceListingStatus, MarketplaceListingStatus[]>
        {
            [MarketplaceListingStatus.Active] = [MarketplaceListingStatus.Suspended, MarketplaceListingStatus.Closed],
            [MarketplaceListingStatus.Suspended] = [MarketplaceListingStatus.Active, MarketplaceListingStatus.Closed],
            [MarketplaceListingStatus.Closed] = []
        };

    // Rechazar solo se hace al revisar un pago (InReview). Cancelar: el comprador antes de pagar o con la reserva
    // confirmada (antes del inicio); el propietario con la reserva confirmada. Un pago en revision se resuelve
    // confirmando o rechazando, no cancelando.
    private static readonly IReadOnlyDictionary<MarketplaceReservationStatus, MarketplaceReservationStatus[]> ReservationMap =
        new Dictionary<MarketplaceReservationStatus, MarketplaceReservationStatus[]>
        {
            [MarketplaceReservationStatus.PendingPayment] =
                [MarketplaceReservationStatus.InReview, MarketplaceReservationStatus.Expired, MarketplaceReservationStatus.Cancelled],
            [MarketplaceReservationStatus.InReview] =
                [MarketplaceReservationStatus.Confirmed, MarketplaceReservationStatus.Rejected],
            [MarketplaceReservationStatus.Confirmed] =
                [MarketplaceReservationStatus.Completed, MarketplaceReservationStatus.Cancelled],
            [MarketplaceReservationStatus.Completed] = [],
            [MarketplaceReservationStatus.Cancelled] = [],
            [MarketplaceReservationStatus.Expired] = [],
            [MarketplaceReservationStatus.Rejected] = []
        };

    private static readonly IReadOnlyDictionary<MarketplacePaymentStatus, MarketplacePaymentStatus[]> PaymentMap =
        new Dictionary<MarketplacePaymentStatus, MarketplacePaymentStatus[]>
        {
            [MarketplacePaymentStatus.Submitted] = [MarketplacePaymentStatus.Approved, MarketplacePaymentStatus.Rejected],
            [MarketplacePaymentStatus.Approved] = [],
            [MarketplacePaymentStatus.Rejected] = []
        };

    // None -> Pending al confirmarse la reserva; Pending -> Credited al acreditar, Held si hay reclamo, None si se cancela
    // antes; Held vuelve a Pending al resolverse a favor del propietario; Credited -> Reversed solo por reversa manual.
    private static readonly IReadOnlyDictionary<MarketplaceCreditStatus, MarketplaceCreditStatus[]> CreditMap =
        new Dictionary<MarketplaceCreditStatus, MarketplaceCreditStatus[]>
        {
            [MarketplaceCreditStatus.None] = [MarketplaceCreditStatus.Pending],
            [MarketplaceCreditStatus.Pending] =
                [MarketplaceCreditStatus.Credited, MarketplaceCreditStatus.Held, MarketplaceCreditStatus.None],
            [MarketplaceCreditStatus.Held] = [MarketplaceCreditStatus.Pending, MarketplaceCreditStatus.None],
            [MarketplaceCreditStatus.Credited] = [MarketplaceCreditStatus.Reversed],
            [MarketplaceCreditStatus.Reversed] = []
        };

    public static bool CanTransition(MarketplaceListingStatus from, MarketplaceListingStatus to) => ListingMap[from].Contains(to);
    public static bool CanTransition(MarketplaceReservationStatus from, MarketplaceReservationStatus to) => ReservationMap[from].Contains(to);
    public static bool CanTransition(MarketplacePaymentStatus from, MarketplacePaymentStatus to) => PaymentMap[from].Contains(to);
    public static bool CanTransition(MarketplaceCreditStatus from, MarketplaceCreditStatus to) => CreditMap[from].Contains(to);

    public static bool IsFinal(MarketplaceReservationStatus status) => ReservationMap[status].Length == 0;

    public static void EnsureTransition(MarketplaceListingStatus from, MarketplaceListingStatus to) =>
        Ensure(CanTransition(from, to), "la publicación", from, to);

    public static void EnsureTransition(MarketplaceReservationStatus from, MarketplaceReservationStatus to) =>
        Ensure(CanTransition(from, to), "la reserva", from, to);

    public static void EnsureTransition(MarketplacePaymentStatus from, MarketplacePaymentStatus to) =>
        Ensure(CanTransition(from, to), "el pago", from, to);

    public static void EnsureTransition(MarketplaceCreditStatus from, MarketplaceCreditStatus to) =>
        Ensure(CanTransition(from, to), "la acreditación", from, to);

    private static void Ensure<T>(bool allowed, string what, T from, T to) where T : struct, Enum
    {
        if (!allowed)
        {
            throw new MarketplaceTransitionException($"No se puede pasar {what} de {from} a {to}.");
        }
    }
}
