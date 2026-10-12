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
