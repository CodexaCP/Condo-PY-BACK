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

    // Donde se edita: "building" (pestana de la ficha del edificio), "finance" (pantallas de Finanzas) o vacio si no se edita.
    public string? LinkKind { get; set; }

    // Para "building": general, legal, billing, accounting o config. Para "finance": settings o budget.
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
