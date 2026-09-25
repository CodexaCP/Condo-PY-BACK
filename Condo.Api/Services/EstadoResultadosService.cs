using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

public interface IEstadoResultadosService
{
    Task<EstadoResultadosReportDto?> BuildReportAsync(Guid buildingId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}

public class EstadoResultadosService(ICondoDbContext dbContext) : IEstadoResultadosService
{
    public async Task<EstadoResultadosReportDto?> BuildReportAsync(Guid buildingId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var buildingName = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == buildingId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (buildingName is null)
            return null;

        var collectedAmount = await dbContext.Payments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsReversed && x.Unit!.BuildingId == buildingId
                     && x.PaymentDate >= fromDate && x.PaymentDate <= toDate)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        // AccumulatedBalance es el arrastre de saldo entre periodos (ver LibroMovimientosService),
        // no un ingreso nuevo del periodo, asi que se excluye del estado de resultados.
        var incomeByCategory = await dbContext.BuildingIncomes
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Category != BuildingIncomeCategory.AccumulatedBalance
                     && x.IncomeDate >= fromDate && x.IncomeDate <= toDate)
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var expenseByCategory = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId
                     && x.ExpenseDate >= fromDate && x.ExpenseDate <= toDate)
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var incomeLines = new List<EstadoResultadosLineDto>();
        if (collectedAmount != 0)
            incomeLines.Add(new EstadoResultadosLineDto { Label = "Cobros de expensas", Amount = collectedAmount });

        incomeLines.AddRange(incomeByCategory
            .Where(x => x.Amount != 0)
            .Select(x => new EstadoResultadosLineDto { Label = CategoryLabels.IncomeLabel(x.Category), Amount = x.Amount }));

        var expenseLines = expenseByCategory
            .Where(x => x.Amount != 0)
            .Select(x => new EstadoResultadosLineDto { Label = CategoryLabels.ExpenseLabel(x.Category), Amount = x.Amount })
            .ToList();

        incomeLines = incomeLines.OrderByDescending(x => x.Amount).ToList();
        expenseLines = expenseLines.OrderByDescending(x => x.Amount).ToList();

        var totalIncome = incomeLines.Sum(x => x.Amount);
        var totalExpense = expenseLines.Sum(x => x.Amount);

        return new EstadoResultadosReportDto
        {
            BuildingId = buildingId,
            BuildingName = buildingName,
            FromDate = fromDate,
            ToDate = toDate,
            IncomeLines = incomeLines,
            TotalIncome = totalIncome,
            ExpenseLines = expenseLines,
            TotalExpense = totalExpense,
            NetResult = totalIncome - totalExpense
        };
    }
}
