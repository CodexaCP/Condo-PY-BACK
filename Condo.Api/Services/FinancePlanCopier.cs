using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Copia el plan de cuentas de un edificio a otro para no volver a armarlo a mano. Las cuentas se emparejan por su funcion especial (si la
/// tienen) o por su codigo, y toman nombre, codigo del contador y estado (activa o no) del edificio de origen; las que no existen en el
/// destino se crean, en cualquier nivel del arbol. No elimina nada del destino ni toca sus gastos, ingresos o presupuesto, y lo que no se
/// puede copiar (codigos repetidos, tipos distintos, cuentas con movimientos) se informa. No guarda: lo hace quien lo llama.
/// </summary>
public class FinancePlanCopier(ICondoDbContext dbContext)
{
    private const int MaxMessages = 20;

    public async Task<LedgerCategoryCopyResultDto> CopyAsync(
        Guid sourceBuildingId, Guid targetBuildingId, Guid companyId, CancellationToken cancellationToken)
    {
        var source = await dbContext.LedgerCategories.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == sourceBuildingId)
            .ToListAsync(cancellationToken);
        var target = await dbContext.LedgerCategories
            .Where(x => !x.IsDeleted && x.BuildingId == targetBuildingId)
            .ToListAsync(cancellationToken);

        var withMovements = (await dbContext.BuildingExpenses.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == targetBuildingId && x.LedgerCategoryId != null)
                .Select(x => x.LedgerCategoryId!.Value).Distinct().ToListAsync(cancellationToken))
            .Concat(await dbContext.BuildingIncomes.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == targetBuildingId && x.LedgerCategoryId != null)
                .Select(x => x.LedgerCategoryId!.Value).Distinct().ToListAsync(cancellationToken))
            .Concat(await dbContext.RecurringBuildingExpenses.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == targetBuildingId && x.LedgerCategoryId != null)
                .Select(x => x.LedgerCategoryId!.Value).Distinct().ToListAsync(cancellationToken))
            .ToHashSet();
        var withBudget = (await dbContext.BudgetLines.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == targetBuildingId && x.Amount != 0m)
                .Select(x => x.CategoryId).Distinct().ToListAsync(cancellationToken))
            .ToHashSet();

        var result = new LedgerCategoryCopyResultDto();
        var usedCodes = target.Select(x => x.Code.ToUpperInvariant()).ToHashSet();
        var map = new Dictionary<Guid, LedgerCategory>();

        void Note(string message)
        {
            result.Skipped++;
            if (result.Messages.Count < MaxMessages)
            {
                result.Messages.Add(message);
            }
        }

        // Aplica el codigo si no choca con otra cuenta del destino; devuelve si algo cambio.
        bool ApplyCode(LedgerCategory t, string code)
        {
            if (t.Code == code)
            {
                return false;
            }

            if (!string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase) && usedCodes.Contains(code.ToUpperInvariant()))
            {
                Note($"El código {code} ya lo usa otra cuenta del edificio destino: «{t.Name}» conserva el {t.Code}.");
                return false;
            }

            usedCodes.Remove(t.Code.ToUpperInvariant());
            t.Code = code;
            usedCodes.Add(code.ToUpperInvariant());
            return true;
        }

        bool ApplyCommon(LedgerCategory t, LedgerCategory s)
        {
            var changed = ApplyCode(t, s.Code);
            if (t.Name != s.Name) { t.Name = s.Name; changed = true; }
            if (t.ExternalCode != s.ExternalCode) { t.ExternalCode = s.ExternalCode; changed = true; }
            if (t.IsActive != s.IsActive) { t.IsActive = s.IsActive; changed = true; }
            return changed;
        }

        // Nivel de cada cuenta de origen: los grupos se copian antes que sus cuentas.
        var sourceById = source.ToDictionary(x => x.Id);
        int Depth(LedgerCategory c)
        {
            var depth = 1;
            var current = c;
            while (current.ParentId.HasValue && sourceById.TryGetValue(current.ParentId.Value, out var parent) && depth < 20)
            {
                current = parent;
                depth++;
            }

            return depth;
        }

        foreach (var s in source.OrderBy(Depth).ThenBy(x => x.Code, StringComparer.Ordinal))
        {
            LedgerCategory? parent = null;
            if (s.ParentId.HasValue && !map.TryGetValue(s.ParentId.Value, out parent))
            {
                Note($"«{s.Name}» ({s.Code}) no se copió porque su grupo no se pudo copiar.");
                continue;
            }

            if (parent is not null && parent.Type != s.Type)
            {
                Note($"«{s.Name}» ({s.Code}) es de otro tipo que su grupo en el edificio destino: no se copió.");
                continue;
            }

            // Emparejar: por funcion especial y, si no, por codigo.
            var t = s.SystemKey is not null
                ? target.FirstOrDefault(x => x.SystemKey == s.SystemKey)
                : target.FirstOrDefault(x => string.Equals(x.Code, s.Code, StringComparison.OrdinalIgnoreCase));

            if (t is not null)
            {
                if (t.Type != s.Type)
                {
                    Note($"La cuenta {s.Code} es de otro tipo en el edificio destino: no se copió ni sus subcuentas.");
                    continue;
                }

                var changed = ApplyCommon(t, s);

                // La categoria de la liquidacion no cambia si ya tiene gastos o ingresos cargados ni si tiene una funcion especial.
                if (t.SystemKey is null && (t.ExpenseCategory != s.ExpenseCategory || t.IncomeCategory != s.IncomeCategory))
                {
                    if (withMovements.Contains(t.Id))
                    {
                        Note($"La cuenta {t.Code} ya tiene gastos o ingresos cargados: conserva su categoría de liquidación.");
                    }
                    else
                    {
                        t.ExpenseCategory = s.ExpenseCategory;
                        t.IncomeCategory = s.IncomeCategory;
                        changed = true;
                    }
                }

                if (changed) result.Updated++;
                map[s.Id] = t;
                continue;
            }

            if (usedCodes.Contains(s.Code.ToUpperInvariant()))
            {
                // El codigo lo usa una cuenta con otra funcion: no se pisa.
                Note($"El código {s.Code} («{s.Name}») ya lo usa otra cuenta del edificio destino: no se copió ni sus subcuentas.");
                continue;
            }

            // Una cuenta con presupuesto, movimientos o funcion especial no puede pasar a ser un grupo.
            if (parent is not null && !target.Any(x => x.ParentId == parent.Id)
                && (withBudget.Contains(parent.Id) || withMovements.Contains(parent.Id) || parent.SystemKey is not null))
            {
                Note($"«{s.Name}» ({s.Code}) no se copió: la cuenta {parent.Code} del edificio destino ya tiene presupuesto, movimientos o una función especial.");
                continue;
            }

            var created = new LedgerCategory
            {
                CompanyId = companyId,
                BuildingId = targetBuildingId,
                ParentId = parent?.Id,
                Code = s.Code,
                Name = s.Name,
                Type = s.Type,
                ExternalCode = s.ExternalCode,
                SystemKey = s.SystemKey,
                ExpenseCategory = s.ExpenseCategory,
                IncomeCategory = s.IncomeCategory,
                IsActive = s.IsActive
            };
            dbContext.LedgerCategories.Add(created);
            target.Add(created);
            usedCodes.Add(created.Code.ToUpperInvariant());
            map[s.Id] = created;
            result.Created++;
        }

        return result;
    }
}
