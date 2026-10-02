using System.Text.Json;
using Condo.Application.Abstractions;
using Condo.Domain.Entities;

namespace Condo.Api.Services;

/// <summary>
/// Registra en MarketplaceEvent quien hizo cada accion del marketplace, cuando, sobre que, de que estado a cual y con que
/// importes. Solo agrega el evento al contexto: lo guarda quien llama, en la MISMA transaccion que el cambio auditado, asi
/// no puede quedar un cambio sin su evento ni un evento sin su cambio.
/// </summary>
public class MarketplaceAudit(ICondoDbContext dbContext, ITenantContext tenantContext, IHttpContextAccessor httpContextAccessor)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public MarketplaceEvent Record(
        Guid companyId,
        Guid buildingId,
        string entityType,
        Guid entityId,
        string action,
        string? fromStatus = null,
        string? toStatus = null,
        object? data = null,
        Guid? userId = null,
        bool automatic = false)
    {
        var http = httpContextAccessor.HttpContext;

        var evt = new MarketplaceEvent
        {
            CompanyId = companyId,
            BuildingId = buildingId,
            // Sin usuario autenticado, o con "automatic" (el sistema actua durante la peticion de otro), queda nulo.
            UserId = automatic ? null : userId ?? (tenantContext.IsAuthenticated ? tenantContext.UserId : null),
            TimestampUtc = DateTime.UtcNow,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            IpAddress = ResolveIp(http),
            UserAgent = Truncate(http?.Request.Headers.UserAgent.ToString(), 300),
            DataJson = data is null ? null : JsonSerializer.Serialize(data, JsonOptions)
        };

        dbContext.MarketplaceEvents.Add(evt);
        return evt;
    }

    // Detras de nginx la conexion llega desde el proxy: se usa el ultimo valor de X-Forwarded-For (el que agrega el proxy;
    // los anteriores los puede escribir el cliente) y, sin proxy, la direccion de la conexion.
    private static string? ResolveIp(HttpContext? http)
    {
        if (http is null)
        {
            return null;
        }

        var forwarded = http.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var last = forwarded.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (!string.IsNullOrWhiteSpace(last))
            {
                return Truncate(last, 64);
            }
        }

        return Truncate(http.Connection.RemoteIpAddress?.ToString(), 64);
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
