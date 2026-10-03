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

    // El usuario actual puede modificar la configuracion (fecha de arranque, cuentas y plan de cuentas): solo SuperAdmin.
    public bool CanEdit { get; set; }

    // El usuario actual puede modificar el presupuesto (SuperAdmin y Administrador de empresa).
    public bool CanEditBudget { get; set; }
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
    // La cuenta tiene una funcion especial (SystemKey): se puede renombrar, recodificar y desactivar, pero no mover, cambiar de tipo ni eliminar.
    public bool IsTemplate { get; set; }
    public bool HasChildren { get; set; }
    // Subrubro de gastos o ingresos (hoja): con que categoria cuenta en la liquidacion lo que se carga en el. Se deduce de la
    // plantilla o la fija quien crea el rubro. Nulas en los rubros principales y en los de fondo.
    public BuildingExpenseCategory? ExpenseCategory { get; set; }
    public BuildingIncomeCategory? IncomeCategory { get; set; }
    // Ya tiene gastos o ingresos cargados: no se elimina ni se le cambia el tipo o la categoria; solo se desactiva.
    public bool HasMovements { get; set; }
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
    // Solo en las cuentas (hojas) de gastos o ingresos sin funcion especial: categoria de la liquidacion (sin dato, la que ya tenia o "Otro").
    public BuildingExpenseCategory? ExpenseCategory { get; set; }
    public BuildingIncomeCategory? IncomeCategory { get; set; }
    // Funcion especial de la cuenta (por ejemplo "Collection.Ordinary"): sin dato se conserva la que tenia; vacio la quita; con valor la asigna
    // (si otra cuenta la tenia, se la quita: cada funcion va en una sola cuenta).
    public string? SystemKey { get; set; }
}

public enum LedgerPlanApplyMode
{
    // Sustituye el plan entero (desvincula rubros de gastos e ingresos y borra el presupuesto).
    Replace = 1,
    // Solo agrega las cuentas que faltan (por codigo).
    AddMissing = 2,
    // Agrega las que faltan y actualiza nombre, codigo del contador, estado y categoria de las que ya existen.
    Update = 3
}

/// <summary>Lo que se desvincula o se borra al reemplazar el plan de cuentas de un edificio.</summary>
public class LedgerPlanImpactDto
{
    public int Categories { get; set; }
    public int Expenses { get; set; }
    public int Incomes { get; set; }
    public int RecurringExpenses { get; set; }
    public int BudgetLines { get; set; }
    public bool HasImpact => Expenses + Incomes + RecurringExpenses + BudgetLines > 0;
}

public class LedgerPlanApplyRequest
{
    public Guid BuildingId { get; set; }
    public LedgerPlanApplyMode Mode { get; set; } = LedgerPlanApplyMode.AddMissing;
    // Obligatorio para reemplazar cuando hay gastos, ingresos, plantillas o presupuesto que se pierden.
    public bool ConfirmReplace { get; set; }
}

public class LedgerPlanApplyResultDto
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int RemovedCategories { get; set; }
    public int UnlinkedExpenses { get; set; }
    public int UnlinkedIncomes { get; set; }
    public int UnlinkedRecurringExpenses { get; set; }
    public int DeletedBudgetLines { get; set; }
    public List<string> Messages { get; set; } = new();
}

/// <summary>Una fila del plan importado: salida de la vista previa y entrada de la confirmacion (el usuario puede corregir tipo y categoria).</summary>
public class LedgerPlanImportRowDto
{
    public int RowNumber { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentCode { get; set; }
    public LedgerCategoryType Type { get; set; }
    public int Level { get; set; }
    public bool IsLeaf { get; set; }
    public string? ExternalCode { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SystemKey { get; set; }
    public BuildingExpenseCategory? ExpenseCategory { get; set; }
    public BuildingIncomeCategory? IncomeCategory { get; set; }
    // La categoria la sugirio el sistema por el nombre.
    public bool CategorySuggested { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class LedgerPlanImportPreviewDto
{
    public List<LedgerPlanImportRowDto> Rows { get; set; } = new();
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public bool HasErrors { get; set; }
    // Lo que se perderia si se reemplaza el plan actual por este.
    public LedgerPlanImpactDto Impact { get; set; } = new();
}

public class LedgerPlanImportCommitRequest
{
    public Guid BuildingId { get; set; }
    // Replace o Update (AddMissing tambien se acepta: no toca las cuentas existentes).
    public LedgerPlanApplyMode Mode { get; set; } = LedgerPlanApplyMode.Replace;
    public bool ConfirmReplace { get; set; }
    public List<LedgerPlanImportRowDto> Rows { get; set; } = new();
}

public class LedgerCategoryBulkActiveRequest
{
    public Guid BuildingId { get; set; }
    public List<Guid> Ids { get; set; } = new();
    public bool IsActive { get; set; }
}

public class LedgerCategoryCopyRequest
{
    public Guid SourceBuildingId { get; set; }
    public Guid TargetBuildingId { get; set; }
}

public class LedgerCategoryCopyResultDto
{
    public int Updated { get; set; }
    public int Created { get; set; }
    public int Skipped { get; set; }
    // Lo que no se pudo copiar y por que (codigos repetidos, tipos distintos).
    public List<string> Messages { get; set; } = new();
}
