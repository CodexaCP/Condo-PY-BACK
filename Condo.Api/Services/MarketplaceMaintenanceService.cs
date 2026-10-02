using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Proceso de fondo del marketplace. Cada pocos segundos vence las reservas que no recibieron el comprobante a tiempo
/// (liberando su horario y avisando al comprador) y pausa las publicaciones cuyo dueño dejo de ser el propietario principal.
/// Las consultas tambien aplican estas reglas al momento, asi que nada queda bloqueado si este proceso se atrasa.
/// </summary>
public sealed class MarketplaceMaintenanceService(
    IServiceScopeFactory scopeFactory, ILogger<MarketplaceMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

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
                logger.LogError(ex, "Error en el mantenimiento del marketplace.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var reservations = scope.ServiceProvider.GetRequiredService<MarketplaceReservationService>();
        var expired = await reservations.ExpireStaleAsync(ct);
        if (expired > 0)
        {
            logger.LogInformation("Marketplace: {Count} reservas vencidas por falta de pago.", expired);
        }

        // Reservas terminadas -> finalizadas, y acreditacion al saldo del propietario pasadas 24 h sin reclamo.
        var credits = scope.ServiceProvider.GetRequiredService<MarketplaceCreditService>();
        var (completed, credited) = await credits.ProcessDueAsync(ct);
        if (completed > 0 || credited > 0)
        {
            logger.LogInformation("Marketplace: {Completed} reservas finalizadas, {Credited} acreditaciones al saldo.", completed, credited);
        }

        // Alertas al revisor: pagos que siguen sin revisar (a los 15 minutos y luego cada hora).
        var payments = scope.ServiceProvider.GetRequiredService<MarketplacePaymentService>();
        var alerted = await payments.SendReviewAlertsAsync(ct);
        if (alerted > 0)
        {
            logger.LogInformation("Marketplace: {Count} alertas de pagos por revisar.", alerted);
        }

        // Publicaciones de dueños que ya no son el principal: solo de edificios con el modulo encendido.
        var db = scope.ServiceProvider.GetRequiredService<CondoDbContext>();
        var listings = scope.ServiceProvider.GetRequiredService<MarketplaceListingService>();
        var buildingIds = await db.MarketplaceListings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == Condo.Domain.Enums.MarketplaceListingStatus.Active)
            .Select(x => x.BuildingId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var buildingId in buildingIds)
        {
            var suspended = await listings.SyncOwnershipAsync(buildingId, ct);
            if (suspended > 0)
            {
                logger.LogInformation("Marketplace: {Count} publicaciones pausadas por cambio de propietario principal.", suspended);
            }
        }
    }
}
