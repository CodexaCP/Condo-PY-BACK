using System.Text.Json;
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
[Route("api/invoice-series")]
public class InvoiceSeriesController(ICondoDbContext dbContext, IAccessScopeService accessScope, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvoiceSeriesDto>>> GetAll(
        [FromQuery] Guid? buildingId, [FromQuery] InvoiceSeriesDocumentType? documentType, CancellationToken cancellationToken)
    {
        if (!CanManageInvoices())
        {
            return Forbid();
        }

        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.InvoiceSeries.AsNoTracking().Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
            {
                query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            }
            else
            {
                query = query.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
            }
        }

        if (buildingId.HasValue)
        {
            query = query.Where(x => x.BuildingId == buildingId.Value);
        }

        if (documentType.HasValue)
        {
            query = query.Where(x => x.DocumentType == documentType.Value);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var series = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new InvoiceSeriesDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                DocumentType = x.DocumentType,
                Ruc = x.Ruc,
                RazonSocial = x.RazonSocial,
                Establecimiento = x.Establecimiento,
                PuntoExpedicion = x.PuntoExpedicion,
                NumeroTimbrado = x.NumeroTimbrado,
                RangoDesde = x.RangoDesde,
                RangoHasta = x.RangoHasta,
                CorrelativoActual = x.CorrelativoActual,
                NumerosDisponibles = x.RangoHasta - x.CorrelativoActual,
                VigenciaDesde = x.VigenciaDesde,
                VigenciaHasta = x.VigenciaHasta,
                Activo = x.Activo,
                ProximoAAgotarse = (x.RangoHasta - x.CorrelativoActual) <= 50,
                ProximoAVencer = x.VigenciaHasta <= today.AddDays(15),
                DireccionEstablecimiento = x.DireccionEstablecimiento,
                ActividadEconomica = x.ActividadEconomica,
                ImprentaNumeroHabilitacion = x.ImprentaNumeroHabilitacion,
                ImprentaRuc = x.ImprentaRuc,
                ImprentaRazonSocial = x.ImprentaRazonSocial,
                FieldPositionsJson = x.FieldPositionsJson,
                ReferenceScanUrl = x.ReferenceScanUrl
            })
            .ToListAsync(cancellationToken);

        return Ok(series);
    }

    [HttpPost]
    public async Task<ActionResult<InvoiceSeriesDto>> Create([FromBody] CreateInvoiceSeriesRequest request, CancellationToken cancellationToken)
    {
        if (!CanManageInvoices())
        {
            return Forbid();
        }

        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId, cancellationToken);

        if (building is null)
        {
            return BadRequest("El edificio no existe.");
        }

        if (!await accessScope.CanAccessBuildingAsync(building.Id, cancellationToken))
        {
            return Forbid();
        }

        var companyId = building.CompanyId ?? accessScope.CompanyId;
        if (!companyId.HasValue)
        {
            return BadRequest("No se pudo determinar la compañía del edificio.");
        }

        var duplicate = await dbContext.InvoiceSeries.AnyAsync(x =>
            !x.IsDeleted &&
            x.CompanyId == companyId.Value &&
            x.Establecimiento == request.Establecimiento &&
            x.PuntoExpedicion == request.PuntoExpedicion &&
            x.NumeroTimbrado == request.NumeroTimbrado &&
            x.DocumentType == request.DocumentType, cancellationToken);

        if (duplicate)
        {
            return Conflict("Ya existe un timbrado con ese establecimiento, punto de expedición, número y tipo de documento.");
        }

        var entity = new InvoiceSeries
        {
            CompanyId = companyId.Value,
            BuildingId = building.Id,
            DocumentType = request.DocumentType,
            Ruc = request.Ruc.Trim(),
            RazonSocial = request.RazonSocial.Trim(),
            Establecimiento = request.Establecimiento.Trim(),
            PuntoExpedicion = request.PuntoExpedicion.Trim(),
            NumeroTimbrado = request.NumeroTimbrado.Trim(),
            RangoDesde = request.RangoDesde,
            RangoHasta = request.RangoHasta,
            CorrelativoActual = request.ProximoNumero - 1,
            VigenciaDesde = request.VigenciaDesde,
            VigenciaHasta = request.VigenciaHasta,
            Activo = true,
            DireccionEstablecimiento = request.DireccionEstablecimiento.Trim(),
            ActividadEconomica = request.ActividadEconomica.Trim(),
            ImprentaNumeroHabilitacion = TrimOrNull(request.ImprentaNumeroHabilitacion),
            ImprentaRuc = TrimOrNull(request.ImprentaRuc),
            ImprentaRazonSocial = TrimOrNull(request.ImprentaRazonSocial)
        };

        dbContext.InvoiceSeries.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.InvoiceAuditLogs.Add(new InvoiceAuditLog
        {
            CompanyId = entity.CompanyId,
            InvoiceId = null,
            InvoiceSeriesId = entity.Id,
            Action = InvoiceAuditAction.SeriesCreated,
            UserId = tenantContext.UserId,
            TimestampUtc = DateTime.UtcNow,
            DatosAntesJson = null,
            DatosDespuesJson = JsonSerializer.Serialize(new
            {
                entity.Id,
                entity.BuildingId,
                entity.Ruc,
                entity.RazonSocial,
                entity.Establecimiento,
                entity.PuntoExpedicion,
                entity.NumeroTimbrado,
                entity.RangoDesde,
                entity.RangoHasta,
                entity.VigenciaDesde,
                entity.VigenciaHasta
            }),
            Detalle = $"Timbrado {entity.NumeroTimbrado} creado para el edificio {building.Name}."
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAll), null, ToDto(entity, building.Name, today: DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    // Calibracion de posiciones para que la factura calce sobre el papel preimpreso de este timbrado puntual
    // (cada imprenta puede entregarlo con un desvio distinto). No afecta a otros timbrados ni al diseno base.
    [HttpPut("{id:guid}/field-positions")]
    public async Task<ActionResult<InvoiceSeriesDto>> UpdateFieldPositions(
        Guid id, [FromBody] UpdateInvoiceSeriesCalibrationRequest request, CancellationToken cancellationToken)
    {
        if (!CanManageInvoices())
        {
            return Forbid();
        }

        var entity = await dbContext.InvoiceSeries
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null) return NotFound();

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        entity.FieldPositionsJson = request.Positions.Count > 0 ? JsonSerializer.Serialize(request.Positions) : null;
        entity.ReferenceScanUrl = string.IsNullOrWhiteSpace(request.ReferenceScanUrl) ? null : request.ReferenceScanUrl.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(entity, entity.Building?.Name ?? string.Empty, today: DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    // Factura de datos de ejemplo (no una real) con las posiciones calibradas guardadas en este timbrado,
    // para poder imprimir sobre el papel preimpreso y verificar que calza antes de emitir facturas de verdad.
    [HttpGet("{id:guid}/sample-pdf")]
    public async Task<IActionResult> DownloadSamplePdf(Guid id, CancellationToken cancellationToken)
    {
        if (!CanManageInvoices())
        {
            return Forbid();
        }

        var entity = await dbContext.InvoiceSeries
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null) return NotFound();

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var sample = new InvoiceDto
        {
            Id = Guid.NewGuid(),
            CompanyId = entity.CompanyId,
            BuildingId = entity.BuildingId,
            BuildingName = entity.Building?.Name ?? "EDIFICIO DE EJEMPLO",
            UnitId = Guid.NewGuid(),
            UnitCode = "01-01",
            InvoiceSeriesId = entity.Id,
            SeriesRazonSocial = entity.RazonSocial,
            SeriesRuc = entity.Ruc,
            SeriesNumeroTimbrado = entity.NumeroTimbrado,
            SeriesEstablecimiento = entity.Establecimiento,
            SeriesPuntoExpedicion = entity.PuntoExpedicion,
            SeriesVigenciaDesde = entity.VigenciaDesde,
            SeriesVigenciaHasta = entity.VigenciaHasta,
            BuildingAddress = entity.Building?.Address ?? entity.DireccionEstablecimiento,
            BuildingPhone = entity.Building?.ContactPhone,
            PeriodDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
            PeriodYear = DateTime.UtcNow.Year,
            PeriodMonth = DateTime.UtcNow.Month,
            UnitCoefficient = 0.007m,
            BuildingOrdinaryTotal = 68_666_823m,
            FieldPositionsJson = entity.FieldPositionsJson,
            ClienteNombre = "CLIENTE DE EJEMPLO",
            ClienteDocumento = "1234567",
            Status = InvoiceStatus.Issued,
            Numero = entity.CorrelativoActual + 1,
            NumeroFormateado = $"{entity.Establecimiento}-{entity.PuntoExpedicion}-{(entity.CorrelativoActual + 1):D7}",
            MontoTotal = 576_802m,
            Detalle =
            [
                new InvoiceLineDto { Concepto = "Expensa ordinaria", ChargeType = ExpenseChargeType.Ordinary, Monto = 576_802m }
            ],
            FechaEmisionUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        var document = new InvoicePdfDocument(sample, standardTemplate: true);
        var pdfBytes = document.GeneratePdf();
        return File(pdfBytes, "application/pdf", $"calibracion_{entity.NumeroTimbrado}.pdf");
    }

    [HttpPut("{id:guid}/desactivar")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        if (!CanManageInvoices())
        {
            return Forbid();
        }

        var entity = await dbContext.InvoiceSeries.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        if (!entity.Activo)
        {
            return NoContent();
        }

        entity.Activo = false;
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.InvoiceAuditLogs.Add(new InvoiceAuditLog
        {
            CompanyId = entity.CompanyId,
            InvoiceId = null,
            InvoiceSeriesId = entity.Id,
            Action = InvoiceAuditAction.SeriesUpdated,
            UserId = tenantContext.UserId,
            TimestampUtc = DateTime.UtcNow,
            DatosAntesJson = JsonSerializer.Serialize(new { Activo = true }),
            DatosDespuesJson = JsonSerializer.Serialize(new { Activo = false }),
            Detalle = $"Timbrado {entity.NumeroTimbrado} desactivado manualmente."
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private bool CanManageInvoices() =>
        accessScope.IsSuperAdmin ||
        accessScope.IsCompanyAdmin ||
        string.Equals(tenantContext.Role, "BuildingManager", StringComparison.OrdinalIgnoreCase);

    private static InvoiceSeriesDto ToDto(InvoiceSeries entity, string buildingName, DateOnly today) => new()
    {
        Id = entity.Id,
        CompanyId = entity.CompanyId,
        BuildingId = entity.BuildingId,
        BuildingName = buildingName,
        DocumentType = entity.DocumentType,
        Ruc = entity.Ruc,
        RazonSocial = entity.RazonSocial,
        Establecimiento = entity.Establecimiento,
        PuntoExpedicion = entity.PuntoExpedicion,
        NumeroTimbrado = entity.NumeroTimbrado,
        RangoDesde = entity.RangoDesde,
        RangoHasta = entity.RangoHasta,
        CorrelativoActual = entity.CorrelativoActual,
        NumerosDisponibles = entity.RangoHasta - entity.CorrelativoActual,
        VigenciaDesde = entity.VigenciaDesde,
        VigenciaHasta = entity.VigenciaHasta,
        Activo = entity.Activo,
        ProximoAAgotarse = (entity.RangoHasta - entity.CorrelativoActual) <= 50,
        ProximoAVencer = entity.VigenciaHasta <= today.AddDays(15),
        DireccionEstablecimiento = entity.DireccionEstablecimiento,
        ActividadEconomica = entity.ActividadEconomica,
        ImprentaNumeroHabilitacion = entity.ImprentaNumeroHabilitacion,
        ImprentaRuc = entity.ImprentaRuc,
        ImprentaRazonSocial = entity.ImprentaRazonSocial,
        FieldPositionsJson = entity.FieldPositionsJson,
        ReferenceScanUrl = entity.ReferenceScanUrl
    };

    private static bool IsValidRequest(CreateInvoiceSeriesRequest request, out string error)
    {
        if (request.BuildingId == Guid.Empty)
        {
            error = "El edificio es obligatorio.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Ruc) || request.Ruc.Trim().Length > 20)
        {
            error = "El RUC es obligatorio y no puede superar los 20 caracteres.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.RazonSocial) || request.RazonSocial.Trim().Length > 200)
        {
            error = "La razón social es obligatoria y no puede superar los 200 caracteres.";
            return false;
        }

        if (!IsDigits(request.Establecimiento, 3))
        {
            error = "El establecimiento debe tener 3 dígitos.";
            return false;
        }

        if (!IsDigits(request.PuntoExpedicion, 3))
        {
            error = "El punto de expedición debe tener 3 dígitos.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.NumeroTimbrado) || !request.NumeroTimbrado.All(char.IsDigit) || request.NumeroTimbrado.Length > 20)
        {
            error = "El número de timbrado debe ser numérico.";
            return false;
        }

        if (request.RangoDesde <= 0 || request.RangoHasta <= 0 || request.RangoDesde > request.RangoHasta)
        {
            error = "El rango de numeración es inválido.";
            return false;
        }

        if (request.ProximoNumero < request.RangoDesde || request.ProximoNumero > request.RangoHasta)
        {
            error = "El próximo número debe estar dentro del rango del timbrado.";
            return false;
        }

        if (request.VigenciaDesde > request.VigenciaHasta)
        {
            error = "La vigencia del timbrado es inválida.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.DireccionEstablecimiento) || request.DireccionEstablecimiento.Trim().Length > 300)
        {
            error = "La dirección del establecimiento es obligatoria y no puede superar los 300 caracteres.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.ActividadEconomica) || request.ActividadEconomica.Trim().Length > 200)
        {
            error = "La actividad económica es obligatoria y no puede superar los 200 caracteres.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsDigits(string value, int expectedLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length == expectedLength && value.Trim().All(char.IsDigit);

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
