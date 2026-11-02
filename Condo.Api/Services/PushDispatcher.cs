using Condo.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

public class PushDispatcher(ICondoDbContext dbContext, IPushNotificationSender sender, ILogger<PushDispatcher> logger)
{
    public Task NotifyUserAsync(Guid recipientId, string title, string body, string? entityType, Guid? entityId, CancellationToken ct, string? notificationType = null)
        => NotifyUsersAsync(new[] { recipientId }, title, body, entityType, entityId, ct, notificationType);

    /// <summary>
    /// Un aviso a todos los celulares Android registrados (cada token una sola vez); devuelve a cuantos se les envio. Los tokens del navegador
    /// (plataforma "web") quedan fuera: el aviso habla del APK, que solo existe en Android.
    /// </summary>
    public async Task<int> NotifyAllDevicesAsync(string title, string body, string notificationType, CancellationToken ct)
    {
        try
        {
            var tokens = await dbContext.DeviceTokens
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsActive && x.Platform == "android")
                .Select(x => x.Token)
                .Distinct()
                .ToListAsync(ct);

            var data = new Dictionary<string, string> { ["type"] = notificationType };
            foreach (var token in tokens)
            {
                await sender.SendAsync(token, title, body, data, ct);
            }

            return tokens.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error enviando push notification a todos los dispositivos.");
            return 0;
        }
    }

    public async Task NotifyUsersAsync(IEnumerable<Guid> recipientIds, string title, string body, string? entityType, Guid? entityId, CancellationToken ct, string? notificationType = null)
    {
        try
        {
            var ids = recipientIds.ToList();
            if (ids.Count == 0) return;

            var tokens = await dbContext.DeviceTokens
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsActive && ids.Contains(x.UserId))
                .Select(x => x.Token)
                .ToListAsync(ct);

            if (tokens.Count == 0) return;

            var data = new Dictionary<string, string>();
            if (entityType != null) data["entityType"] = entityType;
            if (entityId != null) data["entityId"] = entityId.Value.ToString();
            if (notificationType != null) data["type"] = notificationType;

            foreach (var token in tokens)
            {
                await sender.SendAsync(token, title, body, data, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error enviando push notification.");
        }
    }
}
