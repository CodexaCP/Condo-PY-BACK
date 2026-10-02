using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Copia el plan de cuentas de un edificio a otro para no volver a armarlo a mano: los rubros de la plantilla se emparejan por su
/// clave y toman nombre, codigo del contador y estado (activo o no) del edificio de origen; los rubros propios (grupos y subrubros)
/// que no existen en el destino se crean. No elimina nada del destino ni toca sus gastos, ingresos o presupuesto, y lo que no se
/// puede copiar (codigos repetidos, tipos distintos, rubros con movimientos) se informa. No guarda: lo hace quien lo llama.
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
            .ToHashSet();
        var withBudget = (await dbContext.BudgetLines.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == targetBuildingId && x.Amount != 0m)
                .Select(x => x.CategoryId).Distinct().ToListAsync(cancellationToken))
            .ToHashSet();

        var result = new LedgerCategoryCopyResultDto();
        var usedCodes = target.Select(x => x.Code.ToUpperInvariant()).ToHashSet();
        var groupMap = new Dictionary<Guid, LedgerCategory>();

        void Note(string message)
        {
            result.Skipped++;
            if (result.Messages.Count < MaxMessages)
            {
                result.Messages.Add(message);
            }
        }

        // Aplica el codigo si no choca con otro rubro del destino; devuelve si algo cambio.
        bool ApplyCode(LedgerCategory t, string code)
        {
            if (t.Code == code)
            {
                return false;
            }

            if (!string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase) && usedCodes.Contains(code.ToUpperInvariant()))
            {
                Note($"El código {code} ya lo usa otro rubro del edificio destino: «{t.Name}» conserva el {t.Code}.");
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

        LedgerCategory Create(LedgerCategory s, Guid? parentId)
        {
            var created = new LedgerCategory
            {
                CompanyId = companyId,
                BuildingId = targetBuildingId,
                ParentId = parentId,
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
            result.Created++;
            return created;
        }

        // 1) Rubros principales, emparejados por codigo.
        foreach (var s in source.Where(x => !x.ParentId.HasValue).OrderBy(x => x.Code, StringComparer.Ordinal))
        {
            var t = target.FirstOrDefault(x => !x.ParentId.HasValue && string.Equals(x.Code, s.Code, StringComparison.OrdinalIgnoreCase));
            if (t is not null)
            {
                if (t.Type != s.Type)
                {
                    Note($"El rubro {s.Code} es de otro tipo en el edificio destino: no se copió ni sus subrubros.");
                    continue;
                }

                if (ApplyCommon(t, s)) result.Updated++;
                groupMap[s.Id] = t;
            }
            else if (usedCodes.Contains(s.Code.ToUpperInvariant()))
            {
                Note($"El código {s.Code} («{s.Name}») ya lo usa un subrubro del edificio destino: no se copió ni sus subrubros.");
            }
            else
            {
                groupMap[s.Id] = Create(s, null);
            }
        }

        // 2) Subrubros: los de la plantilla se emparejan por su clave; los propios, por codigo dentro del mismo rubro principal.
        foreach (var s in source.Where(x => x.ParentId.HasValue).OrderBy(x => x.Code, StringComparer.Ordinal))
        {
            if (!groupMap.TryGetValue(s.ParentId!.Value, out var parent))
            {
                Note($"«{s.Name}» ({s.Code}) no se copió porque su rubro principal no se pudo copiar.");
                continue;
            }

            if (s.SystemKey is not null)
            {
                var t = target.FirstOrDefault(x => x.SystemKey == s.SystemKey);
                if (t is not null)
                {
                    if (ApplyCommon(t, s)) result.Updated++;
                }
                else if (usedCodes.Contains(s.Code.ToUpperInvariant()))
                {
                    Note($"El código {s.Code} («{s.Name}») ya lo usa otro rubro del edificio destino: no se copió.");
                }
                else
                {
                    Create(s, parent.Id);
                }

                continue;
            }

            var own = target.FirstOrDefault(x =>
                x.SystemKey == null && x.ParentId == parent.Id && string.Equals(x.Code, s.Code, StringComparison.OrdinalIgnoreCase));
            if (own is not null)
            {
                if (own.Type != s.Type)
                {
                    Note($"El rubro {s.Code} es de otro tipo en el edificio destino: no se copió.");
                    continue;
                }

                var changed = ApplyCommon(own, s);

                // La categoria de la liquidacion no cambia si ya tiene gastos o ingresos cargados.
                if ((own.ExpenseCategory != s.ExpenseCategory || own.IncomeCategory != s.IncomeCategory) && !withMovements.Contains(own.Id))
                {
                    own.ExpenseCategory = s.ExpenseCategory;
                    own.IncomeCategory = s.IncomeCategory;
                    changed = true;
                }

                if (changed) result.Updated++;
                continue;
            }

            if (usedCodes.Contains(s.Code.ToUpperInvariant()))
            {
                Note($"El código {s.Code} («{s.Name}») ya lo usa otro rubro del edificio destino: no se copió.");
                continue;
            }

            // Un rubro con presupuesto cargado no puede pasar a ser un grupo (el presupuesto se carga en los subrubros).
            var parentHasChildren = target.Any(x => x.ParentId == parent.Id);
            if (!parentHasChildren && (withBudget.Contains(parent.Id) || withMovements.Contains(parent.Id)))
            {
                Note($"«{s.Name}» ({s.Code}) no se copió: el rubro {parent.Code} del edificio destino ya tiene presupuesto o movimientos.");
                continue;
            }

            Create(s, parent.Id);
        }

        return result;
    }
}
