using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Runs daily to:
///   • Send expiry alerts at -5d, -1d, 0d, +1d, +GracePeriodDays relative to EndDate.
///   • Suspend (IsActive=false) plans that have exceeded the grace period.
/// AlertLevel on BuildingPlan tracks which milestone was last processed:
///   1 = -5d sent  |  2 = -1d sent  |  3 = 0d sent  |  4 = +1d sent  |  5 = suspended
/// </summary>
public sealed class PlanExpiryService(
    IServiceScopeFactory scopeFactory,
    ILogger<PlanExpiryService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error procesando vencimientos de planes.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CondoDbContext>();

        var today = DateTime.UtcNow.Date;

        // Only active, non-archived plans that haven't been fully processed (AlertLevel < 5)
        var plans = await db.BuildingPlans
            .Include(x => x.Plan)
            .Include(x => x.Building)
            .Where(x => !x.IsDeleted && !x.IsArchived && x.IsActive && (x.AlertLevel == null || x.AlertLevel < 5))
            .ToListAsync(ct);

        if (plans.Count == 0) return;

        // Get all users that should receive alerts per building
        var buildingIds = plans.Select(x => x.BuildingId).Distinct().ToList();

        // CompanyAdmin and BuildingManager users for each building
        var usersByBuilding = await db.UserBuildingAccesses
            .AsNoTracking()
            .Include(x => x.ApplicationUser)
            .Where(x => !x.IsDeleted && x.IsActive && buildingIds.Contains(x.BuildingId)
                     && x.ApplicationUser != null && !x.ApplicationUser.IsDeleted && x.ApplicationUser.IsActive)
            .GroupBy(x => x.BuildingId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.ApplicationUser!).ToList(), ct);

        // Also include CompanyAdmin users scoped to the company (not via building access)
        var companyIds = plans
            .Where(x => x.Building?.CompanyId != null)
            .Select(x => x.Building!.CompanyId!.Value)
            .Distinct()
            .ToList();

        var companyAdmins = await db.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive
                     && x.Role == UserRole.CompanyAdmin
                     && x.CompanyId != null && companyIds.Contains(x.CompanyId.Value))
            .ToListAsync(ct);

        var notifications = new List<Notification>();
        var changed = false;

        foreach (var bp in plans)
        {
            var graceDays = bp.Plan?.GracePeriodDays ?? 5;
            var daysRemaining = (int)(bp.EndDate.Date - today).TotalDays;
            var currentLevel = bp.AlertLevel ?? 0;

            // Determine which milestone to act on
            if (daysRemaining >= 5 && currentLevel < 1)
            {
                // Not yet at -5d window — nothing to do
                continue;
            }

            if (daysRemaining >= 1 && daysRemaining < 5 && currentLevel < 1)
            {
                // Between -5d and -2d — send the -5d alert (slightly late, still relevant)
                await SendAlertAsync(db, bp, 1, NotificationType.PlanExpiringSoon,
                    "Plan próximo a vencer",
                    $"El plan \"{bp.Plan?.Name}\" del edificio \"{bp.Building?.Name}\" vence en {daysRemaining} día(s).",
                    usersByBuilding, companyAdmins, notifications, ct);
                bp.AlertLevel = 1;
                changed = true;
                continue;
            }

            if (daysRemaining == 1 && currentLevel < 2)
            {
                await SendAlertAsync(db, bp, 2, NotificationType.PlanExpiringSoon,
                    "Plan vence mañana",
                    $"El plan \"{bp.Plan?.Name}\" del edificio \"{bp.Building?.Name}\" vence mañana. Asegúrese de tener el comprobante listo.",
                    usersByBuilding, companyAdmins, notifications, ct);
                bp.AlertLevel = 2;
                changed = true;
                continue;
            }

            if (daysRemaining == 0 && currentLevel < 3)
            {
                await SendAlertAsync(db, bp, 3, NotificationType.PlanExpired,
                    "Plan vence hoy",
                    $"El plan \"{bp.Plan?.Name}\" del edificio \"{bp.Building?.Name}\" vence hoy. Dispone de {graceDays} día(s) de gracia.",
                    usersByBuilding, companyAdmins, notifications, ct);
                bp.AlertLevel = 3;
                changed = true;
                continue;
            }

            if (daysRemaining == -1 && currentLevel < 4)
            {
                await SendAlertAsync(db, bp, 4, NotificationType.PlanExpired,
                    "Plan vencido — período de gracia activo",
                    $"El plan \"{bp.Plan?.Name}\" del edificio \"{bp.Building?.Name}\" está vencido. Restan {graceDays - 1} día(s) de gracia antes de la suspensión.",
                    usersByBuilding, companyAdmins, notifications, ct);
                bp.AlertLevel = 4;
                changed = true;
                continue;
            }

            if (daysRemaining <= -graceDays && currentLevel < 5)
            {
                // Suspend the plan
                bp.IsActive = false;
                bp.AlertLevel = 5;

                await SendAlertAsync(db, bp, 5, NotificationType.PlanSuspended,
                    "Acceso suspendido — plan vencido",
                    $"El plan \"{bp.Plan?.Name}\" del edificio \"{bp.Building?.Name}\" ha sido suspendido por falta de pago. Contacte al administrador.",
                    usersByBuilding, companyAdmins, notifications, ct);
                changed = true;

                logger.LogWarning(
                    "BuildingPlan {Id} suspendido — edificio {Building}, plan {Plan}.",
                    bp.Id, bp.Building?.Name, bp.Plan?.Name);
            }
        }

        if (notifications.Count > 0)
            db.Notifications.AddRange(notifications);

        if (changed || notifications.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "PlanExpiryService: {Notif} notificaciones enviadas, {Plans} planes actualizados.",
                notifications.Count, plans.Count(x => x.AlertLevel.HasValue));
        }
    }

    private static Task SendAlertAsync(
        CondoDbContext db,
        BuildingPlan bp,
        byte level,
        NotificationType type,
        string title,
        string body,
        Dictionary<Guid, List<ApplicationUser>> usersByBuilding,
        List<ApplicationUser> companyAdmins,
        List<Notification> notifications,
        CancellationToken ct)
    {
        var companyId = bp.Building?.CompanyId ?? bp.Building?.Condominium?.CompanyId;

        // Recipients = building-level users + company-level admins for this building's company
        var recipients = new HashSet<Guid>();

        if (usersByBuilding.TryGetValue(bp.BuildingId, out var buildingUsers))
            foreach (var u in buildingUsers) recipients.Add(u.Id);

        if (companyId.HasValue)
            foreach (var u in companyAdmins.Where(u => u.CompanyId == companyId))
                recipients.Add(u.Id);

        foreach (var userId in recipients)
        {
            notifications.Add(new Notification
            {
                RecipientId = userId,
                CompanyId = companyId ?? Guid.Empty,
                Type = type,
                Title = title,
                Body = body,
                IsRead = false,
                EntityType = "BuildingPlan",
                EntityId = bp.Id
            });
        }

        return Task.CompletedTask;
    }
}
