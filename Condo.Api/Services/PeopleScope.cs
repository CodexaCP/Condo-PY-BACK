using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Alcance por edificio para personas (propietarios y residentes). Son registros de la EMPRESA, pero el
/// personal acotado (Encargado, Operador, Administrador de un condominio) solo debe ver y tocar a quienes
/// tienen alguna unidad en sus edificios. Regla:
///   - visible: tiene al menos una unidad vinculada en un edificio del alcance, o no tiene ninguna unidad
///     (recien creado / sin asignar: hay que poder verlo para asignarle su unidad);
///   - "alcance total": TODAS sus unidades estan en el alcance. Solo entonces se pueden cambiar datos que
///     afectan a los demas edificios donde tambien figura (acceso: contrasena, usuario, correo, estado).
/// El Administrador de toda la empresa y el SuperAdmin no pasan por aqui (IAccessScopeService.HasFullCompanyScope).
/// </summary>
public static class PeopleScope
{
    // ─── Filtros para listados (se traducen a SQL) ───────────────────────────

    public static IQueryable<ApplicationUser> VisibleUsers(
        this IQueryable<ApplicationUser> query, ICondoDbContext db, HashSet<Guid> scope) =>
        query.Where(u =>
            db.UnitOwners.Any(o => !o.IsDeleted && o.OwnerId == u.Id && o.Unit != null && scope.Contains(o.Unit.BuildingId))
            || db.UnitResidents.Any(r => !r.IsDeleted && r.Resident != null && r.Resident.ApplicationUserId == u.Id
                                         && r.Unit != null && scope.Contains(r.Unit.BuildingId))
            || (!db.UnitOwners.Any(o => !o.IsDeleted && o.OwnerId == u.Id && o.Unit != null && !o.Unit.IsDeleted)
                && !db.UnitResidents.Any(r => !r.IsDeleted && r.Resident != null && r.Resident.ApplicationUserId == u.Id
                                              && r.Unit != null && !r.Unit.IsDeleted)));

    public static IQueryable<Resident> VisibleResidents(this IQueryable<Resident> query, HashSet<Guid> scope) =>
        query.Where(r =>
            r.UnitResidents.Any(l => !l.IsDeleted && l.Unit != null && scope.Contains(l.Unit.BuildingId))
            || !r.UnitResidents.Any(l => !l.IsDeleted && l.Unit != null && !l.Unit.IsDeleted));

    // ─── Edificios donde una persona tiene unidades (para chequeos puntuales) ─

    public static async Task<HashSet<Guid>> BuildingsOfUserAsync(ICondoDbContext db, Guid userId, CancellationToken ct)
    {
        var owned = await db.UnitOwners
            .AsNoTracking()
            .Where(o => !o.IsDeleted && o.OwnerId == userId && o.Unit != null && !o.Unit.IsDeleted)
            .Select(o => o.Unit!.BuildingId)
            .Distinct()
            .ToListAsync(ct);

        var resided = await db.UnitResidents
            .AsNoTracking()
            .Where(r => !r.IsDeleted && r.Unit != null && !r.Unit.IsDeleted
                        && r.Resident != null && !r.Resident.IsDeleted && r.Resident.ApplicationUserId == userId)
            .Select(r => r.Unit!.BuildingId)
            .Distinct()
            .ToListAsync(ct);

        return owned.Concat(resided).ToHashSet();
    }

    public static async Task<HashSet<Guid>> BuildingsOfResidentAsync(ICondoDbContext db, Guid residentId, CancellationToken ct) =>
        (await db.UnitResidents
            .AsNoTracking()
            .Where(l => !l.IsDeleted && l.ResidentId == residentId && l.Unit != null && !l.Unit.IsDeleted)
            .Select(l => l.Unit!.BuildingId)
            .Distinct()
            .ToListAsync(ct))
        .ToHashSet();

    // Sin unidades = visible; con unidades = al menos una en el alcance.
    public static bool IsVisible(HashSet<Guid> linkedBuildings, HashSet<Guid> scope) =>
        linkedBuildings.Count == 0 || linkedBuildings.Overlaps(scope);

    // Todas sus unidades dentro del alcance (sin unidades cuenta como total).
    public static bool IsFullyInScope(HashSet<Guid> linkedBuildings, HashSet<Guid> scope) =>
        linkedBuildings.IsSubsetOf(scope);
}
