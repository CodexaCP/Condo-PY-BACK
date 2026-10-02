namespace Condo.Api.Services;

/// <summary>
/// Configuracion de plataforma del marketplace (seccion "Marketplace" de appsettings). El plazo para pagar es el mismo para
/// todos los edificios; si falta o es invalido se usa el valor por defecto.
/// </summary>
public class MarketplaceOptions
{
    public const int DefaultPaymentTimeoutMinutes = 10;

    // Minutos que una reserva puede esperar el comprobante antes de vencer y liberar el horario.
    public int PaymentTimeoutMinutes { get; set; } = DefaultPaymentTimeoutMinutes;

    public int EffectivePaymentTimeoutMinutes =>
        PaymentTimeoutMinutes is >= 1 and <= 120 ? PaymentTimeoutMinutes : DefaultPaymentTimeoutMinutes;
}
