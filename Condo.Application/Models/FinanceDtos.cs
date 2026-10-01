using Condo.Domain.Enums;

namespace Condo.Application.Models;

// ── Acceso al modulo ──────────────────────────────────────────────────────────

// Edificio del usuario con el modulo "Finanzas del edificio" efectivamente disponible (habilitado y con plan que lo incluye).
public class FinanceBuildingAccessDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public bool SetupCompleted { get; set; }
}

// Fila del listado del SuperAdmin: todos los edificios con su plan y el estado del interruptor.
public class FinanceAdminBuildingDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string CondominiumName { get; set; } = string.Empty;
    public Guid? PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanStatus { get; set; } = string.Empty;
    public bool PlanIncludesFinanceModule { get; set; }
    public bool ModuleEnabled { get; set; }
    // Habilitado y con plan que lo incluye: lo que realmente ven los usuarios del edificio.
    public bool ModuleAvailable { get; set; }
    public bool SetupCompleted { get; set; }
    public DateOnly? FinanceStartDate { get; set; }
    public DateTime? EnabledAtUtc { get; set; }
}

// ── Configuracion ─────────────────────────────────────────────────────────────

public class FinanceSettingsDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public DateOnly? FinanceStartDate { get; set; }
    public int FiscalYearStartMonth { get; set; }
    public Guid? DefaultAccountId { get; set; }
    public bool SetupCompleted { get; set; }
    public DateTime? SetupCompletedAtUtc { get; set; }

    // Regla fija del modulo (decision 1): se informa en pantalla, no se elige.
    public string CashBasis { get; set; } = "Percibido";
    public string ReceivablesBasis { get; set; } = "Devengado";

    // Porcentaje de aporte al fondo de reserva del edificio (se edita en el edificio).
    public decimal? ReserveFundPercentage { get; set; }

    public int AccountCount { get; set; }
    public int CategoryCount { get; set; }

    // Lo que falta para poder completar el asistente (vacio = listo para completar).
    public IReadOnlyList<string> MissingForSetup { get; set; } = [];

    // El usuario actual puede modificar la configuracion (SuperAdmin y Administrador de empresa).
    public bool CanEdit { get; set; }
}

public class FinanceSettingsUpdateRequest
{
    public DateOnly? FinanceStartDate { get; set; }
    public int FiscalYearStartMonth { get; set; } = 1;
}

// ── Cuentas financieras ───────────────────────────────────────────────────────

public class FinancialAccountDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public FinancialAccountType Type { get; set; }
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; }
}

public class FinancialAccountUpsertRequest
{
    // Solo se usa al crear; al editar la cuenta conserva su edificio.
    public Guid BuildingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public FinancialAccountType Type { get; set; } = FinancialAccountType.Cash;
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;
}

// ── Plan de cuentas ───────────────────────────────────────────────────────────

public class LedgerCategoryDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LedgerCategoryType Type { get; set; }
    public string? ExternalCode { get; set; }
    public string? SystemKey { get; set; }
    public bool IsActive { get; set; }
    // Rubro de la plantilla estandar: se puede renombrar, recodificar y desactivar, pero no mover ni eliminar.
    public bool IsTemplate { get; set; }
    public bool HasChildren { get; set; }
}

public class LedgerCategoryUpsertRequest
{
    // Solo se usa al crear; al editar el rubro conserva su edificio.
    public Guid BuildingId { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    // Sin padre manda este tipo; con padre se hereda el del padre.
    public LedgerCategoryType Type { get; set; } = LedgerCategoryType.Expense;
    public string? ExternalCode { get; set; }
    public bool IsActive { get; set; } = true;
}
