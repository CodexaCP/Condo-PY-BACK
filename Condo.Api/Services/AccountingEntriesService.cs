using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Asientos sugeridos (partida doble) a partir del libro de Finanzas, y la asignacion de las cuentas del plan que hacen de contrapartida
/// (cada cuenta financiera y el IVA credito). Son sugeridos, no asentados: no cambian ningun saldo ni el libro. La base es la caja (percibido).
/// </summary>
public class AccountingEntriesService(ICondoDbContext dbContext, FinanceLedgerService ledger, ITenantContext tenantContext)
{
    // Un rango de asientos no abarca mas de esto (igual que el libro de compras).
    public const int MaxRangeDays = 400;

    private ConfigAuditWriter Audit => new(dbContext, tenantContext);

    // ─── Cuentas del plan que se pueden elegir ────────────────────────────────

    // Cuentas finales de activo o de fondos. Se admiten las inactivas: el plan de plantilla trae varias (por ejemplo el IVA credito fiscal) sin
    // activar, y aca la cuenta solo se usa para rotular el asiento, no para cargar movimientos. Las activas van primero.
    private static List<LedgerCategory> Candidates(LedgerContext ctx)
    {
        var parents = ctx.Categories.Where(c => c.ParentId.HasValue).Select(c => c.ParentId!.Value).ToHashSet();
        return ctx.Categories
            .Where(c => !parents.Contains(c.Id) && c.Type is LedgerCategoryType.Asset or LedgerCategoryType.Fund)
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .ToList();
    }

    private static AccountingPlanAccount ToPlan(LedgerCategory c) => new(c.Id, c.Code, c.ExternalCode, c.Name);

    // ─── Contrapartidas ───────────────────────────────────────────────────────

    public async Task<AccountingRolesDto> RolesAsync(LedgerContext ctx, bool canEdit, CancellationToken ct)
    {
        var roles = await dbContext.LedgerAccountRoles.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId)
            .ToListAsync(ct);
        var options = Candidates(ctx);
        var optionIds = options.Select(o => o.Id).ToHashSet();
        var byId = ctx.ById;

        var used = roles.Select(r => r.LedgerCategoryId).ToHashSet();
        var suggestionPool = options.Where(o => !used.Contains(o.Id)).ToList();

        var accounts = new List<AccountingRoleAccountDto>();
        foreach (var account in ctx.Accounts.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var role = roles.FirstOrDefault(r => r.Role == LedgerAccountRoleKind.FinancialAccount && r.FinancialAccountId == account.Id);
            var category = role is not null && byId.TryGetValue(role.LedgerCategoryId, out var c) ? c : null;
            var suggested = category is null ? Suggest(account, suggestionPool) : null;
            if (suggested is not null) suggestionPool.Remove(suggested);

            accounts.Add(new AccountingRoleAccountDto
            {
                FinancialAccountId = account.Id,
                Name = account.Name,
                Type = account.Type,
                IsActive = account.IsActive,
                LedgerCategoryId = category?.Id,
                Code = category?.Code,
                CategoryName = category?.Name,
                SuggestedCategoryId = suggested?.Id
            });
        }

        var vatRole = roles.FirstOrDefault(r => r.Role == LedgerAccountRoleKind.VatCredit);
        var vatCategory = vatRole is not null && byId.TryGetValue(vatRole.LedgerCategoryId, out var vc) ? vc : null;
        var suggestedVat = vatCategory is null
            ? suggestionPool.FirstOrDefault(o => o.Type == LedgerCategoryType.Asset && o.Name.Contains("IVA", StringComparison.OrdinalIgnoreCase)
                                                 && o.Name.Contains("cr", StringComparison.OrdinalIgnoreCase))
            : null;

