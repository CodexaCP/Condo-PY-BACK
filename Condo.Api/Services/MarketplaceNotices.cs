using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Avisos del marketplace compartidos por los servicios: notificacion en la app (se guarda con el cambio) y push (se manda
/// despues de guardar, fuera de la transaccion), a quienes revisan en el edificio, y el formato de montos y horarios.
/// </summary>
internal static class MarketplaceNotices
{
    /// <summary>Agrega la notificacion al contexto y deja el push en la lista para mandarlo despues de guardar.</summary>
    public static void Add(
        CondoDbContext db, Guid companyId, Guid recipientId, NotificationType type, string title, string body,
        string entityType, Guid entityId, List<MarketplacePushItem> pushes)
    {
        db.Notifications.Add(new Notification
        {
            CompanyId = companyId,
            RecipientId = recipientId,
            Type = type,
            Title = title,
            Body = body,
            EntityType = entityType,
            EntityId = entityId
        });
        pushes.Add(new MarketplacePushItem(recipientId, title, body, entityId, entityType));
    }

    /// <summary>
    /// Quienes revisan y resuelven en el edificio: el Administrador de empresa (de la empresa y condominio del edificio) y los
    /// Encargados y Operadores con acceso al edificio. Es la misma regla que ya usa el pago de expensas.
    /// </summary>
    public static async Task<List<Guid>> ReviewerIdsAsync(CondoDbContext db, Guid companyId, Guid buildingId, CancellationToken ct)
    {
        var condominiumId = await db.Buildings.AsNoTracking()
            .Where(x => x.Id == buildingId).Select(x => x.CondominiumId).FirstOrDefaultAsync(ct);

        return await db.ApplicationUsers.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.CompanyId == companyId
                        && ((x.Role == UserRole.CompanyAdmin && (x.CondominiumId == null || x.CondominiumId == condominiumId))
                            || ((x.Role == UserRole.BuildingManager || x.Role == UserRole.CompanyOperator)
                                && x.BuildingAccesses.Any(a => !a.IsDeleted && a.IsActive && a.BuildingId == buildingId))))
            .Select(x => x.Id)
            .ToListAsync(ct);
    }

    public static async Task DispatchAsync(PushDispatcher push, IReadOnlyList<MarketplacePushItem> pushes)
    {
        foreach (var item in pushes)
        {
            await push.NotifyUserAsync(item.RecipientId, item.Title, item.Body, item.EntityType, item.EntityId, CancellationToken.None);
        }
    }

    public static string Gs(decimal amount) => $"Gs. {amount:N0}";

    public static string FormatRange(DateTime startsUtc, DateTime endsUtc)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Asuncion");
        var s = TimeZoneInfo.ConvertTimeFromUtc(MarketplaceReservationViews.AsUtc(startsUtc), tz);
        var e = TimeZoneInfo.ConvertTimeFromUtc(MarketplaceReservationViews.AsUtc(endsUtc), tz);
        return s.Date == e.Date
            ? $"el {s:dd/MM/yyyy} de {s:HH:mm} a {e:HH:mm} hs"
            : $"del {s:dd/MM/yyyy HH:mm} al {e:dd/MM/yyyy HH:mm} hs";
    }
}
