using Condo.Application.Services;
using Condo.Infrastructure.Persistence;

namespace Condo.Api.Services;

/// <summary>
/// Proceso programado de los avisos de vencimiento: cada hora revisa los edificios con el aviso encendido. La regla vive en
/// <see cref="PaymentReminderRunner"/> (un solo aviso por persona y periodo, desde las 8 de la mañana de Paraguay).
/// </summary>
public sealed class PaymentReminderService(IServiceScopeFactory scopeFactory, ILogger<PaymentReminderService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CondoDbContext>();
                var push = scope.ServiceProvider.GetRequiredService<PushDispatcher>();

                var now = DateTime.UtcNow.AddHours(-3);   // hora de Paraguay (UTC-3), como el libro de Finanzas
                var result = await new PaymentReminderRunner(db, push).RunAsync(FinancePeriods.Today(), now.Hour, stoppingToken);
                if (result.Notifications > 0)
                {
                    logger.LogInformation("Avisos de vencimiento: {Count} enviados.", result.Notifications);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al enviar los avisos de vencimiento.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
