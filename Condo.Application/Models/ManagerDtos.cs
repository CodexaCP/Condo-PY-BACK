namespace Condo.Application.Models;

/// <summary>Resumen liviano de UN edificio para la seccion del Encargado en la app.</summary>
public class ManagerSummaryDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;

    // Cosas que esperan una accion del Encargado.
    public int PendingOwnerPayments { get; set; }
    public int UnderReviewOwnerPayments { get; set; }
    public int PendingClaims { get; set; }
    public int InProgressClaims { get; set; }
    public int PendingReservations { get; set; }

    // Periodo vigente: el del mes actual o, si no existe, el ultimo no borrador. null si el edificio no tiene periodos.
    public ManagerPeriodDto? CurrentPeriod { get; set; }
    public decimal CurrentPeriodCharged { get; set; }
    public decimal CurrentPeriodCollected { get; set; }
    public decimal CollectionRatePercentage { get; set; }

    // Morosidad del edificio: periodos ya publicados o cerrados y vencidos, con saldo pendiente.
    public decimal OverdueBalance { get; set; }
    public int UnitsInArrears { get; set; }

    public ManagerPlanDto? Plan { get; set; }
}

public class ManagerPeriodDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
}

public class ManagerPlanDto
{
    public string Name { get; set; } = string.Empty;

    // Active | ExpiringSoon | Expired | ReadOnly | Blocked
    public string Status { get; set; } = string.Empty;
    public DateTime EndDate { get; set; }
    public int DaysUntilExpiry { get; set; }

    // Solo con el plan vencido; null mientras esta vigente.
    public int? DaysUntilBlocked { get; set; }
}

/// <summary>Version de la app Android: la app compara su versionCode con estos valores al abrir.</summary>
public class AppVersionDto
{
    public int MinVersionCode { get; set; }
    public int LatestVersionCode { get; set; }
    public string ApkUrl { get; set; } = string.Empty;
    public string? Message { get; set; }
}
