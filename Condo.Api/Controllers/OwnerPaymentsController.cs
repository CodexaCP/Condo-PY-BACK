using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Api.Documents;
using QuestPDF.Fluent;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/owner-payments")]
public class OwnerPaymentsController(
    ICondoDbContext dbContext,
    ITenantContext tenantContext) : ControllerBase
{
    // ─── GET MY DEBT (Owner only) ────────────────────────────────────────────
    [HttpGet("my-debt")]
    public async Task<ActionResult<IReadOnlyList<OwnerDebtUnitDto>>> GetMyDebt(CancellationToken ct)
    {
        if (!IsOwner()) return Forbid();

        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();
        var ownerId = tenantContext.UserId;

        var unitIds = await LoadLinkedUnitIdsAsync(ownerId, companyId.Value, ct);

        if (!unitIds.Any()) return Ok(new List<OwnerDebtUnitDto>());

        var charges = await dbContext.ExpenseCharges
            .AsNoTracking()
            .Include(x => x.Unit).ThenInclude(u => u!.Building)
            .Include(x => x.ExpensePeriod)
            .Include(x => x.Allocations.Where(a => !a.IsDeleted))
            .Where(x => !x.IsDeleted && !x.IsReversal && unitIds.Contains(x.UnitId) && x.CompanyId == companyId.Value && x.ExpensePeriod!.Status == ExpensePeriodStatus.Published)
            .ToListAsync(ct);

        var result = charges
            .Where(c => c.Amount - c.Allocations.Sum(a => a.AllocatedAmount) > 0)
            .GroupBy(c => c.UnitId)
            .Select(g =>
            {
                var first = g.First();
                return new OwnerDebtUnitDto
                {
                    UnitId = g.Key,
                    UnitCode = first.Unit?.Code ?? string.Empty,
                    BuildingName = first.Unit?.Building?.Name ?? string.Empty,
                    TotalDebt = g.Sum(c => c.Amount - c.Allocations.Sum(a => a.AllocatedAmount)),
                    Charges = g
                        .OrderBy(c => c.ExpensePeriod!.Year).ThenBy(c => c.ExpensePeriod!.Month)
                        .Select(c => new OwnerDebtChargeDto
                        {
                            ChargeId = c.Id,
                            Concept = c.Concept,
                            ChargeType = c.ChargeType.ToString(),
                            PeriodYear = c.ExpensePeriod!.Year,
                            PeriodMonth = c.ExpensePeriod.Month,
                            Amount = c.Amount,
                            PendingAmount = c.Amount - c.Allocations.Sum(a => a.AllocatedAmount)
                        }).ToList()
                };
            })
            .Where(d => d.TotalDebt > 0)
            .OrderBy(d => d.BuildingName).ThenBy(d => d.UnitCode)
            .ToList();

        return Ok(result);
    }

    // ─── GET MY CREDIT (Owner only) ─────────────────────────────────────────
    [HttpGet("my-credit")]
    public async Task<ActionResult<OwnerCreditDto>> GetMyCredit(CancellationToken ct)
    {
        if (!IsOwner()) return Forbid();
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        var credit = await dbContext.OwnerCredits
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == tenantContext.UserId && x.CompanyId == companyId.Value, ct);

        return Ok(new OwnerCreditDto { Amount = credit?.Amount ?? 0 });
    }

    // ─── GET OWNER CREDIT (Manager) ──────────────────────────────────────────
    [HttpGet("credit/{ownerId:guid}")]
    public async Task<ActionResult<OwnerCreditDto>> GetOwnerCredit(Guid ownerId, CancellationToken ct)
    {
        if (!CanManagePayments()) return Forbid();
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        var credit = await dbContext.OwnerCredits
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == ownerId && x.CompanyId == companyId.Value, ct);

        return Ok(new OwnerCreditDto { Amount = credit?.Amount ?? 0 });
    }

    // ─── GET ALL ─────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OwnerPaymentDto>>> GetAll(
        [FromQuery] string? status,
        [FromQuery] Guid? ownerId,
        CancellationToken ct)
    {
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        IQueryable<OwnerPayment> query = dbContext.OwnerPayments
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.ReviewedByUser)
            .Include(x => x.Units.Where(u => !u.IsDeleted))
                .ThenInclude(u => u.Unit).ThenInclude(u => u!.Building)
            .Where(x => !x.IsDeleted && x.CompanyId == companyId.Value);

        if (IsOwner())
        {
            query = query.Where(x => x.OwnerId == tenantContext.UserId);
        }
        else if (CanManagePayments())
        {
            if (ownerId.HasValue)
                query = query.Where(x => x.OwnerId == ownerId.Value);

            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<OwnerPaymentStatus>(status, true, out var parsedStatus))
                query = query.Where(x => x.Status == parsedStatus);
        }
        else
        {
            return Forbid();
        }

        var payments = await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        return Ok(payments.Select(ToDto).ToList());
    }

    // ─── GET BY ID ───────────────────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OwnerPaymentDto>> GetById(Guid id, CancellationToken ct)
    {
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        var payment = await dbContext.OwnerPayments
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.ReviewedByUser)
            .Include(x => x.Units.Where(u => !u.IsDeleted))
                .ThenInclude(u => u.Unit).ThenInclude(u => u!.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.CompanyId == companyId.Value, ct);

        if (payment is null) return NotFound();

        if (IsOwner() && payment.OwnerId != tenantContext.UserId) return Forbid();
        if (!IsOwner() && !CanManagePayments()) return Forbid();

        var dto = ToDto(payment);

        if (payment.Status == OwnerPaymentStatus.Approved)
        {
            dto.Applications = await dbContext.Payments
                .AsNoTracking()
                .Where(x => !x.IsDeleted && !x.IsReversed && x.Reference == payment.Reference && x.CompanyId == companyId.Value)
                .SelectMany(p => p.Allocations
                    .Where(a => !a.IsDeleted)
                    .Select(a => new OwnerPaymentApplicationDto
                    {
                        UnitCode = p.Unit != null ? p.Unit.Code : string.Empty,
                        Concept = a.Charge != null ? a.Charge.Concept : string.Empty,
                        PeriodYear = p.ExpensePeriod != null ? p.ExpensePeriod.Year : 0,
                        PeriodMonth = p.ExpensePeriod != null ? p.ExpensePeriod.Month : 0,
                        Amount = a.AllocatedAmount
                    }))
                .OrderBy(a => a.PeriodYear).ThenBy(a => a.PeriodMonth).ThenBy(a => a.UnitCode)
                .ToListAsync(ct);
        }

        return Ok(dto);
    }

    // ─── CREATE (Owner only) ─────────────────────────────────────────────────
    [HttpPost]
    public async Task<ActionResult<OwnerPaymentDto>> Create(
        [FromBody] OwnerPaymentCreateRequest request, CancellationToken ct)
    {
        if (!IsOwner()) return Forbid();

        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();
        var ownerId = tenantContext.UserId;

        if (request.DeclaredAmount <= 0)
            return BadRequest("El monto declarado debe ser mayor a cero.");
        if (request.UnitIds is null || !request.UnitIds.Any())
            return BadRequest("Debe seleccionar al menos una unidad.");

        var ownerUnitIds = await LoadLinkedUnitIdsAsync(ownerId, companyId.Value, ct);

        if (request.UnitIds.Except(ownerUnitIds).Any())
            return BadRequest("Una o más unidades no pertenecen a este propietario.");

        var year = DateTime.UtcNow.Year;
        var countThisYear = await dbContext.OwnerPayments
            .CountAsync(x => x.CompanyId == companyId.Value && x.CreatedAtUtc.Year == year, ct);
        var reference = $"PAY-{year}-{(countThisYear + 1):D6}";

        var ownerPayment = new OwnerPayment
        {
            CompanyId = companyId.Value,
            OwnerId = ownerId,
            PaymentDate = request.PaymentDate,
            ComprobanteUrl = request.ComprobanteUrl?.Trim() ?? string.Empty,
            DeclaredAmount = request.DeclaredAmount,
            Status = OwnerPaymentStatus.Pending,
            Reference = reference
        };

        foreach (var unitId in request.UnitIds.Distinct())
        {
            ownerPayment.Units.Add(new OwnerPaymentUnit
            {
                CompanyId = companyId.Value,
                UnitId = unitId,
                AllocatedAmount = 0
            });
        }

        dbContext.OwnerPayments.Add(ownerPayment);

        // Notify managers
        var managerIds = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.CompanyId == companyId.Value && x.IsActive &&
                (x.Role == UserRole.BuildingManager || x.Role == UserRole.CompanyAdmin || x.Role == UserRole.CompanyOperator))
            .Select(x => x.Id)
            .ToListAsync(ct);

        foreach (var managerId in managerIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                CompanyId = companyId.Value,
                RecipientId = managerId,
                Type = NotificationType.OwnerPaymentSubmitted,
                Title = "Nuevo pago enviado",
                Body = $"Un propietario envió un pago por {request.DeclaredAmount:N0}. Ref: {reference}. Toca para ver más detalles.",
                EntityType = "OwnerPayment",
                EntityId = ownerPayment.Id
            });
        }

        await dbContext.SaveChangesAsync(ct);

        var saved = await dbContext.OwnerPayments
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.ReviewedByUser)
            .Include(x => x.Units.Where(u => !u.IsDeleted))
                .ThenInclude(u => u.Unit).ThenInclude(u => u!.Building)
            .FirstAsync(x => x.Id == ownerPayment.Id, ct);

        return Ok(ToDto(saved));
    }

    // ─── REVIEW (Manager enters amount) ──────────────────────────────────────
    [HttpPut("{id:guid}/review")]
    public async Task<ActionResult<OwnerPaymentDto>> Review(
        Guid id, [FromBody] OwnerPaymentReviewRequest request, CancellationToken ct)
    {
        if (!CanManagePayments()) return Forbid();

        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        var payment = await dbContext.OwnerPayments
            .Include(x => x.Owner)
            .Include(x => x.ReviewedByUser)
            .Include(x => x.Units.Where(u => !u.IsDeleted))
                .ThenInclude(u => u.Unit).ThenInclude(u => u!.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.CompanyId == companyId.Value, ct);

        if (payment is null) return NotFound();
        if (payment.Status != OwnerPaymentStatus.Pending)
            return BadRequest("Solo se pueden revisar pagos en estado PENDIENTE.");
        if (request.ReviewedAmount <= 0)
            return BadRequest("El monto revisado debe ser mayor a cero.");

        payment.Status = OwnerPaymentStatus.UnderReview;
        payment.ReviewedAmount = request.ReviewedAmount;
        payment.ReviewedByUserId = tenantContext.UserId;
        payment.ReviewedAt = DateTime.UtcNow;

        dbContext.Notifications.Add(new Notification
        {
            CompanyId = companyId.Value,
            RecipientId = payment.OwnerId,
            Type = NotificationType.PaymentUnderReview,
            Title = "Tu pago está en revisión",
            Body = $"Tu pago {payment.Reference} está siendo revisado. Toca para ver más detalles.",
            EntityType = "OwnerPayment",
            EntityId = payment.Id
        });

        await dbContext.SaveChangesAsync(ct);
        return Ok(ToDto(payment));
    }

    // ─── APPROVE ─────────────────────────────────────────────────────────────
    [HttpPut("{id:guid}/approve")]
    public async Task<ActionResult<OwnerPaymentDto>> Approve(Guid id, CancellationToken ct)
    {
        if (!CanManagePayments()) return Forbid();

        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        var payment = await dbContext.OwnerPayments
            .Include(x => x.Owner)
            .Include(x => x.ReviewedByUser)
            .Include(x => x.Units.Where(u => !u.IsDeleted))
                .ThenInclude(u => u.Unit).ThenInclude(u => u!.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.CompanyId == companyId.Value, ct);

        if (payment is null) return NotFound();
        if (payment.Status != OwnerPaymentStatus.UnderReview)
            return BadRequest("Solo se pueden aprobar pagos en estado EN REVISIÓN.");
        if (!payment.ReviewedAmount.HasValue || payment.ReviewedAmount.Value <= 0)
            return BadRequest("El pago no tiene un monto revisado válido.");

        await SettlePaymentAsync(payment, companyId.Value, ct);

        payment.Status = OwnerPaymentStatus.Approved;
        payment.ResolvedAt = DateTime.UtcNow;

        dbContext.Notifications.Add(new Notification
        {
            CompanyId = companyId.Value,
            RecipientId = payment.OwnerId,
            Type = NotificationType.PaymentApproved,
            Title = "Tu pago fue aprobado",
            Body = $"Tu pago {payment.Reference} fue aprobado exitosamente. Toca para ver más detalles.",
            EntityType = "OwnerPayment",
            EntityId = payment.Id
        });

        await dbContext.SaveChangesAsync(ct);
        return Ok(ToDto(payment));
    }

    // ─── REJECT ──────────────────────────────────────────────────────────────
    [HttpPut("{id:guid}/reject")]
    public async Task<ActionResult<OwnerPaymentDto>> Reject(
        Guid id, [FromBody] OwnerPaymentRejectRequest request, CancellationToken ct)
    {
        if (!CanManagePayments()) return Forbid();

        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        var payment = await dbContext.OwnerPayments
            .Include(x => x.Owner)
            .Include(x => x.ReviewedByUser)
            .Include(x => x.Units.Where(u => !u.IsDeleted))
                .ThenInclude(u => u.Unit).ThenInclude(u => u!.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.CompanyId == companyId.Value, ct);

        if (payment is null) return NotFound();
        if (payment.Status == OwnerPaymentStatus.Approved || payment.Status == OwnerPaymentStatus.Rejected)
            return BadRequest("No se puede rechazar un pago ya resuelto.");

        var reason = request.RejectionReason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest("El motivo de rechazo es obligatorio.");
        if (reason.Length > 500)
            return BadRequest("El motivo no puede superar los 500 caracteres.");

        payment.Status = OwnerPaymentStatus.Rejected;
        payment.RejectionReason = reason;
        payment.ResolvedAt = DateTime.UtcNow;

        dbContext.Notifications.Add(new Notification
        {
            CompanyId = companyId.Value,
            RecipientId = payment.OwnerId,
            Type = NotificationType.PaymentRejected,
            Title = "Tu pago fue rechazado",
            Body = $"Tu pago {payment.Reference} fue rechazado. Motivo: {reason}. Toca para ver más detalles.",
            EntityType = "OwnerPayment",
            EntityId = payment.Id
        });

        await dbContext.SaveChangesAsync(ct);
        return Ok(ToDto(payment));
    }

    // ─── RECEIPT PDF ─────────────────────────────────────────────────────────
    [HttpGet("{id:guid}/receipt-pdf")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadReceiptPdf(
        Guid id,
        [FromQuery(Name = "access_token")] string? _,
        CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true) return Unauthorized();

        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        var payment = await dbContext.OwnerPayments
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.Units.Where(u => !u.IsDeleted))
                .ThenInclude(u => u.Unit).ThenInclude(u => u!.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.CompanyId == companyId.Value, ct);

        if (payment is null) return NotFound();
        if (IsOwner() && payment.OwnerId != tenantContext.UserId) return Forbid();
        if (!IsOwner() && !CanManagePayments()) return Forbid();

        var settlements = await dbContext.Payments
            .AsNoTracking()
            .Include(x => x.Unit)
            .Include(x => x.ExpensePeriod)
            .Include(x => x.Allocations.Where(a => !a.IsDeleted))
                .ThenInclude(a => a.Charge)
            .Where(x => !x.IsDeleted && !x.IsReversed && x.Reference == payment.Reference && x.CompanyId == companyId.Value)
            .ToListAsync(ct);

        var credit = await dbContext.OwnerCredits
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == payment.OwnerId && x.CompanyId == companyId.Value, ct);

        var data = new OwnerPaymentReceiptData(
            Id:             payment.Id,
            Reference:      payment.Reference,
            OwnerFullName:  payment.Owner?.FullName ?? string.Empty,
            PaymentDate:    payment.PaymentDate,
            DeclaredAmount: payment.DeclaredAmount,
            ReviewedAmount: payment.ReviewedAmount ?? 0,
            Status:         payment.Status.ToString(),
            CreatedAtUtc:   payment.CreatedAtUtc,
            Units: payment.Units
                .Where(u => !u.IsDeleted)
                .Select(u => new OwnerPaymentReceiptUnitRow(
                    u.Unit?.Code ?? string.Empty,
                    u.Unit?.Building?.Name ?? string.Empty,
                    u.AllocatedAmount))
                .ToList(),
            Settlements: settlements
                .SelectMany(p => p.Allocations.Select(a => new OwnerPaymentReceiptSettlementRow(
                    p.Unit?.Code ?? string.Empty,
                    a.Charge?.Concept ?? string.Empty,
                    p.ExpensePeriod?.Year ?? 0,
                    p.ExpensePeriod?.Month ?? 0,
                    a.AllocatedAmount)))
                .ToList(),
            RemainingCredit: credit?.Amount ?? 0
        );

        var doc      = new OwnerPaymentReceiptPdfDocument(data);
        var pdfBytes = doc.GeneratePdf();
        var fileName = $"comprobante_{payment.Reference}_{payment.PaymentDate:yyyyMMdd}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    // ─── APPLY CREDIT (Owner) ────────────────────────────────────────────────
    [HttpPost("apply-credit")]
    public async Task<ActionResult<ApplyCreditResultDto>> ApplyMyCredit(CancellationToken ct)
    {
        if (!IsOwner()) return Forbid();
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();
        return await RunApplyCredit(tenantContext.UserId, companyId.Value, ct);
    }

    // ─── APPLY CREDIT (Manager) ──────────────────────────────────────────────
    [HttpPost("apply-credit/{ownerId:guid}")]
    public async Task<ActionResult<ApplyCreditResultDto>> ApplyCreditByManager(Guid ownerId, CancellationToken ct)
    {
        if (!CanManagePayments()) return Forbid();
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();
        return await RunApplyCredit(ownerId, companyId.Value, ct);
    }

    private async Task<ActionResult<ApplyCreditResultDto>> RunApplyCredit(Guid ownerId, Guid companyId, CancellationToken ct)
    {
        var credit = await dbContext.OwnerCredits
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == ownerId && x.CompanyId == companyId, ct);

        if (credit is null || credit.Amount <= 0)
            return BadRequest("El propietario no tiene saldo a favor.");

        var unitIds = await LoadLinkedUnitIdsAsync(ownerId, companyId, ct);

        var available = credit.Amount;
        var paymentDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var reference = $"CREDIT-{DateTime.UtcNow:yyyyMMddHHmmss}";

        var charges = await dbContext.ExpenseCharges
            .Include(x => x.ExpensePeriod)
            .Include(x => x.Unit).ThenInclude(u => u!.Building)
            .Include(x => x.Allocations.Where(a => !a.IsDeleted))
            .Where(x => !x.IsDeleted && !x.IsReversal && unitIds.Contains(x.UnitId) && x.CompanyId == companyId && x.ExpensePeriod!.Status == ExpensePeriodStatus.Published)
            .ToListAsync(ct);

        var pending = charges
            .Where(c => c.Amount - c.Allocations.Sum(a => a.AllocatedAmount) > 0)
            .OrderBy(c => c.ExpensePeriod!.Year)
            .ThenBy(c => c.ExpensePeriod!.Month)
            .ThenByDescending(c => c.Amount)
            // Desempate fijo entre unidades con el mismo periodo y monto: edificio y luego unidad.
            .ThenBy(c => c.Unit!.Building!.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Unit!.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.CreatedAtUtc)
            .ToList();

        var settled = 0m;
        var count = 0;

        foreach (var charge in pending)
        {
            if (available <= 0) break;
            var pendingAmount = charge.Amount - charge.Allocations.Sum(a => a.AllocatedAmount);
            // Siempre del más antiguo al más reciente: nunca se salta un cargo antiguo.
            if (available < pendingAmount) break;

            var paymentRecord = new Payment
            {
                CompanyId = companyId,
                ExpensePeriodId = charge.ExpensePeriodId,
                UnitId = charge.UnitId,
                PaymentDate = paymentDate,
                Amount = pendingAmount,
                Method = PaymentMethod.BankTransfer,
                Reference = reference,
                Notes = $"Aplicación de saldo a favor. Ref: {reference}"
            };
            dbContext.Payments.Add(paymentRecord);
            dbContext.PaymentAllocations.Add(new PaymentAllocation
            {
                CompanyId = companyId,
                PaymentId = paymentRecord.Id,
                ExpenseChargeId = charge.Id,
                AllocatedAmount = pendingAmount
            });

            available -= pendingAmount;
            settled += pendingAmount;
            count++;
        }

        if (count == 0)
            return BadRequest("No hay cargos pendientes que puedan liquidarse con el saldo disponible.");

        credit.Amount = available;
        await dbContext.SaveChangesAsync(ct);

        return Ok(new ApplyCreditResultDto
        {
            SettledAmount = settled,
            RemainingCredit = available,
            ChargesSettled = count
        });
    }

    // ─── SETTLEMENT LOGIC ────────────────────────────────────────────────────

    private async Task SettlePaymentAsync(OwnerPayment ownerPayment, Guid companyId, CancellationToken ct)
    {
        // El pago se aplica siempre al período más antiguo entre TODAS las unidades del
        // propietario, sin importar cuáles se marcaron al enviarlo.
        var unitIds = await LoadLinkedUnitIdsAsync(ownerPayment.OwnerId, companyId, ct);

        foreach (var declaredUnit in ownerPayment.Units.Where(u => !u.IsDeleted))
        {
            if (!unitIds.Contains(declaredUnit.UnitId)) unitIds.Add(declaredUnit.UnitId);
        }

        var credit = await dbContext.OwnerCredits
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == ownerPayment.OwnerId && x.CompanyId == companyId, ct);
        if (credit is null)
        {
            credit = new OwnerCredit { CompanyId = companyId, OwnerId = ownerPayment.OwnerId, Amount = 0 };
            dbContext.OwnerCredits.Add(credit);
        }

        var available = ownerPayment.ReviewedAmount!.Value + credit.Amount;

        var charges = await dbContext.ExpenseCharges
            .Include(x => x.ExpensePeriod)
            .Include(x => x.Unit).ThenInclude(u => u!.Building)
            .Include(x => x.Allocations.Where(a => !a.IsDeleted))
            .Where(x => !x.IsDeleted && !x.IsReversal && unitIds.Contains(x.UnitId) && x.CompanyId == companyId && x.ExpensePeriod!.Status == ExpensePeriodStatus.Published)
            .ToListAsync(ct);

        var pendingCharges = charges
            .Where(c => c.Amount - c.Allocations.Sum(a => a.AllocatedAmount) > 0)
            .OrderBy(c => c.ExpensePeriod!.Year)
            .ThenBy(c => c.ExpensePeriod!.Month)
            .ThenByDescending(c => c.Amount)
            // Desempate fijo entre unidades con el mismo periodo y monto: edificio y luego unidad.
            .ThenBy(c => c.Unit!.Building!.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Unit!.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.CreatedAtUtc)
            .ToList();

        var allocatedPerUnit = ownerPayment.Units
            .Where(u => !u.IsDeleted)
            .ToDictionary(u => u.UnitId, u => 0m);

        foreach (var charge in pendingCharges)
        {
            if (available <= 0) break;

            var pendingAmount = charge.Amount - charge.Allocations.Sum(a => a.AllocatedAmount);
            // Regla: siempre del más antiguo al más reciente. Si no alcanza para el cargo más
            // antiguo pendiente, no se salta a uno más nuevo: el resto queda como saldo a favor.
            if (available < pendingAmount) break;

            var paymentRecord = new Payment
            {
                CompanyId = companyId,
                ExpensePeriodId = charge.ExpensePeriodId,
                UnitId = charge.UnitId,
                PaymentDate = ownerPayment.PaymentDate,
                Amount = pendingAmount,
                Method = PaymentMethod.BankTransfer,
                Reference = ownerPayment.Reference,
                Notes = $"Pago de propietario aprobado. Ref: {ownerPayment.Reference}"
            };
            dbContext.Payments.Add(paymentRecord);

            dbContext.PaymentAllocations.Add(new PaymentAllocation
            {
                CompanyId = companyId,
                PaymentId = paymentRecord.Id,
                ExpenseChargeId = charge.Id,
                AllocatedAmount = pendingAmount
            });

            available -= pendingAmount;

            if (!allocatedPerUnit.ContainsKey(charge.UnitId))
            {
                var extraUnit = new OwnerPaymentUnit
                {
                    CompanyId = companyId,
                    OwnerPaymentId = ownerPayment.Id,
                    UnitId = charge.UnitId,
                    AllocatedAmount = 0
                };
                dbContext.OwnerPaymentUnits.Add(extraUnit);
                ownerPayment.Units.Add(extraUnit);
                allocatedPerUnit[charge.UnitId] = 0m;
            }

            allocatedPerUnit[charge.UnitId] += pendingAmount;
        }

        foreach (var pUnit in ownerPayment.Units.Where(u => !u.IsDeleted))
        {
            if (allocatedPerUnit.TryGetValue(pUnit.UnitId, out var allocated))
                pUnit.AllocatedAmount = allocated;
        }

        credit.Amount = available;
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────

    // Mismas unidades que ve la app en "Mis unidades": vínculo de propietario (UnitOwner)
    // más vínculo de residente activo (UnitResident.Resident.ApplicationUserId).
    private async Task<List<Guid>> LoadLinkedUnitIdsAsync(Guid userId, Guid companyId, CancellationToken ct)
    {
        var owned = await dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.OwnerId == userId && x.CompanyId == companyId)
            .Select(x => x.UnitId)
            .ToListAsync(ct);

        var resided = await dbContext.UnitResidents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null && x.CompanyId == companyId
                     && x.Resident != null && !x.Resident.IsDeleted && x.Resident.ApplicationUserId == userId)
            .Select(x => x.UnitId)
            .ToListAsync(ct);

        return owned.Union(resided).ToList();
    }

    private bool IsOwner() =>
        string.Equals(tenantContext.Role, "Owner", StringComparison.OrdinalIgnoreCase);

    private bool CanManagePayments()
    {
        if (tenantContext.IsSuperAdmin) return true;
        var role = tenantContext.Role;
        return string.Equals(role, "BuildingManager", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "CompanyAdmin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "CompanyOperator", StringComparison.OrdinalIgnoreCase);
    }

    private static OwnerPaymentDto ToDto(OwnerPayment p) => new()
    {
        Id = p.Id,
        OwnerId = p.OwnerId,
        OwnerFullName = p.Owner?.FullName ?? string.Empty,
        PaymentDate = p.PaymentDate,
        ComprobanteUrl = p.ComprobanteUrl,
        DeclaredAmount = p.DeclaredAmount,
        ReviewedAmount = p.ReviewedAmount,
        Status = p.Status.ToString(),
        Reference = p.Reference,
        RejectionReason = p.RejectionReason,
        ReviewedByUserFullName = p.ReviewedByUser?.FullName,
        ReviewedAt = p.ReviewedAt,
        ResolvedAt = p.ResolvedAt,
        CreatedAtUtc = p.CreatedAtUtc,
        Units = p.Units
            .Where(u => !u.IsDeleted)
            .Select(u => new OwnerPaymentUnitDto
            {
                UnitId = u.UnitId,
                UnitCode = u.Unit?.Code ?? string.Empty,
                BuildingName = u.Unit?.Building?.Name ?? string.Empty,
                AllocatedAmount = u.AllocatedAmount
            }).ToList()
    };
}
