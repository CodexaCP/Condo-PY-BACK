using System.Text.Json;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Condo.Api.Controllers;

// Nota de credito interna: ajuste sobre una factura ya emitida. Nunca modifica ni reemplaza la
// factura original — se crea en borrador (sin efecto en el saldo), y recien al aprobarse (rol
// superior al que la crea) genera los ExpenseCharge de reverso que bajan el saldo real. El
// documento fiscal oficial de la NC (papel o electronico) se registra aparte, en cualquier momento.
[ApiController]
[Authorize]
[Route("api/credit-notes")]
public class CreditNotesController(
    ICondoDbContext dbContext, IAccessScopeService accessScope, ITenantContext tenantContext, OwnerCreditService credits) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CreditNoteDto>>> GetAll(
        [FromQuery] Guid? invoiceId,
        [FromQuery] Guid? buildingId,
        [FromQuery] CreditNoteStatus? status,
        CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.CreditNotes.AsNoTracking().Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
                query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            else
                query = query.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
        }

        if (invoiceId.HasValue) query = query.Where(x => x.InvoiceId == invoiceId.Value);
        if (buildingId.HasValue) query = query.Where(x => x.BuildingId == buildingId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => ToRow(x))
            .ToListAsync(cancellationToken);

        return Ok(rows.Select(r => ToDto(r, [], [])).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CreditNoteDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var row = await LoadRowAsync(id, cancellationToken);
        if (row is null) return NotFound();

        if (!await accessScope.CanAccessBuildingAsync(row.BuildingId, cancellationToken)) return Forbid();

        var lines = await LoadLinesAsync(id, cancellationToken);
        var attachments = await LoadAttachmentsAsync(id, cancellationToken);
        return Ok(ToDto(row, lines, attachments));
    }

    // Cargos del comprobante de esta factura que todavia admiten ajuste (para armar el formulario
    // de "nueva NC" en el front sin que tenga que adivinar los ExpenseChargeId).
    [HttpGet("adjustable-charges")]
    public async Task<ActionResult<IReadOnlyList<AdjustableChargeDto>>> GetAdjustableCharges(
        [FromQuery] Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await dbContext.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == invoiceId, cancellationToken);
        if (invoice is null) return NotFound();
        if (!await accessScope.CanAccessBuildingAsync(invoice.BuildingId, cancellationToken)) return Forbid();

        var charges = await dbContext.PaymentAllocations
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.PaymentId == invoice.PaymentId && a.Charge != null && !a.Charge.IsReversal)
            .Select(a => a.Charge!)
            .Distinct()
            .ToListAsync(cancellationToken);

        var result = new List<AdjustableChargeDto>();
        foreach (var charge in charges)
        {
            var alreadyAdjusted = await dbContext.CreditNoteLines.AsNoTracking()
                .Where(l => !l.IsDeleted && l.ExpenseChargeId == charge.Id && l.CreditNote!.Status == CreditNoteStatus.Approved)
                .SumAsync(l => (decimal?)l.Amount, cancellationToken) ?? 0m;
            var alreadyPaid = await dbContext.PaymentAllocations.AsNoTracking()
                .Where(a => !a.IsDeleted && a.ExpenseChargeId == charge.Id && a.Payment != null && !a.Payment.IsReversed)
                .SumAsync(a => (decimal?)a.AllocatedAmount, cancellationToken) ?? 0m;

            result.Add(new AdjustableChargeDto
            {
                ExpenseChargeId = charge.Id,
                Concept = charge.Concept,
                ChargeType = charge.ChargeType,
                Amount = charge.Amount,
                AlreadyAdjusted = alreadyAdjusted,
                Adjustable = charge.Amount - alreadyAdjusted,
                AlreadyPaid = alreadyPaid
            });
        }

        return Ok(result.Where(x => x.Adjustable > 0).OrderBy(x => x.Concept).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<CreditNoteDto>> Create([FromBody] CreateCreditNoteRequest request, CancellationToken cancellationToken)
    {
        if (!CanCreateCreditNotes()) return Forbid();

        var motivo = request.Motivo?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500)
            return BadRequest("El motivo es obligatorio (máximo 500 caracteres).");

        if (request.Lines is null || request.Lines.Count == 0)
            return BadRequest("La nota de crédito debe tener al menos una línea de ajuste.");

        var invoice = await dbContext.Invoices
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.InvoiceId, cancellationToken);
        if (invoice is null) return BadRequest("La factura no existe.");
        if (invoice.Status != InvoiceStatus.Issued)
            return BadRequest("Solo se puede emitir una nota de crédito sobre una factura emitida.");

        if (!await accessScope.CanAccessBuildingAsync(invoice.BuildingId, cancellationToken)) return Forbid();

        // Los cargos ajustables son los que efectivamente componen el comprobante de esta factura
        // (los que su Payment cubrio), no cualquier cargo de la unidad/periodo.
        var validChargeIds = (await dbContext.PaymentAllocations
                .AsNoTracking()
                .Where(a => !a.IsDeleted && a.PaymentId == invoice.PaymentId)
                .Select(a => a.ExpenseChargeId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var lineErrors = new List<string>();
        var newLines = new List<CreditNoteLine>();
        decimal total = 0;

        foreach (var lineRequest in request.Lines)
        {
            if (!validChargeIds.Contains(lineRequest.ExpenseChargeId))
            {
                lineErrors.Add($"El cargo {lineRequest.ExpenseChargeId} no pertenece al comprobante de esta factura.");
                continue;
            }

            if (lineRequest.Amount <= 0)
            {
                lineErrors.Add("El importe de cada línea debe ser mayor a cero.");
                continue;
            }

            var (error, _) = await ValidateLineAmountAsync(lineRequest.ExpenseChargeId, lineRequest.Amount, null, cancellationToken);
            if (error is not null)
            {
                lineErrors.Add(error);
                continue;
            }

            newLines.Add(new CreditNoteLine
            {
                CompanyId = invoice.CompanyId,
                ExpenseChargeId = lineRequest.ExpenseChargeId,
                Amount = lineRequest.Amount,
                Concept = string.IsNullOrWhiteSpace(lineRequest.Concept) ? null : lineRequest.Concept.Trim()
            });
            total += lineRequest.Amount;
        }

        if (lineErrors.Count > 0) return BadRequest(string.Join(" ", lineErrors));

        var creditNote = new CreditNote
        {
            CompanyId = invoice.CompanyId,
            BuildingId = invoice.BuildingId,
            UnitId = invoice.UnitId,
            InvoiceId = invoice.Id,
            Motivo = motivo,
            Amount = total,
            Status = CreditNoteStatus.Draft,
            CreatedByUserId = tenantContext.UserId
        };
        foreach (var line in newLines) creditNote.Lines.Add(line);

        dbContext.CreditNotes.Add(creditNote);
        await dbContext.SaveChangesAsync(cancellationToken);

        await LogAsync(creditNote.Id, creditNote.CompanyId, CreditNoteAuditAction.Created, before: null,
            after: new { creditNote.Id, creditNote.InvoiceId, creditNote.Amount, creditNote.Status },
            detalle: $"Borrador creado sobre la factura {invoice.NumeroFormateado ?? invoice.Id.ToString()}. Motivo: {motivo}.",
            cancellationToken);

        var row = await LoadRowAsync(creditNote.Id, cancellationToken);
        var lines = await LoadLinesAsync(creditNote.Id, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = creditNote.Id }, ToDto(row!, lines, []));
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<CreditNoteDto>> Approve(Guid id, CancellationToken cancellationToken)
    {
        if (!CanApproveCreditNotes()) return Forbid();

        var creditNote = await dbContext.CreditNotes
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (creditNote is null) return NotFound();

        if (!await accessScope.CanAccessBuildingAsync(creditNote.BuildingId, cancellationToken)) return Forbid();
        if (creditNote.Status != CreditNoteStatus.Draft)
            return Conflict("Solo se puede aprobar una nota de crédito en borrador.");

        // Revalidar contra el estado actual: pudo haber cambiado desde que se armo el borrador
        // (otra NC aprobada sobre el mismo cargo, un pago nuevo aplicado, etc).
        foreach (var line in creditNote.Lines)
        {
            var (error, _) = await ValidateLineAmountAsync(line.ExpenseChargeId, line.Amount, creditNote.Id, cancellationToken);
            if (error is not null) return Conflict(error);
        }

        var beforeSnapshot = new { creditNote.Status };

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var tracked = await dbContext.CreditNotes.Include(x => x.Lines)
                .FirstAsync(x => x.Id == id, cancellationToken);

            var totalExcess = 0m;

            foreach (var line in tracked.Lines)
            {
                var originalCharge = await dbContext.ExpenseCharges
                    .FirstAsync(x => x.Id == line.ExpenseChargeId, cancellationToken);

                // El excedente se calcula ANTES de crear el reverso (compara contra lo ya pagado del
                // cargo original), sino GetLineExcessAsync se veria a si misma como "ya ajustado".
                totalExcess += await GetLineExcessAsync(line, cancellationToken);

                dbContext.ExpenseCharges.Add(new ExpenseCharge
                {
                    CompanyId = tracked.CompanyId,
                    ExpensePeriodId = originalCharge.ExpensePeriodId,
                    UnitId = originalCharge.UnitId,
                    ChargeType = ExpenseChargeType.Adjustment,
                    IsLateFee = false,
                    Concept = $"Nota de crédito: {originalCharge.Concept}",
                    Amount = -line.Amount,
                    Notes = line.Concept ?? tracked.Motivo,
                    IsReversal = true,
                    ReversalOfChargeId = originalCharge.Id,
                    SourceCreditNoteId = tracked.Id
                });
            }

            if (totalExcess > 0)
            {
                var ownerId = await GetUnitOwnerIdAsync(tracked.UnitId, cancellationToken);
                if (ownerId.HasValue)
                {
                    var reference = tracked.FiscalNumero ?? tracked.Motivo;
                    await credits.AddCreditNoteExcessLotAsync(ownerId.Value, tracked.CompanyId, totalExcess, tracked.Id, reference, cancellationToken);
                }
            }

            tracked.Status = CreditNoteStatus.Approved;
            tracked.ApprovedAtUtc = DateTime.UtcNow;
            tracked.ApprovedByUserId = tenantContext.UserId;

            dbContext.CreditNoteAuditLogs.Add(new CreditNoteAuditLog
            {
                CompanyId = tracked.CompanyId,
                CreditNoteId = tracked.Id,
                Action = CreditNoteAuditAction.Approved,
                UserId = tenantContext.UserId,
                TimestampUtc = DateTime.UtcNow,
                DatosAntesJson = JsonSerializer.Serialize(beforeSnapshot),
                DatosDespuesJson = JsonSerializer.Serialize(new { tracked.Status, tracked.ApprovedAtUtc }),
                Detalle = $"Nota de crédito aprobada por Gs. {tracked.Amount:N0}."
            });

            await AddCreditNoteApprovedNotificationAsync(tracked, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        var row = await LoadRowAsync(id, cancellationToken);
        var lines = await LoadLinesAsync(id, cancellationToken);
        var attachments = await LoadAttachmentsAsync(id, cancellationToken);
        return Ok(ToDto(row!, lines, attachments));
    }

    // Numeracion automatica con un timbrado registrado como DocumentType=CreditNote (mismo
    // mecanismo que InvoicesController.Emit: correlativo atomico, sin CDC/XML/SIFEN). Es opcional
    // y separado de Approve(): la NC ya aplico su efecto en el saldo al aprobarse; esto solo le
    // asigna el numero fiscal. Convive con el registro manual de PUT .../fiscal-data.
    [HttpPost("{id:guid}/emit")]
    public async Task<ActionResult<CreditNoteDto>> Emit(Guid id, [FromBody] EmitCreditNoteRequest request, CancellationToken cancellationToken)
    {
        if (!CanCreateCreditNotes()) return Forbid();

        var creditNote = await dbContext.CreditNotes.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (creditNote is null) return NotFound();

        if (!await accessScope.CanAccessBuildingAsync(creditNote.BuildingId, cancellationToken)) return Forbid();
        if (creditNote.Status != CreditNoteStatus.Approved)
            return Conflict("Solo se puede numerar una nota de crédito ya aprobada.");
        if (creditNote.Numero.HasValue)
            return Conflict("Esta nota de crédito ya tiene un número fiscal asignado.");

        var series = await dbContext.InvoiceSeries
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.InvoiceSeriesId, cancellationToken);

        if (series is null) return BadRequest("El timbrado no existe.");
        if (series.DocumentType != InvoiceSeriesDocumentType.CreditNote)
            return BadRequest("El timbrado seleccionado no está registrado para notas de crédito.");
        if (series.BuildingId != creditNote.BuildingId)
            return BadRequest("El timbrado no corresponde al edificio de la nota de crédito.");

        var beforeSnapshot = new { creditNote.Numero, creditNote.FiscalNumero };
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

            var tracked = await dbContext.CreditNotes.FirstAsync(x => x.Id == id, cancellationToken);
            tracked.InvoiceSeriesId = series.Id;
            tracked.Numero = numero;
            tracked.FiscalNumero = $"{series.Establecimiento}-{series.PuntoExpedicion}-{numero:D7}";
            tracked.FiscalTimbrado = series.NumeroTimbrado;
            tracked.FiscalFechaEmisionUtc = DateTime.UtcNow;

            dbContext.CreditNoteAuditLogs.Add(new CreditNoteAuditLog
            {
                CompanyId = tracked.CompanyId,
                CreditNoteId = tracked.Id,
                Action = CreditNoteAuditAction.Issued,
                UserId = tenantContext.UserId,
                TimestampUtc = DateTime.UtcNow,
                DatosAntesJson = JsonSerializer.Serialize(beforeSnapshot),
                DatosDespuesJson = JsonSerializer.Serialize(new { tracked.Numero, tracked.FiscalNumero }),
                Detalle = $"Numerada con el timbrado {tracked.FiscalTimbrado} como {tracked.FiscalNumero}."
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        if (exhausted)
            return Conflict("El timbrado está agotado, vencido o inactivo. Cargá un nuevo timbrado antes de emitir.");

        var row = await LoadRowAsync(id, cancellationToken);
        var lines = await LoadLinesAsync(id, cancellationToken);
        var attachments = await LoadAttachmentsAsync(id, cancellationToken);
        return Ok(ToDto(row!, lines, attachments));
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<CreditNoteDto>> Reject(Guid id, [FromBody] RejectCreditNoteRequest request, CancellationToken cancellationToken)
    {
        if (!CanApproveCreditNotes()) return Forbid();

        var motivo = request.Motivo?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500)
            return BadRequest("El motivo de rechazo es obligatorio (máximo 500 caracteres).");

        var creditNote = await dbContext.CreditNotes.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (creditNote is null) return NotFound();

        if (!await accessScope.CanAccessBuildingAsync(creditNote.BuildingId, cancellationToken)) return Forbid();
        if (creditNote.Status != CreditNoteStatus.Draft)
            return Conflict("Solo se puede rechazar una nota de crédito en borrador.");

        var beforeSnapshot = new { creditNote.Status };

        creditNote.Status = CreditNoteStatus.Rejected;
        creditNote.RejectionReason = motivo;
        creditNote.RejectedAtUtc = DateTime.UtcNow;
        creditNote.RejectedByUserId = tenantContext.UserId;
        await dbContext.SaveChangesAsync(cancellationToken);

        await LogAsync(creditNote.Id, creditNote.CompanyId, CreditNoteAuditAction.Rejected, before: beforeSnapshot,
            after: new { creditNote.Status, creditNote.RejectionReason },
            detalle: $"Nota de crédito rechazada. Motivo: {motivo}.", cancellationToken);

        var row = await LoadRowAsync(id, cancellationToken);
        var lines = await LoadLinesAsync(id, cancellationToken);
        return Ok(ToDto(row!, lines, []));
    }

    [HttpPost("{id:guid}/void")]
    public async Task<ActionResult<CreditNoteDto>> Void(Guid id, [FromBody] VoidCreditNoteRequest request, CancellationToken cancellationToken)
    {
        if (!CanApproveCreditNotes()) return Forbid();

        var motivo = request.Motivo?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500)
            return BadRequest("El motivo de anulación es obligatorio (máximo 500 caracteres).");

        var creditNote = await dbContext.CreditNotes.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (creditNote is null) return NotFound();

        if (!await accessScope.CanAccessBuildingAsync(creditNote.BuildingId, cancellationToken)) return Forbid();
        if (creditNote.Status != CreditNoteStatus.Approved)
            return Conflict("Solo se puede anular una nota de crédito aprobada.");

        var beforeSnapshot = new { creditNote.Status };

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var tracked = await dbContext.CreditNotes.FirstAsync(x => x.Id == id, cancellationToken);

            // Contra-reverso de cada cargo que genero esta NC al aprobarse — mismo mecanismo que
            // ExpenseChargesController.Reverse, sin tocar el cargo original ni perder trazabilidad.
            var reversalCharges = await dbContext.ExpenseCharges
                .Where(x => !x.IsDeleted && x.SourceCreditNoteId == tracked.Id)
                .ToListAsync(cancellationToken);

            foreach (var reversal in reversalCharges)
            {
                var alreadyUndone = await dbContext.ExpenseCharges
                    .AnyAsync(x => !x.IsDeleted && x.ReversalOfChargeId == reversal.Id, cancellationToken);
                if (alreadyUndone) continue;

                dbContext.ExpenseCharges.Add(new ExpenseCharge
                {
                    CompanyId = reversal.CompanyId,
                    ExpensePeriodId = reversal.ExpensePeriodId,
                    UnitId = reversal.UnitId,
                    ChargeType = ExpenseChargeType.Adjustment,
                    IsLateFee = false,
                    Concept = $"Anulación de nota de crédito: {reversal.Concept}",
                    Amount = -reversal.Amount,
                    Notes = $"Anula el ajuste de la nota de crédito. Motivo: {motivo}",
                    IsReversal = true,
                    ReversalOfChargeId = reversal.Id,
                    SourceCreditNoteId = tracked.Id
                });
            }

            tracked.Status = CreditNoteStatus.Voided;
            tracked.VoidReason = motivo;
            tracked.VoidedAtUtc = DateTime.UtcNow;
            tracked.VoidedByUserId = tenantContext.UserId;

            dbContext.CreditNoteAuditLogs.Add(new CreditNoteAuditLog
            {
                CompanyId = tracked.CompanyId,
                CreditNoteId = tracked.Id,
                Action = CreditNoteAuditAction.Voided,
                UserId = tenantContext.UserId,
                TimestampUtc = DateTime.UtcNow,
                DatosAntesJson = JsonSerializer.Serialize(beforeSnapshot),
                DatosDespuesJson = JsonSerializer.Serialize(new { tracked.Status, tracked.VoidReason }),
                Detalle = $"Nota de crédito anulada. Motivo: {motivo}."
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        var row = await LoadRowAsync(id, cancellationToken);
        var lines = await LoadLinesAsync(id, cancellationToken);
        var attachments = await LoadAttachmentsAsync(id, cancellationToken);
        return Ok(ToDto(row!, lines, attachments));
    }

    [HttpPut("{id:guid}/fiscal-data")]
    public async Task<ActionResult<CreditNoteDto>> RegisterFiscalData(
        Guid id, [FromBody] RegisterCreditNoteFiscalDataRequest request, CancellationToken cancellationToken)
    {
        if (!CanCreateCreditNotes()) return Forbid();

        var creditNote = await dbContext.CreditNotes.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (creditNote is null) return NotFound();
        if (!await accessScope.CanAccessBuildingAsync(creditNote.BuildingId, cancellationToken)) return Forbid();

        var beforeSnapshot = new
        {
            creditNote.FiscalDocumentType, creditNote.FiscalNumero, creditNote.FiscalTimbrado,
            creditNote.FiscalCdc, creditNote.FiscalFechaEmisionUtc, creditNote.FiscalEstado
        };

        creditNote.FiscalDocumentType = request.DocumentType;
        creditNote.FiscalNumero = Trim(request.Numero, 50);
        creditNote.FiscalTimbrado = Trim(request.Timbrado, 50);
        creditNote.FiscalCdc = Trim(request.Cdc, 100);
        creditNote.FiscalFechaEmisionUtc = request.FechaEmisionUtc;
        creditNote.FiscalEstado = Trim(request.Estado, 100);
        creditNote.FiscalObservaciones = Trim(request.Observaciones, 1000);
        await dbContext.SaveChangesAsync(cancellationToken);

        await LogAsync(creditNote.Id, creditNote.CompanyId, CreditNoteAuditAction.FiscalDataRegistered, before: beforeSnapshot,
            after: new { creditNote.FiscalDocumentType, creditNote.FiscalNumero, creditNote.FiscalTimbrado, creditNote.FiscalCdc },
            detalle: "Datos del documento fiscal oficial registrados/actualizados.", cancellationToken);

        var row = await LoadRowAsync(id, cancellationToken);
        var lines = await LoadLinesAsync(id, cancellationToken);
        var attachments = await LoadAttachmentsAsync(id, cancellationToken);
        return Ok(ToDto(row!, lines, attachments));
    }

    [HttpPost("{id:guid}/attachments")]
    public async Task<ActionResult<CreditNoteAttachmentDto>> AddAttachment(
        Guid id, [FromBody] AddCreditNoteAttachmentRequest request, CancellationToken cancellationToken)
    {
        if (!CanCreateCreditNotes()) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Url) || string.IsNullOrWhiteSpace(request.FileName))
            return BadRequest("La URL y el nombre del archivo son obligatorios.");

        var creditNote = await dbContext.CreditNotes.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (creditNote is null) return NotFound();
        if (!await accessScope.CanAccessBuildingAsync(creditNote.BuildingId, cancellationToken)) return Forbid();

        var attachment = new CreditNoteAttachment
        {
            CompanyId = creditNote.CompanyId,
            CreditNoteId = creditNote.Id,
            Url = request.Url.Trim(),
            FileName = request.FileName.Trim(),
            Kind = request.Kind,
            UploadedByUserId = tenantContext.UserId
        };
        dbContext.CreditNoteAttachments.Add(attachment);
        await dbContext.SaveChangesAsync(cancellationToken);

        await LogAsync(creditNote.Id, creditNote.CompanyId, CreditNoteAuditAction.AttachmentAdded, before: null,
            after: new { attachment.Id, attachment.FileName, attachment.Kind },
            detalle: $"Adjunto agregado: {attachment.FileName}.", cancellationToken);

        return Ok(new CreditNoteAttachmentDto
        {
            Id = attachment.Id,
            Url = attachment.Url,
            FileName = attachment.FileName,
            Kind = attachment.Kind,
            UploadedByUserId = attachment.UploadedByUserId,
            UploadedAtUtc = attachment.UploadedAtUtc
        });
    }

    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DeleteAttachment(Guid id, Guid attachmentId, CancellationToken cancellationToken)
    {
        if (!CanCreateCreditNotes()) return Forbid();

        var creditNote = await dbContext.CreditNotes.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (creditNote is null) return NotFound();
        if (!await accessScope.CanAccessBuildingAsync(creditNote.BuildingId, cancellationToken)) return Forbid();

        var attachment = await dbContext.CreditNoteAttachments
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == attachmentId && x.CreditNoteId == id, cancellationToken);
        if (attachment is null) return NotFound();

        attachment.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        await LogAsync(creditNote.Id, creditNote.CompanyId, CreditNoteAuditAction.AttachmentRemoved, before: null,
            after: new { attachment.Id, attachment.FileName },
            detalle: $"Adjunto eliminado: {attachment.FileName}.", cancellationToken);

        return NoContent();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private bool CanCreateCreditNotes() =>
        accessScope.IsSuperAdmin ||
        accessScope.IsCompanyAdmin ||
        string.Equals(tenantContext.Role, "BuildingManager", StringComparison.OrdinalIgnoreCase);

    // Aprobar/rechazar/anular es lo que efectivamente mueve el saldo — mismo nivel que hoy publica
    // o rechaza la liquidacion de expensas, un escalon por encima de quien arma el borrador.
    private bool CanApproveCreditNotes() =>
        accessScope.IsSuperAdmin || accessScope.IsCompanyAdmin;

    // Verifica que el importe de una linea no exceda lo que todavia se puede ajustar de ese cargo
    // (monto original menos lo ya reducido por otras NC Approved). Si el cargo ya tenia pagos por
    // encima del nuevo monto neto, el excedente NO bloquea la NC: se acredita como saldo a favor
    // del propietario al aprobar (ver GetExcessAsync/Approve), trazado a esta NC.
    private async Task<(string? Error, decimal Adjustable)> ValidateLineAmountAsync(
        Guid expenseChargeId, decimal amount, Guid? excludeCreditNoteId, CancellationToken cancellationToken)
    {
        var charge = await dbContext.ExpenseCharges
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expenseChargeId, cancellationToken);
        if (charge is null) return ($"El cargo {expenseChargeId} no existe.", 0);
        if (charge.IsReversal) return ($"El cargo {expenseChargeId} ya es una reversión, no se puede ajustar con una NC.", 0);

        var alreadyAdjusted = await GetAlreadyAdjustedAsync(expenseChargeId, excludeCreditNoteId, cancellationToken);

        var adjustable = charge.Amount - alreadyAdjusted;
        if (amount > adjustable)
            return ($"El cargo \"{charge.Concept}\" solo admite un ajuste de hasta Gs. {adjustable:N0} (ya tiene Gs. {alreadyAdjusted:N0} ajustados).", adjustable);

        return (null, adjustable);
    }

    private async Task<decimal> GetAlreadyAdjustedAsync(Guid expenseChargeId, Guid? excludeCreditNoteId, CancellationToken cancellationToken) =>
        await dbContext.CreditNoteLines
            .AsNoTracking()
            .Where(l => !l.IsDeleted && l.ExpenseChargeId == expenseChargeId
                        && l.CreditNote!.Status == CreditNoteStatus.Approved
                        && (!excludeCreditNoteId.HasValue || l.CreditNoteId != excludeCreditNoteId.Value))
            .SumAsync(l => (decimal?)l.Amount, cancellationToken) ?? 0m;

    // Cuanto excedente deja una linea aprobada: lo ya pagado del cargo menos lo que queda neto
    // despues de este ajuste (y de los previos ya aprobados). Positivo = hay que acreditarlo.
    private async Task<decimal> GetLineExcessAsync(CreditNoteLine line, CancellationToken cancellationToken)
    {
        var charge = await dbContext.ExpenseCharges.AsNoTracking()
            .FirstAsync(x => x.Id == line.ExpenseChargeId, cancellationToken);
        var alreadyAdjusted = await GetAlreadyAdjustedAsync(line.ExpenseChargeId, line.CreditNoteId, cancellationToken);
        var alreadyPaid = await dbContext.PaymentAllocations.AsNoTracking()
            .Where(a => !a.IsDeleted && a.ExpenseChargeId == line.ExpenseChargeId && a.Payment != null && !a.Payment.IsReversed)
            .SumAsync(a => (decimal?)a.AllocatedAmount, cancellationToken) ?? 0m;

        var netAfter = charge.Amount - alreadyAdjusted - line.Amount;
        return Math.Max(0, alreadyPaid - netAfter);
    }

    // Dueño principal de la unidad, a quien se le acredita el excedente. Igual criterio que ya usan
    // Invoice/AccountStatements para resolver "el propietario" de una unidad.
    private async Task<Guid?> GetUnitOwnerIdAsync(Guid unitId, CancellationToken cancellationToken) =>
        await dbContext.UnitOwners.AsNoTracking()
            .Where(x => !x.IsDeleted && x.UnitId == unitId)
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.CreatedAtUtc)
            .Select(x => (Guid?)x.OwnerId)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task AddCreditNoteApprovedNotificationAsync(CreditNote creditNote, CancellationToken cancellationToken)
    {
        var invoice = await dbContext.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == creditNote.InvoiceId, cancellationToken);
        if (invoice is null) return;

        Guid? ownerPaymentId = invoice.OwnerPaymentId;
        Guid? recipientId = null;

        if (ownerPaymentId.HasValue)
        {
            var ownerPayment = await dbContext.OwnerPayments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == ownerPaymentId.Value, cancellationToken);
            recipientId = ownerPayment?.OwnerId;
        }
        else
        {
            var payment = await dbContext.Payments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == invoice.PaymentId, cancellationToken);
            if (payment is not null)
            {
                var ownerPayment = await dbContext.OwnerPayments.AsNoTracking()
                    .Where(x => !x.IsDeleted && x.CompanyId == creditNote.CompanyId && x.Reference == payment.Reference)
                    .OrderByDescending(x => x.CreatedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);
                ownerPaymentId = ownerPayment?.Id;
                recipientId = ownerPayment?.OwnerId;
            }
        }

        if (recipientId is null) return;

        dbContext.Notifications.Add(new Notification
        {
            CompanyId = creditNote.CompanyId,
            RecipientId = recipientId.Value,
            Type = NotificationType.CreditNoteApproved,
            Title = "Se aprobó una nota de crédito",
            Body = $"Se aprobó un ajuste de Gs. {creditNote.Amount:N0} sobre tu comprobante {invoice.NumeroFormateado ?? string.Empty}. Toca para ver más detalles.",
            EntityType = "OwnerPayment",
            EntityId = ownerPaymentId
        });
    }

    private async Task LogAsync(Guid creditNoteId, Guid companyId, CreditNoteAuditAction action, object? before, object? after, string detalle, CancellationToken cancellationToken)
    {
        dbContext.CreditNoteAuditLogs.Add(new CreditNoteAuditLog
        {
            CompanyId = companyId,
            CreditNoteId = creditNoteId,
            Action = action,
            UserId = tenantContext.UserId,
            TimestampUtc = DateTime.UtcNow,
            DatosAntesJson = before is null ? null : JsonSerializer.Serialize(before),
            DatosDespuesJson = after is null ? null : JsonSerializer.Serialize(after),
            Detalle = detalle
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private async Task<List<CreditNoteLineDto>> LoadLinesAsync(Guid creditNoteId, CancellationToken cancellationToken) =>
        await dbContext.CreditNoteLines
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.CreditNoteId == creditNoteId)
            .Select(x => new CreditNoteLineDto
            {
                Id = x.Id,
                ExpenseChargeId = x.ExpenseChargeId,
                ChargeConcept = x.ExpenseCharge != null ? x.ExpenseCharge.Concept : string.Empty,
                ChargeType = x.ExpenseCharge != null ? x.ExpenseCharge.ChargeType : ExpenseChargeType.Ordinary,
                ChargeAmount = x.ExpenseCharge != null ? x.ExpenseCharge.Amount : 0,
                Amount = x.Amount,
                Concept = x.Concept
            })
            .ToListAsync(cancellationToken);

    private async Task<List<CreditNoteAttachmentDto>> LoadAttachmentsAsync(Guid creditNoteId, CancellationToken cancellationToken) =>
        await dbContext.CreditNoteAttachments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.CreditNoteId == creditNoteId)
            .OrderBy(x => x.UploadedAtUtc)
            .Select(x => new CreditNoteAttachmentDto
            {
                Id = x.Id,
                Url = x.Url,
                FileName = x.FileName,
                Kind = x.Kind,
                UploadedByUserId = x.UploadedByUserId,
                UploadedByName = x.UploadedByUser != null ? x.UploadedByUser.FullName : null,
                UploadedAtUtc = x.UploadedAtUtc
            })
            .ToListAsync(cancellationToken);

    private static CreditNoteRow ToRow(CreditNote x) => new(
        x.Id, x.CompanyId, x.BuildingId, x.Building != null ? x.Building.Name : string.Empty,
        x.UnitId, x.Unit != null ? x.Unit.Code : string.Empty,
        x.InvoiceId, x.Invoice != null ? x.Invoice.NumeroFormateado : null, x.Invoice != null ? x.Invoice.MontoTotal : 0,
        x.Motivo, x.Amount, x.Status,
        x.CreatedByUserId, x.CreatedByUser != null ? x.CreatedByUser.FullName : null, x.CreatedAtUtc,
        x.ApprovedAtUtc, x.ApprovedByUser != null ? x.ApprovedByUser.FullName : null,
        x.RejectionReason, x.RejectedAtUtc, x.RejectedByUser != null ? x.RejectedByUser.FullName : null,
        x.VoidReason, x.VoidedAtUtc, x.VoidedByUser != null ? x.VoidedByUser.FullName : null,
        x.InvoiceSeriesId, x.Numero,
        x.FiscalDocumentType, x.FiscalNumero, x.FiscalTimbrado, x.FiscalCdc, x.FiscalFechaEmisionUtc,
        x.FiscalEstado, x.FiscalObservaciones);

    private async Task<CreditNoteRow?> LoadRowAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.CreditNotes
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => ToRow(x))
            .FirstOrDefaultAsync(cancellationToken);

    private static CreditNoteDto ToDto(CreditNoteRow r, List<CreditNoteLineDto> lines, List<CreditNoteAttachmentDto> attachments) => new()
    {
        Id = r.Id,
        CompanyId = r.CompanyId,
        BuildingId = r.BuildingId,
        BuildingName = r.BuildingName,
        UnitId = r.UnitId,
        UnitCode = r.UnitCode,
        InvoiceId = r.InvoiceId,
        InvoiceNumeroFormateado = r.InvoiceNumeroFormateado,
        InvoiceMontoTotal = r.InvoiceMontoTotal,
        Motivo = r.Motivo,
        Amount = r.Amount,
        Status = r.Status,
        CreatedByUserId = r.CreatedByUserId,
        CreatedByName = r.CreatedByName,
        CreatedAtUtc = r.CreatedAtUtc,
        ApprovedAtUtc = r.ApprovedAtUtc,
        ApprovedByName = r.ApprovedByName,
        RejectionReason = string.IsNullOrEmpty(r.RejectionReason) ? null : r.RejectionReason,
        RejectedAtUtc = r.RejectedAtUtc,
        RejectedByName = r.RejectedByName,
        VoidReason = string.IsNullOrEmpty(r.VoidReason) ? null : r.VoidReason,
        VoidedAtUtc = r.VoidedAtUtc,
        VoidedByName = r.VoidedByName,
        InvoiceSeriesId = r.InvoiceSeriesId,
        Numero = r.Numero,
        FiscalDocumentType = r.FiscalDocumentType,
        FiscalNumero = r.FiscalNumero,
        FiscalTimbrado = r.FiscalTimbrado,
        FiscalCdc = r.FiscalCdc,
        FiscalFechaEmisionUtc = r.FiscalFechaEmisionUtc,
        FiscalEstado = r.FiscalEstado,
        FiscalObservaciones = r.FiscalObservaciones,
        Lines = lines,
        Attachments = attachments
    };

    private sealed record CreditNoteRow(
        Guid Id, Guid CompanyId, Guid BuildingId, string BuildingName, Guid UnitId, string UnitCode,
        Guid InvoiceId, string? InvoiceNumeroFormateado, decimal InvoiceMontoTotal,
        string Motivo, decimal Amount, CreditNoteStatus Status,
        Guid CreatedByUserId, string? CreatedByName, DateTime CreatedAtUtc,
        DateTime? ApprovedAtUtc, string? ApprovedByName,
        string? RejectionReason, DateTime? RejectedAtUtc, string? RejectedByName,
        string? VoidReason, DateTime? VoidedAtUtc, string? VoidedByName,
        Guid? InvoiceSeriesId, long? Numero,
        CreditNoteFiscalDocumentType? FiscalDocumentType, string? FiscalNumero, string? FiscalTimbrado,
        string? FiscalCdc, DateTime? FiscalFechaEmisionUtc, string? FiscalEstado, string? FiscalObservaciones);
}
