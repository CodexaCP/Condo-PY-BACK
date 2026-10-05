using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Runs daily to:
///   • Auto-pause campaigns where EndDate &lt; today (IsActive → false).
///   • Send a 7-day expiry warning to the campaign creator when NotifyBeforeExpiry is true.
/// Only SuperAdmin can create campaigns (Fase 3), so CreatedByUserId is always the SuperAdmin.
/// </summary>
public sealed class AdCampaignMaintenanceService(
    IServiceScopeFactory scopeFactory,
    ILogger<AdCampaignMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

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
                logger.LogError(ex, "Error procesando mantenimiento de campañas publicitarias.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CondoDbContext>();

        var today = DateTime.UtcNow.Date;
        var warningDate = today.AddDays(7);

        var campaigns = await db.AdCampaigns
            .Where(c => !c.IsDeleted && (c.IsActive || (!c.ExpiryNotificationSent && c.NotifyBeforeExpiry)))
            .ToListAsync(ct);

        if (campaigns.Count == 0) return;

        var notifications = new List<Notification>();
        var changed = false;

        foreach (var campaign in campaigns)
        {
            // Auto-pause expired campaigns
            if (campaign.IsActive && campaign.EndDate.Date < today)
            {
                campaign.IsActive = false;
                changed = true;

                notifications.Add(new Notification
                {
                    RecipientId = campaign.CreatedByUserId,
                    CompanyId = campaign.CompanyId,
                    Type = NotificationType.AdCampaignPaused,
                    Title = "Campaña pausada automáticamente",
                    Body = $"La campaña de \"{campaign.AdvertiserName}\" fue pausada porque su fecha de vencimiento ({campaign.EndDate:dd/MM/yyyy}) ya pasó.",
                    IsRead = false,
                    EntityType = "AdCampaign",
                    EntityId = campaign.Id
                });

                logger.LogInformation(
                    "AdCampaign {Id} ({Advertiser}) pausada automáticamente — venció el {EndDate:yyyy-MM-dd}.",
                    campaign.Id, campaign.AdvertiserName, campaign.EndDate);
            }

            // 7-day expiry warning (only once per campaign)
            if (!campaign.ExpiryNotificationSent
                && campaign.NotifyBeforeExpiry
                && campaign.IsActive
                && campaign.EndDate.Date == warningDate)
            {
                campaign.ExpiryNotificationSent = true;
                changed = true;

                notifications.Add(new Notification
                {
                    RecipientId = campaign.CreatedByUserId,
                    CompanyId = campaign.CompanyId,
                    Type = NotificationType.AdCampaignExpiringSoon,
                    Title = "Campaña por vencer",
                    Body = $"La campaña de \"{campaign.AdvertiserName}\" vence el {campaign.EndDate:dd/MM/yyyy}. Renovála antes de esa fecha para evitar interrupción.",
                    IsRead = false,
                    EntityType = "AdCampaign",
                    EntityId = campaign.Id
                });

                logger.LogInformation(
                    "AdCampaign {Id} ({Advertiser}): notificación de vencimiento enviada (vence {EndDate:yyyy-MM-dd}).",
                    campaign.Id, campaign.AdvertiserName, campaign.EndDate);
            }
        }

        if (notifications.Count > 0)
            db.Notifications.AddRange(notifications);

        if (changed || notifications.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "AdCampaignMaintenanceService: {Notif} notificaciones, {Paused} campañas pausadas.",
                notifications.Count,
                notifications.Count(n => n.Type == NotificationType.AdCampaignPaused));
        }
    }
}
