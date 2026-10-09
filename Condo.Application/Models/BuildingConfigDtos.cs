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
