using System.Security.Claims;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Condo.Api.Services;

/// <summary>
/// El token dura 8 horas y lleva rol, empresa y condominio "congelados". Sin este control, un usuario desactivado o
/// eliminado, con el rol cambiado o de una empresa desactivada seguia entrando hasta que el token vencia.
/// En cada request se verifica contra la base (con una cache de 1 minuto por usuario, o sea 1 consulta por minuto
/// y usuario) que la cuenta siga activa, no este eliminada, su empresa este activa y que rol, empresa y condominio
/// coincidan con los del token. Si algo cambio, responde 401 y el cliente vuelve a iniciar sesion.
/// </summary>
public static class SessionRevalidation
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(60);
    private static readonly AccountSnapshot Invalid = new(false, string.Empty, string.Empty, string.Empty);

    private sealed record AccountSnapshot(bool Valid, string Role, string CompanyId, string CondominiumId);

    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.Fail("El token no identifica a un usuario.");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var cache = services.GetRequiredService<IMemoryCache>();
        var cancellationToken = context.HttpContext.RequestAborted;

        var snapshot = await cache.GetOrCreateAsync($"session:{userId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTime;

            var db = services.GetRequiredService<CondoDbContext>();
            var row = await db.ApplicationUsers
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new
                {
                    u.IsDeleted,
                    u.IsActive,
                    u.Role,
                    u.CompanyId,
                    u.CondominiumId,
                    CompanyOk = u.Company != null && u.Company.IsActive && !u.Company.IsDeleted
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (row is null || row.IsDeleted || !row.IsActive) return Invalid;

            // Solo el SuperAdmin puede no tener empresa; el resto necesita una empresa activa.
            if (row.Role != UserRole.SuperAdmin && !row.CompanyOk) return Invalid;

            return new AccountSnapshot(
                true,
                row.Role.ToString(),
                row.CompanyId?.ToString() ?? string.Empty,
                row.CondominiumId?.ToString() ?? string.Empty);
        });

        if (snapshot is null || !snapshot.Valid)
        {
            context.Fail("La cuenta ya no está activa.");
            return;
        }

        var sameRole = string.Equals(principal!.FindFirstValue(ClaimTypes.Role), snapshot.Role, StringComparison.OrdinalIgnoreCase);
        var sameCompany = string.Equals(principal.FindFirstValue("companyId") ?? string.Empty, snapshot.CompanyId, StringComparison.OrdinalIgnoreCase);
        var sameCondominium = string.Equals(principal.FindFirstValue("condominiumId") ?? string.Empty, snapshot.CondominiumId, StringComparison.OrdinalIgnoreCase);

        if (!sameRole || !sameCompany || !sameCondominium)
        {
            context.Fail("Los permisos de la cuenta cambiaron. Iniciá sesión de nuevo.");
        }
    }
}
