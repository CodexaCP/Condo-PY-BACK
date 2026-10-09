using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

public sealed record PaymentReminderResult(int Notifications);

/// <summary>
/// Avisos de vencimiento (Centro de configuracion, reglas de avisos): para los edificios que encendieron "antes del vencimiento" o "el dia
/// del vencimiento", avisa a los propietarios y residentes de las unidades que todavia deben el comprobante de un periodo publicado que vence
/// ese dia (o dentro de N dias). Un solo aviso por persona y periodo: si ya se le mando uno del mismo tipo para ese periodo, no se repite. No
/// avisa a quien ya pago (los pagos revertidos no cuentan como pagados). Solo envia de dia, desde las 8 de la manana de Paraguay.
/// </summary>
public sealed class PaymentReminderRunner(ICondoDbContext dbContext, PushDispatcher? push = null)
{
    public const int FirstHour = 8;

    public async Task<PaymentReminderResult> RunAsync(DateOnly today, int localHour, CancellationToken ct)
    {
        if (localHour < FirstHour) return new PaymentReminderResult(0);

        var rules = await dbContext.BuildingNoticeRules.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && (x.Kind == NoticeKind.BeforeDue || x.Kind == NoticeKind.OnDue))
            .ToListAsync(ct);
        if (rules.Count == 0) return new PaymentReminderResult(0);

        var buildingIds = rules.Select(x => x.BuildingId).Distinct().ToList();
        var buildings = (await dbContext.Buildings.AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsActive && buildingIds.Contains(x.Id))
                .ToListAsync(ct))
            .ToDictionary(x => x.Id);

        var sent = 0;
        foreach (var rule in rules)
        {
            if (!buildings.TryGetValue(rule.BuildingId, out var building)) continue;

            var daysBefore = rule.Kind == NoticeKind.BeforeDue ? rule.OffsetDays ?? NoticeRules.DefaultOffsetDays : 0;
            var dueDate = today.AddDays(daysBefore);
            sent += await RemindAsync(building, rule.Kind, dueDate, daysBefore, ct);
        }

        return new PaymentReminderResult(sent);
    }

    private async Task<int> RemindAsync(Building building, NoticeKind kind, DateOnly dueDate, int daysBefore, CancellationToken ct)
    {
        var periods = await dbContext.ExpensePeriods.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == building.Id && x.Status == ExpensePeriodStatus.Published && x.DueDate == dueDate)
            .Select(x => new { x.Id, x.Name, x.CompanyId })
            .ToListAsync(ct);
        if (periods.Count == 0) return 0;

        var type = kind == NoticeKind.BeforeDue ? NotificationType.PaymentDueSoon : NotificationType.PaymentDueToday;
        var sent = 0;

        foreach (var period in periods)
        {
            var charges = (await dbContext.ExpenseCharges.AsNoTracking()
                    .Where(x => !x.IsDeleted && x.ExpensePeriodId == period.Id)
                    .Select(x => new { x.UnitId, x.Amount })
                    .ToListAsync(ct))
                .GroupBy(x => x.UnitId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
            if (charges.Count == 0) continue;

            var paid = (await dbContext.Payments.AsNoTracking()
                    .Where(x => !x.IsDeleted && !x.IsReversed && x.ExpensePeriodId == period.Id)
                    .Select(x => new { x.UnitId, x.Amount })
                    .ToListAsync(ct))
                .GroupBy(x => x.UnitId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

            var owingUnits = charges.Where(c => c.Value - paid.GetValueOrDefault(c.Key, 0m) > 0m).Select(c => c.Key).ToList();
            if (owingUnits.Count == 0) continue;

            var recipientsByUnit = await NoticeRecipients.ForUnitsAsync(dbContext, owingUnits, ct);
            var recipients = recipientsByUnit.Values.SelectMany(x => x).ToHashSet();
            if (recipients.Count == 0) continue;

            // Un solo aviso por persona, tipo y periodo.
            var already = (await dbContext.Notifications.AsNoTracking()
                    .Where(x => !x.IsDeleted && x.Type == type && x.EntityId == period.Id && recipients.Contains(x.RecipientId))
                    .Select(x => x.RecipientId)
                    .ToListAsync(ct))
                .ToHashSet();
            recipients.ExceptWith(already);
            if (recipients.Count == 0) continue;

            var avoidLateFee = building.LateFeeRatePercentage is > 0m ? " para evitar la mora" : string.Empty;
            var (title, body) = kind == NoticeKind.BeforeDue
                ? ("Tu expensa vence pronto",
                   $"La expensa de {period.Name} vence el {dueDate:dd/MM/yyyy} ({(daysBefore == 1 ? "mañana" : $"en {daysBefore} días")}). Pagá a tiempo{avoidLateFee}.")
                : ("Tu expensa vence hoy",
                   $"La expensa de {period.Name} vence hoy ({dueDate:dd/MM/yyyy}). Pagá hoy{avoidLateFee}.");

            foreach (var recipientId in recipients)
            {
                dbContext.Notifications.Add(new Notification
                {
                    CompanyId = period.CompanyId,
                    RecipientId = recipientId,
                    Type = type,
                    Title = title,
                    Body = body,
                    EntityType = "ExpensePeriod",
                    EntityId = period.Id
                });
            }

            await dbContext.SaveChangesAsync(ct);
            sent += recipients.Count;

            if (push is not null)
            {
                await push.NotifyUsersAsync(recipients, title, body, "ExpensePeriod", period.Id, ct, type.ToString());
            }
        }

        return sent;
    }
}
