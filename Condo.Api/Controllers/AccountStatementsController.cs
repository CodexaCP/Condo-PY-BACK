using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/account-statements")]
public class AccountStatementsController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet("units/{unitId:guid}")]
    public async Task<ActionResult<IReadOnlyList<AccountStatementPeriodDto>>> GetUnitStatements(Guid unitId, CancellationToken cancellationToken)
    {
        var unit = await dbContext.Units
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == unitId, cancellationToken);

        if (unit is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var statements = await dbContext.ExpensePeriods
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == unit.BuildingId)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .Select(x => new AccountStatementPeriodDto
            {
                ExpensePeriodId = x.Id,
                ExpensePeriodName = x.Name,
                Year = x.Year,
                Month = x.Month,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                DueDate = x.DueDate,
                Status = x.Status,
                TotalCharges = 0m,
                TotalPayments = dbContext.Payments
                    .Where(p => !p.IsDeleted && p.ExpensePeriodId == x.Id && p.UnitId == unitId)
                    .Sum(p => (decimal?)p.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        foreach (var statement in statements)
        {
            statement.TotalCharges = await GetEffectiveChargesAsync(unit, statement.ExpensePeriodId, cancellationToken);
        }

        foreach (var statement in statements)
        {
            statement.Balance = statement.TotalCharges - statement.TotalPayments;
        }

        return Ok(statements);
    }

    [HttpGet("units/{unitId:guid}/periods/{expensePeriodId:guid}")]
    public async Task<ActionResult<AccountStatementDetailDto>> GetUnitStatementDetail(Guid unitId, Guid expensePeriodId, CancellationToken cancellationToken)
    {
        var unit = await dbContext.Units
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == unitId, cancellationToken);

        if (unit is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expensePeriodId && x.BuildingId == unit.BuildingId, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        var charges = await GetEffectiveChargeItemsAsync(unit, period, cancellationToken);

        var payments = await dbContext.Payments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId && x.UnitId == unitId)
            .OrderByDescending(x => x.PaymentDate)
            .Select(x => new AccountStatementPaymentDto
            {
                Id = x.Id,
                PaymentDate = x.PaymentDate,
                Amount = x.Amount,
                Method = x.Method,
                Reference = x.Reference,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);

        var totalCharges = charges.Sum(x => x.Amount);
        var totalPayments = payments.Sum(x => x.Amount);

        return Ok(new AccountStatementDetailDto
        {
            UnitId = unit.Id,
            UnitCode = unit.Code,
            BuildingId = unit.BuildingId,
            BuildingName = unit.Building?.Name ?? string.Empty,
            ExpensePeriodId = period.Id,
            ExpensePeriodName = period.Name,
            Year = period.Year,
            Month = period.Month,
            StartDate = period.StartDate,
            EndDate = period.EndDate,
            DueDate = period.DueDate,
            Status = period.Status,
            Charges = charges,
            Payments = payments,
            TotalCharges = totalCharges,
            TotalPayments = totalPayments,
            Balance = totalCharges - totalPayments
        });
    }

    [HttpGet("units/{unitId:guid}/periods/{expensePeriodId:guid}/receipt")]
    public async Task<ActionResult<ExpenseReceiptDto>> GetExpenseReceipt(Guid unitId, Guid expensePeriodId, CancellationToken cancellationToken)
    {
        var unit = await dbContext.Units
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == unitId, cancellationToken);

        if (unit is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expensePeriodId && x.BuildingId == unit.BuildingId, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        var holder = await dbContext.UnitResidents
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.UnitId == unitId &&
                (x.EndDate == null || x.EndDate >= period.EndDate) &&
                x.StartDate <= period.EndDate)
            .OrderByDescending(x => x.IsPrimary)
            .ThenByDescending(x => x.StartDate)
            .Select(x => new
            {
                x.Resident!.FullName,
                x.Resident!.DocumentNumber
            })
            .FirstOrDefaultAsync(cancellationToken);

        var charges = await dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId && x.UnitId == unitId)
            .OrderBy(x => x.ChargeType)
            .ThenBy(x => x.Concept)
            .Select(x => new ExpenseReceiptChargeDto
            {
                Id = x.Id,
                ChargeType = x.ChargeType,
                Concept = x.Concept,
                Amount = x.Amount,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);

        return Ok(new ExpenseReceiptDto
        {
            UnitId = unit.Id,
            UnitCode = unit.Code,
            BuildingId = unit.BuildingId,
            BuildingName = unit.Building?.Name ?? string.Empty,
            ExpensePeriodId = period.Id,
            ExpensePeriodName = period.Name,
            Year = period.Year,
            Month = period.Month,
            DueDate = period.DueDate,
            HolderName = holder?.FullName ?? "Titular no asignado",
            HolderDocumentNumber = holder?.DocumentNumber ?? string.Empty,
            UnitCoefficient = unit.Coefficient,
            Charges = charges,
            OrdinaryAmount = charges.Where(x => x.ChargeType == Domain.Enums.ExpenseChargeType.Ordinary).Sum(x => x.Amount),
            ReserveFundAmount = charges.Where(x => x.ChargeType == Domain.Enums.ExpenseChargeType.ReserveFund).Sum(x => x.Amount),
            ExtraordinaryAmount = charges.Where(x => x.ChargeType == Domain.Enums.ExpenseChargeType.Extraordinary).Sum(x => x.Amount),
            IndividualAmount = charges.Where(x => x.ChargeType == Domain.Enums.ExpenseChargeType.Individual).Sum(x => x.Amount),
            AdjustmentAmount = charges.Where(x => x.ChargeType == Domain.Enums.ExpenseChargeType.Adjustment).Sum(x => x.Amount),
            TotalAmount = charges.Sum(x => x.Amount)
        });
    }

    private async Task<decimal> GetEffectiveChargesAsync(
        Unit unit,
        Guid expensePeriodId,
        CancellationToken cancellationToken)
    {
        var charges = await GetEffectiveChargeItemsAsync(unit, new ExpensePeriod { Id = expensePeriodId, BuildingId = unit.BuildingId }, cancellationToken);
        return charges.Sum(x => x.Amount);
    }

    private async Task<List<AccountStatementChargeDto>> GetEffectiveChargeItemsAsync(
        Unit unit,
        ExpensePeriod period,
        CancellationToken cancellationToken)
    {
        var periodStatus = period.Status;
        if (periodStatus == default)
        {
            periodStatus = await dbContext.ExpensePeriods
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.Id == period.Id)
                .Select(x => x.Status)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (periodStatus == ExpensePeriodStatus.Draft)
        {
            return [];
        }

        return await dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == period.Id && x.UnitId == unit.Id)
            .OrderBy(x => x.Concept)
            .Select(x => new AccountStatementChargeDto
            {
                Id = x.Id,
                ChargeType = x.ChargeType,
                Concept = x.Concept,
                Amount = x.Amount,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);
    }
}
