using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Api.Documents;
using Condo.Api.Services;
using QuestPDF.Fluent;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/owner-payments")]
public class OwnerPaymentsController(
    ICondoDbContext dbContext,
    ITenantContext tenantContext,
    IAccessScopeService accessScope,
    OwnerCreditService credits,
    ComprobanteService comprobantes,
    InvoiceDraftService invoiceDrafts,
    PushDispatcher pushDispatcher) : ControllerBase
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

        if (!await OwnerInScopeAsync(ownerId, companyId.Value, requireAll: false, ct)) return NotFound();

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
            .Include(x => x.Units.Where(u => !u.IsDeleted)).ThenInclude(u => u.Unit)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.CompanyId == companyId.Value, ct);
        if (payment is null) return NotFound();

        if (IsOwner() && payment.OwnerId != tenantContext.UserId) return Forbid();
        if (!IsOwner() && !CanManagePayments()) return Forbid();

        if (!IsOwner() && !TouchesScope(payment, await accessScope.GetAccessibleBuildingIdsAsync(ct)))
            return NotFound();

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

        if (!await OwnerInScopeAsync(ownerId, companyId.Value, requireAll: false, ct)) return NotFound();

        return Ok(await LoadCreditMovementsAsync(ownerId, companyId.Value, ct));
    }

    // ─── CREDIT BREAKDOWN ────────────────────────────────────────────────────
    // Desglose del saldo a favor para la ficha del propietario: cada lote con su origen (comprobante, reserva del Marketplace,
    // nota de credito, nota de credito del proveedor o saldo anterior), lo que queda de cada uno y como se fue usando.
    [HttpGet("credit-breakdown/{ownerId:guid}")]
    public async Task<ActionResult<OwnerCreditBreakdownDto>> GetOwnerCreditBreakdown(Guid ownerId, CancellationToken ct)
    {
        if (!CanManagePayments()) return Forbid();
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        if (!await OwnerInScopeAsync(ownerId, companyId.Value, requireAll: false, ct)) return NotFound();

        var amount = (await dbContext.OwnerCredits
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == ownerId && x.CompanyId == companyId.Value, ct))?.Amount ?? 0m;

        var lots = await dbContext.OwnerCreditMovements
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.OwnerId == ownerId && x.CompanyId == companyId.Value && x.Kind == OwnerCreditMovementKind.Generated)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var unitIds = lots.Where(x => x.UnitId != null).Select(x => x.UnitId!.Value).Distinct().ToList();
        var units = unitIds.Count == 0
            ? new Dictionary<Guid, (string Code, string Building)>()
            : (await dbContext.Units
                .AsNoTracking()
                .Where(u => unitIds.Contains(u.Id) && u.CompanyId == companyId.Value)
                .Select(u => new { u.Id, u.Code, Building = u.Building!.Name })
                .ToListAsync(ct))
                .ToDictionary(u => u.Id, u => (u.Code, u.Building));

        var activeRemaining = lots.Where(x => !x.OnHold).Sum(x => x.RemainingAmount);

        var uses = await dbContext.OwnerCreditMovements
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.OwnerId == ownerId && x.CompanyId == companyId.Value && x.Kind == OwnerCreditMovementKind.Applied)
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

        return Ok(new OwnerCreditBreakdownDto
        {
            Amount = amount,
            UntracedAmount = decimal.Max(0m, amount - activeRemaining),
            HeldAmount = lots.Where(x => x.OnHold).Sum(x => x.RemainingAmount),
            Lots = lots.Select(x => new OwnerCreditLotDto
            {
                Id = x.Id,
                CreatedAtUtc = x.CreatedAtUtc,
                Origin = x.SupplierCreditNoteId != null ? "SupplierCreditNote"
                    : x.CreditNoteId != null ? "CreditNote"
                    : x.MarketplaceReservationId != null ? "Marketplace"
                    : x.OwnerPaymentId != null ? "OwnerPayment"
                    : "Previous",
                Reference = x.SourceReference,
                Description = x.Description,
                BuildingName = x.UnitId != null && units.TryGetValue(x.UnitId.Value, out var u) ? u.Building : null,
                UnitCode = x.UnitId != null && units.TryGetValue(x.UnitId.Value, out var u2) ? u2.Code : null,
                OriginalAmount = x.Amount,
                RemainingAmount = x.RemainingAmount,
                OnHold = x.OnHold,
                OwnerPaymentId = x.OwnerPaymentId,
                MarketplaceReservationId = x.MarketplaceReservationId
            }).ToList(),
            Uses = uses
        });
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
        [FromQuery] Guid? buildingId,
        [FromQuery] string? channel,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
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

        HashSet<Guid>? staffScope = null;

        if (IsOwner())
        {
            query = query.Where(x => x.OwnerId == tenantContext.UserId);
        }
        else if (CanManagePayments())
        {
            // El personal solo ve pagos con al menos una unidad en un edificio de su alcance.
            var scope = await accessScope.GetAccessibleBuildingIdsAsync(ct);
            staffScope = scope;
            query = query.Where(x => x.Units.Any(u => !u.IsDeleted && u.Unit != null && scope.Contains(u.Unit.BuildingId)));

            if (ownerId.HasValue)
                query = query.Where(x => x.OwnerId == ownerId.Value);

            // channel = App (declarados por el propietario) o Web (registrados por el personal); otro valor se ignora.
            if (Enum.TryParse<OwnerPaymentChannel>(channel, true, out var parsedChannel))
                query = query.Where(x => x.Channel == parsedChannel);

            // Solo los pagos que tocan este edificio (que ademas tiene que estar en el alcance del usuario).
            if (buildingId.HasValue)
            {
                if (!scope.Contains(buildingId.Value)) return Forbid();
                query = query.Where(x => x.Units.Any(u => !u.IsDeleted && u.Unit != null && u.Unit.BuildingId == buildingId.Value));
            }

            // status admite una lista separada por comas ("Pending,UnderReview"); los valores no validos se ignoran.
            if (!string.IsNullOrWhiteSpace(status))
            {
                var statuses = status
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => Enum.TryParse<OwnerPaymentStatus>(s, true, out var parsed) ? (OwnerPaymentStatus?)parsed : null)
                    .Where(s => s.HasValue)
                    .Select(s => s!.Value)
                    .ToList();

                if (statuses.Count > 0)
                    query = query.Where(x => statuses.Contains(x.Status));
            }
        }
        else
        {
            return Forbid();
        }

        query = query.OrderByDescending(x => x.CreatedAtUtc);

        // Sin page se devuelve todo (como siempre: la web no pagina). Con page, se pagina y el total viaja en
        // el encabezado X-Total-Count.
        if (page.HasValue)
        {
            var size = Math.Clamp(pageSize ?? 25, 1, 100);
            Response.Headers["X-Total-Count"] = (await query.CountAsync(ct)).ToString();
            query = query.Skip((Math.Max(page.Value, 1) - 1) * size).Take(size);
        }

        var payments = await query.ToListAsync(ct);
        return Ok(payments.Select(p => ToDto(p, staffScope is null || FullyInScope(p, staffScope))).ToList());
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

        var canProcess = true;
        if (!IsOwner())
        {
            var scope = await accessScope.GetAccessibleBuildingIdsAsync(ct);
            if (!TouchesScope(payment, scope)) return NotFound();
            canProcess = FullyInScope(payment, scope);
        }

        var dto = ToDto(payment, canProcess);

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

        var ownerPayment = new OwnerPayment
        {
            CompanyId = companyId.Value,
            OwnerId = ownerId,
            PaymentDate = request.PaymentDate,
            ComprobanteUrl = request.ComprobanteUrl?.Trim() ?? string.Empty,
            DeclaredAmount = request.DeclaredAmount,
            Status = OwnerPaymentStatus.Pending,
            Reference = await NextReferenceAsync(companyId.Value, year, ct)
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

        // Dos pagos de la misma empresa enviados a la vez pueden calcular la misma referencia: el indice unico
        // (empresa, referencia) rechaza al segundo y se reintenta con la siguiente. Las entidades siguen en
        // estado Added tras el error, asi que alcanza con cambiar la referencia y volver a guardar.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await dbContext.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException exception) when (attempt < 5 && IsReferenceCollision(exception))
            {
                ownerPayment.Reference = await NextReferenceAsync(companyId.Value, year, ct);
            }
        }

        var reference = ownerPayment.Reference;

        // Notify managers: solo quienes trabajan con los edificios del pago (Encargado/Operador con el edificio
        // asignado; Administrador de empresa de toda la empresa, o de su condominio si esta acotado a uno).
        var paymentUnitIds = request.UnitIds.Distinct().ToList();
        var paymentBuildingIds = await dbContext.Units
            .AsNoTracking()
            .Where(u => !u.IsDeleted && paymentUnitIds.Contains(u.Id))
            .Select(u => u.BuildingId)
            .Distinct()
            .ToListAsync(ct);
        var paymentCondominiumIds = await dbContext.Buildings
            .AsNoTracking()
            .Where(b => paymentBuildingIds.Contains(b.Id) && b.CondominiumId != null)
            .Select(b => b.CondominiumId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var managerIds = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.CompanyId == companyId.Value && x.IsActive &&
                ((x.Role == UserRole.CompanyAdmin &&
                  (x.CondominiumId == null || paymentCondominiumIds.Contains(x.CondominiumId.Value))) ||
                 ((x.Role == UserRole.BuildingManager || x.Role == UserRole.CompanyOperator) &&
                  x.BuildingAccesses.Any(a => !a.IsDeleted && a.IsActive && paymentBuildingIds.Contains(a.BuildingId)))))
            .Select(x => x.Id)
            .ToListAsync(ct);

        var submittedTitle = "Nuevo pago enviado";
        var submittedBody = $"Un propietario envió un pago por {request.DeclaredAmount:N0}. Ref: {reference}. Toca para ver más detalles.";

        foreach (var managerId in managerIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                CompanyId = companyId.Value,
                RecipientId = managerId,
                Type = NotificationType.OwnerPaymentSubmitted,
                Title = submittedTitle,
                Body = submittedBody,
                EntityType = "OwnerPayment",
                EntityId = ownerPayment.Id
            });
        }

        await dbContext.SaveChangesAsync(ct);
        await pushDispatcher.NotifyUsersAsync(managerIds, submittedTitle, submittedBody, "OwnerPayment", ownerPayment.Id, ct);

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

        var scope = await accessScope.GetAccessibleBuildingIdsAsync(ct);
        if (!TouchesScope(payment, scope)) return NotFound();
        if (!FullyInScope(payment, scope)) return StatusCode(StatusCodes.Status403Forbidden, OutOfScopeMessage);

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
        if (!CoverageInScope(reviewCoverage, scope))
            return StatusCode(StatusCodes.Status403Forbidden, OutOfScopeMessage);

        payment.Status = OwnerPaymentStatus.UnderReview;
        payment.ReviewedAmount = request.ReviewedAmount;
        payment.ReviewedByUserId = tenantContext.UserId;
        payment.ReviewedAt = DateTime.UtcNow;

        var reviewTitle = "Tu pago está en revisión";
        var reviewBody = $"Tu pago {payment.Reference} está siendo revisado. Toca para ver más detalles.";

        dbContext.Notifications.Add(new Notification
        {
            CompanyId = companyId.Value,
            RecipientId = payment.OwnerId,
            Type = NotificationType.PaymentUnderReview,
            Title = reviewTitle,
            Body = reviewBody,
            EntityType = "OwnerPayment",
            EntityId = payment.Id
        });

        await dbContext.SaveChangesAsync(ct);
        await pushDispatcher.NotifyUserAsync(payment.OwnerId, reviewTitle, reviewBody, "OwnerPayment", payment.Id, ct);
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

        var scope = await accessScope.GetAccessibleBuildingIdsAsync(ct);
        if (!TouchesScope(payment, scope)) return NotFound();
        if (!FullyInScope(payment, scope)) return StatusCode(StatusCodes.Status403Forbidden, OutOfScopeMessage);

        if (payment.Status != OwnerPaymentStatus.UnderReview)
            return BadRequest("Solo se pueden aprobar pagos en estado EN REVISIÓN.");
        if (!payment.ReviewedAmount.HasValue || payment.ReviewedAmount.Value <= 0)
            return BadRequest("El pago no tiene un monto revisado válido.");

        try
        {
            await SettlePaymentAsync(payment, companyId.Value, scope, ct);
        }
        catch (PeriodClosedException exception)
        {
            return FinancePeriodGuard.ClosedResponse(exception.Month);
        }
        catch (OutOfScopeException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }

        payment.Status = OwnerPaymentStatus.Approved;
        payment.ResolvedAt = DateTime.UtcNow;

        var approvedTitle = "Tu pago fue aprobado";
        var approvedBody = $"Tu pago {payment.Reference} fue aprobado exitosamente. Toca para ver más detalles.";

        // El aviso de pago recibido se puede apagar por edificio (Centro de configuracion); encendido es el comportamiento de siempre.
        var notifyApproved = await PaymentReceivedNoticeActiveAsync(payment, ct);
        if (notifyApproved)
        {
            dbContext.Notifications.Add(new Notification
            {
                CompanyId = companyId.Value,
                RecipientId = payment.OwnerId,
                Type = NotificationType.PaymentApproved,
                Title = approvedTitle,
                Body = approvedBody,
                EntityType = "OwnerPayment",
                EntityId = payment.Id
            });
        }

        await dbContext.SaveChangesAsync(ct);
        if (notifyApproved)
            await pushDispatcher.NotifyUserAsync(payment.OwnerId, approvedTitle, approvedBody, "OwnerPayment", payment.Id, ct);

        // Genera de una vez los borradores de factura de este pago (uno por comprobante) — la emision
        // (elegir timbrado y numerar) sigue siendo un paso manual aparte, porque ahi se consume un
        // numero real sobre el papel preimpreso.
        await invoiceDrafts.CreateDraftsFromOwnerPaymentAsync(payment, tenantContext.UserId, ct);

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

        var scope = await accessScope.GetAccessibleBuildingIdsAsync(ct);
        if (!TouchesScope(payment, scope)) return NotFound();
        if (!FullyInScope(payment, scope)) return StatusCode(StatusCodes.Status403Forbidden, OutOfScopeMessage);

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

        var rejectedTitle = "Tu pago fue rechazado";
        var rejectedBody = $"Tu pago {payment.Reference} fue rechazado. Motivo: {reason}. Toca para ver más detalles.";

        dbContext.Notifications.Add(new Notification
        {
            CompanyId = companyId.Value,
            RecipientId = payment.OwnerId,
            Type = NotificationType.PaymentRejected,
            Title = rejectedTitle,
            Body = rejectedBody,
            EntityType = "OwnerPayment",
            EntityId = payment.Id
        });

        await dbContext.SaveChangesAsync(ct);
        await pushDispatcher.NotifyUserAsync(payment.OwnerId, rejectedTitle, rejectedBody, "OwnerPayment", payment.Id, ct);
        return Ok(ToDto(payment));
    }

    // ─── REGISTRO POR EL SISTEMA (canal Web) ─────────────────────────────────
    // Para el propietario que no usa la app: el personal registra el cobro y el pago sigue EXACTAMENTE el
    // mismo camino que uno aprobado desde la app (mismos comprobantes completos del mas antiguo al mas
    // nuevo, mismo saldo a favor, misma referencia PAY-, mismo borrador de factura y mismos avisos).
    // Solo cambia que nace ya aprobado.

    [HttpGet("register-preview/{ownerId:guid}")]
    public async Task<ActionResult<RegisterPreviewDto>> GetRegisterPreview(Guid ownerId, CancellationToken ct)
    {
        if (!CanManagePayments()) return Forbid();
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        var owner = await LoadOwnerAsync(ownerId, companyId.Value, ct);
        if (owner is null) return NotFound();
        var scope = await accessScope.GetAccessibleBuildingIdsAsync(ct);
        if (!await OwnerInScopeAsync(ownerId, companyId.Value, requireAll: false, ct)) return NotFound();

        var unitIds = await credits.LoadLinkedUnitIdsAsync(ownerId, companyId.Value, ct);
        var open = unitIds.Count == 0
            ? new List<Comprobante>()
            : await comprobantes.LoadAsync(unitIds, companyId.Value, false, ct);
        var credit = await GetAvailableCreditAsync(ownerId, companyId.Value, ct);

        var pending = await dbContext.OwnerPayments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.CompanyId == companyId.Value && x.OwnerId == ownerId
                        && (x.Status == OwnerPaymentStatus.Pending || x.Status == OwnerPaymentStatus.UnderReview))
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new RegisterPreviewPendingPaymentDto
            {
                Id = x.Id, Reference = x.Reference, Status = x.Status.ToString(), DeclaredAmount = x.DeclaredAmount
            })
            .ToListAsync(ct);

        var preview = new RegisterPreviewDto
        {
            OwnerId = owner.Id,
            OwnerFullName = owner.FullName,
            AvailableCredit = credit,
            PendingOwnerPayments = pending
        };

        var running = 0m;
        foreach (var c in open)
        {
            running += c.Total;
            preview.Comprobantes.Add(new RegisterComprobanteDto
            {
                UnitId = c.UnitId,
                UnitCode = c.UnitCode,
                BuildingId = c.BuildingId,
                BuildingName = c.BuildingName,
                ExpensePeriodId = c.ExpensePeriodId,
                PeriodYear = c.Year,
                PeriodMonth = c.Month,
                Total = c.Total,
                CumulativeTotal = running,
                AmountToReceive = Math.Max(0m, running - credit),
                InScope = scope.Contains(c.BuildingId),
                Lines = c.Lines.Select(l => new RegisterComprobanteLineDto { Concept = l.Charge.Concept, Pending = l.Pending }).ToList()
            });
        }

        return Ok(preview);
    }

    [HttpPost("register")]
    public async Task<ActionResult<OwnerPaymentDto>> Register([FromBody] OwnerPaymentRegisterRequest request, CancellationToken ct)
    {
        if (!CanManagePayments()) return Forbid();
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        var externalReference = request.ExternalReference?.Trim() ?? string.Empty;
        var notes = request.Notes?.Trim() ?? string.Empty;

        if (request.OwnerId == Guid.Empty) return BadRequest("El propietario es obligatorio.");
        if (request.Amount <= 0) return BadRequest("El monto debe ser mayor a cero.");
        if (request.PaymentDate == default) return BadRequest("La fecha de pago es obligatoria.");
        if (request.PaymentDate > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            return BadRequest("La fecha de pago no puede ser futura.");
        if (!Enum.IsDefined(request.Method)) return BadRequest("El método de pago no es válido.");
        if (externalReference.Length > 100) return BadRequest("El número de transferencia/documento no puede superar los 100 caracteres.");
        if (notes.Length > 500) return BadRequest("Las notas no pueden superar los 500 caracteres.");

        var owner = await LoadOwnerAsync(request.OwnerId, companyId.Value, ct);
        if (owner is null) return NotFound();
        if (!await OwnerInScopeAsync(owner.Id, companyId.Value, requireAll: false, ct)) return NotFound();

        // Si el propietario ya mando un pago desde la app que sigue sin resolver, se resuelve primero (si no,
        // los dos pagarian los mismos comprobantes y el segundo en aprobarse fallaria).
        var unresolved = await dbContext.OwnerPayments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.CompanyId == companyId.Value && x.OwnerId == owner.Id
                        && (x.Status == OwnerPaymentStatus.Pending || x.Status == OwnerPaymentStatus.UnderReview))
            .Select(x => x.Reference)
            .FirstOrDefaultAsync(ct);
        if (unresolved is not null)
            return Conflict($"El propietario tiene el pago {unresolved} pendiente de revisión desde la app. " +
                            "Apruébelo o rechácelo antes de registrar otro pago.");

        var scope = await accessScope.GetAccessibleBuildingIdsAsync(ct);
        var unitIds = await credits.LoadLinkedUnitIdsAsync(owner.Id, companyId.Value, ct);
        var open = unitIds.Count == 0
            ? new List<Comprobante>()
            : await comprobantes.LoadAsync(unitIds, companyId.Value, false, ct);
        var availableCredit = await GetAvailableCreditAsync(owner.Id, companyId.Value, ct);

        var (coverage, _) = CoverWithCredit(request.Amount, availableCredit, open);
        if (!coverage.Exact)
            return BadRequest(ComprobanteService.MismatchMessage(request.Amount + availableCredit, open));
        if (!CoverageInScope(coverage, scope))
            return StatusCode(StatusCodes.Status403Forbidden, OutOfScopeMessage);

        // Cierre contable de un mes: se rechaza antes de crear el pago, para no dejar un registro rechazado.
        var closedRegister = await FindClosedForCoverageAsync(coverage, request.PaymentDate, ct);
        if (closedRegister is not null)
            return FinancePeriodGuard.ClosedResponse(closedRegister);

        var now = DateTime.UtcNow;
        var year = now.Year;
        var ownerPayment = new OwnerPayment
        {
            CompanyId = companyId.Value,
            OwnerId = owner.Id,
            PaymentDate = request.PaymentDate,
            DeclaredAmount = request.Amount,
            ReviewedAmount = request.Amount,
            Status = OwnerPaymentStatus.UnderReview,
            Channel = OwnerPaymentChannel.Web,
            Method = request.Method,
            ExternalReference = externalReference,
            Notes = notes,
            ReviewedByUserId = tenantContext.UserId,
            ReviewedAt = now,
            Reference = await NextReferenceAsync(companyId.Value, year, ct)
        };
        dbContext.OwnerPayments.Add(ownerPayment);

        // Misma proteccion que Create: dos pagos a la vez pueden calcular la misma referencia.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await dbContext.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException exception) when (attempt < 5 && IsReferenceCollision(exception))
            {
                ownerPayment.Reference = await NextReferenceAsync(companyId.Value, year, ct);
            }
        }

        try
        {
            await SettlePaymentAsync(ownerPayment, companyId.Value, scope, ct);
        }
        catch (InvalidOperationException exception)
        {
            // Algo cambio entre la validacion y la liquidacion (otro usuario cobro al mismo tiempo): no queda un
            // pago a medias.
            ownerPayment.Status = OwnerPaymentStatus.Rejected;
            ownerPayment.RejectionReason = "No se pudo registrar: " + exception.Message;
            ownerPayment.IsDeleted = true;
            await dbContext.SaveChangesAsync(ct);
            if (exception is PeriodClosedException closedException)
                return FinancePeriodGuard.ClosedResponse(closedException.Month);
            return exception is OutOfScopeException
                ? StatusCode(StatusCodes.Status403Forbidden, exception.Message)
                : BadRequest(exception.Message);
        }

        ownerPayment.Status = OwnerPaymentStatus.Approved;
        ownerPayment.ResolvedAt = now;

        var title = "Registramos tu pago";
        var body = $"Se registró tu pago {ownerPayment.Reference} por Gs. {ComprobanteService.Gs(request.Amount)}. Toca para ver más detalles.";
        // El aviso de pago recibido se puede apagar por edificio (Centro de configuracion); encendido es el comportamiento de siempre.
        var notifyRegistered = await PaymentReceivedNoticeActiveAsync(ownerPayment, ct);
        if (notifyRegistered)
        {
            dbContext.Notifications.Add(new Notification
            {
                CompanyId = companyId.Value,
                RecipientId = owner.Id,
                Type = NotificationType.PaymentApproved,
                Title = title,
                Body = body,
                EntityType = "OwnerPayment",
                EntityId = ownerPayment.Id
            });
        }

        await dbContext.SaveChangesAsync(ct);
        if (notifyRegistered)
            await pushDispatcher.NotifyUserAsync(owner.Id, title, body, "OwnerPayment", ownerPayment.Id, ct);
        await invoiceDrafts.CreateDraftsFromOwnerPaymentAsync(ownerPayment, tenantContext.UserId, ct);

        var saved = await dbContext.OwnerPayments
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.ReviewedByUser)
            .Include(x => x.Units.Where(u => !u.IsDeleted))
                .ThenInclude(u => u.Unit).ThenInclude(u => u!.Building)
            .FirstAsync(x => x.Id == ownerPayment.Id, ct);

        return Ok(ToDto(saved));
    }

    // ─── REVERSE (solo pagos registrados por el sistema) ─────────────────────
    [HttpPut("{id:guid}/reverse")]
    public async Task<ActionResult<OwnerPaymentDto>> Reverse(
        Guid id, [FromBody] OwnerPaymentReverseRequest request, CancellationToken ct)
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

        var scope = await accessScope.GetAccessibleBuildingIdsAsync(ct);
        if (!TouchesScope(payment, scope)) return NotFound();
        if (!FullyInScope(payment, scope)) return StatusCode(StatusCodes.Status403Forbidden, OutOfScopeMessage);

        if (payment.Channel != OwnerPaymentChannel.Web)
            return BadRequest("Solo se pueden revertir pagos registrados por el sistema. Un pago declarado desde la app se corrige con una nota de crédito.");
        if (payment.Status != OwnerPaymentStatus.Approved)
            return BadRequest("Solo se puede revertir un pago aprobado que no haya sido revertido.");

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0) return BadRequest("El motivo de la reversa es obligatorio.");
        if (reason.Length > 400) return BadRequest("El motivo no puede superar los 400 caracteres.");

        var settlements = await dbContext.Payments
            .Include(x => x.Allocations.Where(a => !a.IsDeleted))
            .Where(x => !x.IsDeleted && !x.IsReversed && x.CompanyId == companyId.Value && x.Reference == payment.Reference)
            .ToListAsync(ct);
        var paymentIds = settlements.Select(x => x.Id).ToList();

        // Cierre contable de un mes: revertir cambia la caja del mes en que se hizo cada pago.
        var closedReverse = await FindClosedForPaymentsAsync(settlements, ct);
        if (closedReverse is not null)
            return FinancePeriodGuard.ClosedResponse(closedReverse);

        // Si el pago uso saldo a favor, devolverlo exige reconstruir los lotes: se hace con un ajuste manual.
        var usedCredit = await dbContext.OwnerCreditMovements
            .AnyAsync(x => !x.IsDeleted && x.Kind == OwnerCreditMovementKind.Applied && x.PaymentId != null && paymentIds.Contains(x.PaymentId.Value), ct);
        if (usedCredit)
            return Conflict("Este pago usó saldo a favor del propietario, por lo que no se puede revertir desde aquí. Consulte con el administrador del sistema.");

        var invoices = await dbContext.Invoices
            .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Voided
                        && (i.OwnerPaymentId == payment.Id || paymentIds.Contains(i.PaymentId)))
            .ToListAsync(ct);
        if (invoices.Any(i => i.Status == InvoiceStatus.Issued))
            return Conflict("Este pago ya tiene una factura emitida. Anule la factura primero (Facturación › Facturas) y luego revierta el pago.");

        var now = DateTime.UtcNow;

        // Los borradores todavia no tienen numero ni timbrado: se descartan.
        foreach (var draft in invoices)
        {
            draft.Status = InvoiceStatus.Voided;
            draft.FechaAnulacionUtc = now;
            draft.MotivoAnulacion = $"Pago {payment.Reference} revertido: {reason}";
            dbContext.InvoiceAuditLogs.Add(new InvoiceAuditLog
            {
                CompanyId = draft.CompanyId,
                InvoiceId = draft.Id,
                Action = InvoiceAuditAction.Voided,
                UserId = tenantContext.UserId,
                TimestampUtc = now,
                DatosAntesJson = System.Text.Json.JsonSerializer.Serialize(new { Status = InvoiceStatus.Draft }),
                DatosDespuesJson = System.Text.Json.JsonSerializer.Serialize(new { draft.Status, draft.MotivoAnulacion }),
                Detalle = $"Borrador descartado: se revirtió el pago {payment.Reference}. Motivo: {reason}."
            });
        }

        // Se conserva el rastro (el Payment queda marcado como revertido) y se liberan los cargos.
        foreach (var settlement in settlements)
        {
            settlement.IsReversed = true;
            settlement.ReversedAt = now;
            foreach (var allocation in settlement.Allocations) allocation.IsDeleted = true;
        }

        payment.Status = OwnerPaymentStatus.Rejected;
        payment.RejectionReason = $"PAGO REVERTIDO: {reason}";
        payment.ReversedAt = now;
        payment.ResolvedAt = now;

        var title = "Un pago registrado fue revertido";
        var body = $"El pago {payment.Reference} fue revertido por la administración. Motivo: {reason}. Toca para ver más detalles.";
        dbContext.Notifications.Add(new Notification
        {
            CompanyId = companyId.Value,
            RecipientId = payment.OwnerId,
            Type = NotificationType.PaymentRejected,
            Title = title,
            Body = body,
            EntityType = "OwnerPayment",
            EntityId = payment.Id
        });

        await dbContext.SaveChangesAsync(ct);
        await pushDispatcher.NotifyUserAsync(payment.OwnerId, title, body, "OwnerPayment", payment.Id, ct);
        return Ok(ToDto(payment));
    }

    private async Task<ApplicationUser?> LoadOwnerAsync(Guid ownerId, Guid companyId, CancellationToken ct) =>
        await dbContext.ApplicationUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == ownerId && x.CompanyId == companyId && x.Role == UserRole.Owner, ct);

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

        if (!IsOwner() && !TouchesScope(payment, await accessScope.GetAccessibleBuildingIdsAsync(ct)))
            return NotFound();

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

        // Aplicar saldo a favor recorre todas las unidades del propietario: exige que todas esten en el alcance.
        if (!await OwnerInScopeAsync(ownerId, companyId.Value, requireAll: true, ct)) return NotFound();

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
    private async Task SettlePaymentAsync(OwnerPayment ownerPayment, Guid companyId, HashSet<Guid> scope, CancellationToken ct)
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

        // Se valida antes de tocar nada: no se liquida ningun comprobante de un edificio fuera del alcance.
        if (!CoverageInScope(coverage, scope))
            throw new OutOfScopeException(OutOfScopeMessage);

        // Cierre contable de un mes: los pagos nuevos llevan la fecha del pago.
        var closedMonth = await FindClosedForCoverageAsync(coverage, ownerPayment.PaymentDate, ct);
        if (closedMonth is not null)
            throw new PeriodClosedException(closedMonth);

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
                Method = ownerPayment.Method,
                Reference = ownerPayment.Reference,
                Notes = ownerPayment.Channel == OwnerPaymentChannel.Web
                    ? $"Pago registrado por el sistema. Comprobante {comprobante.Label}. Ref: {ownerPayment.Reference}" +
                      (string.IsNullOrWhiteSpace(ownerPayment.ExternalReference) ? "" : $". Doc.: {ownerPayment.ExternalReference}")
                    : $"Pago de propietario aprobado. Comprobante {comprobante.Label}. Ref: {ownerPayment.Reference}"
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

    // ─── REFERENCIA DEL PAGO ─────────────────────────────────────────────────
    // PAY-{año}-{n de 6 digitos}, numerada POR EMPRESA (dos empresas pueden tener el mismo PAY-2026-000001;
    // por eso toda busqueda por referencia debe filtrar tambien por empresa).
    private async Task<string> NextReferenceAsync(Guid companyId, int year, CancellationToken ct)
    {
        var prefix = $"PAY-{year}-";

        // El sufijo lleva ceros a la izquierda: el mayor en orden alfabetico es el ultimo numero usado
        // (incluye pagos eliminados, para no reutilizar numeros).
        var last = await dbContext.OwnerPayments
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Reference.StartsWith(prefix))
            .OrderByDescending(x => x.Reference)
            .Select(x => x.Reference)
            .FirstOrDefaultAsync(ct);

        var next = last is not null && int.TryParse(last[prefix.Length..], out var number) ? number + 1 : 1;
        return $"{prefix}{next:D6}";
    }

    private static bool IsReferenceCollision(DbUpdateException exception) =>
        exception.InnerException is SqlException sql
        && (sql.Number == 2601 || sql.Number == 2627)
        && sql.Message.Contains("IX_OwnerPayments_", StringComparison.OrdinalIgnoreCase);

    // ─── ALCANCE POR EDIFICIO (personal) ─────────────────────────────────────
    // Un pago de propietario puede abarcar unidades de varios edificios. El personal (Encargado/Operador)
    // solo trabaja con los edificios que tiene asignados; el Administrador de empresa, con los de su empresa
    // (o su condominio, si esta acotado a uno). Regla:
    //   - ver: el pago tiene al menos una unidad en un edificio de su alcance;
    //   - revisar / aprobar / rechazar: TODAS las unidades del pago y TODOS los comprobantes que cubre
    //     estan en su alcance (un pago se liquida completo, del mas antiguo al mas nuevo, no por edificio).
    private const string OutOfScopeMessage =
        "Este pago incluye unidades o comprobantes de edificios que no tenés asignados. " +
        "Debe procesarlo un Administrador de empresa o un encargado con acceso a todos esos edificios.";

    private sealed class OutOfScopeException(string message) : InvalidOperationException(message);

    // El pago caeria en un mes contable cerrado (Centro de configuracion): se responde 409 con el codigo de cierre.
    private sealed class PeriodClosedException(ClosedMonth month) : InvalidOperationException(FinancePeriodGuard.MessageFor(month))
    {
        public ClosedMonth Month { get; } = month;
    }

    // Aviso "pago recibido" del Centro de configuracion: encendido salvo que el edificio lo haya apagado. Un pago de varios edificios avisa
    // si el aviso esta encendido en alguno de ellos.
    private async Task<bool> PaymentReceivedNoticeActiveAsync(OwnerPayment payment, CancellationToken ct)
    {
        var unitIds = payment.Units.Where(u => !u.IsDeleted).Select(u => u.UnitId).Distinct().ToList();
        var buildingIds = await dbContext.Units.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .Select(u => u.BuildingId)
            .Distinct()
            .ToListAsync(ct);
        if (buildingIds.Count == 0) return true;

        return (await NoticeRules.BuildingsWithActiveAsync(dbContext, buildingIds, NoticeKind.PaymentReceived, ct)).Count > 0;
    }

    // Cierre contable de un mes: el primer mes cerrado entre los pagos indicados (cada uno en el edificio de su periodo, con su fecha).
    private async Task<ClosedMonth?> FindClosedForPaymentsAsync(IReadOnlyCollection<Payment> payments, CancellationToken ct) =>
        await new FinancePeriodGuard(dbContext).FindClosedForPaymentsAsync(payments, ct);

    // Cierre contable de un mes: el primer mes cerrado entre los edificios que cubre el pago, segun la fecha del pago.
    private async Task<ClosedMonth?> FindClosedForCoverageAsync(ComprobanteCoverage coverage, DateOnly paymentDate, CancellationToken ct)
    {
        var guard = new FinancePeriodGuard(dbContext);
        foreach (var buildingId in coverage.Covered.Select(c => c.BuildingId).Distinct())
        {
            var closed = await guard.FindClosedAsync(buildingId, paymentDate, ct);
            if (closed is not null) return closed;
        }

        return null;
    }

    private static bool TouchesScope(OwnerPayment payment, HashSet<Guid> scope) =>
        payment.Units.Any(u => !u.IsDeleted && u.Unit is not null && scope.Contains(u.Unit.BuildingId));

    private static bool FullyInScope(OwnerPayment payment, HashSet<Guid> scope)
    {
        var units = payment.Units.Where(u => !u.IsDeleted).ToList();
        return units.Count > 0 && units.All(u => u.Unit is not null && scope.Contains(u.Unit.BuildingId));
    }

    private static bool CoverageInScope(ComprobanteCoverage coverage, HashSet<Guid> scope) =>
        coverage.Covered.All(c => scope.Contains(c.BuildingId));

    // Propietario visible para el personal: tiene al menos una unidad vinculada en su alcance
    // (requireAll: todas, para acciones que lo afectan por completo, como aplicar saldo a favor).
    private async Task<bool> OwnerInScopeAsync(Guid ownerId, Guid companyId, bool requireAll, CancellationToken ct)
    {
        var unitIds = await credits.LoadLinkedUnitIdsAsync(ownerId, companyId, ct);
        if (unitIds.Count == 0)
            return accessScope.IsCompanyAdmin && !accessScope.CondominiumId.HasValue;

        var scope = await accessScope.GetAccessibleBuildingIdsAsync(ct);
        var buildingIds = await dbContext.Units
            .AsNoTracking()
            .Where(u => !u.IsDeleted && unitIds.Contains(u.Id))
            .Select(u => u.BuildingId)
            .Distinct()
            .ToListAsync(ct);

        return requireAll ? buildingIds.All(scope.Contains) : buildingIds.Any(scope.Contains);
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

    private static OwnerPaymentDto ToDto(OwnerPayment p, bool canProcess = true) => new()
    {
        CanProcess = canProcess,
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
        Channel = p.Channel.ToString(),
        Method = p.Method.ToString(),
        ExternalReference = p.ExternalReference,
        Notes = p.Notes,
        ReversedAt = p.ReversedAt,
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
