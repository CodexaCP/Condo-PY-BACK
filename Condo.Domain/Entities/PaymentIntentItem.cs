using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Comprobante (unidad + periodo) incluido en un cobro online. El comprobante no tiene identidad propia (se calcula de los cargos), asi que el
// cobro guarda la pareja y una foto de lo que debia en ese momento. Mientras el cobro esta abierto (IsOpen) el comprobante queda reservado:
// un indice unico impide ponerlo en otro cobro abierto.
public class PaymentIntentItem : CompanyScopedEntity
{
    public Guid IntentId { get; set; }
    public Guid UnitId { get; set; }
    public Guid ExpensePeriodId { get; set; }
    public decimal Amount { get; set; }

    // JSON con los cargos y pendientes del comprobante al crear el cobro (concepto, cargo, pendiente).
    public string? SnapshotJson { get; set; }

    // Se mantiene en la misma transaccion que el estado del cobro: verdadero mientras el cobro esta abierto.
    public bool IsOpen { get; set; } = true;

    public Company? Company { get; set; }
    public PaymentIntent? Intent { get; set; }
    public Unit? Unit { get; set; }
    public ExpensePeriod? ExpensePeriod { get; set; }
}
