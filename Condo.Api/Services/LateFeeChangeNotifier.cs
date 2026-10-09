using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Aviso de "Cambio en interes por mora" cuando un edificio cambia su tasa o frecuencia: lo reciben los propietarios, los residentes con
/// usuario, los encargados con acceso al edificio y los administradores y operadores de la empresa. Agrega las notificaciones al
/// contexto (las guarda el SaveChanges de quien llama) y devuelve el push pendiente, que se manda recien despues de guardar.
/// </summary>
public sealed class LateFeeChangeNotifier(ICondoDbContext dbContext, ITenantContext tenantContext)
{
    public sealed record PendingPush(HashSet<Guid> RecipientIds, string Title, string Body);

    public async Task<PendingPush> QueueAsync(
        Building building,
        Guid companyId,
        decimal? previousRate, LateFeeFrequency? previousFrequency,
        decimal? newRate, LateFeeFrequency? newFrequency,
        CancellationToken cancellationToken)
    {
        var actorId = tenantContext.UserId;
        var actorName = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => x.Id == actorId)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Un administrador";

        var recipientIds = new HashSet<Guid>();

        // Propietarios de unidades del edificio
        recipientIds.UnionWith(await dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == building.Id)
            .Select(x => x.OwnerId)
            .ToListAsync(cancellationToken));

        // Residentes activos (vinculados directamente por Id de usuario)
        recipientIds.UnionWith(await dbContext.UnitResidents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null
                     && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == building.Id
                     && x.Resident != null && !x.Resident.IsDeleted && x.Resident.ApplicationUserId != null)
            .Select(x => x.Resident!.ApplicationUserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken));

        // Managers con acceso al edificio
        recipientIds.UnionWith(await dbContext.UserBuildingAccesses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == building.Id)
            .Select(x => x.ApplicationUserId)
            .ToListAsync(cancellationToken));

        // Admins y operadores de la empresa
        recipientIds.UnionWith(await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.CompanyId == companyId
                     && (x.Role == UserRole.CompanyAdmin || x.Role == UserRole.CompanyOperator))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken));

        var previousLabel = Label(previousRate, previousFrequency);
        var newLabel = Label(newRate, newFrequency);
        var changedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        var body = $"{actorName} cambió el interés por mora del edificio {building.Name} de «{previousLabel}» a «{newLabel}» el {changedAt}.";
        const string title = "Cambio en interés por mora";

        foreach (var recipientId in recipientIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                CompanyId = companyId,
                RecipientId = recipientId,
                Type = NotificationType.LateFeeConfigChanged,
                Title = title,
                Body = body,
                EntityType = "Building",
                EntityId = building.Id
            });
        }

        return new PendingPush(recipientIds, title, body);
    }

    private static string Label(decimal? rate, LateFeeFrequency? frequency) =>
        rate.HasValue && frequency.HasValue
            ? $"{rate.Value:0.##}% {FrequencyLabel(frequency.Value)}"
            : "sin mora";

    private static string FrequencyLabel(LateFeeFrequency frequency) => frequency switch
    {
        LateFeeFrequency.Daily => "diario",
        LateFeeFrequency.Weekly => "semanal",
        LateFeeFrequency.Biweekly => "quincenal",
        _ => frequency.ToString()
    };
}
