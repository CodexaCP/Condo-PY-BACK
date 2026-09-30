using Condo.Application.Abstractions;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Saldo con el que cierra un periodo: es el "Saldo acumulado" con el que arranca el mes siguiente. Lo usan el
/// arrastre de saldo y el clonado de periodo, para que los dos den siempre el mismo valor.
/// </summary>
public static class PeriodClosingBalance
{
    public sealed record Result(decimal TotalIngresos, decimal TotalGastos, decimal Saldo, string Note);

    public static async Task<Result> ComputeAsync(
        ICondoDbContext dbContext, Building building, Guid sourcePeriodId, CancellationToken cancellationToken)
    {
        var totalIngresos = await dbContext.BuildingIncomes
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == sourcePeriodId && x.BuildingId == building.Id)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var totalGastos = await dbContext.BuildingExpenses
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == sourcePeriodId && x.BuildingId == building.Id)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        // Edificio que acredita los ingresos a los propietarios: lo que sobra de ingresos menos gastos.
        if (building.IncomeTreatment != IncomeTreatment.ToReserveFund)
        {
            return new Result(
                totalIngresos, totalGastos, totalIngresos - totalGastos,
                $"Rollover automático: ingresos {totalIngresos:N0} - gastos {totalGastos:N0}");
        }

        // Edificio cuyos ingresos van al fondo de reserva: el saldo que pasa es el del fondo (ingresos sin Fondo
        // operativo + aporte del % - lo que pago el fondo), el mismo que sale en la planilla de liquidacion.
        var expenses = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == sourcePeriodId && x.BuildingId == building.Id)
            .Select(x => new { x.Category, x.Amount, x.DistributionType, x.PaidByReserveFund })
            .ToListAsync(cancellationToken);
        var fundIncomes = await dbContext.BuildingIncomes
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == sourcePeriodId && x.BuildingId == building.Id
                        && x.Category != BuildingIncomeCategory.OperationalFund)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var commonBase = expenses
            .Where(x => x.DistributionType is BuildingExpenseDistributionType.ByCoefficient or BuildingExpenseDistributionType.FixedPerUnit
                        && x.Category != BuildingExpenseCategory.ReserveFund
                        && !x.PaidByReserveFund)
            .Sum(x => x.Amount);
        var contributions = SettlementContributions.Compute(
            commonBase, 0m, building.ReserveFundPercentage, building.ExtraordinaryPercentage);
        var reserveCategory = expenses
            .Where(x => x.Category == BuildingExpenseCategory.ReserveFund && !x.PaidByReserveFund)
            .Sum(x => x.Amount);
        var paidByFund = expenses.Where(x => x.PaidByReserveFund).Sum(x => x.Amount);

        var saldo = SettlementContributions.ReserveFundBalance(reserveCategory, contributions.ReserveContribution, fundIncomes, paidByFund);
        return new Result(
            fundIncomes, paidByFund, saldo,
            $"Rollover automático (fondo de reserva): ingresos {fundIncomes:N0} + aporte {contributions.ReserveContribution:N0} - pagado por el fondo {paidByFund:N0}");
    }
}
