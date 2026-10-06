using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

// Movimientos recientes de la plataforma para el panel del SuperAdmin. Se arman con las fechas de alta y de modificacion de las
// empresas, los administradores, los condominios y los edificios (todavia no hay un registro de auditoria con el usuario que lo hizo).
[ApiController]
[Authorize]
[Route("api/platform-activity")]
public class PlatformActivityController(ICondoDbContext dbContext, ITenantContext tenantContext) : ControllerBase
{
    // Paraguay opera en UTC-3 todo el ano: los dias del grafico se cuentan con esa hora.
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(-3);

    // Una empresa "actualizada" es la que se modifico despues de su alta (con un margen por el propio alta).
    private static readonly TimeSpan UpdateMargin = TimeSpan.FromMinutes(2);

    private sealed record Event(DateTime AtUtc, string Kind, string Title, string Detail);

    [HttpGet]
    public async Task<ActionResult<PlatformActivityDto>> Get(
        [FromQuery] int days = 7, [FromQuery] int limit = 8, CancellationToken cancellationToken = default)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        days = Math.Clamp(days, 1, 31);
        limit = Math.Clamp(limit, 1, 50);

        var firstDay = DateOnly.FromDateTime(DateTime.UtcNow + LocalOffset).AddDays(-(days - 1));
        var sinceUtc = firstDay.ToDateTime(TimeOnly.MinValue) - LocalOffset;

        // Grafico: todos los movimientos de la ventana de dias.
        var inWindow = await LoadAsync(sinceUtc, null, cancellationToken);
        var perDay = inWindow
            .GroupBy(e => DateOnly.FromDateTime(e.AtUtc + LocalOffset))
            .ToDictionary(g => g.Key, g => g.Count());

        var result = new PlatformActivityDto
        {
            Days = Enumerable.Range(0, days)
                .Select(i => firstDay.AddDays(i))
                .Select(d => new PlatformActivityDayDto { Date = d, Count = perDay.GetValueOrDefault(d) })
                .ToList()
        };

        // Lista: los ultimos movimientos sin limite de fecha (si en la semana no hubo, igual se ven los ultimos que hubo).
        var latest = await LoadAsync(null, limit, cancellationToken);
        result.Items = latest
            .OrderByDescending(e => e.AtUtc)
            .Take(limit)
            .Select(e => new PlatformActivityItemDto { AtUtc = e.AtUtc, Kind = e.Kind, Title = e.Title, Detail = e.Detail })
            .ToList();

        return Ok(result);
    }

    // Movimientos desde una fecha (para el grafico) o los ultimos N de cada origen (para la lista).
    private async Task<List<Event>> LoadAsync(DateTime? sinceUtc, int? perSource, CancellationToken ct)
    {
        var events = new List<Event>();

        var companiesCreated = dbContext.Companies.AsNoTracking().Where(x => !x.IsDeleted);
        if (sinceUtc.HasValue) companiesCreated = companiesCreated.Where(x => x.CreatedAtUtc >= sinceUtc.Value);
        var companiesCreatedOrdered = companiesCreated.OrderByDescending(x => x.CreatedAtUtc);
        var createdRows = perSource.HasValue
            ? await companiesCreatedOrdered.Take(perSource.Value).Select(x => new { x.CreatedAtUtc, x.Name }).ToListAsync(ct)
            : await companiesCreatedOrdered.Select(x => new { x.CreatedAtUtc, x.Name }).ToListAsync(ct);
        events.AddRange(createdRows.Select(x => new Event(x.CreatedAtUtc, "CompanyCreated", "Nueva empresa", $"Empresa {x.Name} registrada")));

        // Pocas empresas: se traen y el margen de "modificada despues del alta" se aplica en memoria (no depende de la traduccion a SQL).
        var companyRows = await dbContext.Companies.AsNoTracking().Where(x => !x.IsDeleted)
            .Select(x => new { x.CreatedAtUtc, x.UpdatedAtUtc, x.Name }).ToListAsync(ct);
        var updatedRows = companyRows
            .Where(x => x.UpdatedAtUtc > x.CreatedAtUtc.Add(UpdateMargin) && (!sinceUtc.HasValue || x.UpdatedAtUtc >= sinceUtc.Value))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(perSource ?? int.MaxValue);
        events.AddRange(updatedRows.Select(x => new Event(x.UpdatedAtUtc, "CompanyUpdated", "Actualización de empresa", $"Datos de {x.Name} actualizados")));

        var admins = dbContext.ApplicationUsers.AsNoTracking().Where(x => !x.IsDeleted && x.Role == UserRole.CompanyAdmin);
        if (sinceUtc.HasValue) admins = admins.Where(x => x.CreatedAtUtc >= sinceUtc.Value);
        var adminsOrdered = admins.OrderByDescending(x => x.CreatedAtUtc);
        var adminRows = perSource.HasValue
            ? await adminsOrdered.Take(perSource.Value).Select(x => new { x.CreatedAtUtc, x.FullName, Company = x.Company != null ? x.Company.Name : null }).ToListAsync(ct)
            : await adminsOrdered.Select(x => new { x.CreatedAtUtc, x.FullName, Company = x.Company != null ? x.Company.Name : null }).ToListAsync(ct);
        events.AddRange(adminRows.Select(x => new Event(x.CreatedAtUtc, "AdminCreated", "Nuevo administrador",
            x.Company is null ? $"{x.FullName} registrado" : $"{x.FullName} asignado a {x.Company}")));

        var condominiums = dbContext.Condominiums.AsNoTracking().Where(x => !x.IsDeleted);
        if (sinceUtc.HasValue) condominiums = condominiums.Where(x => x.CreatedAtUtc >= sinceUtc.Value);
        var condominiumsOrdered = condominiums.OrderByDescending(x => x.CreatedAtUtc);
        var condominiumRows = perSource.HasValue
            ? await condominiumsOrdered.Take(perSource.Value).Select(x => new { x.CreatedAtUtc, x.Name }).ToListAsync(ct)
            : await condominiumsOrdered.Select(x => new { x.CreatedAtUtc, x.Name }).ToListAsync(ct);
        events.AddRange(condominiumRows.Select(x => new Event(x.CreatedAtUtc, "CondominiumCreated", "Nuevo condominio", $"Condominio {x.Name} registrado")));

        var buildings = dbContext.Buildings.AsNoTracking().Where(x => !x.IsDeleted);
        if (sinceUtc.HasValue) buildings = buildings.Where(x => x.CreatedAtUtc >= sinceUtc.Value);
        var buildingsOrdered = buildings.OrderByDescending(x => x.CreatedAtUtc);
        var buildingRows = perSource.HasValue
            ? await buildingsOrdered.Take(perSource.Value).Select(x => new { x.CreatedAtUtc, x.Name }).ToListAsync(ct)
            : await buildingsOrdered.Select(x => new { x.CreatedAtUtc, x.Name }).ToListAsync(ct);
        events.AddRange(buildingRows.Select(x => new Event(x.CreatedAtUtc, "BuildingCreated", "Nuevo edificio", $"Edificio {x.Name} registrado")));

        return events;
    }
}
