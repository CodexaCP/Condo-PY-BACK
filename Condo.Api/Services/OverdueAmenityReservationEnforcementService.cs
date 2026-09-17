using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Para edificios con BlockOverdueAmenityReservations activo, cancela automáticamente
/// las reservas de amenities en estado PendingPayment de unidades que están en mora,
/// y notifica al residente/propietario que la reservó.
/// </summary>
public sealed class OverdueAmenityReservationEnforcementService(
    IServiceScopeFactory scopeFactory, ILogger<OverdueAmenityReservationEnforcementService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnforceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al cancelar reservas de amenities por mora.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task EnforceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondoDbContext>();
        var overdueService = scope.ServiceProvider.GetRequiredService<IUnitOverdueService>();

        var buildingIds = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.BlockOverdueAmenityReservations)
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (buildingIds.Count == 0) return;

        var overdueUnitIds = await overdueService.GetOverdueUnitIdsByBuildingAsync(buildingIds, ct);
        if (overdueUnitIds.Count == 0) return;

        var pendingReservations = await dbContext.AmenityReservations
            .Include(x => x.Amenity)
            .Where(x => !x.IsDeleted
                     && buildingIds.Contains(x.BuildingId)
                     && x.Status == AmenityReservationStatus.PendingPayment)
            .ToListAsync(ct);

        if (pendingReservations.Count == 0) return;

        var reservedUnitIds = await ResolveUnitIdsByUserAsync(
            dbContext, pendingReservations.Select(x => x.ReservedByUserId).Distinct(), ct);

        var cancelled = 0;
        foreach (var reservation in pendingReservations)
        {
            var userUnitIds = reservedUnitIds.GetValueOrDefault(reservation.ReservedByUserId, []);
            if (!userUnitIds.Any(overdueUnitIds.Contains)) continue;

            reservation.Status = AmenityReservationStatus.Cancelled;
            reservation.RejectionReason = "Cancelada automáticamente: la unidad tiene pagos atrasados.";

            dbContext.Notifications.Add(new Notification
            {
                CompanyId = reservation.CompanyId,
                RecipientId = reservation.ReservedByUserId,
                Type = NotificationType.AmenityReservationUpdated,
                Title = "Reserva cancelada por mora",
                Body = $"Tu reserva de {reservation.Amenity?.Name} fue cancelada automáticamente por tener pagos atrasados. " +
                       "Regularizá tu situación para volver a reservar.",
                EntityType = "AmenityReservation",
                EntityId = reservation.Id
            });

            cancelled++;
        }

        if (cancelled > 0)
        {
            await dbContext.SaveChangesAsync(ct);
            logger.LogInformation("Mora: {Count} reservas de amenities canceladas automáticamente.", cancelled);
        }
    }

    private static async Task<Dictionary<Guid, HashSet<Guid>>> ResolveUnitIdsByUserAsync(
        CondoDbContext dbContext, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.ToList();
        if (ids.Count == 0) return new Dictionary<Guid, HashSet<Guid>>();

        var users = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Role, x.Email })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, HashSet<Guid>>();

        var ownerUserIds = users.Where(x => x.Role == UserRole.Owner).Select(x => x.Id).ToList();
        if (ownerUserIds.Count > 0)
        {
            var ownerUnits = await dbContext.UnitOwners
                .AsNoTracking()
                .Where(x => !x.IsDeleted && ownerUserIds.Contains(x.OwnerId) && x.Unit != null && !x.Unit.IsDeleted)
                .Select(x => new { x.OwnerId, UnitId = x.Unit!.Id })
                .ToListAsync(ct);
            foreach (var row in ownerUnits)
                result.GetOrAdd(row.OwnerId).Add(row.UnitId);
        }

        var residentUsers = users.Where(x => x.Role == UserRole.Resident).ToList();
        if (residentUsers.Count > 0)
        {
            var emails = residentUsers.Select(x => x.Email).ToHashSet();
            var residentUnits = await dbContext.UnitResidents
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.EndDate == null
                         && x.Resident != null && !x.Resident.IsDeleted && emails.Contains(x.Resident.Email)
                         && x.Unit != null && !x.Unit.IsDeleted)
                .Select(x => new { ResidentEmail = x.Resident!.Email, UnitId = x.Unit!.Id })
                .ToListAsync(ct);

            foreach (var user in residentUsers)
            {
                foreach (var row in residentUnits.Where(x => x.ResidentEmail == user.Email))
                    result.GetOrAdd(user.Id).Add(row.UnitId);
            }
        }

        return result;
    }
}

internal static class DictionaryExtensions
{
    public static HashSet<Guid> GetOrAdd(this Dictionary<Guid, HashSet<Guid>> dict, Guid key)
    {
        if (!dict.TryGetValue(key, out var set))
        {
            set = [];
            dict[key] = set;
        }
        return set;
    }
}
