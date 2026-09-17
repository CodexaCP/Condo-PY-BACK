using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Mantiene coherente el checkbox "Es residente" de un propietario con un vínculo real
/// de residencia (Resident + UnitResident), para que cuente en el módulo de Residentes
/// y en los contadores del dashboard, en vez de ser solo cosmético.
/// </summary>
public interface IOwnerResidencySyncService
{
    Task SyncAsync(ApplicationUser owner, Guid companyId, CancellationToken ct);
}

public class OwnerResidencySyncService(ICondoDbContext dbContext) : IOwnerResidencySyncService
{
    public async Task SyncAsync(ApplicationUser owner, Guid companyId, CancellationToken ct)
    {
        try
        {
            var resident = await dbContext.Residents
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.ApplicationUserId == owner.Id, ct);

            if (!owner.IsResident)
            {
                if (resident is null) return;

                var activeLinks = await dbContext.UnitResidents
                    .Where(x => !x.IsDeleted && x.ResidentId == resident.Id && x.EndDate == null)
                    .ToListAsync(ct);

                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                foreach (var link in activeLinks)
                    link.EndDate = today < link.StartDate ? link.StartDate : today;

                if (activeLinks.Count > 0)
                    await dbContext.SaveChangesAsync(ct);
                return;
            }

            if (resident is null)
            {
                var documentNumber = string.IsNullOrWhiteSpace(owner.DocumentNumber)
                    ? $"OWN-{owner.Id:N}"[..20]
                    : owner.DocumentNumber.Trim();

                resident = new Resident
                {
                    CompanyId = companyId,
                    FullName = owner.FullName,
                    DocumentType = owner.DocumentType,
                    DocumentNumber = documentNumber,
                    Email = owner.Email,
                    PhoneNumber = owner.Phone ?? string.Empty,
                    IsOwner = true,
                    IsActive = true,
                    ApplicationUserId = owner.Id
                };

                dbContext.Residents.Add(resident);
                await dbContext.SaveChangesAsync(ct);
            }

            var ownedUnitIds = await dbContext.UnitOwners
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.OwnerId == owner.Id && x.Unit != null && !x.Unit.IsDeleted)
                .OrderByDescending(x => x.IsPrimary)
                .Select(x => x.UnitId)
                .ToListAsync(ct);

            if (ownedUnitIds.Count == 0) return;

            var hasActiveLink = await dbContext.UnitResidents
                .AsNoTracking()
                .AnyAsync(x => !x.IsDeleted && x.ResidentId == resident.Id && x.EndDate == null, ct);

            if (hasActiveLink) return;

            dbContext.UnitResidents.Add(new UnitResident
            {
                CompanyId = companyId,
                UnitId = ownedUnitIds[0],
                ResidentId = resident.Id,
                IsPrimary = true,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow)
            });

            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // No bloquea la operación que disparó la sincronización si falla (ej. choque de
            // índice único); se puede completar manualmente desde el módulo de Residentes.
        }
    }
}
