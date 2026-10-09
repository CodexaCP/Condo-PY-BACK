using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Configuracion del modulo "Finanzas del edificio" (una por edificio). El interruptor del modulo vive en
// Building.FinanceModuleEnabled; aca queda la fecha de arranque y el estado del asistente de configuracion.
// Regla fija de criterio (decision 1): caja y saldos por lo percibido; cuentas por cobrar y morosidad por lo devengado.
public class FinanceSettings : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }

    // El modulo solo considera movimientos desde esta fecha, mas los saldos iniciales de las cuentas.
    public DateOnly? FinanceStartDate { get; set; }

    // Mes (1-12) en que empieza el ejercicio.
    public int FiscalYearStartMonth { get; set; } = 1;

    // Cuenta (caja o banco) donde el libro asienta lo que no trae cuenta propia: cobros que no son en efectivo, ingresos y gastos
    // del edificio. Nula = el libro usa el unico banco activo, si hay uno solo.
    public Guid? DefaultAccountId { get; set; }

    // Cierre de periodo (Centro de configuracion): opcional por edificio y apagado por defecto. Con el interruptor apagado los meses
    // cerrados no bloquean nada; no se puede apagar mientras haya meses cerrados (primero se reabren, con motivo).
    public bool PeriodClosingEnabled { get; set; }

    public bool SetupCompleted { get; set; }
    public DateTime? SetupCompletedAtUtc { get; set; }
    public Guid? SetupCompletedByUserId { get; set; }

    // Ultima vez que el SuperAdmin encendio el modulo para el edificio.
    public DateTime? EnabledAtUtc { get; set; }
    public Guid? EnabledByUserId { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
}
