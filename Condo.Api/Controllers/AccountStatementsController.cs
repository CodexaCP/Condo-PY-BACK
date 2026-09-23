using Condo.Api.Documents;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/account-statements")]
public class AccountStatementsController(ICondoDbContext dbContext, IAccessScopeService accessScope, ITenantContext tenantContext) : ControllerBase
{
    // Propietarios, inquilinos y demas usuarios finales solo ven periodos ya publicados;
    // los roles administrativos ven todos los estados.
    private bool IsEndUser =>
        !tenantContext.IsSuperAdmin &&
        !(string.Equals(tenantContext.Role, "CompanyAdmin", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(tenantContext.Role, "CompanyOperator", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(tenantContext.Role, "BuildingManager", StringComparison.OrdinalIgnoreCase));

    private async Task<bool> CanAccessUnitAsync(Unit unit, CancellationToken cancellationToken)
    {
        if (await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
            return true;

        var uid = tenantContext.UserId;

        if (await dbContext.UnitOwners.AnyAsync(x => !x.IsDeleted && x.UnitId == unit.Id && x.OwnerId == uid, cancellationToken))
            return true;

        return await dbContext.UnitResidents.AnyAsync(
            x => !x.IsDeleted && x.UnitId == unit.Id && x.EndDate == null
              && x.Resident != null && !x.Resident.IsDeleted && x.Resident.ApplicationUserId == uid,
            cancellationToken);
    }

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

        if (!await CanAccessUnitAsync(unit, cancellationToken))
        {
            return Forbid();
        }

        var statements = await dbContext.ExpensePeriods
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == unit.BuildingId && (!IsEndUser || x.Status == ExpensePeriodStatus.Published))
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
                    .Where(p => !p.IsDeleted && !p.IsReversed && p.ExpensePeriodId == x.Id && p.UnitId == unitId)
                    .Sum(p => (decimal?)p.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        foreach (var statement in statements)
        {
            if (statement.Status != ExpensePeriodStatus.Draft)
            {
                statement.TotalCharges = await dbContext.ExpenseCharges
                    .AsNoTracking()
                    .Where(x => !x.IsDeleted && x.ExpensePeriodId == statement.ExpensePeriodId && x.UnitId == unitId)
                    .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
            }
        }

        foreach (var statement in statements)
        {
            statement.Balance = statement.TotalCharges - statement.TotalPayments;
        }

        // Compute running balance oldest → newest, then return newest first
        var ordered = statements.OrderBy(x => x.Year).ThenBy(x => x.Month).ToList();
        var running = 0m;
        foreach (var s in ordered)
        {
            s.PreviousBalance = running;
            running += s.Balance;
            s.RunningBalance = running;
        }

        return Ok(statements);
    }

    [HttpGet("units/{unitId:guid}/statement-pdf")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadStatementPdf(
        Guid unitId,
        [FromQuery(Name = "access_token")] string? _,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        var unit = await dbContext.Units
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == unitId, cancellationToken);

        if (unit is null) return NotFound();
        if (!await CanAccessUnitAsync(unit, cancellationToken)) return Forbid();

        var statements = await dbContext.ExpensePeriods
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == unit.BuildingId && (!IsEndUser || x.Status == ExpensePeriodStatus.Published))
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
                    .Where(p => !p.IsDeleted && !p.IsReversed && p.ExpensePeriodId == x.Id && p.UnitId == unitId)
                    .Sum(p => (decimal?)p.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        foreach (var s in statements)
        {
            if (s.Status != ExpensePeriodStatus.Draft)
            {
                s.TotalCharges = await dbContext.ExpenseCharges
                    .AsNoTracking()
                    .Where(x => !x.IsDeleted && x.ExpensePeriodId == s.ExpensePeriodId && x.UnitId == unitId)
                    .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
            }
            s.Balance = s.TotalCharges - s.TotalPayments;
        }

        var ordered = statements.OrderBy(x => x.Year).ThenBy(x => x.Month).ToList();
        var running = 0m;
        foreach (var s in ordered)
        {
            s.PreviousBalance = running;
            running += s.Balance;
            s.RunningBalance = running;
        }

        var buildingName = unit.Building?.Name ?? string.Empty;
        var document = new AccountStatementPdfDocument(unit.Code, buildingName, statements);
        var bytes = document.GeneratePdf();
        var fileName = $"estado-cuenta_{unit.Code}_{DateTime.Now:yyyyMMdd}.pdf";
        return File(bytes, "application/pdf", fileName);
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

        if (!await CanAccessUnitAsync(unit, cancellationToken))
        {
            return Forbid();
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expensePeriodId && x.BuildingId == unit.BuildingId && (!IsEndUser || x.Status == ExpensePeriodStatus.Published), cancellationToken);

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
                Notes = x.Notes,
                IsReversed = x.IsReversed,
                ReversedAt = x.ReversedAt
            })
            .ToListAsync(cancellationToken);

        var totalCharges = charges.Sum(x => x.Amount);
        var totalPayments = payments.Where(x => !x.IsReversed).Sum(x => x.Amount);

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

    // Facturas emitidas de los pagos de esta unidad en este periodo (para descargarlas desde la app).
    [HttpGet("units/{unitId:guid}/periods/{expensePeriodId:guid}/invoices")]
    public async Task<ActionResult<IReadOnlyList<OwnerPaymentInvoiceDto>>> GetUnitPeriodInvoices(
        Guid unitId, Guid expensePeriodId, CancellationToken cancellationToken)
    {
        var unit = await dbContext.Units
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == unitId, cancellationToken);

        if (unit is null) return NotFound();
        if (!await CanAccessUnitAsync(unit, cancellationToken)) return Forbid();

        var periodVisible = await dbContext.ExpensePeriods
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.Id == expensePeriodId && x.BuildingId == unit.BuildingId
                           && (!IsEndUser || x.Status == ExpensePeriodStatus.Published), cancellationToken);
        if (!periodVisible) return NotFound();

