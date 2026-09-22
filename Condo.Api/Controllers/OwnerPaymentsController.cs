using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Api.Documents;
using Condo.Api.Services;
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
    ITenantContext tenantContext,
    OwnerCreditService credits,
    ComprobanteService comprobantes) : ControllerBase
{
    // ─── GET MY DEBT (Owner only) ────────────────────────────────────────────
    [HttpGet("my-debt")]
    public async Task<ActionResult<IReadOnlyList<OwnerDebtUnitDto>>> GetMyDebt(CancellationToken ct)
    {
        if (!IsOwner()) return Forbid();

        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();
        var ownerId = tenantContext.UserId;

        var unitIds = await credits.LoadLinkedUnitIdsAsync(ownerId, companyId.Value, ct);

        if (!unitIds.Any()) return Ok(new List<OwnerDebtUnitDto>());

        var (charges, pendingById) = await credits.LoadPendingChargesAsync(unitIds, companyId.Value, false, ct);

        var result = charges
            .Where(c => pendingById[c.Id] > 0)
            .GroupBy(c => c.UnitId)
            .Select(g =>
            {
                var first = g.First();
                return new OwnerDebtUnitDto
                {
                    UnitId = g.Key,
                    UnitCode = first.Unit?.Code ?? string.Empty,
                    BuildingName = first.Unit?.Building?.Name ?? string.Empty,
                    TotalDebt = g.Sum(c => pendingById[c.Id]),
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
                            PendingAmount = pendingById[c.Id]
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

    // ─── INVOICES OF A PAYMENT ───────────────────────────────────────────────
    // Facturas emitidas a partir de los pagos que generó la aprobación de este pago del propietario.
    [HttpGet("{id:guid}/invoices")]
    public async Task<ActionResult<IReadOnlyList<OwnerPaymentInvoiceDto>>> GetInvoices(Guid id, CancellationToken ct)
    {
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        var payment = await dbContext.OwnerPayments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.CompanyId == companyId.Value, ct);
        if (payment is null) return NotFound();

        if (IsOwner() && payment.OwnerId != tenantContext.UserId) return Forbid();
        if (!IsOwner() && !CanManagePayments()) return Forbid();

        var invoices = await dbContext.Invoices
            .AsNoTracking()
            .Where(i => !i.IsDeleted && i.CompanyId == companyId.Value && i.Status == InvoiceStatus.Issued
                        && (i.OwnerPaymentId == payment.Id
                            || (i.Payment != null && !i.Payment.IsReversed && i.Payment.Reference == payment.Reference)))
            .OrderBy(i => i.Numero)
            .Select(i => new OwnerPaymentInvoiceDto
            {
                Id = i.Id,
                NumeroFormateado = i.NumeroFormateado,
                UnitCode = i.Unit != null ? i.Unit.Code : string.Empty,
                MontoTotal = i.MontoTotal,
                FechaEmisionUtc = i.FechaEmisionUtc
            })
            .ToListAsync(ct);

        return Ok(invoices);
    }

    // ─── CREDIT HISTORY ──────────────────────────────────────────────────────
    // Cuando se genero cada saldo a favor (y de que comprobante) y cuando/como/a que se aplico.
    [HttpGet("credit-movements")]
    public async Task<ActionResult<IReadOnlyList<OwnerCreditMovementDto>>> GetMyCreditMovements(CancellationToken ct)
    {
        if (!IsOwner()) return Forbid();
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        return Ok(await LoadCreditMovementsAsync(tenantContext.UserId, companyId.Value, ct));
    }

    [HttpGet("credit-movements/{ownerId:guid}")]
    public async Task<ActionResult<IReadOnlyList<OwnerCreditMovementDto>>> GetOwnerCreditMovements(Guid ownerId, CancellationToken ct)
    {
        if (!CanManagePayments()) return Forbid();
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        return Ok(await LoadCreditMovementsAsync(ownerId, companyId.Value, ct));
    }

    private async Task<List<OwnerCreditMovementDto>> LoadCreditMovementsAsync(Guid ownerId, Guid companyId, CancellationToken ct) =>
        await dbContext.OwnerCreditMovements
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.OwnerId == ownerId && x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new OwnerCreditMovementDto
            {
                Id = x.Id,
                CreatedAtUtc = x.CreatedAtUtc,
                Kind = x.Kind.ToString(),
                ApplyMode = x.ApplyMode == null ? null : x.ApplyMode.ToString(),
                Amount = x.Amount,
                SourceReference = x.SourceReference,
                PaymentId = x.PaymentId,
                Description = x.Description
            })
            .ToListAsync(ct);

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

        var ownerUnitIds = await credits.LoadLinkedUnitIdsAsync(ownerId, companyId.Value, ct);

        if (request.UnitIds.Except(ownerUnitIds).Any())
            return BadRequest("Una o más unidades no pertenecen a este propietario.");

        // El pago solo se acepta si cubre exactamente comprobantes completos (del más antiguo al más
        // reciente); si el monto solo no alcanza, se prueba sumando el saldo a favor disponible
        // (se descuenta solo, nunca lo elige el propietario).
        var openComprobantes = await comprobantes.LoadAsync(ownerUnitIds, companyId.Value, false, ct);
        var availableCreditForCreate = await GetAvailableCreditAsync(ownerId, companyId.Value, ct);
        var (createCoverage, _) = CoverWithCredit(request.DeclaredAmount, availableCreditForCreate, openComprobantes);
        if (!createCoverage.Exact)
            return BadRequest(ComprobanteService.MismatchMessage(request.DeclaredAmount + availableCreditForCreate, openComprobantes));

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

        var openForReview = await LoadOpenComprobantesAsync(payment, companyId.Value, ct);
        var availableCreditForReview = await GetAvailableCreditAsync(payment.OwnerId, companyId.Value, ct);
        var (reviewCoverage, _) = CoverWithCredit(request.ReviewedAmount, availableCreditForReview, openForReview);
        if (!reviewCoverage.Exact)
            return BadRequest(ComprobanteService.MismatchMessage(request.ReviewedAmount + availableCreditForReview, openForReview)
                              + " Rechace el pago para que el propietario lo envíe nuevamente.");

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

        try
        {
            await SettlePaymentAsync(payment, companyId.Value, ct);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }

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
        return await RunApplyCredit(tenantContext.UserId, companyId.Value, CreditApplyMode.ManualApp, ct);
    }

    // ─── APPLY CREDIT (Manager) ──────────────────────────────────────────────
    [HttpPost("apply-credit/{ownerId:guid}")]
    public async Task<ActionResult<ApplyCreditResultDto>> ApplyCreditByManager(Guid ownerId, CancellationToken ct)
    {
        if (!CanManagePayments()) return Forbid();
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();
        return await RunApplyCredit(ownerId, companyId.Value, CreditApplyMode.ManualManager, ct);
    }

    private async Task<ActionResult<ApplyCreditResultDto>> RunApplyCredit(Guid ownerId, Guid companyId, CreditApplyMode mode, CancellationToken ct)
    {
        if (!OwnerCreditFeature.Enabled)
            return BadRequest("El saldo a favor no está habilitado por ahora.");

        var result = await credits.ApplyCreditAsync(ownerId, companyId, mode, null, ct);

        if (result is null)
            return BadRequest("El propietario no tiene saldo a favor.");
        if (result.ChargesSettled == 0)
            return BadRequest("No hay cargos pendientes que puedan liquidarse con el saldo disponible.");

        return Ok(result);
    }

    // ─── SETTLEMENT LOGIC ────────────────────────────────────────────────────

    // Comprobantes pendientes del propietario: todas sus unidades vinculadas más las declaradas en el pago.
    private async Task<List<Comprobante>> LoadOpenComprobantesAsync(OwnerPayment ownerPayment, Guid companyId, CancellationToken ct)
    {
        var unitIds = await credits.LoadLinkedUnitIdsAsync(ownerPayment.OwnerId, companyId, ct);

        foreach (var declaredUnit in ownerPayment.Units.Where(u => !u.IsDeleted))
        {
            if (!unitIds.Contains(declaredUnit.UnitId)) unitIds.Add(declaredUnit.UnitId);
        }

        return await comprobantes.LoadAsync(unitIds, companyId, true, ct);
    }

    // Un comprobante = un pago completo. Se cubren comprobantes enteros, del más antiguo al más
    // reciente, y el monto (sumado al saldo a favor disponible) debe coincidir exactamente: sin
    // pagos parciales ni pago por línea. El Payment de cada comprobante siempre vale su monto
    // completo (eso es lo que factura Invoice) — el saldo a favor es un detalle de cómo se
    // financió, se descuenta aparte vía OwnerCreditMovement, nunca reduce el Payment/la factura.
    private async Task SettlePaymentAsync(OwnerPayment ownerPayment, Guid companyId, CancellationToken ct)
    {
        var open = await LoadOpenComprobantesAsync(ownerPayment, companyId, ct);
        var reviewedAmount = ownerPayment.ReviewedAmount!.Value;

        var ownerCredit = await dbContext.OwnerCredits
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == ownerPayment.OwnerId && x.CompanyId == companyId, ct);
        var availableCredit = ownerCredit?.Amount ?? 0m;

        var (coverage, creditNeeded) = CoverWithCredit(reviewedAmount, availableCredit, open);

        if (!coverage.Exact)
            throw new InvalidOperationException(ComprobanteService.MismatchMessage(reviewedAmount + availableCredit, open)
                                                + " Rechace el pago para que el propietario lo envíe nuevamente.");

        var allocatedPerUnit = ownerPayment.Units
            .Where(u => !u.IsDeleted)
            .ToDictionary(u => u.UnitId, u => 0m);

        var createdPayments = new List<(Comprobante Comprobante, Payment Payment)>();

        foreach (var comprobante in coverage.Covered)
        {
            var paymentRecord = new Payment
            {
                CompanyId = companyId,
                ExpensePeriodId = comprobante.ExpensePeriodId,
                UnitId = comprobante.UnitId,
                PaymentDate = ownerPayment.PaymentDate,
                Amount = comprobante.Total,
                Method = PaymentMethod.BankTransfer,
                Reference = ownerPayment.Reference,
                Notes = $"Pago de propietario aprobado. Comprobante {comprobante.Label}. Ref: {ownerPayment.Reference}"
            };
            dbContext.Payments.Add(paymentRecord);
            createdPayments.Add((comprobante, paymentRecord));

            foreach (var (charge, pending) in comprobante.Lines)
            {
                dbContext.PaymentAllocations.Add(new PaymentAllocation
                {
                    CompanyId = companyId,
                    PaymentId = paymentRecord.Id,
                    ExpenseChargeId = charge.Id,
                    AllocatedAmount = pending
                });
            }

            if (!allocatedPerUnit.ContainsKey(comprobante.UnitId))
            {
                var extraUnit = new OwnerPaymentUnit
                {
                    CompanyId = companyId,
                    OwnerPaymentId = ownerPayment.Id,
                    UnitId = comprobante.UnitId,
                    AllocatedAmount = 0
                };
                dbContext.OwnerPaymentUnits.Add(extraUnit);
                ownerPayment.Units.Add(extraUnit);
                allocatedPerUnit[comprobante.UnitId] = 0m;
            }

            allocatedPerUnit[comprobante.UnitId] += comprobante.Total;
        }

        foreach (var pUnit in ownerPayment.Units.Where(u => !u.IsDeleted))
        {
            if (allocatedPerUnit.TryGetValue(pUnit.UnitId, out var allocated))
                pUnit.AllocatedAmount = allocated;
        }

        // Saldo a favor consumido automáticamente (nunca lo elige el propietario), del comprobante
        // más antiguo cubierto en adelante. creditNeeded ya viene en 0 si el monto solo alcanzaba.
        var creditUsed = creditNeeded;
        if (creditUsed > 0 && ownerCredit is not null)
        {
            var lots = await credits.EnsureLotsAsync(ownerPayment.OwnerId, companyId, ownerCredit.Amount, ct);
            var remaining = creditUsed;
            foreach (var (comprobante, paymentRecord) in createdPayments)
            {
                if (remaining <= 0) break;
                var take = Math.Min(remaining, comprobante.Total);
                credits.ConsumeLots(lots, take, ownerPayment.OwnerId, companyId, CreditApplyMode.OnPaymentApproval,
                    paymentRecord.Id, null, $"Aplicado automáticamente al comprobante {comprobante.Label}.");
                paymentRecord.Notes += $" Incluye Gs. {ComprobanteService.Gs(take)} de saldo a favor.";
                remaining -= take;
            }

            ownerCredit.Amount -= creditUsed;
        }
    }

    private async Task<decimal> GetAvailableCreditAsync(Guid ownerId, Guid companyId, CancellationToken ct) =>
        (await dbContext.OwnerCredits.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == ownerId && x.CompanyId == companyId, ct))?.Amount ?? 0m;

    // Prueba primero el monto solo (comportamiento de siempre, no se toca un pago que ya cerraba
    // exacto aunque el propietario tenga saldo a favor sin usar) y, solo si no alcanza a cubrir
    // comprobantes completos, prueba sumando el saldo a favor disponible. Nunca fuerza el crédito
    // cuando no hace falta — sumarlo siempre rompía pagos exactos si quedaba un resto de crédito.
    private static (ComprobanteCoverage Coverage, decimal CreditUsed) CoverWithCredit(
        decimal amount, decimal availableCredit, IReadOnlyList<Comprobante> comprobantes)
    {
        var withoutCredit = ComprobanteService.Cover(amount, comprobantes);
        if (withoutCredit.Exact) return (withoutCredit, 0m);
        if (availableCredit <= 0) return (withoutCredit, 0m);

        var withCredit = ComprobanteService.Cover(amount + availableCredit, comprobantes);
        if (!withCredit.Exact) return (withCredit, 0m);

        var creditUsed = Math.Min(availableCredit, Math.Max(0, withCredit.CoveredTotal - amount));
        return (withCredit, creditUsed);
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────

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
