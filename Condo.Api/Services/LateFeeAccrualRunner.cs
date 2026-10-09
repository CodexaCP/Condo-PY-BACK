using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

public sealed record LateFeeRunResult(int ChargesCreated, int UnitsNotified);

/// <summary>
/// Mora automatica: para cada edificio con tasa configurada genera un recargo por cada intervalo (diario, semanal o quincenal) transcurrido
/// desde el corte de mora del periodo. Interes simple: cada intervalo suma tasa % de la base adeudada (sin capitalizar recargos previos).
/// Respeta la politica del edificio: que cargos entran en la base (reserva, extraordinario, individual), la mora minima por intervalo, el
/// tope acumulado por unidad y periodo, y las unidades exoneradas. Deja de acumular cuando la unidad salda su deuda del periodo; un pago
/// revertido vuelve a dejar la deuda abierta. Separado del servicio programado para poder probarlo con una fecha dada.
/// </summary>
public sealed class LateFeeAccrualRunner(ICondoDbContext dbContext, PushDispatcher? push = null)
{
    public async Task<LateFeeRunResult> RunAsync(DateOnly today, CancellationToken ct)
    {
        var buildings = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive
                     && x.LateFeeRatePercentage != null && x.LateFeeRatePercentage > 0m
                     && x.LateFeeFrequency != null)
            .ToListAsync(ct);

        var created = 0;
        var notified = 0;
        foreach (var building in buildings)
        {
            var (c, n) = await RunBuildingAsync(building, today, ct);
            created += c;
            notified += n;
        }

