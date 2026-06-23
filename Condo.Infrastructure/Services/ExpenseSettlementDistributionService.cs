using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Infrastructure.Services;

public class ExpenseSettlementDistributionService(ICondoDbContext dbContext) : IExpenseSettlementDistributionService
{
    private const decimal CoefficientDistributionExpectedTotal = 1.00m;
    private const decimal CoefficientDistributionTolerance = 0.0001m;

    public async Task<ExpenseSettlementChargePreviewDto> PreviewAsync(
        ExpensePeriod period,
        ExpenseSettlement settlement,
        CancellationToken cancellationToken)
    {
        var units = await dbContext.Units
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.BuildingId == period.BuildingId)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

        if (units.Count == 0)
        {
            throw new InvalidOperationException("There are no active units available for this building.");
        }

        var expenses = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == period.Id)
            .OrderBy(x => x.ExpenseDate)
            .ThenBy(x => x.Description)
            .ToListAsync(cancellationToken);

        var incomes = await dbContext.BuildingIncomes
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == period.Id)
            .OrderBy(x => x.IncomeDate)
            .ThenBy(x => x.Description)
            .ToListAsync(cancellationToken);

        var totalCoefficient = units.Sum(x => x.Coefficient);
        var requiresCoefficient = expenses.Any(x => x.DistributionType == BuildingExpenseDistributionType.ByCoefficient) || incomes.Count > 0;
        if (requiresCoefficient && !HasValidCoefficientBase(totalCoefficient))
        {
            throw new InvalidOperationException($"No se puede distribuir por coeficiente porque la suma de coeficientes del edificio debe ser {CoefficientDistributionExpectedTotal:0.####} y actualmente es {totalCoefficient:0.####}.");
        }

        var items = new List<ExpenseSettlementChargePreviewItemDto>();

        foreach (var expense in expenses)
        {
            switch (expense.DistributionType)
            {
                case BuildingExpenseDistributionType.ByCoefficient:
                    items.AddRange(DistributeByCoefficient(units, totalCoefficient, expense.Amount, ResolveChargeType(expense), expense.Description.Trim(), expense.Notes.Trim(), expense.Id, settlement.Id));
                    break;
                case BuildingExpenseDistributionType.FixedPerUnit:
                    items.AddRange(DistributeFixedPerUnit(units, expense.Amount, ResolveChargeType(expense), expense.Description.Trim(), expense.Notes.Trim(), expense.Id, settlement.Id));
                    break;
                case BuildingExpenseDistributionType.IndividualUnit:
                    items.Add(CreateIndividualItem(units, expense, settlement.Id));
                    break;
                case BuildingExpenseDistributionType.NonDistributed:
                    break;
                default:
                    throw new InvalidOperationException($"Distribution type {expense.DistributionType} is not supported in settlement generation.");
            }
        }

        foreach (var income in incomes)
        {
            items.AddRange(DistributeByCoefficient(
                units,
                totalCoefficient,
                -income.Amount,
                ExpenseChargeType.Adjustment,
                $"Compensacion {income.Description.Trim()}",
                income.Notes.Trim(),
                null,
                settlement.Id));
        }

        return new ExpenseSettlementChargePreviewDto
        {
            ExpensePeriodId = period.Id,
            ExpensePeriodName = period.Name,
            BuildingId = period.BuildingId,
            BuildingName = period.Building?.Name ?? string.Empty,
            SettlementId = settlement.Id,
            ChargeCount = items.Count,
            UnitsAffected = items.Select(x => x.UnitId).Distinct().Count(),
            TotalGeneratedAmount = items.Sum(x => x.Amount),
            Items = items
        };
    }

    private static IReadOnlyList<ExpenseSettlementChargePreviewItemDto> DistributeFixedPerUnit(
        IReadOnlyList<Unit> units,
        decimal totalAmount,
        ExpenseChargeType chargeType,
        string concept,
        string notes,
        Guid sourceBuildingExpenseId,
        Guid sourceSettlementId)
    {
        var items = new List<ExpenseSettlementChargePreviewItemDto>(units.Count);
        var roundedTotal = decimal.Round(totalAmount, 2, MidpointRounding.AwayFromZero);
        var baseAmount = decimal.Round(roundedTotal / units.Count, 2, MidpointRounding.AwayFromZero);
        var remaining = roundedTotal;

        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            var amount = index == units.Count - 1 ? remaining : baseAmount;
            remaining -= amount;

            items.Add(new ExpenseSettlementChargePreviewItemDto
            {
                UnitId = unit.Id,
                UnitCode = unit.Code,
                ChargeType = chargeType,
                Concept = concept,
                Amount = amount,
                Notes = notes,
                SourceBuildingExpenseId = sourceBuildingExpenseId,
                SourceSettlementId = sourceSettlementId
            });
        }

        return items;
    }

    private static IReadOnlyList<ExpenseSettlementChargePreviewItemDto> DistributeByCoefficient(
        IReadOnlyList<Unit> units,
        decimal totalCoefficient,
        decimal totalAmount,
        ExpenseChargeType chargeType,
        string concept,
        string notes,
        Guid? sourceBuildingExpenseId,
        Guid sourceSettlementId)
    {
        var items = new List<ExpenseSettlementChargePreviewItemDto>(units.Count);
        var roundedTotal = decimal.Round(totalAmount, 2, MidpointRounding.AwayFromZero);
        var distributed = 0m;

        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            var amount = index == units.Count - 1
                ? roundedTotal - distributed
                : decimal.Round(roundedTotal * unit.Coefficient, 2, MidpointRounding.AwayFromZero);

            distributed += amount;

            items.Add(new ExpenseSettlementChargePreviewItemDto
            {
                UnitId = unit.Id,
                UnitCode = unit.Code,
                ChargeType = chargeType,
                Concept = concept,
                Amount = amount,
                Notes = notes,
                SourceBuildingExpenseId = sourceBuildingExpenseId,
                SourceSettlementId = sourceSettlementId
            });
        }

        return items;
    }

    private static bool HasValidCoefficientBase(decimal totalCoefficient) =>
        decimal.Abs(totalCoefficient - CoefficientDistributionExpectedTotal) <= CoefficientDistributionTolerance;

    private static ExpenseSettlementChargePreviewItemDto CreateIndividualItem(
        IReadOnlyList<Unit> units,
        BuildingExpense expense,
        Guid sourceSettlementId)
    {
        var unit = units.FirstOrDefault(x => x.Id == expense.TargetUnitId);
        if (unit is null)
        {
            throw new InvalidOperationException($"Target unit for expense {expense.Description} was not found or is inactive.");
        }

        return new ExpenseSettlementChargePreviewItemDto
        {
            UnitId = unit.Id,
            UnitCode = unit.Code,
            ChargeType = ExpenseChargeType.Individual,
            Concept = expense.Description.Trim(),
            Amount = decimal.Round(expense.Amount, 2, MidpointRounding.AwayFromZero),
            Notes = expense.Notes.Trim(),
            SourceBuildingExpenseId = expense.Id,
            SourceSettlementId = sourceSettlementId
        };
    }

    private static ExpenseChargeType ResolveChargeType(BuildingExpense expense) =>
        expense.DistributionType == BuildingExpenseDistributionType.IndividualUnit
            ? ExpenseChargeType.Individual
            : expense.Category == BuildingExpenseCategory.ReserveFund
                ? ExpenseChargeType.ReserveFund
                : expense.Category == BuildingExpenseCategory.Extraordinary
                    ? ExpenseChargeType.Extraordinary
                    : ExpenseChargeType.Ordinary;
}
