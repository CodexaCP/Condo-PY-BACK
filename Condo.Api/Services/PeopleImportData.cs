using Condo.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

// Acceso a datos comun de las cargas masivas de personas: quien puede importar, en que empresa y con que edificios y unidades.
public static class PeopleImportData
{
    // El SuperAdmin y el Administrador de toda la empresa. El personal acotado a un condominio o a edificios sueltos no importa:
    // las personas son de la empresa y una carga masiva puede tocar cualquier edificio.
    public static bool CanImport(IAccessScopeService scope) => scope.IsSuperAdmin || (scope.IsCompanyAdmin && scope.HasFullCompanyScope);

    // El Administrador de empresa importa siempre en su empresa; el SuperAdmin elige la empresa.
    public static async Task<(Guid? CompanyId, string? Error)> ResolveCompanyAsync(
        ICondoDbContext db, IAccessScopeService scope, Guid? requested, CancellationToken ct)
    {
        if (!scope.IsSuperAdmin)
            return scope.CompanyId.HasValue ? (scope.CompanyId, null) : (null, "No se pudo determinar la empresa.");

        if (!requested.HasValue) return (null, "Elegí la empresa en la que se cargan las personas.");
        var exists = await db.Companies.AsNoTracking().AnyAsync(x => x.Id == requested.Value && !x.IsDeleted, ct);
        return exists ? (requested, null) : (null, "La empresa indicada no existe.");
    }

    // Edificios de la empresa que se pueden elegir (los accesibles: un edificio en restriccion total por plan vencido queda afuera)
    // y sus unidades.
    public static async Task<(List<ImportBuildingRef> Buildings, List<ImportUnitRef> Units)> LoadBuildingsAndUnitsAsync(
        ICondoDbContext db, IAccessScopeService scope, Guid companyId, CancellationToken ct)
    {
        var accessible = scope.IsSuperAdmin ? null : await scope.GetAccessibleBuildingIdsAsync(ct);

        var rows = await db.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && (x.CompanyId == companyId || (x.Condominium != null && x.Condominium.CompanyId == companyId)))
            .Select(x => new { x.Id, x.Name, x.Code })
            .ToListAsync(ct);

        var buildings = rows
            .Where(x => accessible is null || accessible.Contains(x.Id))
            .Select(x => new ImportBuildingRef(x.Id, companyId, x.Name, x.Code))
            .ToList();

        var buildingIds = buildings.Select(b => b.Id).ToList();
        var units = await db.Units
            .AsNoTracking()
            .Where(x => !x.IsDeleted && buildingIds.Contains(x.BuildingId))
            .Select(x => new ImportUnitRef(x.Id, x.BuildingId, x.CompanyId, x.Code))
            .ToListAsync(ct);

        return (buildings, units);
    }

    public static Dictionary<string, ImportBuildingRef> ByLabel(IEnumerable<ImportBuildingRef> buildings) =>
        buildings.ToDictionary(b => PeopleImportCommon.NormalizeLabel(b.Label), b => b);

    public static Dictionary<(Guid BuildingId, string Code), ImportUnitRef> ByKey(IEnumerable<ImportUnitRef> units)
    {
        var result = new Dictionary<(Guid, string), ImportUnitRef>();
        foreach (var u in units) result.TryAdd((u.BuildingId, PeopleImportCommon.NormalizeUnitCode(u.Code)), u);
        return result;
    }
}