        return new LateFeeRunResult(created, notified);
    }

    private async Task<(int Created, int Notified)> RunBuildingAsync(Building building, DateOnly today, CancellationToken ct)
    {
        var intervalDays = building.LateFeeFrequency!.Value switch
        {
            LateFeeFrequency.Daily => 1,
            LateFeeFrequency.Weekly => 7,
            LateFeeFrequency.Biweekly => 15,
            _ => 0
        };
        if (intervalDays == 0) return (0, 0);

        var rate = building.LateFeeRatePercentage!.Value;
        var exempt = (await dbContext.Units.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == building.Id && x.LateFeeExempt)
                .Select(x => x.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var periods = await dbContext.ExpensePeriods
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == building.Id && x.Status == ExpensePeriodStatus.Published)
            .Select(x => new { x.Id, x.Name, x.DueDate, x.LateFeeDate })
            .ToListAsync(ct);

        var newCharges = new List<ExpenseCharge>();
        // Unidades a las que esta corrida les aplica mora por primera vez en el periodo (para avisarles una sola vez).
        var firstTime = new List<(Guid UnitId, Guid PeriodId, string PeriodName, Guid CompanyId)>();

        foreach (var period in periods)
        {
            var threshold = period.LateFeeDate ?? period.DueDate;
            if (threshold >= today) continue;

            var elapsedIntervals = (today.DayNumber - threshold.DayNumber) / intervalDays;
            if (elapsedIntervals <= 0) continue;

            var chargeRows = await dbContext.ExpenseCharges
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.ExpensePeriodId == period.Id)
                .Select(x => new { x.UnitId, x.CompanyId, x.Amount, x.IsLateFee, x.AutoLateFeeIntervalIndex, x.ChargeType })
                .ToListAsync(ct);
            if (chargeRows.Count == 0) continue;

            // Un pago revertido ya no cuenta: la deuda vuelve a estar abierta y la mora sigue corriendo.
            var paymentMap = (await dbContext.Payments
                    .AsNoTracking()
                    .Where(x => !x.IsDeleted && !x.IsReversed && x.ExpensePeriodId == period.Id)
                    .Select(x => new { x.UnitId, x.Amount })
                    .ToListAsync(ct))
                .GroupBy(x => x.UnitId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

            foreach (var unitGroup in chargeRows.GroupBy(x => new { x.UnitId, x.CompanyId }))
            {
                if (exempt.Contains(unitGroup.Key.UnitId)) continue;

                // Base de calculo: lo que se le cobro sin la mora, segun los cargos que el edificio hace pasar por mora.
                var baseAmount = unitGroup.Where(x => !x.IsLateFee && CountsForLateFee(building, x.ChargeType)).Sum(x => x.Amount);
                if (baseAmount <= 0m) continue;

                var totalCharged = unitGroup.Sum(x => x.Amount);
                var pending = totalCharged - paymentMap.GetValueOrDefault(unitGroup.Key.UnitId, 0m);
                if (pending <= 0m) continue; // deuda saldada: no acumula mas

                var lastAppliedIndex = unitGroup
                    .Where(x => x.AutoLateFeeIntervalIndex.HasValue)
                    .Select(x => x.AutoLateFeeIntervalIndex!.Value)
                    .DefaultIfEmpty(0)
                    .Max();
                if (lastAppliedIndex >= elapsedIntervals) continue;

                var feePerInterval = decimal.Round(baseAmount * (rate / 100m), 2, MidpointRounding.AwayFromZero);
                var minApplied = false;
                if (building.LateFeeMinAmount is > 0m && feePerInterval < building.LateFeeMinAmount.Value)
                {
                    feePerInterval = building.LateFeeMinAmount.Value;
                    minApplied = true;
                }

                if (feePerInterval <= 0m) continue;

                // Tope: lo acumulado en mora (automatica o manual) no pasa de un % de la base.
                var alreadyLate = unitGroup.Where(x => x.IsLateFee).Sum(x => x.Amount);
                var capAmount = building.LateFeeCapPercentage is > 0m
                    ? decimal.Round(baseAmount * (building.LateFeeCapPercentage.Value / 100m), 2, MidpointRounding.AwayFromZero)
                    : (decimal?)null;

                for (var index = lastAppliedIndex + 1; index <= elapsedIntervals; index++)
                {
                    var amount = feePerInterval;
                    var capApplied = false;
                    if (capAmount.HasValue)
                    {
                        var room = capAmount.Value - alreadyLate;
                        if (room <= 0m) break;
                        if (amount > room)
                        {
                            amount = room;
                            capApplied = true;
                        }
                    }

                    newCharges.Add(new ExpenseCharge
                    {
                        CompanyId = unitGroup.Key.CompanyId,
                        ExpensePeriodId = period.Id,
                        UnitId = unitGroup.Key.UnitId,
                        ChargeType = ExpenseChargeType.Adjustment,
                        IsLateFee = true,
                        AutoLateFeeIntervalIndex = index,
                        Concept = $"Mora {rate:0.##}% ({LateFeeFrequencyLabel(building.LateFeeFrequency.Value)}) #{index} — {period.Name}",
                        Amount = amount,
                        Notes = $"Mora automática: {rate:0.##}% sobre expensa original de {baseAmount:N0}. Intervalo {index} desde {threshold:yyyy-MM-dd}."
                                + (minApplied && !capApplied ? $" Se aplicó la mora mínima de {building.LateFeeMinAmount!.Value:N0}." : string.Empty)
                                + (capApplied ? $" Llegó al tope de {building.LateFeeCapPercentage!.Value:0.##}% ({capAmount!.Value:N0})." : string.Empty)
                    });
                    alreadyLate += amount;

                    if (lastAppliedIndex == 0 && index == 1)
                    {
                        firstTime.Add((unitGroup.Key.UnitId, period.Id, period.Name, unitGroup.Key.CompanyId));
                    }
                }
            }
        }

        if (newCharges.Count == 0) return (0, 0);

        dbContext.ExpenseCharges.AddRange(newCharges);

        // Aviso de mora aplicada: una sola vez por unidad y periodo, y solo si el edificio lo tiene encendido.
        var pushes = new List<(HashSet<Guid> Recipients, string Title, string Body, Guid PeriodId)>();
        if (firstTime.Count > 0 && await NoticeRules.IsActiveAsync(dbContext, building.Id, NoticeKind.LateFeeApplied, ct))
        {
            var recipientsByUnit = await NoticeRecipients.ForUnitsAsync(dbContext, firstTime.Select(x => x.UnitId).Distinct().ToList(), ct);
            foreach (var group in firstTime.GroupBy(x => x.PeriodId))
            {
                var recipients = group.SelectMany(x => recipientsByUnit.GetValueOrDefault(x.UnitId) ?? []).ToHashSet();
                var already = (await dbContext.Notifications.AsNoTracking()
                        .Where(x => !x.IsDeleted && x.Type == NotificationType.LateFeeApplied && x.EntityId == group.Key && recipients.Contains(x.RecipientId))
                        .Select(x => x.RecipientId)
                        .ToListAsync(ct))
                    .ToHashSet();
                recipients.ExceptWith(already);
                if (recipients.Count == 0) continue;

                var periodName = group.First().PeriodName;
                const string title = "Se aplicó mora a tu expensa";
                var body = $"La expensa de {periodName} está vencida y se le aplicó interés por mora. Revisá tu estado de cuenta y regularizá el pago.";
                foreach (var recipientId in recipients)
                {
                    dbContext.Notifications.Add(new Notification
                    {
                        CompanyId = group.First().CompanyId,
                        RecipientId = recipientId,
                        Type = NotificationType.LateFeeApplied,
                        Title = title,
                        Body = body,
                        EntityType = "ExpensePeriod",
                        EntityId = group.Key
                    });
                }

                pushes.Add((recipients, title, body, group.Key));
            }
        }

        await dbContext.SaveChangesAsync(ct);

        if (push is not null)
        {
            foreach (var (recipients, title, body, periodId) in pushes)
            {
                await push.NotifyUsersAsync(recipients, title, body, "ExpensePeriod", periodId, ct, nameof(NotificationType.LateFeeApplied));
            }
        }

        return (newCharges.Count, pushes.Sum(x => x.Recipients.Count));
    }

    // Las expensas ordinarias y los ajustes siempre entran en la base de la mora; reserva, extraordinario e individual, segun el edificio.
    private static bool CountsForLateFee(Building building, ExpenseChargeType type) => type switch
    {
        ExpenseChargeType.ReserveFund => building.LateFeeAppliesToReserve,
        ExpenseChargeType.Extraordinary => building.LateFeeAppliesToExtraordinary,
        ExpenseChargeType.Individual => building.LateFeeAppliesToIndividual,
        _ => true
    };

    private static string LateFeeFrequencyLabel(LateFeeFrequency frequency) => frequency switch
    {
        LateFeeFrequency.Daily => "diario",
        LateFeeFrequency.Weekly => "semanal",
        LateFeeFrequency.Biweekly => "quincenal",
        _ => frequency.ToString()
    };
}
