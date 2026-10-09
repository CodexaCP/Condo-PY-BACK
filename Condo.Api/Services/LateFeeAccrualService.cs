using Condo.Application.Abstractions;
using Condo.Infrastructure.Persistence;

namespace Condo.Api.Services;

/// <summary>
/// Proceso automático de mora: cada 6 horas genera los recargos por mora de los edificios con tasa configurada. La regla (intervalos,
/// política del edificio, exoneraciones) vive en <see cref="LateFeeAccrualRunner"/>.
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
        var push = scope.ServiceProvider.GetRequiredService<PushDispatcher>();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var result = await new LateFeeAccrualRunner(dbContext, push).RunAsync(today, ct);
        if (result.ChargesCreated > 0)
        {
            logger.LogInformation(
                "Mora automática: {Count} recargos generados, {Notified} destinatarios avisados.", result.ChargesCreated, result.UnitsNotified);
        }
    }
}