        // Cada factura queda ligada a un unico comprobante (unidad + periodo) a traves de i.Payment
        // (ver InvoicesController.CreateDraftsFromOwnerPayment). No hace falta (ni conviene) buscar por
        // Reference del OwnerPayment: eso traia tambien facturas de OTROS periodos cubiertos por el mismo
        // pago aprobado, mostrando en un mes facturas que en realidad correspondian a otros meses.
        var invoices = await dbContext.Invoices
            .AsNoTracking()
            .Where(i => !i.IsDeleted && i.UnitId == unitId && i.Status == InvoiceStatus.Issued
                        && i.Payment != null && !i.Payment.IsReversed && i.Payment.ExpensePeriodId == expensePeriodId)
            .OrderBy(i => i.Numero)
            .Select(i => new OwnerPaymentInvoiceDto
            {
                Id = i.Id,
                NumeroFormateado = i.NumeroFormateado,
                UnitCode = unit.Code,
                MontoTotal = i.MontoTotal,
                FechaEmisionUtc = i.FechaEmisionUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(invoices);
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

        if (!await CanAccessUnitAsync(unit, cancellationToken))
        {
            return Forbid();
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expensePeriodId && x.BuildingId == unit.BuildingId && (!IsEndUser || x.Status == ExpensePeriodStatus.Published), cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        var (ownerName, ownerDocType, ownerDocNum, residentName, residentDocType, residentDocNum) =
            await GetUnitPersonInfoAsync(unitId, period.EndDate, cancellationToken);

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
                Notes = x.Notes,
                IsReversal = x.IsReversal
            })
            .ToListAsync(cancellationToken);

        var receiptPayments = await dbContext.Payments
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
                Notes = x.Notes,
                IsReversed = x.IsReversed,
                ReversedAt = x.ReversedAt
            })
            .ToListAsync(cancellationToken);

        var totalAmount = charges.Sum(x => x.Amount);
        var totalPaid = receiptPayments.Where(x => !x.IsReversed).Sum(x => x.Amount);

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
            OwnerName = ownerName,
            OwnerDocumentType = ownerDocType,
            OwnerDocumentNumber = ownerDocNum,
            ResidentName = residentName,
            ResidentDocumentType = residentDocType,
            ResidentDocumentNumber = residentDocNum,
            UnitCoefficient = unit.Coefficient,
            Charges = charges,
            Payments = receiptPayments,
            OrdinaryAmount = charges.Where(x => x.ChargeType == Domain.Enums.ExpenseChargeType.Ordinary).Sum(x => x.Amount),
            ReserveFundAmount = charges.Where(x => x.ChargeType == Domain.Enums.ExpenseChargeType.ReserveFund).Sum(x => x.Amount),
            ExtraordinaryAmount = charges.Where(x => x.ChargeType == Domain.Enums.ExpenseChargeType.Extraordinary).Sum(x => x.Amount),
            IndividualAmount = charges.Where(x => x.ChargeType == Domain.Enums.ExpenseChargeType.Individual).Sum(x => x.Amount),
            AdjustmentAmount = charges.Where(x => x.ChargeType == Domain.Enums.ExpenseChargeType.Adjustment).Sum(x => x.Amount),
            TotalAmount = totalAmount,
            TotalPayments = totalPaid,
            Balance = totalAmount - totalPaid
        });
    }

    [HttpGet("units/{unitId:guid}/periods/{expensePeriodId:guid}/receipt-pdf")]
    public async Task<IActionResult> DownloadReceiptPdf(Guid unitId, Guid expensePeriodId, CancellationToken cancellationToken)
    {
        var unit = await dbContext.Units
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == unitId, cancellationToken);

        if (unit is null) return NotFound();
        if (!await CanAccessUnitAsync(unit, cancellationToken)) return Forbid();

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expensePeriodId && x.BuildingId == unit.BuildingId && (!IsEndUser || x.Status == ExpensePeriodStatus.Published), cancellationToken);

        if (period is null) return NotFound();

        var (ownerName, ownerDocType, ownerDocNum, residentName, residentDocType, residentDocNum) =
            await GetUnitPersonInfoAsync(unitId, period.EndDate, cancellationToken);

        var charges = await dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId && x.UnitId == unitId)
            .OrderBy(x => x.ChargeType).ThenBy(x => x.Concept)
            .Select(x => new ExpenseReceiptChargeDto
            {
                Id = x.Id,
                ChargeType = x.ChargeType,
                Concept = x.Concept,
                Amount = x.Amount,
                Notes = x.Notes,
                IsReversal = x.IsReversal
            })
            .ToListAsync(cancellationToken);

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
                Notes = x.Notes,
                IsReversed = x.IsReversed,
                ReversedAt = x.ReversedAt
            })
            .ToListAsync(cancellationToken);

        var totalAmount = charges.Sum(x => x.Amount);
        var totalPaid = payments.Where(x => !x.IsReversed).Sum(x => x.Amount);

        var buildingOrdinaryTotal = await dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsReversal
                        && x.ExpensePeriodId == expensePeriodId
                        && x.ChargeType == ExpenseChargeType.Ordinary)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var receipt = new ExpenseReceiptDto
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
            OwnerName = ownerName,
            OwnerDocumentType = ownerDocType,
            OwnerDocumentNumber = ownerDocNum,
            ResidentName = residentName,
            ResidentDocumentType = residentDocType,
            ResidentDocumentNumber = residentDocNum,
            UnitCoefficient = unit.Coefficient,
            BuildingOrdinaryTotal = buildingOrdinaryTotal,
            Charges = charges,
            Payments = payments,
            OrdinaryAmount = charges.Where(x => x.ChargeType == ExpenseChargeType.Ordinary).Sum(x => x.Amount),
            ReserveFundAmount = charges.Where(x => x.ChargeType == ExpenseChargeType.ReserveFund).Sum(x => x.Amount),
            ExtraordinaryAmount = charges.Where(x => x.ChargeType == ExpenseChargeType.Extraordinary).Sum(x => x.Amount),
            IndividualAmount = charges.Where(x => x.ChargeType == ExpenseChargeType.Individual).Sum(x => x.Amount),
            AdjustmentAmount = charges.Where(x => x.ChargeType == ExpenseChargeType.Adjustment).Sum(x => x.Amount),
            TotalAmount = totalAmount,
            TotalPayments = totalPaid,
            Balance = totalAmount - totalPaid
        };

        var document = new ReceiptPdfDocument(receipt);
        var pdfBytes = document.GeneratePdf();
        var fileName = $"comprobante_{unit.Code}_{period.Name.Replace(" ", "_")}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
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
                Notes = x.Notes,
                IsReversal = x.IsReversal
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<(string OwnerName, string? OwnerDocType, string? OwnerDocNumber,
                         string ResidentName, string? ResidentDocType, string? ResidentDocNumber)>
        GetUnitPersonInfoAsync(Guid unitId, DateOnly periodEndDate, CancellationToken ct)
    {
        var owner = await dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.UnitId == unitId)
            .OrderByDescending(x => x.IsPrimary)
            .Select(x => new { x.Owner!.FullName, x.Owner!.DocumentType, x.Owner!.DocumentNumber, x.Owner!.IsResident })
            .FirstOrDefaultAsync(ct);

        string? residentName, residentDocType, residentDocNumber;
        if (owner?.IsResident == true)
        {
            residentName    = owner.FullName;
            residentDocType = owner.DocumentType;
            residentDocNumber = owner.DocumentNumber;
        }
        else
        {
            var res = await dbContext.UnitResidents
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.UnitId == unitId
                         && (x.EndDate == null || x.EndDate >= periodEndDate)
                         && x.StartDate <= periodEndDate)
                .OrderByDescending(x => x.IsPrimary).ThenByDescending(x => x.StartDate)
                .Select(x => new { x.Resident!.FullName, x.Resident!.DocumentType, x.Resident!.DocumentNumber })
                .FirstOrDefaultAsync(ct);
            residentName      = res?.FullName;
            residentDocType   = res?.DocumentType;
            residentDocNumber = res?.DocumentNumber;
        }

        return (
            owner?.FullName ?? "—",
            owner?.DocumentType,
            owner?.DocumentNumber,
            residentName ?? "—",
            residentDocType,
            residentDocNumber
        );
    }
}
