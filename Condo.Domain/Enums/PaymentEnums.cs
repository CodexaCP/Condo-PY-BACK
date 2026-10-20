namespace Condo.Domain.Enums;

// Ambiente de una conexion de pago: sandbox y produccion usan claves y direcciones distintas, por eso son conexiones separadas.
public enum PaymentEnvironment
{
    Sandbox = 1,
    Production = 2
}

// Estado de una conexion de pago. Draft: cargada incompleta. PendingValidation: completa, todavia sin habilitar para cobrar.
// Active: habilitada. Disabled: apagada por la administracion (se conserva la configuracion).
public enum PaymentConnectionStatus
{
    Draft = 1,
    PendingValidation = 2,
    Active = 3,
    Disabled = 4
}

// Estado de un cobro (lo que Condo-PY quiere cobrar). Open: hay un intento vigente esperando el pago. Paid: confirmado y aplicado (existe
// un pago de propietario aprobado). Closed: termino sin pago (rechazado, cancelado o vencido). ReviewRequired: el proveedor cobro pero no
// se pudo aplicar (el importe cambio, el comprobante ya estaba pagado, mes cerrado...) y la administracion lo resuelve. Reversed: estuvo
// pagado y se revirtio.
public enum PaymentIntentStatus
{
    Open = 1,
    Paid = 2,
    Closed = 3,
    ReviewRequired = 4,
    Reversed = 5
}

// Estado de un intento de cobro (cada sesion abierta con el proveedor). Created: armado, todavia sin respuesta del proveedor. Pending:
// el proveedor lo acepto y espera el pago. Confirmed / Rejected / Cancelled / Expired: terminales. Reversed: confirmado y despues revertido.
public enum PaymentAttemptStatus
{
    Created = 1,
    Pending = 2,
    Confirmed = 3,
    Rejected = 4,
    Cancelled = 5,
    Expired = 6,
    Reversed = 7
}

// Sentido de un evento del registro de cobros: Inbound lo recibimos del proveedor (aviso), Outbound lo pedimos nosotros (crear, consultar,
// revertir) y Internal es una decision propia (vencimiento, revision).
public enum PaymentEventDirection
{
    Inbound = 1,
    Outbound = 2,
    Internal = 3
}
