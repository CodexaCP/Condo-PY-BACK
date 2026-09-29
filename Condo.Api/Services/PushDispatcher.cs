using Condo.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

public class PushDispatcher(ICondoDbContext dbContext, IPushNotificationSender sender, ILogger<PushDispatcher> logger)
{
    public Task NotifyUserAsync(Guid recipientId, string title, string body, string? entityType, Guid? entityId, CancellationToken ct, string? notificationType = null)
        => NotifyUsersAsync(new[] { recipientId }, title, body, entityType, entityId, ct, notificationType);

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
