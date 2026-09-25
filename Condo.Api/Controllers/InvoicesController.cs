using System.Text.Json;
using Condo.Api.Documents;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QuestPDF.Fluent;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/invoices")]
public class InvoicesController(
    ICondoDbContext dbContext, IAccessScopeService accessScope, ITenantContext tenantContext, IWebHostEnvironment env,
    Condo.Api.Services.InvoiceDraftService draftService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        [FromQuery] Guid? unitId,
        [FromQuery] InvoiceStatus? status,
        CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.Invoices.AsNoTracking().Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
                query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            else
                query = query.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
        }

        if (buildingId.HasValue) query = query.Where(x => x.BuildingId == buildingId.Value);
        if (unitId.HasValue) query = query.Where(x => x.UnitId == unitId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new InvoiceRow(
                x.Id, x.CompanyId, x.BuildingId, x.Building != null ? x.Building.Name : string.Empty,
                x.UnitId, x.Unit != null ? x.Unit.Code : string.Empty,
                x.PaymentId, x.InvoiceSeriesId,
                x.Series != null ? x.Series.RazonSocial : null,
                x.Series != null ? x.Series.Ruc : null,
                x.Series != null ? x.Series.NumeroTimbrado : null,
                x.Series != null ? x.Series.Establecimiento : null,
                x.Series != null ? x.Series.PuntoExpedicion : null,
                x.Series != null ? x.Series.VigenciaDesde : null,
                x.Series != null ? x.Series.VigenciaHasta : null,
                x.Building != null ? x.Building.Address : null,
                x.Building != null ? ((x.Building.ContactPhonePrefix ?? "") + " " + (x.Building.ContactPhone ?? "")).Trim() : null,
                x.Status, x.Numero, x.NumeroFormateado, x.MontoTotal, x.DetalleSnapshotJson,
                x.FechaEmisionUtc, x.FechaAnulacionUtc, x.MotivoAnulacion, x.ReemplazadaPorInvoiceId, x.CreatedAtUtc,
                x.Payment != null && x.Payment.ExpensePeriod != null ? x.Payment.ExpensePeriod.DueDate : (DateOnly?)null,
                x.Payment != null ? x.Payment.ExpensePeriodId : (Guid?)null,
                x.Payment != null && x.Payment.ExpensePeriod != null ? x.Payment.ExpensePeriod.Year : (int?)null,
                x.Payment != null && x.Payment.ExpensePeriod != null ? x.Payment.ExpensePeriod.Month : (int?)null,
                x.Unit != null ? x.Unit.Coefficient : 0m,
                x.Series != null ? x.Series.FieldPositionsJson : null,
                x.Series != null && x.Series.HideFrame,
                null))
            .ToListAsync(cancellationToken);

        return Ok(rows.Select(ToDto).ToList());
    }

    // Embudo de facturacion: pagos sin ninguna factura (huecos que hoy nadie ve salvo buscando a
    // mano), borradores sin emitir, y emitidas — para el edificio/alcance del usuario.
    [HttpGet("funnel")]
    public async Task<ActionResult<InvoiceFunnelDto>> GetFunnel(
        [FromQuery] Guid? buildingId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 5000 ? 25 : pageSize;
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var invoiceQuery = dbContext.Invoices.AsNoTracking().Where(x => !x.IsDeleted);
        var paymentQuery = dbContext.Payments.AsNoTracking().Where(x => !x.IsDeleted && !x.IsReversed);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
            {
                invoiceQuery = invoiceQuery.Where(x => x.CompanyId == accessScope.CompanyId.Value);
                paymentQuery = paymentQuery.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            }
            else
            {
                invoiceQuery = invoiceQuery.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
                paymentQuery = paymentQuery.Where(x => x.Unit != null && accessibleBuildingIds.Contains(x.Unit.BuildingId));
            }
        }

        if (buildingId.HasValue)
        {
            invoiceQuery = invoiceQuery.Where(x => x.BuildingId == buildingId.Value);
            paymentQuery = paymentQuery.Where(x => x.Unit != null && x.Unit.BuildingId == buildingId.Value);
        }

        var draftsNotEmitted = await invoiceQuery.CountAsync(x => x.Status == InvoiceStatus.Draft, cancellationToken);
        var issued = await invoiceQuery.CountAsync(x => x.Status == InvoiceStatus.Issued, cancellationToken);

        var paymentsWithoutInvoice = await paymentQuery
            .Where(p => !dbContext.Invoices.Any(inv => !inv.IsDeleted && inv.PaymentId == p.Id))
            .OrderByDescending(p => p.PaymentDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new InvoiceFunnelPaymentItemDto
            {
                PaymentId = p.Id,
                BuildingId = p.Unit!.BuildingId,
                BuildingName = p.Unit.Building != null ? p.Unit.Building.Name : string.Empty,
                UnitCode = p.Unit.Code,
                Amount = p.Amount,
                PaymentDate = p.PaymentDate,
                Reference = p.Reference
            })
            .ToListAsync(cancellationToken);

        var paymentsWithoutInvoiceTotal = await paymentQuery
            .CountAsync(p => !dbContext.Invoices.Any(inv => !inv.IsDeleted && inv.PaymentId == p.Id), cancellationToken);

        return Ok(new InvoiceFunnelDto
        {
            PaymentsWithoutInvoice = paymentsWithoutInvoiceTotal,
            DraftsNotEmitted = draftsNotEmitted,
            Issued = issued,
            Page = page,
            PageSize = pageSize,
            PaymentsWithoutInvoiceItems = paymentsWithoutInvoice
        });
    }

    // Consulta profesional de facturas: filtros, orden, paginacion y trazabilidad completa
    // (factura → comprobante/periodo → liquidacion → pago del propietario → cliente y timbrado).
    [HttpGet("ledger")]
    public async Task<ActionResult<InvoiceLedgerDto>> GetLedger([FromQuery] InvoiceLedgerQuery q, CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);
        var query = dbContext.Invoices.AsNoTracking().Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
                query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            else
                query = query.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
        }

        if (q.BuildingId.HasValue) query = query.Where(x => x.BuildingId == q.BuildingId.Value);
        if (q.UnitId.HasValue) query = query.Where(x => x.UnitId == q.UnitId.Value);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (q.Year.HasValue) query = query.Where(x => x.Payment!.ExpensePeriod!.Year == q.Year.Value);
        if (q.Month.HasValue) query = query.Where(x => x.Payment!.ExpensePeriod!.Month == q.Month.Value);

        if (q.From.HasValue)
        {
            var from = q.From.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(x => (x.FechaEmisionUtc ?? x.CreatedAtUtc) >= from);
        }
        if (q.To.HasValue)
        {
            var toExclusive = q.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(x => (x.FechaEmisionUtc ?? x.CreatedAtUtc) < toExclusive);
        }

        if (q.OwnerPaymentId.HasValue)
        {
            var ownerPaymentId = q.OwnerPaymentId.Value;
            var ownerPaymentRef = await dbContext.OwnerPayments.AsNoTracking()
                .Where(x => x.Id == ownerPaymentId).Select(x => x.Reference).FirstOrDefaultAsync(cancellationToken);
            query = query.Where(x => x.OwnerPaymentId == ownerPaymentId
                                     || (x.OwnerPaymentId == null && ownerPaymentRef != null && x.Payment!.Reference == ownerPaymentRef));
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            query = query.Where(x =>
                (x.NumeroFormateado != null && x.NumeroFormateado.Contains(term))
                || x.Unit!.Code.Contains(term)
                || x.Building!.Name.Contains(term)
                || x.Payment!.Reference.Contains(term)
                || (x.Series != null && x.Series.NumeroTimbrado.Contains(term))
                || dbContext.UnitOwners.Any(o => !o.IsDeleted && o.UnitId == x.UnitId && o.Owner != null && o.Owner.FullName.Contains(term)));
        }

        // Resumen por estado sobre todo el conjunto filtrado (no solo la pagina).
        var byStatus = await query
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(x => x.MontoTotal) })
            .ToListAsync(cancellationToken);

        var summary = new InvoiceLedgerSummaryDto
        {
            DraftCount = byStatus.Where(x => x.Status == InvoiceStatus.Draft).Sum(x => x.Count),
            IssuedCount = byStatus.Where(x => x.Status == InvoiceStatus.Issued).Sum(x => x.Count),
            VoidedCount = byStatus.Where(x => x.Status == InvoiceStatus.Voided).Sum(x => x.Count),
            DraftAmount = byStatus.Where(x => x.Status == InvoiceStatus.Draft).Sum(x => x.Amount),
            IssuedAmount = byStatus.Where(x => x.Status == InvoiceStatus.Issued).Sum(x => x.Amount),
            VoidedAmount = byStatus.Where(x => x.Status == InvoiceStatus.Voided).Sum(x => x.Amount)
        };
        var totalCount = summary.DraftCount + summary.IssuedCount + summary.VoidedCount;

        var descending = !string.Equals(q.SortDir, "asc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<Invoice> ordered = (q.SortBy?.ToLowerInvariant()) switch
        {
            "numero" => descending ? query.OrderByDescending(x => x.Numero) : query.OrderBy(x => x.Numero),
            "monto" => descending ? query.OrderByDescending(x => x.MontoTotal) : query.OrderBy(x => x.MontoTotal),
            "unidad" => descending ? query.OrderByDescending(x => x.Unit!.Code) : query.OrderBy(x => x.Unit!.Code),
            "periodo" => descending
                ? query.OrderByDescending(x => x.Payment!.ExpensePeriod!.Year).ThenByDescending(x => x.Payment!.ExpensePeriod!.Month)
                : query.OrderBy(x => x.Payment!.ExpensePeriod!.Year).ThenBy(x => x.Payment!.ExpensePeriod!.Month),
            _ => descending
                ? query.OrderByDescending(x => x.FechaEmisionUtc ?? x.CreatedAtUtc)
                : query.OrderBy(x => x.FechaEmisionUtc ?? x.CreatedAtUtc)
        };
        ordered = ordered.ThenByDescending(x => x.CreatedAtUtc);

        var page = Math.Max(q.Page, 1);
        var pageSize = Math.Clamp(q.PageSize, 1, 2000);

        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id, x.Status, x.Numero, x.NumeroFormateado, x.MontoTotal, x.DetalleSnapshotJson,
                x.FechaEmisionUtc, x.FechaAnulacionUtc, x.MotivoAnulacion, x.CreatedAtUtc,
                x.OwnerPaymentId, x.PaymentId, x.UnitId, x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                SeriesRazonSocial = x.Series != null ? x.Series.RazonSocial : null,
                SeriesRuc = x.Series != null ? x.Series.Ruc : null,
                SeriesNumeroTimbrado = x.Series != null ? x.Series.NumeroTimbrado : null,
                SeriesEstablecimiento = x.Series != null ? x.Series.Establecimiento : null,
                SeriesPuntoExpedicion = x.Series != null ? x.Series.PuntoExpedicion : null,
                PaymentReference = x.Payment != null ? x.Payment.Reference : string.Empty,
                PaymentDate = x.Payment != null ? x.Payment.PaymentDate : default,
                PaymentAmount = x.Payment != null ? x.Payment.Amount : 0m,
                ExpensePeriodId = x.Payment != null ? x.Payment.ExpensePeriodId : Guid.Empty,
                PeriodYear = x.Payment != null && x.Payment.ExpensePeriod != null ? x.Payment.ExpensePeriod.Year : 0,
                PeriodMonth = x.Payment != null && x.Payment.ExpensePeriod != null ? x.Payment.ExpensePeriod.Month : 0,
                PeriodName = x.Payment != null && x.Payment.ExpensePeriod != null ? x.Payment.ExpensePeriod.Name : string.Empty,
                PeriodStatus = x.Payment != null && x.Payment.ExpensePeriod != null ? x.Payment.ExpensePeriod.Status : default,
                PeriodDueDate = x.Payment != null && x.Payment.ExpensePeriod != null ? x.Payment.ExpensePeriod.DueDate : default
            })
            .ToListAsync(cancellationToken);

        var periodIds = rows.Select(r => r.ExpensePeriodId).Distinct().ToList();
        var unitIds = rows.Select(r => r.UnitId).Distinct().ToList();
        var ownerPaymentIds = rows.Where(r => r.OwnerPaymentId.HasValue).Select(r => r.OwnerPaymentId!.Value).Distinct().ToList();
        var paymentRefs = rows.Where(r => !r.OwnerPaymentId.HasValue).Select(r => r.PaymentReference).Distinct().ToList();

        var settlements = await dbContext.ExpenseSettlements.AsNoTracking()
            .Where(s => !s.IsDeleted && periodIds.Contains(s.ExpensePeriodId))
            .Select(s => new
            {
                s.ExpensePeriodId, s.Status, s.ApprovedAtUtc, s.PublishedAtUtc,
                ApprovedBy = s.ApprovedByUser != null ? s.ApprovedByUser.FullName : null,
                PublishedBy = s.PublishedByUser != null ? s.PublishedByUser.FullName : null
            })
            .ToListAsync(cancellationToken);

        var ownerPayments = await dbContext.OwnerPayments.AsNoTracking()
            .Where(o => !o.IsDeleted && (ownerPaymentIds.Contains(o.Id) || paymentRefs.Contains(o.Reference)))
            .Select(o => new
            {
                o.Id, o.Reference, o.Status, o.ResolvedAt,
                OwnerName = o.Owner != null ? o.Owner.FullName : null,
                ReviewedBy = o.ReviewedByUser != null ? o.ReviewedByUser.FullName : null
            })
            .ToListAsync(cancellationToken);

        var owners = await dbContext.UnitOwners.AsNoTracking()
            .Where(o => !o.IsDeleted && unitIds.Contains(o.UnitId) && o.Owner != null)
            .Select(o => new { o.UnitId, o.IsPrimary, o.CreatedAtUtc, Name = o.Owner!.FullName, Doc = o.Owner.DocumentNumber })
            .ToListAsync(cancellationToken);

        var comprobantes = await dbContext.ExpenseCharges.AsNoTracking()
            .Where(c => !c.IsDeleted && unitIds.Contains(c.UnitId) && periodIds.Contains(c.ExpensePeriodId))
            .GroupBy(c => new { c.UnitId, c.ExpensePeriodId })
            .Select(g => new { g.Key.UnitId, g.Key.ExpensePeriodId, Total = g.Sum(c => c.Amount) })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r =>
        {
            var lines = JsonSerializer.Deserialize<List<InvoiceLineDto>>(r.DetalleSnapshotJson) ?? new();
            var settlement = settlements.FirstOrDefault(s => s.ExpensePeriodId == r.ExpensePeriodId);
            var ownerPayment = (r.OwnerPaymentId.HasValue ? ownerPayments.FirstOrDefault(o => o.Id == r.OwnerPaymentId.Value) : null)
                               ?? ownerPayments.FirstOrDefault(o => o.Reference == r.PaymentReference);
            var client = owners.Where(o => o.UnitId == r.UnitId).OrderByDescending(o => o.IsPrimary).ThenBy(o => o.CreatedAtUtc).FirstOrDefault();
            var comprobante = comprobantes.FirstOrDefault(c => c.UnitId == r.UnitId && c.ExpensePeriodId == r.ExpensePeriodId);

            return new InvoiceLedgerRowDto
            {
                Id = r.Id, Status = r.Status, Numero = r.Numero, NumeroFormateado = r.NumeroFormateado, MontoTotal = r.MontoTotal,
                FechaEmisionUtc = r.FechaEmisionUtc, FechaAnulacionUtc = r.FechaAnulacionUtc, MotivoAnulacion = r.MotivoAnulacion,
                CreatedAtUtc = r.CreatedAtUtc,
                LineCount = lines.Count,
                MoraTotal = lines.Where(l => l.Concepto != null && l.Concepto.StartsWith("Mora", StringComparison.OrdinalIgnoreCase)).Sum(l => l.Monto),
                SeriesRazonSocial = r.SeriesRazonSocial, SeriesRuc = r.SeriesRuc, SeriesNumeroTimbrado = r.SeriesNumeroTimbrado,
                SeriesEstablecimiento = r.SeriesEstablecimiento, SeriesPuntoExpedicion = r.SeriesPuntoExpedicion,
                BuildingId = r.BuildingId, BuildingName = r.BuildingName, UnitId = r.UnitId, UnitCode = r.UnitCode,
                ClienteNombre = client?.Name, ClienteDocumento = client?.Doc,
                ExpensePeriodId = r.ExpensePeriodId, PeriodYear = r.PeriodYear, PeriodMonth = r.PeriodMonth,
                PeriodName = r.PeriodName, PeriodStatus = r.PeriodStatus.ToString(), PeriodDueDate = r.PeriodDueDate,
                ComprobanteTotal = comprobante?.Total ?? 0m,
                LiquidationStatus = settlement?.Status.ToString(),
                LiquidationApprovedAtUtc = settlement?.ApprovedAtUtc, LiquidationApprovedBy = settlement?.ApprovedBy,
                LiquidationPublishedAtUtc = settlement?.PublishedAtUtc, LiquidationPublishedBy = settlement?.PublishedBy,
                PaymentId = r.PaymentId, PaymentReference = r.PaymentReference, PaymentDate = r.PaymentDate, PaymentAmount = r.PaymentAmount,
                OwnerPaymentId = ownerPayment?.Id ?? r.OwnerPaymentId,
                OwnerPaymentReference = ownerPayment?.Reference, OwnerPaymentStatus = ownerPayment?.Status.ToString(),
                OwnerName = ownerPayment?.OwnerName, OwnerPaymentReviewedBy = ownerPayment?.ReviewedBy,
                OwnerPaymentResolvedAtUtc = ownerPayment?.ResolvedAt
            };
        }).ToList();

        return Ok(new InvoiceLedgerDto { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize, Summary = summary });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var row = await LoadRowAsync(id, cancellationToken);
        if (row is null) return NotFound();

        if (!await accessScope.CanAccessBuildingAsync(row.BuildingId, cancellationToken)) return Forbid();

        return Ok(ToDto(row));
    }

    [HttpPost("draft")]
    public async Task<ActionResult<InvoiceDto>> CreateDraft([FromBody] CreateInvoiceDraftRequest request, CancellationToken cancellationToken)
    {
        if (!CanManageInvoices()) return Forbid();

        if (request.PaymentId == Guid.Empty)
        {
            return BadRequest("El pago es obligatorio.");
        }

        var payment = await dbContext.Payments
            .AsNoTracking()
            .Include(x => x.Unit)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.PaymentId, cancellationToken);

        if (payment is null) return BadRequest("El pago no existe.");
        if (payment.IsReversed) return BadRequest("No se puede facturar un pago revertido.");
        if (payment.Unit is null) return BadRequest("El pago no tiene una unidad válida.");

        if (!await accessScope.CanAccessBuildingAsync(payment.Unit.BuildingId, cancellationToken)) return Forbid();

        var alreadyInvoiced = await dbContext.Invoices.AnyAsync(x =>
            !x.IsDeleted && x.PaymentId == request.PaymentId && x.Status != InvoiceStatus.Voided, cancellationToken);

        if (alreadyInvoiced)
        {
            return Conflict("Este pago ya tiene una factura vigente (borrador o emitida).");
        }

        var allocations = await dbContext.PaymentAllocations
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.PaymentId == request.PaymentId)
            .Select(a => new InvoiceLineDto
            {
                Concepto = a.Charge != null ? a.Charge.Concept : "Cargo",
                ChargeType = a.Charge != null ? a.Charge.ChargeType : (ExpenseChargeType?)null,
                Monto = a.AllocatedAmount
            })
            .ToListAsync(cancellationToken);

        var allocatedTotal = allocations.Sum(a => a.Monto);
        var remainder = payment.Amount - allocatedTotal;
        if (remainder > 0.01m)
        {
            allocations.Add(new InvoiceLineDto { Concepto = "Saldo a cuenta / crédito", Monto = remainder });
        }

        var entity = new Invoice
        {
            CompanyId = payment.CompanyId,
            BuildingId = payment.Unit.BuildingId,
            UnitId = payment.UnitId,
            PaymentId = payment.Id,
            Status = InvoiceStatus.Draft,
            MontoTotal = payment.Amount,
            DetalleSnapshotJson = JsonSerializer.Serialize(allocations),
            CreatedByUserId = tenantContext.UserId
        };

        dbContext.Invoices.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await LogAsync(entity.Id, entity.CompanyId, InvoiceAuditAction.DraftCreated, before: null,
            after: new { entity.Id, entity.PaymentId, entity.MontoTotal, entity.Status },
            detalle: $"Borrador creado a partir del pago {entity.PaymentId}.", cancellationToken);

        var row = await LoadRowAsync(entity.Id, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(row!));
    }

    // Un pago de propietario aprobado cubre uno o más comprobantes completos (unidad + periodo). Cada
    // comprobante queda registrado como un pago; se prepara un borrador de factura por comprobante
    // (capital y mora), ligado al pago aprobado: unidad, comprobante, pago y factura quedan enlazados.
    [HttpPost("draft-from-owner-payment")]
    public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> CreateDraftsFromOwnerPayment(
        [FromBody] CreateOwnerPaymentInvoiceDraftsRequest request, CancellationToken cancellationToken)
    {
        if (!CanManageInvoices()) return Forbid();
        if (request.OwnerPaymentId == Guid.Empty) return BadRequest("El pago es obligatorio.");

        var ownerPayment = await dbContext.OwnerPayments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.OwnerPaymentId, cancellationToken);

        if (ownerPayment is null) return BadRequest("El pago no existe.");
        if (!accessScope.IsSuperAdmin && ownerPayment.CompanyId != tenantContext.CompanyId) return Forbid();
        if (ownerPayment.Status != OwnerPaymentStatus.Approved)
            return BadRequest("Solo se pueden facturar pagos aprobados.");

        var created = await draftService.CreateDraftsFromOwnerPaymentAsync(ownerPayment, tenantContext.UserId, cancellationToken);

        if (created.Count == 0)
            return Conflict("Este pago no tiene comprobantes pendientes de facturar (ya se generaron antes, o no se aplicó a ningún cargo).");

        var result = new List<InvoiceDto>();
        foreach (var id in created)
        {
            var row = await LoadRowAsync(id, cancellationToken);
            if (row is not null) result.Add(ToDto(row));
        }

        return Ok(result);
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid id, CancellationToken cancellationToken)
    {
        // La URL es siempre la misma para la misma factura; sin esto el navegador puede servir una version
        // cacheada vieja (p.ej. con posiciones de calibracion desactualizadas) en vez de pedir una nueva.
        Response.Headers.CacheControl = "no-store";

        var row = await LoadRowAsync(id, cancellationToken);
        if (row is null) return NotFound();

        // Personal del edificio, o el propietario/residente de la unidad (solo facturas emitidas).
        var isStaff = await accessScope.CanAccessBuildingAsync(row.BuildingId, cancellationToken);
        if (!isStaff && !(row.Status == InvoiceStatus.Issued && await IsLinkedToUnitAsync(row.UnitId, cancellationToken)))
            return Forbid();

        var dto = ToDto(row);
        (dto.ClienteNombre, dto.ClienteDocumento) = await LoadClientAsync(dto.UnitId, cancellationToken);
        var buildingTemplate = await dbContext.Buildings.AsNoTracking()
            .Where(x => x.Id == dto.BuildingId)
            .Select(x => new { x.UseStandardTemplates, x.InvoiceTemplateUrl })
            .FirstOrDefaultAsync(cancellationToken);
        var standardTemplate = buildingTemplate?.UseStandardTemplates ?? true;

        if (row.PeriodId.HasValue)
        {
            dto.BuildingOrdinaryTotal = await dbContext.ExpenseCharges
                .AsNoTracking()
                .Where(x => !x.IsDeleted && !x.IsReversal
                            && x.ExpensePeriodId == row.PeriodId.Value
                            && x.ChargeType == ExpenseChargeType.Ordinary)
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        }

        // La plantilla propia del edificio (cargada por el superadmin) manda sobre el escaneo de
        // calibracion del timbrado — este ultimo queda solo como respaldo si el edificio no tiene una.
        var backgroundImage = TryReadReferenceScan(buildingTemplate?.InvoiceTemplateUrl)
            ?? TryReadReferenceScan(row.SeriesReferenceScanUrl);
        // Con papel propio (imagen de fondo) el texto va en negro simple, sin los colores de marca de
        // CONDOPY — esos solo tienen sentido sobre la plantilla estandar del sistema.
        var document = new InvoicePdfDocument(dto, standardTemplate: backgroundImage is null && standardTemplate, backgroundImage);
        var pdfBytes = document.GeneratePdf();

        await LogAsync(dto.Id, dto.CompanyId, InvoiceAuditAction.Printed, before: null,
            after: new { dto.Status, dto.Numero },
            detalle: dto.Status == InvoiceStatus.Issued ? "Reimpresión de factura emitida." : "Impresión de vista previa (borrador).",
            cancellationToken);

        var fileNameSuffix = dto.Status == InvoiceStatus.Issued && dto.NumeroFormateado is not null
            ? dto.NumeroFormateado.Replace("-", "_")
            : $"borrador_{dto.Id.ToString()[..8]}";
        var fileName = $"factura_{dto.UnitCode}_{fileNameSuffix}.pdf";

        return File(pdfBytes, "application/pdf", fileName);
    }

    [HttpPost("{id:guid}/emit")]
    public async Task<ActionResult<InvoiceDto>> Emit(Guid id, [FromBody] EmitInvoiceRequest request, CancellationToken cancellationToken)
    {
        if (!CanManageInvoices()) return Forbid();

        var invoice = await dbContext.Invoices.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (invoice is null) return NotFound();

        if (!await accessScope.CanAccessBuildingAsync(invoice.BuildingId, cancellationToken)) return Forbid();

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return Conflict("Solo se puede emitir una factura que está en borrador.");
        }

        var series = await dbContext.InvoiceSeries
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.InvoiceSeriesId, cancellationToken);

        if (series is null) return BadRequest("El timbrado no existe.");
        if (series.DocumentType != InvoiceSeriesDocumentType.Invoice)
            return BadRequest("El timbrado seleccionado no está registrado para facturas.");
        if (series.BuildingId != invoice.BuildingId) return BadRequest("El timbrado no corresponde al edificio de la factura.");

        var beforeSnapshot = new { invoice.Status, invoice.Numero };

        var exhausted = false;

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            exhausted = false;

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var connection = dbContext.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                UPDATE InvoiceSeries
                SET CorrelativoActual = CorrelativoActual + 1
                OUTPUT INSERTED.CorrelativoActual
                WHERE Id = @seriesId
                  AND IsDeleted = 0
                  AND Activo = 1
                  AND CorrelativoActual < RangoHasta
                  AND CAST(GETUTCDATE() AS date) BETWEEN VigenciaDesde AND VigenciaHasta
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@seriesId";
            parameter.Value = request.InvoiceSeriesId;
            command.Parameters.Add(parameter);

            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            if (scalar is null or DBNull)
            {
                await transaction.RollbackAsync(cancellationToken);
                exhausted = true;
                return;
            }

            var numero = Convert.ToInt64(scalar);

            var tracked = await dbContext.Invoices.FirstAsync(x => x.Id == id, cancellationToken);
            tracked.InvoiceSeriesId = series.Id;
            tracked.Numero = numero;
            tracked.NumeroFormateado = $"{series.Establecimiento}-{series.PuntoExpedicion}-{numero:D7}";
            tracked.Status = InvoiceStatus.Issued;
            tracked.FechaEmisionUtc = DateTime.UtcNow;

            dbContext.InvoiceAuditLogs.Add(new InvoiceAuditLog
            {
                CompanyId = tracked.CompanyId,
                InvoiceId = tracked.Id,
                InvoiceSeriesId = series.Id,
                Action = InvoiceAuditAction.Issued,
                UserId = tenantContext.UserId,
                TimestampUtc = DateTime.UtcNow,
                DatosAntesJson = JsonSerializer.Serialize(beforeSnapshot),
                DatosDespuesJson = JsonSerializer.Serialize(new { tracked.Status, tracked.Numero, tracked.NumeroFormateado }),
                Detalle = $"Factura emitida con el número {tracked.NumeroFormateado}."
            });

            await AddInvoiceIssuedNotificationAsync(tracked, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        if (exhausted)
        {
            return Conflict("El timbrado está agotado, vencido o inactivo. Cargue un nuevo timbrado antes de emitir.");
        }

        var row = await LoadRowAsync(id, cancellationToken);
        return Ok(ToDto(row!));
    }

    [HttpPost("{id:guid}/void")]
    public async Task<ActionResult<InvoiceDto>> Void(Guid id, [FromBody] VoidInvoiceRequest request, CancellationToken cancellationToken)
    {
        if (!CanManageInvoices()) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Motivo) || request.Motivo.Trim().Length > 500)
        {
            return BadRequest("El motivo de anulación es obligatorio (máximo 500 caracteres).");
        }

        var invoice = await dbContext.Invoices.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (invoice is null) return NotFound();

        if (!await accessScope.CanAccessBuildingAsync(invoice.BuildingId, cancellationToken)) return Forbid();

        if (invoice.Status != InvoiceStatus.Issued)
        {
            return Conflict("Solo se puede anular una factura emitida.");
        }

        var beforeSnapshot = new { invoice.Status, invoice.Numero, invoice.NumeroFormateado };

        invoice.Status = InvoiceStatus.Voided;
        invoice.FechaAnulacionUtc = DateTime.UtcNow;
        invoice.MotivoAnulacion = request.Motivo.Trim();

        var successor = new Invoice
        {
            CompanyId = invoice.CompanyId,
            BuildingId = invoice.BuildingId,
            UnitId = invoice.UnitId,
            PaymentId = invoice.PaymentId,
            OwnerPaymentId = invoice.OwnerPaymentId,
            Status = InvoiceStatus.Draft,
            MontoTotal = invoice.MontoTotal,
            DetalleSnapshotJson = invoice.DetalleSnapshotJson,
            CreatedByUserId = tenantContext.UserId
        };

        dbContext.Invoices.Add(successor);
        await dbContext.SaveChangesAsync(cancellationToken);

        invoice.ReemplazadaPorInvoiceId = successor.Id;
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.InvoiceAuditLogs.Add(new InvoiceAuditLog
        {
            CompanyId = invoice.CompanyId,
            InvoiceId = invoice.Id,
            InvoiceSeriesId = invoice.InvoiceSeriesId,
            Action = InvoiceAuditAction.Voided,
            UserId = tenantContext.UserId,
            TimestampUtc = DateTime.UtcNow,
            DatosAntesJson = JsonSerializer.Serialize(beforeSnapshot),
            DatosDespuesJson = JsonSerializer.Serialize(new { invoice.Status, invoice.MotivoAnulacion, SuccessorId = successor.Id }),
            Detalle = $"Factura {invoice.NumeroFormateado} anulada. Motivo: {invoice.MotivoAnulacion}. Sucesora: {successor.Id}."
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var row = await LoadRowAsync(invoice.Id, cancellationToken);
        return Ok(ToDto(row!));
    }

    private async Task<bool> IsLinkedToUnitAsync(Guid unitId, CancellationToken cancellationToken)
    {
        var userId = tenantContext.UserId;

        if (await dbContext.UnitOwners.AsNoTracking()
                .AnyAsync(x => !x.IsDeleted && x.UnitId == unitId && x.OwnerId == userId, cancellationToken))
            return true;

        return await dbContext.UnitResidents.AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.UnitId == unitId && x.EndDate == null
                           && x.Resident != null && !x.Resident.IsDeleted && x.Resident.ApplicationUserId == userId, cancellationToken);
    }

    // Cliente de la factura: propietario principal de la unidad (o el primero); si no hay, el residente actual.
    private async Task<(string? Nombre, string? Documento)> LoadClientAsync(Guid unitId, CancellationToken cancellationToken)
    {
        var owner = await dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.UnitId == unitId && x.Owner != null)
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.CreatedAtUtc)
            .Select(x => new { x.Owner!.FullName, x.Owner.DocumentNumber })
            .FirstOrDefaultAsync(cancellationToken);
        if (owner is not null) return (owner.FullName, owner.DocumentNumber);

        var resident = await dbContext.UnitResidents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.UnitId == unitId && x.EndDate == null && x.Resident != null)
            .Select(x => new { x.Resident!.FullName, x.Resident.DocumentNumber })
            .FirstOrDefaultAsync(cancellationToken);
        return (resident?.FullName, resident?.DocumentNumber);
    }

    private bool CanManageInvoices() =>
        accessScope.IsSuperAdmin ||
        accessScope.IsCompanyAdmin ||
        string.Equals(tenantContext.Role, "BuildingManager", StringComparison.OrdinalIgnoreCase);

    // Notifica al propietario cuando su factura pasa a Emitida. El comprobante (OwnerPayment) destino de la
    // notificación se resuelve directo si la factura lo tiene enlazado; si no (facturas creadas por /draft,
    // no por /draft-from-owner-payment), se busca por Payment.Reference, mismo patrón que OwnerPaymentsController.GetInvoices.
    private async Task AddInvoiceIssuedNotificationAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        Guid? ownerPaymentId = invoice.OwnerPaymentId;
        Guid? recipientId = null;
        string reference = string.Empty;

        if (ownerPaymentId.HasValue)
        {
            var ownerPayment = await dbContext.OwnerPayments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == ownerPaymentId.Value, cancellationToken);
            recipientId = ownerPayment?.OwnerId;
            reference = ownerPayment?.Reference ?? string.Empty;
        }
        else
        {
            var payment = await dbContext.Payments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == invoice.PaymentId, cancellationToken);
            if (payment is not null)
            {
                var ownerPayment = await dbContext.OwnerPayments.AsNoTracking()
                    .Where(x => !x.IsDeleted && x.CompanyId == invoice.CompanyId && x.Reference == payment.Reference)
                    .OrderByDescending(x => x.CreatedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);
                ownerPaymentId = ownerPayment?.Id;
                recipientId = ownerPayment?.OwnerId;
                reference = ownerPayment?.Reference ?? string.Empty;
            }
        }

        if (recipientId is null) return;

        dbContext.Notifications.Add(new Notification
        {
            CompanyId = invoice.CompanyId,
            RecipientId = recipientId.Value,
            Type = NotificationType.InvoiceIssued,
            Title = "Tu factura fue emitida",
            Body = $"Se emitió la factura {invoice.NumeroFormateado} para el comprobante {reference}. Toca para descargarla.",
            EntityType = "OwnerPayment",
            EntityId = ownerPaymentId
        });
    }

    private async Task LogAsync(Guid invoiceId, Guid companyId, InvoiceAuditAction action, object? before, object? after, string detalle, CancellationToken cancellationToken)
    {
        dbContext.InvoiceAuditLogs.Add(new InvoiceAuditLog
        {
            CompanyId = companyId,
            InvoiceId = invoiceId,
            Action = action,
            UserId = tenantContext.UserId,
            TimestampUtc = DateTime.UtcNow,
            DatosAntesJson = before is null ? null : JsonSerializer.Serialize(before),
            DatosDespuesJson = after is null ? null : JsonSerializer.Serialize(after),
            Detalle = detalle
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<InvoiceRow?> LoadRowAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.Invoices
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => new InvoiceRow(
                x.Id, x.CompanyId, x.BuildingId, x.Building != null ? x.Building.Name : string.Empty,
                x.UnitId, x.Unit != null ? x.Unit.Code : string.Empty,
                x.PaymentId, x.InvoiceSeriesId,
                x.Series != null ? x.Series.RazonSocial : null,
                x.Series != null ? x.Series.Ruc : null,
                x.Series != null ? x.Series.NumeroTimbrado : null,
                x.Series != null ? x.Series.Establecimiento : null,
                x.Series != null ? x.Series.PuntoExpedicion : null,
                x.Series != null ? x.Series.VigenciaDesde : null,
                x.Series != null ? x.Series.VigenciaHasta : null,
                x.Building != null ? x.Building.Address : null,
                x.Building != null ? ((x.Building.ContactPhonePrefix ?? "") + " " + (x.Building.ContactPhone ?? "")).Trim() : null,
                x.Status, x.Numero, x.NumeroFormateado, x.MontoTotal, x.DetalleSnapshotJson,
                x.FechaEmisionUtc, x.FechaAnulacionUtc, x.MotivoAnulacion, x.ReemplazadaPorInvoiceId, x.CreatedAtUtc,
                x.Payment != null && x.Payment.ExpensePeriod != null ? x.Payment.ExpensePeriod.DueDate : (DateOnly?)null,
                x.Payment != null ? x.Payment.ExpensePeriodId : (Guid?)null,
                x.Payment != null && x.Payment.ExpensePeriod != null ? x.Payment.ExpensePeriod.Year : (int?)null,
                x.Payment != null && x.Payment.ExpensePeriod != null ? x.Payment.ExpensePeriod.Month : (int?)null,
                x.Unit != null ? x.Unit.Coefficient : 0m,
                x.Series != null ? x.Series.FieldPositionsJson : null,
                x.Series != null && x.Series.HideFrame,
                x.Series != null ? x.Series.ReferenceScanUrl : null))
            .FirstOrDefaultAsync(cancellationToken);

    private static InvoiceDto ToDto(InvoiceRow row) => new()
    {
        Id = row.Id,
        CompanyId = row.CompanyId,
        BuildingId = row.BuildingId,
        BuildingName = row.BuildingName,
        UnitId = row.UnitId,
        UnitCode = row.UnitCode,
        PaymentId = row.PaymentId,
        InvoiceSeriesId = row.InvoiceSeriesId,
        SeriesRazonSocial = row.SeriesRazonSocial,
        SeriesRuc = row.SeriesRuc,
        SeriesNumeroTimbrado = row.SeriesNumeroTimbrado,
        SeriesEstablecimiento = row.SeriesEstablecimiento,
        SeriesPuntoExpedicion = row.SeriesPuntoExpedicion,
        SeriesVigenciaDesde = row.SeriesVigenciaDesde,
        SeriesVigenciaHasta = row.SeriesVigenciaHasta,
        BuildingAddress = row.BuildingAddress,
        BuildingPhone = row.BuildingPhone,
        PeriodDueDate = row.PeriodDueDate,
        PeriodYear = row.PeriodYear,
        PeriodMonth = row.PeriodMonth,
        UnitCoefficient = row.UnitCoefficient,
        FieldPositionsJson = row.FieldPositionsJson,
        HideFrame = row.HideFrame,
        Status = row.Status,
        Numero = row.Numero,
        NumeroFormateado = row.NumeroFormateado,
        MontoTotal = row.MontoTotal,
        Detalle = JsonSerializer.Deserialize<List<InvoiceLineDto>>(row.DetalleSnapshotJson) ?? new(),
        FechaEmisionUtc = row.FechaEmisionUtc,
        FechaAnulacionUtc = row.FechaAnulacionUtc,
        MotivoAnulacion = row.MotivoAnulacion,
        ReemplazadaPorInvoiceId = row.ReemplazadaPorInvoiceId,
        CreatedAtUtc = row.CreatedAtUtc
    };

    private sealed record InvoiceRow(
        Guid Id, Guid CompanyId, Guid BuildingId, string BuildingName,
        Guid UnitId, string UnitCode,
        Guid PaymentId, Guid? InvoiceSeriesId,
        string? SeriesRazonSocial, string? SeriesRuc, string? SeriesNumeroTimbrado,
        string? SeriesEstablecimiento, string? SeriesPuntoExpedicion, DateOnly? SeriesVigenciaDesde, DateOnly? SeriesVigenciaHasta,
        string? BuildingAddress, string? BuildingPhone,
        InvoiceStatus Status, long? Numero, string? NumeroFormateado, decimal MontoTotal, string DetalleSnapshotJson,
        DateTime? FechaEmisionUtc, DateTime? FechaAnulacionUtc, string? MotivoAnulacion, Guid? ReemplazadaPorInvoiceId, DateTime CreatedAtUtc,
        DateOnly? PeriodDueDate, Guid? PeriodId, int? PeriodYear, int? PeriodMonth, decimal UnitCoefficient,
        string? FieldPositionsJson, bool HideFrame, string? SeriesReferenceScanUrl);

    // Mismo mecanismo que la calibracion del timbrado (InvoiceSeriesController.TryReadReferenceScan): si el
    // timbrado tiene un escaneo de referencia calibrado, la factura real tambien se imprime sobre ese papel.
    private byte[]? TryReadReferenceScan(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var fileName = Path.GetFileName(Uri.TryCreate(url, UriKind.Absolute, out var abs) ? abs.AbsolutePath : url);

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif")) return null;

        var path = Path.Combine(env.WebRootPath, "uploads", fileName);
        return System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
    }
}
