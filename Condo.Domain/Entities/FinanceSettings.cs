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

    public bool SetupCompleted { get; set; }
    public DateTime? SetupCompletedAtUtc { get; set; }
    public Guid? SetupCompletedByUserId { get; set; }

    // Ultima vez que el SuperAdmin encendio el modulo para el edificio.
    public DateTime? EnabledAtUtc { get; set; }
    public Guid? EnabledByUserId { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
}
