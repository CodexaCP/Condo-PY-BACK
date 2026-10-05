using System.Net;
using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Runs daily to:
///   • Auto-pause campaigns where EndDate &lt; today (IsActive → false).
///   • When NotifyBeforeExpiry is true and 7 days or less remain: in-app notice to the creator (SuperAdmin) and an
///     email to every active SuperAdmin, the CompanyAdmins of the campaign's company and the BuildingManagers of its buildings.
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
        var expiring = new List<AdCampaign>();
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

            // Aviso de vencimiento: una sola vez por campana, cuando faltan 7 dias o menos (si el servicio
            // estuvo caido justo ese dia, el aviso sale en la siguiente corrida sin esperar al vencimiento).
            if (!campaign.ExpiryNotificationSent
                && campaign.NotifyBeforeExpiry
                && campaign.IsActive
                && campaign.EndDate.Date <= warningDate)
            {
                campaign.ExpiryNotificationSent = true;
                changed = true;
                expiring.Add(campaign);

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

        // Los correos van despues de guardar para no avisar dos veces si el guardado falla; un correo que
        // falle no frena a los demas.
        if (expiring.Count > 0)
            await SendExpiryEmailsAsync(db, scope.ServiceProvider.GetRequiredService<IEmailSender>(), expiring, ct);
    }

    /// <summary>
    /// Correo de "campana por vencer" a: todos los SuperAdmin activos, los CompanyAdmin de la empresa de la
    /// campana y los BuildingManager de los edificios donde se muestra. Cada persona recibe un solo correo
    /// por campana aunque tenga acceso a varios de sus edificios.
    /// </summary>
    private async Task SendExpiryEmailsAsync(
        CondoDbContext db, IEmailSender emailSender, List<AdCampaign> expiring, CancellationToken ct)
    {
        var campaignIds = expiring.Select(c => c.Id).ToList();
        var companyIds = expiring.Select(c => c.CompanyId).Distinct().ToList();

        var superAdmins = await db.ApplicationUsers.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.Role == UserRole.SuperAdmin)
            .ToListAsync(ct);

        var companyAdmins = await db.ApplicationUsers.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.Role == UserRole.CompanyAdmin
                     && x.CompanyId != null && companyIds.Contains(x.CompanyId.Value))
            .ToListAsync(ct);

        var campaignBuildings = await db.AdCampaignBuildings.AsNoTracking()
            .Include(x => x.Building)
            .Where(x => !x.IsDeleted && campaignIds.Contains(x.AdCampaignId))
            .ToListAsync(ct);
        var buildingIds = campaignBuildings.Select(x => x.BuildingId).Distinct().ToList();

        var managers = await db.UserBuildingAccesses.AsNoTracking()
            .Include(x => x.ApplicationUser)
            .Where(x => !x.IsDeleted && x.IsActive && buildingIds.Contains(x.BuildingId)
                     && x.ApplicationUser != null && !x.ApplicationUser.IsDeleted && x.ApplicationUser.IsActive
                     && x.ApplicationUser.Role == UserRole.BuildingManager)
            .ToListAsync(ct);

        var companyNames = await db.Companies.AsNoTracking()
            .Where(x => companyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var sent = 0;
        foreach (var campaign in expiring)
        {
            var buildings = campaignBuildings.Where(x => x.AdCampaignId == campaign.Id).ToList();
            var thisBuildingIds = buildings.Select(x => x.BuildingId).ToHashSet();
            var allNames = buildings.Select(x => x.Building?.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToList();
            companyNames.TryGetValue(campaign.CompanyId, out var companyName);

            // Cada destinatario con el texto de su rol. Un usuario tiene un solo rol, asi que no se repite.
            var emails = new List<(ApplicationUser User, string Subject, string Html)>();

            foreach (var u in superAdmins)
                emails.Add((u, "Campaña publicitaria por vencer",
                    BuildExpiryEmailHtml(u, campaign, UserRole.SuperAdmin, allNames, companyName)));

            foreach (var u in companyAdmins.Where(u => u.CompanyId == campaign.CompanyId))
                emails.Add((u, "Una campaña publicitaria de sus edificios está por vencer",
                    BuildExpiryEmailHtml(u, campaign, UserRole.CompanyAdmin, allNames, companyName)));

            // Al encargado solo se le nombran los edificios que el administra.
            foreach (var g in managers.Where(m => thisBuildingIds.Contains(m.BuildingId)).GroupBy(m => m.ApplicationUser!.Id))
            {
                var manager = g.First().ApplicationUser!;
                var theirs = buildings.Where(b => g.Any(m => m.BuildingId == b.BuildingId))
                    .Select(b => b.Building?.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToList();
                emails.Add((manager, "Una campaña publicitaria de su edificio está por vencer",
                    BuildExpiryEmailHtml(manager, campaign, UserRole.BuildingManager, theirs, companyName)));
            }

            foreach (var (user, subject, html) in emails)
            {
                if (string.IsNullOrWhiteSpace(user.Email)) continue;
                try
                {
                    await emailSender.SendAsync(user.Email, subject, html, ct);
                    sent++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "No se pudo enviar el aviso de vencimiento de la campaña {Id} a {Email}.", campaign.Id, user.Email);
                }
            }
        }

        logger.LogInformation("AdCampaignMaintenanceService: {Mails} correos de campañas por vencer.", sent);
    }

    /// <summary>Texto del correo segun el rol de quien lo recibe.</summary>
    private static string BuildExpiryEmailHtml(
        ApplicationUser user, AdCampaign campaign, UserRole role, List<string> buildingNames, string? companyName)
    {
        string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        var days = Math.Max(0, (int)(campaign.EndDate.Date - DateTime.UtcNow.Date).TotalDays);
        var when = days == 0 ? "vence hoy" : days == 1 ? "vence mañana" : $"vence en {days} días";
        var edificios = buildingNames.Count == 0 ? "—" : string.Join(", ", buildingNames.Select(E));
        var plural = buildingNames.Count > 1;

        var (title, intro, action) = role switch
        {
            UserRole.SuperAdmin => (
                "Campaña publicitaria por vencer",
                $"La campaña de <strong>{E(campaign.AdvertiserName)}</strong> {when}. Al pasar la fecha, el sistema la pausa automáticamente y deja de mostrarse en la app.",
                "Para que siga activa, coordine la renovación con el anunciante y extienda la fecha de fin desde <strong>Publicidad</strong>."),
            UserRole.CompanyAdmin => (
                "Una campaña publicitaria de sus edificios está por vencer",
                $"La publicidad de <strong>{E(campaign.AdvertiserName)}</strong>, que se muestra en {(plural ? "los edificios" : "el edificio")} de su empresa, {when}. Al pasar la fecha dejará de mostrarse a propietarios y residentes.",
                "Si desea que continúe, comuníquese con CONDOPY para gestionar la renovación. Si no se renueva, no necesita hacer nada."),
            _ => (
                "Una campaña publicitaria de su edificio está por vencer",
                $"La publicidad de <strong>{E(campaign.AdvertiserName)}</strong> que se muestra en {(plural ? "sus edificios" : "su edificio")} {when}. Después de esa fecha los propietarios y residentes dejarán de verla en la app.",
                "Es solo un aviso informativo: no tiene que hacer nada en el sistema. Si la administración quiere que continúe, puede comunicárselo a la empresa administradora.")
        };

        var companyRow = role == UserRole.SuperAdmin && !string.IsNullOrWhiteSpace(companyName)
            ? $"<tr><td style=\"padding:4px 12px 4px 0;color:#6b7280\">Empresa</td><td><strong>{E(companyName)}</strong></td></tr>"
            : string.Empty;
        var amountRow = role == UserRole.SuperAdmin && campaign.MonthlyAmount is > 0
            ? $"<tr><td style=\"padding:4px 12px 4px 0;color:#6b7280\">Monto mensual</td><td><strong>Gs. {campaign.MonthlyAmount:N0}</strong></td></tr>"
            : string.Empty;

        return $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2937">
              <h2 style="color:#1385b6;margin:0 0 12px">{E(title)}</h2>
              <p>Hola {E(user.FirstName ?? user.FullName)},</p>
              <p>{intro}</p>
              <table style="border-collapse:collapse;margin:16px 0;font-size:14px">
                <tr><td style="padding:4px 12px 4px 0;color:#6b7280">Anunciante</td><td><strong>{E(campaign.AdvertiserName)}</strong></td></tr>
                {companyRow}
                <tr><td style="padding:4px 12px 4px 0;color:#6b7280">{(plural ? "Edificios" : "Edificio")}</td><td><strong>{edificios}</strong></td></tr>
                {amountRow}
                <tr><td style="padding:4px 12px 4px 0;color:#6b7280">Vencimiento</td><td><strong>{campaign.EndDate:dd/MM/yyyy}</strong></td></tr>
              </table>
              <p>{action}</p>
              <p style="color:#6b7280;font-size:12px;margin-top:24px">Este es un aviso automático de CONDOPY.</p>
            </div>
            """;
    }
}
