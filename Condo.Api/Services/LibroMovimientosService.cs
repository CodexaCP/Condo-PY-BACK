using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

public interface ILibroMovimientosService
{
    Task<LibroMovimientosReportDto?> BuildReportAsync(Guid buildingId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}

public class LibroMovimientosService(ICondoDbContext dbContext) : ILibroMovimientosService
{
    public async Task<LibroMovimientosReportDto?> BuildReportAsync(Guid buildingId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var buildingName = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == buildingId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (buildingName is null)
            return null;

        var payments = await dbContext.Payments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsReversed && x.Unit!.BuildingId == buildingId)
            .Select(x => new { Date = x.PaymentDate, x.Amount, x.Reference, x.Notes, UnitCode = x.Unit!.Code })
            .ToListAsync(cancellationToken);

        // AccumulatedBalance income rows are an internal carry-forward artifact created by the period
        // rollover flow (BuildingIncomesController.Rollover) — they re-label a prior period's balance,
        // not new cash, so including them here would double count money already reflected on the dates
        // it actually moved.
        var incomes = await dbContext.BuildingIncomes
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Category != BuildingIncomeCategory.AccumulatedBalance)
            .Select(x => new { Date = x.IncomeDate, x.Amount, x.Description, x.Category })
            .ToListAsync(cancellationToken);

        var expenses = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .Select(x => new { Date = x.ExpenseDate, x.Amount, x.Description, x.SupplierName, x.Category })
            .ToListAsync(cancellationToken);

        var movements = new List<LibroMovimientoItemDto>();

        foreach (var p in payments)
        {
            movements.Add(new LibroMovimientoItemDto
            {
                Date = p.Date,
                Type = LibroMovimientoType.Cobro,
                Description = string.IsNullOrWhiteSpace(p.Notes) ? "Cobro de expensas" : p.Notes,
                UnitCode = p.UnitCode,
                Reference = string.IsNullOrWhiteSpace(p.Reference) ? null : p.Reference,
                Credit = p.Amount,
                Debit = 0m
            });
        }

        foreach (var i in incomes)
        {
            movements.Add(new LibroMovimientoItemDto
            {
                Date = i.Date,
                Type = LibroMovimientoType.IngresoEdificio,
                Description = string.IsNullOrWhiteSpace(i.Description) ? CategoryLabels.IncomeLabel(i.Category) : i.Description,
                Credit = i.Amount,
                Debit = 0m
            });
        }

        foreach (var e in expenses)
        {
            var description = string.IsNullOrWhiteSpace(e.Description) ? e.SupplierName : e.Description;
            if (string.IsNullOrWhiteSpace(description))
                description = CategoryLabels.ExpenseLabel(e.Category);

            movements.Add(new LibroMovimientoItemDto
            {
                Date = e.Date,
                Type = LibroMovimientoType.GastoEdificio,
                Description = description,
                Reference = string.IsNullOrWhiteSpace(e.SupplierName) ? null : e.SupplierName,
                Credit = 0m,
                Debit = e.Amount
            });
        }

        var openingBalance = movements.Where(m => m.Date < fromDate).Sum(m => m.Credit - m.Debit);

        var itemsInRange = movements
            .Where(m => m.Date >= fromDate && m.Date <= toDate)
            .OrderBy(m => m.Date)
            .ThenBy(m => m.Type)
            .ToList();

        var running = openingBalance;
        foreach (var item in itemsInRange)
        {
            running += item.Credit - item.Debit;
            item.RunningBalance = running;
        }

        return new LibroMovimientosReportDto
        {
            BuildingId = buildingId,
            BuildingName = buildingName,
            FromDate = fromDate,
            ToDate = toDate,
            OpeningBalance = openingBalance,
            TotalCredits = itemsInRange.Sum(x => x.Credit),
            TotalDebits = itemsInRange.Sum(x => x.Debit),
            ClosingBalance = running,
            Items = itemsInRange
        };
    }

}
