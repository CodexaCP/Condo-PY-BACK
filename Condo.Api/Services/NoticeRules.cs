using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Reglas de avisos automaticos de un edificio. Sin fila para un tipo rige su valor por defecto: los avisos que ya existian (pago
/// recibido y periodo publicado) siguen encendidos y los nuevos (antes del vencimiento, el dia del vencimiento y mora aplicada)
/// apagados, asi que nada cambia hasta que alguien los configure.
/// </summary>
public static class NoticeRules
{
    public const int MinOffsetDays = 1;
    public const int MaxOffsetDays = 30;
    public const int DefaultOffsetDays = 3;

    public static bool DefaultActive(NoticeKind kind) => kind is NoticeKind.PaymentReceived or NoticeKind.PeriodPublished;

    public static string Label(NoticeKind kind) => kind switch
    {
        NoticeKind.BeforeDue => "Aviso antes del vencimiento",
        NoticeKind.OnDue => "Aviso el día del vencimiento",
        NoticeKind.LateFeeApplied => "Aviso de mora aplicada",
        NoticeKind.PaymentReceived => "Aviso de pago recibido",
        NoticeKind.PeriodPublished => "Aviso de período publicado",
        _ => kind.ToString()
    };

    /// <summary>Si el aviso esta encendido en el edificio (la regla, o su valor por defecto si no hay).</summary>
    public static async Task<bool> IsActiveAsync(ICondoDbContext dbContext, Guid buildingId, NoticeKind kind, CancellationToken cancellationToken)
    {
        var rule = await dbContext.BuildingNoticeRules.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Kind == kind)
            .Select(x => new { x.IsActive })
            .FirstOrDefaultAsync(cancellationToken);
        return rule?.IsActive ?? DefaultActive(kind);
    }

    /// <summary>Los avisos encendidos de varios edificios a la vez, por tipo (para no consultar edificio por edificio).</summary>
    public static async Task<HashSet<Guid>> BuildingsWithActiveAsync(
        ICondoDbContext dbContext, IReadOnlyCollection<Guid> buildingIds, NoticeKind kind, CancellationToken cancellationToken)
    {
        var rules = await dbContext.BuildingNoticeRules.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Kind == kind && buildingIds.Contains(x.BuildingId))
            .Select(x => new { x.BuildingId, x.IsActive })
            .ToListAsync(cancellationToken);
        var byBuilding = rules.ToDictionary(x => x.BuildingId, x => x.IsActive);
        return buildingIds.Where(id => byBuilding.TryGetValue(id, out var active) ? active : DefaultActive(kind)).ToHashSet();
    }
}

/// <summary>A quien le llega un aviso de una unidad: sus propietarios actuales y sus residentes activos que tienen usuario en la app.</summary>
public static class NoticeRecipients
{
    public static async Task<Dictionary<Guid, HashSet<Guid>>> ForUnitsAsync(
        ICondoDbContext dbContext, IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken)
    {
        var result = unitIds.ToDictionary(id => id, _ => new HashSet<Guid>());
        if (unitIds.Count == 0)
        {
            return result;
        }

        var owners = await dbContext.UnitOwners.AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null && unitIds.Contains(x.UnitId))
            .Select(x => new { x.UnitId, x.OwnerId })
            .ToListAsync(cancellationToken);
        foreach (var owner in owners) result[owner.UnitId].Add(owner.OwnerId);

        var residents = await dbContext.UnitResidents.AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null && unitIds.Contains(x.UnitId)
                        && x.Resident != null && !x.Resident.IsDeleted && x.Resident.IsActive && x.Resident.ApplicationUserId != null)
            .Select(x => new { x.UnitId, UserId = x.Resident!.ApplicationUserId!.Value })
            .ToListAsync(cancellationToken);
        foreach (var resident in residents) result[resident.UnitId].Add(resident.UserId);

        return result;
    }
}
