using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Cobro online: lo que Condo-PY quiere cobrar a un propietario por los comprobantes (unidad + periodo) de UN edificio, con una conexion de
// pago activa. Cada sesion con el proveedor es un PaymentAttempt. El dinero no pasa por Condo-PY: cuando el proveedor confirma, el cobro
// termina en un pago de propietario aprobado (OwnerPayment) por el mismo camino de siempre.
public class PaymentIntent : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid ConnectionId { get; set; }

    // Numero legible para soporte (CP-000123): secuencia global "maximo + 1" con indice unico.
    public long Number { get; set; }

    // Importe total en guaranies, recalculado por el servidor al crear el cobro.
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PYG";

    public PaymentIntentStatus Status { get; set; } = PaymentIntentStatus.Open;
    // Por que se cerro sin pago (Rejected, Cancelled, Expired) o por que necesita revision (importe cambiado, comprobante ya pagado...).
    public string? CloseReason { get; set; }
    public string? ReviewReason { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    // Pago de propietario que resulto de este cobro. Sin clave foranea: el vinculo fuerte es OwnerPayment.PaymentIntentId (unico).
    public Guid? OwnerPaymentId { get; set; }

    // A nombre de quien se cobra: foto de la conexion al crear el cobro, para que corregir el dato despues no reescriba la historia.
    public string HolderName { get; set; } = string.Empty;
    public string HolderTaxId { get; set; } = string.Empty;

    // Cobro de prueba (proveedor falso o sandbox) en un edificio que no es de prueba: se registra pero no crea ningun pago.
    public bool IsSimulated { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public ApplicationUser? Owner { get; set; }
    public PaymentConnection? Connection { get; set; }
    public ICollection<PaymentIntentItem> Items { get; set; } = new List<PaymentIntentItem>();
    public ICollection<PaymentAttempt> Attempts { get; set; } = new List<PaymentAttempt>();
}
