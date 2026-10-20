using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Cuenta de cobros online por liquidar de un edificio: una por edificio, creada por el sistema. Representa lo que el proveedor de pagos
/// debe al edificio por los cobros confirmados; no es dinero disponible y se vacia con una transferencia al banco cuando el proveedor liquida.
/// </summary>
public static class ClearingAccounts
{
    public const string DefaultName = "Cobros online por liquidar";

    /// <summary>
    /// Devuelve la cuenta de cobros por liquidar del edificio, creandola (agregada al contexto, sin guardar) si no existe. Una cuenta
    /// desactivada se reactiva. Quien llama decide si el edificio tiene Finanzas habilitado y guarda los cambios.
    /// </summary>
    public static async Task<FinancialAccount> EnsureAsync(ICondoDbContext dbContext, Guid companyId, Guid buildingId, CancellationToken ct)
    {
        var existing = await dbContext.FinancialAccounts
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == buildingId && x.Type == FinancialAccountType.Clearing, ct);
        if (existing is not null)
        {
            existing.IsActive = true;
            return existing;
        }

        var names = await dbContext.FinancialAccounts.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .Select(x => x.Name)
            .ToListAsync(ct);
        var name = names.Any(n => string.Equals(n, DefaultName, StringComparison.OrdinalIgnoreCase)) ? $"{DefaultName} (sistema)" : DefaultName;

        var account = new FinancialAccount
        {
            CompanyId = companyId,
            BuildingId = buildingId,
            Name = name,
            Type = FinancialAccountType.Clearing,
            OpeningBalance = 0m,
            IsActive = true
        };
        dbContext.FinancialAccounts.Add(account);
        return account;
    }
}
