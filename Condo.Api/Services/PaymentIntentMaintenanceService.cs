namespace Condo.Api.Services;

/// <summary>
/// Proceso de fondo de los cobros online. Cada minuto cierra los cobros abiertos cuyo plazo para pagar vencio: el intento queda vencido y los
/// comprobantes quedan libres para volver a cobrarse. Crear o consultar un cobro tambien aplica esta regla al momento, asi que nada queda
/// bloqueado si este proceso se atrasa. Un aviso del proveedor que llegue despues del vencimiento igual se aplica si todo sigue coincidiendo.
/// Todavia no consulta al proveedor por el estado de un intento vencido: eso depende de que el adaptador real lo ofrezca.
/// </summary>
public sealed class PaymentIntentMaintenanceService(
    IServiceScopeFactory scopeFactory, ILogger<PaymentIntentMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error en el mantenimiento de los cobros online.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // El servicio se esta deteniendo.
        }
    }

    /// <summary>Una pasada: vence los cobros abiertos que pasaron su plazo. Devuelve cuantos cerro.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<OnlinePaymentService>();
        var expired = await payments.ExpireOverdueAsync(null, ct);
        if (expired > 0)
        {
            logger.LogInformation("Pagos online: {Count} cobros vencidos por falta de pago.", expired);
        }

        return expired;
    }
}
