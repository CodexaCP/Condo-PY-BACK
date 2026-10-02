using Condo.Domain.Enums;

namespace Condo.Application.Services;

/// <summary>
/// Reglas puras de cancelaciones, reembolsos, reclamos y aviso de inicio. Viven aparte de los servicios para poder probarlas
/// sin base de datos y para que cada pantalla y cada endpoint apliquen exactamente la misma regla.
/// </summary>
public static class MarketplaceCancellationRules
{
    // Ventana de reclamo: desde que empieza la reserva hasta 24 horas despues de su fin.
    public static readonly TimeSpan ClaimWindow = TimeSpan.FromHours(24);

    // El Encargado puede devolver en cualquier momento, con un maximo de 72 horas desde que se creo el reembolso.
    public const int RefundMaxHours = 72;
    public static readonly TimeSpan RefundMaxTime = TimeSpan.FromHours(RefundMaxHours);

    /// <summary>
    /// Se puede cancelar una reserva ya pagada solo mientras esta confirmada y NO empezo el horario: una vez comenzado, el
    /// sistema asume que se uso y cualquier problema va por "Reportar un problema".
    /// </summary>
    public static bool CanCancelPaid(MarketplaceReservationStatus status, DateTime startsAtUtc, DateTime nowUtc) =>
        status == MarketplaceReservationStatus.Confirmed && nowUtc < AsUtc(startsAtUtc);

    /// <summary>
    /// Lo que se le devuelve al comprador. Si cancela el comprador, solo la base (la comision de gestion no se devuelve); si
    /// cancela el propietario o se resuelve un reclamo a su favor, todo, comision incluida.
    /// </summary>
    public static decimal RefundAmount(MarketplaceRefundOrigin origin, decimal baseAmount, decimal totalAmount) =>
        origin == MarketplaceRefundOrigin.BuyerCancellation ? baseAmount : totalAmount;

    /// <summary>Quien cancela (o pierde el reclamo) por culpa del propietario asume la comision de la gestion.</summary>
    public static bool OwnerAssumesCommission(MarketplaceRefundOrigin origin) => origin != MarketplaceRefundOrigin.BuyerCancellation;

    /// <summary>
    /// "Reportar un problema": con la reserva ya empezada (confirmada) o finalizada, dentro de las 24 horas posteriores a su fin,
    /// mientras la ganancia siga sin acreditar y no haya ya un reclamo abierto.
    /// </summary>
    public static bool CanOpenClaim(
        MarketplaceReservationStatus status, MarketplaceCreditStatus credit,
        DateTime startsAtUtc, DateTime endsAtUtc, bool hasOpenClaim, DateTime nowUtc)
    {
        if (hasOpenClaim || (credit != MarketplaceCreditStatus.Pending && credit != MarketplaceCreditStatus.Held))
        {
            return false;
        }

        var started = nowUtc >= AsUtc(startsAtUtc);
        var open = status == MarketplaceReservationStatus.Completed
                   || (status == MarketplaceReservationStatus.Confirmed && started);
        return open && nowUtc <= AsUtc(endsAtUtc) + ClaimWindow;
    }

    /// <summary>El comprador todavia tiene que contestar el aviso de inicio (llego, no respondio y la reserva no termino).</summary>
    public static bool NeedsStartResponse(
        MarketplaceReservationStatus status, DateTime? startNoticeSentAtUtc,
        MarketplaceStartResponse? response, DateTime endsAtUtc, DateTime nowUtc) =>
        status == MarketplaceReservationStatus.Confirmed
        && startNoticeSentAtUtc.HasValue
        && response is null
        && nowUtc < AsUtc(endsAtUtc);

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
