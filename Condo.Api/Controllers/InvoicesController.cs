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
public class InvoicesController(ICondoDbContext dbContext, IAccessScopeService accessScope, ITenantContext tenantContext) : ControllerBase
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
                x.Status, x.Numero, x.NumeroFormateado, x.MontoTotal, x.DetalleSnapshotJson,
                x.FechaEmisionUtc, x.FechaAnulacionUtc, x.MotivoAnulacion, x.ReemplazadaPorInvoiceId, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(rows.Select(ToDto).ToList());
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

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid id, CancellationToken cancellationToken)
    {
        var row = await LoadRowAsync(id, cancellationToken);
        if (row is null) return NotFound();

        if (!await accessScope.CanAccessBuildingAsync(row.BuildingId, cancellationToken)) return Forbid();

        var dto = ToDto(row);
        var document = new InvoicePdfDocument(dto);
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

    private bool CanManageInvoices() =>
        accessScope.IsSuperAdmin ||
        accessScope.IsCompanyAdmin ||
        string.Equals(tenantContext.Role, "BuildingManager", StringComparison.OrdinalIgnoreCase);

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
                x.Status, x.Numero, x.NumeroFormateado, x.MontoTotal, x.DetalleSnapshotJson,
                x.FechaEmisionUtc, x.FechaAnulacionUtc, x.MotivoAnulacion, x.ReemplazadaPorInvoiceId, x.CreatedAtUtc))
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
        InvoiceStatus Status, long? Numero, string? NumeroFormateado, decimal MontoTotal, string DetalleSnapshotJson,
        DateTime? FechaEmisionUtc, DateTime? FechaAnulacionUtc, string? MotivoAnulacion, Guid? ReemplazadaPorInvoiceId, DateTime CreatedAtUtc);
}
