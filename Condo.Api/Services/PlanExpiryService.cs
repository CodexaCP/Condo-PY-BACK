using System.Net;
using Condo.Application.Abstractions;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Corre cada 6 horas y avisa del vencimiento de los planes de edificio:
///   • Por correo (Resend) y en la campanita del sistema, solo a Administrador de empresa, Operador y
///     Encargado de edificio de la empresa y del edificio del plan. No se manda push.
///   • Las restricciones (solo lectura / bloqueo total) NO las aplica este servicio: se calculan por fechas
///     en <see cref="PlanAccessPolicy"/>. Aca solo se avisa cuando empieza cada etapa.
/// AlertLevel en BuildingPlan guarda el ultimo aviso enviado del periodo actual:
///   1 = faltan 5..2 dias | 2 = vence manana | 3 = vence hoy | 4 = vencido, en gracia
///   5 = termino la gracia: solo lectura | 6 = bloqueo total
/// Si hay varios hitos atrasados (servicio caido) solo se manda el mas reciente.
/// </summary>
public sealed class PlanExpiryService(
    IServiceScopeFactory scopeFactory,
    ILogger<PlanExpiryService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private static readonly UserRole[] RecipientRoles =
        [UserRole.CompanyAdmin, UserRole.CompanyOperator, UserRole.BuildingManager];

    private sealed record Alert(byte Level, NotificationType Type, string Title, string Body);

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
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var today = DateTime.UtcNow.Date;

        // Planes vigentes que todavia no llegaron al ultimo aviso (bloqueo total).
        var plans = await db.BuildingPlans
            .Include(x => x.Plan)
            .Include(x => x.Building)
                .ThenInclude(b => b!.Condominium)
            .Where(x => !x.IsDeleted && !x.IsArchived && (x.AlertLevel == null || x.AlertLevel < 6))
            .ToListAsync(ct);

        if (plans.Count == 0) return;

        var pending = new List<(BuildingPlan Plan, Alert Alert)>();
        foreach (var bp in plans)
        {
            var alert = NextAlert(bp, today);
            if (alert is not null) pending.Add((bp, alert));
        }

        if (pending.Count == 0) return;

        var buildingIds = pending.Select(x => x.Plan.BuildingId).Distinct().ToList();

        // Operadores y encargados con acceso a cada edificio.
        var buildingStaff = await db.UserBuildingAccesses
            .AsNoTracking()
            .Include(x => x.ApplicationUser)
            .Where(x => !x.IsDeleted && x.IsActive && buildingIds.Contains(x.BuildingId)
                     && x.ApplicationUser != null && !x.ApplicationUser.IsDeleted && x.ApplicationUser.IsActive
                     && RecipientRoles.Contains(x.ApplicationUser.Role))
            .ToListAsync(ct);
        var staffByBuilding = buildingStaff
            .GroupBy(x => x.BuildingId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ApplicationUser!).ToList());

        // Administradores de la empresa dueña de cada edificio.
        var companyIds = pending
            .Select(x => CompanyOf(x.Plan))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var companyAdmins = await db.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive
                     && x.Role == UserRole.CompanyAdmin
                     && x.CompanyId != null && companyIds.Contains(x.CompanyId.Value))
            .ToListAsync(ct);

        var notifications = new List<Notification>();
        var emails = new List<(string Email, string Subject, string Html)>();

        foreach (var (bp, alert) in pending)
        {
            var companyId = CompanyOf(bp);
            var recipients = new Dictionary<Guid, ApplicationUser>();

            if (staffByBuilding.TryGetValue(bp.BuildingId, out var staff))
                foreach (var u in staff) recipients[u.Id] = u;

            if (companyId.HasValue)
                foreach (var u in companyAdmins.Where(u => u.CompanyId == companyId))
                    recipients[u.Id] = u;

            foreach (var user in recipients.Values)
            {
                notifications.Add(new Notification
                {
                    RecipientId = user.Id,
                    CompanyId = companyId ?? Guid.Empty,
                    Type = alert.Type,
                    Title = alert.Title,
                    Body = alert.Body,
                    IsRead = false,
                    EntityType = "BuildingPlan",
                    EntityId = bp.Id
                });

                if (!string.IsNullOrWhiteSpace(user.Email))
                    emails.Add((user.Email, alert.Title, BuildEmailHtml(user, bp, alert)));
            }

            bp.AlertLevel = alert.Level;
        }

        if (notifications.Count > 0)
            db.Notifications.AddRange(notifications);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PlanExpiryService: {Plans} planes con aviso nuevo, {Notif} notificaciones, {Mails} correos.",
            pending.Count, notifications.Count, emails.Count);

        // Los correos van despues de guardar para no avisar dos veces si el guardado falla; un correo que
        // falle no frena a los demas.
        foreach (var (email, subject, html) in emails)
        {
            try
            {
                await emailSender.SendAsync(email, subject, html, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "No se pudo enviar el aviso de plan a {Email}.", email);
            }
        }
    }

    private static Guid? CompanyOf(BuildingPlan bp) =>
        bp.Building?.CompanyId ?? bp.Building?.Condominium?.CompanyId;

    /// <summary>Aviso que corresponde enviar hoy para el plan, o null si ya se mando o todavia no toca.</summary>
    private static Alert? NextAlert(BuildingPlan bp, DateTime today)
    {
        var plan = bp.Plan?.Name ?? "—";
        var building = bp.Building?.Name ?? "—";
        var graceDays = bp.Plan?.GracePeriodDays ?? 5;
        var daysRemaining = (int)(bp.EndDate.Date - today).TotalDays;
        var current = bp.AlertLevel ?? 0;

        Alert? alert;

        if (daysRemaining > 5)
        {
            alert = null;
        }
        else if (daysRemaining > 1)
        {
            alert = new Alert(1, NotificationType.PlanExpiringSoon, "Plan próximo a vencer",
                $"El plan \"{plan}\" del edificio \"{building}\" vence en {daysRemaining} días. Envíe el comprobante de pago desde \"Mi plan\".");
        }
        else if (daysRemaining == 1)
        {
            alert = new Alert(2, NotificationType.PlanExpiringSoon, "Plan vence mañana",
                $"El plan \"{plan}\" del edificio \"{building}\" vence mañana. Asegúrese de tener el comprobante listo.");
        }
        else if (daysRemaining == 0)
        {
            alert = new Alert(3, NotificationType.PlanExpired, "Plan vence hoy",
                $"El plan \"{plan}\" del edificio \"{building}\" vence hoy. Dispone de {graceDays} día(s) de gracia.");
        }
        else
        {
            var overdue = -daysRemaining;
            var untilBlocked = PlanAccessPolicy.DaysUntilBlocked(bp.EndDate, graceDays, today);

            alert = PlanAccessPolicy.GetPhase(bp.EndDate, graceDays, today) switch
            {
                PlanAccessPhase.Grace => new Alert(4, NotificationType.PlanExpired,
                    "Plan vencido — período de gracia",
                    $"El plan \"{plan}\" del edificio \"{building}\" está vencido. Quedan {PlanAccessPolicy.ReadOnlyStartsAtDay(graceDays) - overdue} día(s) de gracia antes de que el sistema pase a solo lectura."),
                PlanAccessPhase.ReadOnly => new Alert(5, NotificationType.PlanSuspended,
                    "Sistema en solo lectura — plan vencido",
                    $"Terminó el período de gracia del plan \"{plan}\" del edificio \"{building}\". El sistema quedó en modo consulta: solo puede enviar el comprobante de pago desde \"Mi plan\". En {untilBlocked} día(s) se bloqueará todo el acceso."),
                _ => new Alert(6, NotificationType.PlanSuspended,
                    "Acceso bloqueado — plan vencido",
                    $"El acceso al edificio \"{building}\" está bloqueado por plan vencido (\"{plan}\"). Se habilita cuando se apruebe el pago: envíe el comprobante desde \"Mi plan\".")
            };
        }

        return alert is not null && alert.Level > current ? alert : null;
    }

    private static string BuildEmailHtml(ApplicationUser user, BuildingPlan bp, Alert alert)
    {
        string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

        return $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2937">
              <h2 style="color:#1385b6;margin:0 0 12px">{E(alert.Title)}</h2>
              <p>Hola {E(user.FirstName ?? user.FullName)},</p>
              <p>{E(alert.Body)}</p>
              <table style="border-collapse:collapse;margin:16px 0;font-size:14px">
                <tr><td style="padding:4px 12px 4px 0;color:#6b7280">Edificio</td><td><strong>{E(bp.Building?.Name)}</strong></td></tr>
                <tr><td style="padding:4px 12px 4px 0;color:#6b7280">Plan</td><td><strong>{E(bp.Plan?.Name)}</strong></td></tr>
                <tr><td style="padding:4px 12px 4px 0;color:#6b7280">Vencimiento</td><td><strong>{bp.EndDate:dd/MM/yyyy}</strong></td></tr>
              </table>
              <p>Para regularizarlo ingrese a CONDOPY, sección <strong>Mi plan</strong>, y envíe el comprobante de pago.</p>
              <p style="color:#6b7280;font-size:12px;margin-top:24px">Este es un aviso automático de CONDOPY.</p>
            </div>
            """;
    }
}