        var parentsById = byId;
        return new AccountingRolesDto
        {
            BuildingId = ctx.BuildingId,
            CanEdit = canEdit,
            Accounts = accounts,
            VatCreditCategoryId = vatCategory?.Id,
            VatCreditCode = vatCategory?.Code,
            VatCreditName = vatCategory?.Name,
            SuggestedVatCreditCategoryId = suggestedVat?.Id,
            Options = options.Select(o => new AccountingPlanOptionDto
            {
                Id = o.Id,
                Code = o.Code,
                ExternalCode = o.ExternalCode,
                Name = o.Name,
                Type = o.Type,
                IsActive = o.IsActive,
                GroupName = o.ParentId is { } pid && parentsById.TryGetValue(pid, out var p) ? $"{p.Code} {p.Name}" : string.Empty
            }).ToList(),
            IsComplete = accounts.Where(a => a.IsActive).All(a => a.LedgerCategoryId.HasValue && optionIds.Contains(a.LedgerCategoryId.Value))
                         && vatCategory is not null && optionIds.Contains(vatCategory.Id)
        };
    }

    // Sugerencia por tipo y nombre: banco con "Banco", caja con "Caja" y fondo con "reserva" o "fondo".
    private static LedgerCategory? Suggest(FinancialAccount account, List<LedgerCategory> pool) => account.Type switch
    {
        FinancialAccountType.Bank => pool.FirstOrDefault(o => o.Type == LedgerCategoryType.Asset && o.Name.StartsWith("Banco", StringComparison.OrdinalIgnoreCase)),
        FinancialAccountType.Cash => pool.FirstOrDefault(o => o.Type == LedgerCategoryType.Asset && o.Name.StartsWith("Caja", StringComparison.OrdinalIgnoreCase)),
        FinancialAccountType.ReserveFund => pool.FirstOrDefault(o => o.Type == LedgerCategoryType.Fund && o.Name.Contains("reserva", StringComparison.OrdinalIgnoreCase))
                                            ?? pool.FirstOrDefault(o => o.Type == LedgerCategoryType.Fund),
        _ => null
    };

    public async Task<(AccountingRolesDto? Roles, string? Error)> UpdateRolesAsync(
        LedgerContext ctx, UpdateAccountingRolesRequest request, Guid companyId, CancellationToken ct)
    {
        var items = request.Accounts ?? [];
        if (items.GroupBy(x => x.FinancialAccountId).Any(g => g.Count() > 1)) return (null, "Hay una cuenta financiera repetida.");

        var candidates = Candidates(ctx).ToDictionary(c => c.Id);
        var accountsById = ctx.Accounts.ToDictionary(a => a.Id);
        foreach (var item in items)
        {
            if (!accountsById.ContainsKey(item.FinancialAccountId)) return (null, "Alguna cuenta financiera no existe en este edificio.");
            if (item.LedgerCategoryId.HasValue && !candidates.ContainsKey(item.LedgerCategoryId.Value))
            {
                return (null, "Alguna cuenta del plan no existe en este edificio o no es una cuenta final de activo o de fondos.");
            }
        }

        if (request.SetVatCredit && request.VatCreditCategoryId.HasValue)
        {
            if (!candidates.TryGetValue(request.VatCreditCategoryId.Value, out var vat) || vat.Type != LedgerCategoryType.Asset)
            {
                return (null, "La cuenta del IVA crédito debe ser una cuenta final de activo del plan.");
            }
        }

        var existing = await dbContext.LedgerAccountRoles
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId)
            .ToListAsync(ct);
        var changes = new List<ConfigChange>();
        var now = DateTime.UtcNow;

        string LabelOf(Guid? categoryId) =>
            categoryId.HasValue && ctx.ById.TryGetValue(categoryId.Value, out var c) ? $"{c.Code} {c.Name}" : "Sin asignar";

        void Apply(LedgerAccountRoleKind kind, Guid? financialAccountId, Guid? categoryId, string field, string label)
        {
            var current = existing.FirstOrDefault(x => x.Role == kind && x.FinancialAccountId == financialAccountId);
            if (current?.LedgerCategoryId == categoryId) return;

            changes.Add(new ConfigChange(field, label, LabelOf(current?.LedgerCategoryId), LabelOf(categoryId)));
            if (categoryId is null)
            {
                current!.IsDeleted = true;
                current.UpdatedAtUtc = now;
            }
            else if (current is null)
            {
                dbContext.LedgerAccountRoles.Add(new LedgerAccountRole
                {
                    CompanyId = companyId, BuildingId = ctx.BuildingId, Role = kind, FinancialAccountId = financialAccountId, LedgerCategoryId = categoryId.Value
                });
            }
            else
            {
                current.LedgerCategoryId = categoryId.Value;
                current.UpdatedAtUtc = now;
            }
        }

        foreach (var item in items)
        {
            var account = accountsById[item.FinancialAccountId];
            Apply(LedgerAccountRoleKind.FinancialAccount, account.Id, item.LedgerCategoryId, $"account.{account.Id}", $"Cuenta del plan de «{account.Name}»");
        }

        if (request.SetVatCredit)
        {
            Apply(LedgerAccountRoleKind.VatCredit, null, request.VatCreditCategoryId, "vatCredit", "Cuenta del IVA crédito fiscal");
        }

        if (changes.Count > 0)
        {
            Audit.Add(companyId, ctx.BuildingId, ConfigSectionKeys.Accounting, "Updated",
                $"Asientos contables: cambió la cuenta del plan de {changes.Count} contrapartidas.", "LedgerAccountRole", null, changes);
            await dbContext.SaveChangesAsync(ct);
        }

        return (await RolesAsync(ctx, canEdit: true, ct), null);
    }

    // ─── Asientos ─────────────────────────────────────────────────────────────

    public async Task<(AccountingEntriesDto? Entries, string? Error)> EntriesAsync(
        LedgerContext ctx, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = FinancePeriods.Today();
        var end = to ?? today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        if (end < start) return (null, "La fecha desde no puede ser posterior a la fecha hasta.");
        if (end.DayNumber - start.DayNumber > MaxRangeDays) return (null, $"Los asientos no pueden abarcar más de {MaxRangeDays} días.");

        var rows = await ledger.GetRowsAsync(ctx, start < ctx.StartDate ? ctx.StartDate : start, end, ct);

        var roles = await dbContext.LedgerAccountRoles.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId)
            .ToListAsync(ct);
        var financialPlan = new Dictionary<Guid, AccountingPlanAccount>();
        foreach (var role in roles.Where(r => r.Role == LedgerAccountRoleKind.FinancialAccount && r.FinancialAccountId.HasValue))
        {
            if (ctx.ById.TryGetValue(role.LedgerCategoryId, out var category)) financialPlan[role.FinancialAccountId!.Value] = ToPlan(category);
        }

        AccountingPlanAccount? vatCredit = null;
        var vatRole = roles.FirstOrDefault(r => r.Role == LedgerAccountRoleKind.VatCredit);
        if (vatRole is not null && ctx.ById.TryGetValue(vatRole.LedgerCategoryId, out var vatCategory)) vatCredit = ToPlan(vatCategory);

        var input = new AccountingBuildInput
        {
            FinancialPlan = financialPlan,
            FinancialNames = ctx.Accounts.ToDictionary(a => a.Id, a => a.Name),
            VatCredit = vatCredit,
            RubroPlan = key => ctx.CategoryFor(key) is { } c ? ToPlan(c) : null,
            RubroLabel = key => ctx.CategoryFor(key) is { } c ? $"{c.Code} {c.Name}" : key,
            VatRates = await VatRatesAsync(rows, ct)
        };

        var built = AccountingEntryBuilder.Build(rows, input);
        return (new AccountingEntriesDto
        {
            BuildingId = ctx.BuildingId,
            BuildingName = ctx.BuildingName,
            From = start,
            To = end,
            Entries = built.Entries,
            Totals = built.Totals,
            EntryCount = built.Entries.Count,
            IncompleteCount = built.Entries.Count(e => !e.IsComplete),
            TotalDebit = built.Entries.Sum(e => e.Debit),
            TotalCredit = built.Entries.Sum(e => e.Credit),
            Pending = built.Pending
        }, null);
    }

    // La tasa de IVA de cada gasto y, en las notas de credito del proveedor, la del gasto que corrigen.
    private async Task<Dictionary<(LedgerSourceType, Guid), decimal>> VatRatesAsync(IReadOnlyList<LedgerRow> rows, CancellationToken ct)
    {
        var rates = new Dictionary<(LedgerSourceType, Guid), decimal>();

        var expenseIds = rows.Where(r => r.SourceType == LedgerSourceType.BuildingExpense).Select(r => r.SourceId).Distinct().ToList();
        if (expenseIds.Count > 0)
        {
            var expenses = await dbContext.BuildingExpenses.AsNoTracking()
                .Where(x => expenseIds.Contains(x.Id) && x.VatRate != null)
                .Select(x => new { x.Id, Rate = x.VatRate!.Value })
                .ToListAsync(ct);
            foreach (var e in expenses) rates[(LedgerSourceType.BuildingExpense, e.Id)] = e.Rate;
        }

        var noteIds = rows.Where(r => r.SourceType == LedgerSourceType.SupplierCreditNote).Select(r => r.SourceId).Distinct().ToList();
        if (noteIds.Count > 0)
        {
            var notes = await dbContext.BuildingExpenseCreditNotes.AsNoTracking()
                .Where(x => noteIds.Contains(x.Id) && x.BuildingExpense != null && x.BuildingExpense.VatRate != null)
                .Select(x => new { x.Id, Rate = x.BuildingExpense!.VatRate!.Value })
                .ToListAsync(ct);
            foreach (var n in notes) rates[(LedgerSourceType.SupplierCreditNote, n.Id)] = n.Rate;
        }

        return rates;
    }
}
