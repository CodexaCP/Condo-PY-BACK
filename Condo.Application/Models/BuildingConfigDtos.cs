using Condo.Domain.Enums;

namespace Condo.Application.Models;

// ── Centro de configuracion del edificio ──────────────────────────────────────

/// <summary>Estados posibles de una seccion del Centro (se envian como texto).</summary>
public static class ConfigSectionStatus
{
    public const string Complete = "Complete";
    public const string Incomplete = "Incomplete";
    public const string Optional = "Optional";
    public const string NotAvailable = "NotAvailable";
}

public class BuildingConfigOverviewDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;

    // Las secciones que el usuario puede ver, en orden.
    public IReadOnlyList<ConfigSectionDto> Sections { get; set; } = [];

    // Secciones obligatorias (Complete o Incomplete) y cuantas estan completas. Las opcionales y las no disponibles no cuentan.
    public int RequiredCount { get; set; }
    public int ReadyCount { get; set; }
    public bool ReadyToOperate { get; set; }

    // El modulo Finanzas esta disponible en el edificio (habilitado y con plan que lo incluye).
    public bool FinanceAvailable { get; set; }

    // El usuario puede ver el historial de cambios.
    public bool CanViewAudit { get; set; }
}

public class ConfigSectionDto
{
    public string Key { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = ConfigSectionStatus.Optional;

    // Cuenta para el indicador de "listo para operar". Las secciones opcionales muestran su estado pero no cuentan.
    public bool Required { get; set; }

    // Por que esta incompleta u opcional / no disponible (texto para mostrar).
    public IReadOnlyList<string> Reasons { get; set; } = [];

    // Resumen de solo lectura de lo configurado.
    public IReadOnlyList<ConfigSummaryItemDto> Summary { get; set; } = [];

    // El usuario puede editar esta seccion.
    public bool CanEdit { get; set; }

    // Donde se edita: "building" (pestana de la ficha del edificio), "finance" (pantallas de Finanzas), "self" (en el propio Centro) o
    // vacio si no se edita.
    public string? LinkKind { get; set; }

    // Para "building": general, legal, billing, accounting o config. Para "finance": settings o budget. Para "self" (una seccion del
    // propio Centro): la clave de la seccion.
    public string? LinkTab { get; set; }
}

public class ConfigSummaryItemDto
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class ConfigChangeDto
{
    public string Field { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Before { get; set; }
    public string? After { get; set; }
}

public class ConfigAuditEntryDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Section { get; set; } = string.Empty;
    public string SectionName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public IReadOnlyList<ConfigChangeDto> Changes { get; set; } = [];
}

public class ConfigAuditPageDto
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<ConfigAuditEntryDto> Items { get; set; } = [];
}

// ── Periodo y cierre ──────────────────────────────────────────────────────────

/// <summary>Estados de un mes en la pantalla del cierre (se envian como texto).</summary>
public static class ClosingMonthStatus
{
    public const string Open = "Open";
    public const string Closed = "Closed";
    // El mes en curso: todavia no terminó, no se puede cerrar.
    public const string Current = "Current";
}

public class PeriodClosingDto
{
    public Guid BuildingId { get; set; }

    // Finanzas esta disponible en el edificio (el cierre depende del modulo).
    public bool FinanceAvailable { get; set; }

    // El interruptor del cierre del edificio.
    public bool Enabled { get; set; }

    // El usuario puede encender/apagar, cerrar y reabrir.
    public bool CanEdit { get; set; }

    public DateOnly? FinanceStartDate { get; set; }
    public int FiscalYearStartMonth { get; set; }

    // Del mes de arranque al mes en curso, en orden cronologico.
    public IReadOnlyList<ClosingMonthDto> Months { get; set; } = [];

    // Los ultimos cierres (vigentes y reabiertos), el mas reciente primero.
    public IReadOnlyList<ClosureHistoryDto> History { get; set; } = [];
}

public class ClosingMonthDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Status { get; set; } = ClosingMonthStatus.Open;
    public DateTime? ClosedAtUtc { get; set; }
    public string? ClosedByName { get; set; }

    // Se puede cerrar ahora: cierre encendido, mes terminado y sin meses anteriores abiertos.
    public bool CanClose { get; set; }
    // Si no se puede cerrar, por que.
    public string? CannotCloseReason { get; set; }
    public bool CanReopen { get; set; }
}

public class ClosureHistoryDto
{
    public Guid Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateTime ClosedAtUtc { get; set; }
    public string ClosedByName { get; set; } = string.Empty;
    public DateTime? ReopenedAtUtc { get; set; }
    public string? ReopenedByName { get; set; }
    public string? ReopenReason { get; set; }
}

