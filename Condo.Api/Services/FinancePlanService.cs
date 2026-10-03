using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Aplica un plan de cuentas (el generico o uno importado) a un edificio. <b>Reemplazar</b> sustituye el plan entero: desvincula el rubro de
/// los gastos, ingresos y plantillas recurrentes que lo usaban, borra el presupuesto cargado y crea el plan nuevo. <b>Agregar o actualizar</b>
/// no borra nada: crea las cuentas que faltan (por codigo) y, si se pide, actualiza nombre, codigo del contador, estado y categoria de las
/// que ya existen. Los movimientos, pagos y liquidaciones nunca se tocan: el libro y los reportes se calculan al consultar.
/// </summary>
public class FinancePlanService(ICondoDbContext dbContext)
{
    private const int MaxMessages = 30;

    /// <summary>Lo que se pierde (se desvincula o se borra) al reemplazar el plan de un edificio.</summary>
    public async Task<LedgerPlanImpactDto> GetImpactAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        var categoryIds = dbContext.LedgerCategories.Where(x => !x.IsDeleted && x.BuildingId == buildingId).Select(x => x.Id);
        return new LedgerPlanImpactDto
        {
            Categories = await dbContext.LedgerCategories.CountAsync(x => !x.IsDeleted && x.BuildingId == buildingId, cancellationToken),
            Expenses = await dbContext.BuildingExpenses.CountAsync(x => !x.IsDeleted && x.LedgerCategoryId != null && categoryIds.Contains(x.LedgerCategoryId.Value), cancellationToken),
            Incomes = await dbContext.BuildingIncomes.CountAsync(x => !x.IsDeleted && x.LedgerCategoryId != null && categoryIds.Contains(x.LedgerCategoryId.Value), cancellationToken),
            RecurringExpenses = await dbContext.RecurringBuildingExpenses.CountAsync(x => !x.IsDeleted && x.LedgerCategoryId != null && categoryIds.Contains(x.LedgerCategoryId.Value), cancellationToken),
            BudgetLines = await dbContext.BudgetLines.CountAsync(x => !x.IsDeleted && x.BuildingId == buildingId && x.Amount != 0m, cancellationToken)
        };
    }

    /// <summary>Reemplaza el plan del edificio por <paramref name="rows"/> (ya validadas). Todo o nada: corre en una transaccion.</summary>
    public async Task<LedgerPlanApplyResultDto> ReplaceAsync(
        Guid buildingId, Guid companyId, IReadOnlyList<PlanRowSpec> rows, CancellationToken cancellationToken)
    {
        var impact = await GetImpactAsync(buildingId, cancellationToken);
        var result = new LedgerPlanApplyResultDto
        {
            Created = rows.Count,
            UnlinkedExpenses = impact.Expenses,
            UnlinkedIncomes = impact.Incomes,
            UnlinkedRecurringExpenses = impact.RecurringExpenses,
            DeletedBudgetLines = impact.BudgetLines,
            RemovedCategories = impact.Categories
        };

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var oldIds = dbContext.LedgerCategories.Where(x => !x.IsDeleted && x.BuildingId == buildingId).Select(x => x.Id);

            await dbContext.BuildingExpenses
                .Where(x => x.LedgerCategoryId != null && oldIds.Contains(x.LedgerCategoryId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LedgerCategoryId, (Guid?)null), cancellationToken);
            await dbContext.BuildingIncomes
                .Where(x => x.LedgerCategoryId != null && oldIds.Contains(x.LedgerCategoryId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LedgerCategoryId, (Guid?)null), cancellationToken);
            await dbContext.RecurringBuildingExpenses
                .Where(x => x.LedgerCategoryId != null && oldIds.Contains(x.LedgerCategoryId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LedgerCategoryId, (Guid?)null), cancellationToken);
            await dbContext.BudgetLines
                .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true), cancellationToken);
            await dbContext.LedgerCategories
                .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true), cancellationToken);

            var created = new Dictionary<string, LedgerCategory>(StringComparer.OrdinalIgnoreCase);
            foreach (var spec in OrderByDepth(rows))
            {
                var entity = NewEntity(spec, buildingId, companyId, spec.ParentCode is null ? null : created[spec.ParentCode].Id);
                created[spec.Code] = entity;
                dbContext.LedgerCategories.Add(entity);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        // Los objetos que ya tenia cargados el contexto quedaron desactualizados por las actualizaciones masivas.
        dbContext.ChangeTracker.Clear();
        return result;
    }

    /// <summary>
    /// Agrega las cuentas que faltan (por codigo). Con <paramref name="updateExisting"/> actualiza tambien nombre, codigo del contador, estado
    /// y categoria de liquidacion de las que ya existen (la categoria no cambia si ya tienen gastos o ingresos cargados). No guarda:
    /// lo hace quien lo llama.
    /// </summary>
    public async Task<LedgerPlanApplyResultDto> MergeAsync(
        Guid buildingId, Guid companyId, IReadOnlyList<PlanRowSpec> rows, bool updateExisting, CancellationToken cancellationToken)
    {
        var existing = await dbContext.LedgerCategories
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .ToListAsync(cancellationToken);
        var byCode = existing.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var byKey = existing.Where(x => x.SystemKey != null).ToDictionary(x => x.SystemKey!, StringComparer.Ordinal);
        var parentIds = existing.Where(x => x.ParentId.HasValue).Select(x => x.ParentId!.Value).ToHashSet();

        var withMovements = (await dbContext.BuildingExpenses.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.LedgerCategoryId != null)
                .Select(x => x.LedgerCategoryId!.Value).Distinct().ToListAsync(cancellationToken))
            .Concat(await dbContext.BuildingIncomes.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.LedgerCategoryId != null)
                .Select(x => x.LedgerCategoryId!.Value).Distinct().ToListAsync(cancellationToken))
            .Concat(await dbContext.RecurringBuildingExpenses.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.LedgerCategoryId != null)
                .Select(x => x.LedgerCategoryId!.Value).Distinct().ToListAsync(cancellationToken))
            .ToHashSet();
        var withBudget = (await dbContext.BudgetLines.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Amount != 0m)
                .Select(x => x.CategoryId).Distinct().ToListAsync(cancellationToken))
            .ToHashSet();

        var result = new LedgerPlanApplyResultDto();
        var resolved = new Dictionary<string, LedgerCategory>(StringComparer.OrdinalIgnoreCase);
        var skippedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Note(string message)
        {
            result.Skipped++;
            if (result.Messages.Count < MaxMessages)
            {
                result.Messages.Add(message);
            }
        }

        foreach (var spec in OrderByDepth(rows))
        {
            LedgerCategory? parent = null;
            if (spec.ParentCode is not null)
            {
                if (!resolved.TryGetValue(spec.ParentCode, out parent))
                {
                    Note($"«{spec.Name}» ({spec.Code}) no se agregó porque su grupo {spec.ParentCode} no se pudo agregar.");
                    skippedCodes.Add(spec.Code);
                    continue;
                }
            }

            if (byCode.TryGetValue(spec.Code, out var current))
            {
                if (current.Type != spec.Type)
                {
                    Note($"La cuenta {spec.Code} ya existe con otro tipo: no se tocó ni sus subcuentas.");
                    skippedCodes.Add(spec.Code);
                    continue;
                }

                resolved[spec.Code] = current;
                if (updateExisting && Update(current, spec, withMovements.Contains(current.Id), byKey, Note))
                {
                    result.Updated++;
                }

                continue;
            }

            // Una cuenta con presupuesto o movimientos no puede pasar a ser un grupo.
            if (parent is not null && !parentIds.Contains(parent.Id) && (withBudget.Contains(parent.Id) || withMovements.Contains(parent.Id)))
            {
                Note($"«{spec.Name}» ({spec.Code}) no se agregó: la cuenta {parent.Code} ya tiene presupuesto o movimientos y no puede pasar a ser un grupo.");
                skippedCodes.Add(spec.Code);
                continue;
            }

            var key = spec.SystemKey;
            if (key is not null && byKey.TryGetValue(key, out var holder))
            {
                Note($"La función «{FinanceChartTemplate.FindRole(key)?.Label ?? key}» ya la tiene la cuenta {holder.Code}: «{spec.Name}» se agregó sin ella.");
                key = null;
            }

            var entity = NewEntity(spec with { SystemKey = key }, buildingId, companyId, parent?.Id);
            if (parent is not null)
            {
                parentIds.Add(parent.Id);
            }

            dbContext.LedgerCategories.Add(entity);
            byCode[entity.Code] = entity;
            if (key is not null)
            {
                byKey[key] = entity;
            }

            resolved[spec.Code] = entity;
            result.Created++;
        }

        return result;
    }

    private static bool Update(
        LedgerCategory current, PlanRowSpec spec, bool hasMovements, Dictionary<string, LedgerCategory> byKey, Action<string> note)
    {
        var changed = false;
        if (current.Name != spec.Name) { current.Name = spec.Name; changed = true; }
        if (spec.ExternalCode is not null && current.ExternalCode != spec.ExternalCode) { current.ExternalCode = spec.ExternalCode; changed = true; }
        if (current.IsActive != spec.IsActive) { current.IsActive = spec.IsActive; changed = true; }

        if ((current.ExpenseCategory != spec.ExpenseCategory || current.IncomeCategory != spec.IncomeCategory)
            && (spec.ExpenseCategory.HasValue || spec.IncomeCategory.HasValue) && current.SystemKey is null)
        {
            if (hasMovements)
            {
                note($"La cuenta {current.Code} ya tiene gastos o ingresos cargados: conserva su categoría de liquidación.");
            }
            else
            {
                current.ExpenseCategory = spec.ExpenseCategory;
                current.IncomeCategory = spec.IncomeCategory;
                changed = true;
            }
        }

        return changed;
    }

    private static LedgerCategory NewEntity(PlanRowSpec spec, Guid buildingId, Guid companyId, Guid? parentId) => new()
    {
        CompanyId = companyId,
        BuildingId = buildingId,
        ParentId = parentId,
        Code = spec.Code,
        Name = spec.Name,
        Type = spec.Type,
        ExternalCode = spec.ExternalCode,
        SystemKey = spec.SystemKey,
        ExpenseCategory = spec.ExpenseCategory,
        IncomeCategory = spec.IncomeCategory,
        IsActive = spec.IsActive
    };

    // Los grupos antes que sus cuentas (por nivel), y dentro de cada nivel por codigo.
    private static List<PlanRowSpec> OrderByDepth(IReadOnlyList<PlanRowSpec> rows)
    {
        var byCode = rows.ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);
        int Depth(PlanRowSpec r)
        {
            var depth = 1;
            var current = r;
            while (current.ParentCode is not null && byCode.TryGetValue(current.ParentCode, out var parent) && depth <= FinanceChartTemplate.MaxDepth)
            {
                current = parent;
                depth++;
            }

            return depth;
        }

        return rows.OrderBy(Depth).ThenBy(r => r.Code, StringComparer.Ordinal).ToList();
    }
}
