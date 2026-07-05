using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Proceso automático de mora: para cada edificio con tasa configurada,
/// genera recargos por cada intervalo (diario/semanal/quincenal) transcurrido
/// desde el corte de mora del período. Interés simple: cada intervalo suma
/// tasa% del monto ORIGINAL adeudado (sin capitalizar recargos previos).
/// Deja de acumular cuando la unidad salda su deuda del período.
/// </summary>
public sealed class LateFeeAccrualService(IServiceScopeFactory scopeFactory, ILogger<LateFeeAccrualService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pequeña espera inicial para no competir con el arranque de la app
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AccrueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al acumular recargos por mora automáticos.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task AccrueAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondoDbContext>();

        var today = DateOnly.FromDateTime(DateTime.Now);

        var buildings = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive
                     && x.LateFeeRatePercentage != null && x.LateFeeRatePercentage > 0m
                     && x.LateFeeFrequency != null)
            .Select(x => new { x.Id, x.Name, Rate = x.LateFeeRatePercentage!.Value, Frequency = x.LateFeeFrequency!.Value })
            .ToListAsync(ct);

        foreach (var building in buildings)
        {
            var intervalDays = building.Frequency switch
            {
                LateFeeFrequency.Daily => 1,
                LateFeeFrequency.Weekly => 7,
                LateFeeFrequency.Biweekly => 15,
                _ => 0
            };
            if (intervalDays == 0) continue;

            var periods = await dbContext.ExpensePeriods
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == building.Id && x.Status == ExpensePeriodStatus.Published)
                .Select(x => new { x.Id, x.Name, x.DueDate, x.LateFeeDate })
                .ToListAsync(ct);

            var newCharges = new List<ExpenseCharge>();

            foreach (var period in periods)
            {
                var threshold = period.LateFeeDate ?? period.DueDate;
                if (threshold >= today) continue;

                var elapsedIntervals = (today.DayNumber - threshold.DayNumber) / intervalDays;
                if (elapsedIntervals <= 0) continue;

                var chargeRows = await dbContext.ExpenseCharges
                    .AsNoTracking()
                    .Where(x => !x.IsDeleted && x.ExpensePeriodId == period.Id)
                    .Select(x => new { x.UnitId, x.CompanyId, x.Amount, x.IsLateFee, x.AutoLateFeeIntervalIndex })
                    .ToListAsync(ct);

                if (chargeRows.Count == 0) continue;

                var paymentMap = await dbContext.Payments
                    .AsNoTracking()
                    .Where(x => !x.IsDeleted && x.ExpensePeriodId == period.Id)
                    .GroupBy(x => x.UnitId)
                    .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Amount), ct);

                foreach (var unitGroup in chargeRows.GroupBy(x => new { x.UnitId, x.CompanyId }))
                {
                    var originalAmount = unitGroup.Where(x => !x.IsLateFee).Sum(x => x.Amount);
                    if (originalAmount <= 0m) continue;

                    var totalCharged = unitGroup.Sum(x => x.Amount);
                    var pending = totalCharged - paymentMap.GetValueOrDefault(unitGroup.Key.UnitId, 0m);
                    if (pending <= 0m) continue; // deuda saldada: no acumula más

                    var lastAppliedIndex = unitGroup
                        .Where(x => x.AutoLateFeeIntervalIndex.HasValue)
                        .Select(x => x.AutoLateFeeIntervalIndex!.Value)
                        .DefaultIfEmpty(0)
                        .Max();

                    if (lastAppliedIndex >= elapsedIntervals) continue;

                    var feePerInterval = decimal.Round(originalAmount * (building.Rate / 100m), 2, MidpointRounding.AwayFromZero);
                    if (feePerInterval <= 0m) continue;

                    for (var index = lastAppliedIndex + 1; index <= elapsedIntervals; index++)
                    {
                        newCharges.Add(new ExpenseCharge
                        {
                            CompanyId = unitGroup.Key.CompanyId,
                            ExpensePeriodId = period.Id,
                            UnitId = unitGroup.Key.UnitId,
                            ChargeType = ExpenseChargeType.Adjustment,
                            IsLateFee = true,
                            AutoLateFeeIntervalIndex = index,
                            Concept = $"Mora {building.Rate:0.##}% ({LateFeeFrequencyLabel(building.Frequency)}) #{index} — {period.Name}",
                            Amount = feePerInterval,
                            Notes = $"Mora automática: {building.Rate:0.##}% sobre expensa original de {originalAmount:N0}. Intervalo {index} desde {threshold:yyyy-MM-dd}."
                        });
                    }
                }
            }

            if (newCharges.Count > 0)
            {
                dbContext.ExpenseCharges.AddRange(newCharges);
                await dbContext.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Mora automática: {Count} recargos generados para el edificio {Building}.",
                    newCharges.Count, building.Name);
            }
        }
    }

    private static string LateFeeFrequencyLabel(LateFeeFrequency frequency) => frequency switch
    {
        LateFeeFrequency.Daily => "diario",
        LateFeeFrequency.Weekly => "semanal",
        LateFeeFrequency.Biweekly => "quincenal",
        _ => frequency.ToString()
    };
}