public class SetPeriodClosingRequest
{
    public bool Enabled { get; set; }
}

public class ReopenPeriodRequest
{
    public string Reason { get; set; } = string.Empty;
}

// ── Politica de mora ──────────────────────────────────────────────────────────

public class LateFeePolicyDto
{
    public Guid BuildingId { get; set; }

    // Tasa de interes por intervalo (vacio = el edificio no cobra mora) y cada cuanto se suma.
    public decimal? RatePercentage { get; set; }
    public LateFeeFrequency? Frequency { get; set; }

    // Dias de gracia: la mora arranca vencimiento + gracia en los periodos NUEVOS (los existentes conservan su fecha de corte).
    public int? GraceDays { get; set; }

    // Tope acumulado por unidad y periodo, en % de la base (vacio = sin tope), y mora minima por intervalo (vacio = sin minimo).
    public decimal? CapPercentage { get; set; }
    public decimal? MinAmount { get; set; }

    // Que cargos entran en la base de calculo (las expensas ordinarias y los ajustes siempre entran).
    public bool AppliesToReserve { get; set; } = true;
    public bool AppliesToExtraordinary { get; set; } = true;
    public bool AppliesToIndividual { get; set; } = true;

    // El administrador reviso la politica. Con la tasa vacia significa "este edificio no cobra mora".
    public bool Confirmed { get; set; }

    // Unidades exoneradas de mora (informativo; se marcan desde la unidad).
    public int ExemptUnitCount { get; set; }

    public bool CanEdit { get; set; }
}

public class UpdateLateFeePolicyRequest
{
    public decimal? RatePercentage { get; set; }
    public LateFeeFrequency? Frequency { get; set; }
    public int? GraceDays { get; set; }
    public decimal? CapPercentage { get; set; }
    public decimal? MinAmount { get; set; }
    public bool AppliesToReserve { get; set; } = true;
    public bool AppliesToExtraordinary { get; set; } = true;
    public bool AppliesToIndividual { get; set; } = true;
}

public class UnitLateFeeExemptionRequest
{
    public bool Exempt { get; set; }
    // Obligatorio al exonerar (hasta 300 caracteres).
    public string? Reason { get; set; }
}

// ── Fondos ────────────────────────────────────────────────────────────────────

public class FundPolicyDto
{
    public Guid BuildingId { get; set; }
    public IncomeTreatment IncomeTreatment { get; set; }
    public decimal? ReserveFundPercentage { get; set; }
    public decimal? ExtraordinaryPercentage { get; set; }
    public ReserveUsePolicy ReserveUsePolicy { get; set; }
    public decimal? ReserveUseThreshold { get; set; }
    // El administrador reviso la politica. Sin aportes configurados significa "este edificio no tiene aportes a fondos".
    public bool Confirmed { get; set; }
    public bool CanEdit { get; set; }
}

public class UpdateFundPolicyRequest
{
    public IncomeTreatment IncomeTreatment { get; set; } = IncomeTreatment.CreditToOwners;
    public decimal? ReserveFundPercentage { get; set; }
    public decimal? ExtraordinaryPercentage { get; set; }
    public ReserveUsePolicy ReserveUsePolicy { get; set; } = ReserveUsePolicy.FreeUse;
    public decimal? ReserveUseThreshold { get; set; }
}

// ── Alertas del presupuesto ───────────────────────────────────────────────────

public class BudgetAlertsDto
{
    public Guid BuildingId { get; set; }
    public bool FinanceAvailable { get; set; }
    // Desvio (en %) hasta el cual el renglon queda en amarillo; mas alla, en rojo (1 a 100).
    public int WarnPercent { get; set; } = 10;
    public bool CanEdit { get; set; }
}

public class UpdateBudgetAlertsRequest
{
    public int WarnPercent { get; set; } = 10;
}

// ── Avisos automaticos ────────────────────────────────────────────────────────

public class NoticeRuleDto
{
    public NoticeKind Kind { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    // Solo BeforeDue: dias de anticipacion (1 a 30).
    public int? OffsetDays { get; set; }
    // Sin regla guardada: se muestra el valor por defecto.
    public bool IsDefault { get; set; }
}

public class NoticeRulesDto
{
    public Guid BuildingId { get; set; }
    public IReadOnlyList<NoticeRuleDto> Rules { get; set; } = [];
    public bool CanEdit { get; set; }
}

public class UpdateNoticeRuleItem
{
    public NoticeKind Kind { get; set; }
    public bool IsActive { get; set; }
    public int? OffsetDays { get; set; }
}

public class UpdateNoticeRulesRequest
{
    public List<UpdateNoticeRuleItem> Rules { get; set; } = [];
}
