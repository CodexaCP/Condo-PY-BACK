namespace Condo.Domain.Enums;

// Avisos automaticos que un edificio puede encender o apagar (pantalla y push). BeforeDue lleva su cantidad de dias de anticipacion.
public enum NoticeKind
{
    // N dias antes del vencimiento del periodo, a quien todavia debe el comprobante.
    BeforeDue = 1,
    // El dia del vencimiento, a quien todavia debe el comprobante.
    OnDue = 2,
    // La primera vez que se le aplica mora a una unidad en un periodo.
    LateFeeApplied = 3,
    // Al aprobarse o registrarse un pago ("Tu pago fue aprobado"). Hoy se envia siempre: la regla permite apagarlo.
    PaymentReceived = 4,
    // Al publicarse la liquidacion de un periodo. Hoy se envia siempre: la regla permite apagarlo.
    PeriodPublished = 5
}
