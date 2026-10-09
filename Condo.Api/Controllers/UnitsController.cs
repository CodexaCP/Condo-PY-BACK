using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/units")]
public partial class UnitsController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UnitDto>>> GetAll(CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.Units
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Building != null && !x.Building.IsDeleted);

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

        var units = await query
            .OrderBy(x => x.Code)
            .Select(x => new UnitDto
            {
                Id = x.Id,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                Code = x.Code,
                Floor = x.Floor,
                Coefficient = x.Coefficient,
                IsActive = x.IsActive,
                LateFeeExempt = x.LateFeeExempt,
                LateFeeExemptReason = x.LateFeeExemptReason
            })
            .ToListAsync(cancellationToken);

        return Ok(units);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UnitDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var unit = await dbContext.Units
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id && x.Building != null && !x.Building.IsDeleted)
            .Select(x => new UnitDto
            {
                Id = x.Id,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                Code = x.Code,
                Floor = x.Floor,
                Coefficient = x.Coefficient,
                IsActive = x.IsActive,
                LateFeeExempt = x.LateFeeExempt,
                LateFeeExemptReason = x.LateFeeExemptReason
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (unit is null)
        {
            return NotFound();
        }

        return await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken) ? Ok(unit) : Forbid();
    }

    [HttpPost]
    public async Task<ActionResult<UnitDto>> Create([FromBody] UnitUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId, cancellationToken);

        if (building is null)
        {
            return BadRequest("No se puede crear una unidad sin un edificio valido.");
        }

        if (!await accessScope.CanAccessBuildingAsync(building.Id, cancellationToken))
        {
            return Forbid();
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var duplicatedCode = await dbContext.Units
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == request.BuildingId && x.Code == normalizedCode, cancellationToken);

        if (duplicatedCode)
        {
            return Conflict("Ya existe una unidad con ese codigo dentro del edificio.");
        }

        var effectiveCompanyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar unidades.");
        }

        var entity = new Unit
        {
            CompanyId = effectiveCompanyId.Value,
            BuildingId = request.BuildingId,
            Code = normalizedCode,
            Floor = request.Floor.Trim(),
            Coefficient = request.Coefficient,
            IsActive = request.IsActive
        };

        dbContext.Units.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueCodeViolation(exception))
        {
            return Conflict("Ya existe una unidad con ese codigo dentro del edificio.");
        }

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity, building.Name));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UnitDto>> Update(Guid id, [FromBody] UnitUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var entity = await dbContext.Units.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId, cancellationToken);

        if (building is null || !await accessScope.CanAccessBuildingAsync(building.Id, cancellationToken))
        {
            return BadRequest("No se puede guardar una unidad sin un edificio valido y accesible.");
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var duplicatedCode = await dbContext.Units
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == request.BuildingId && x.Id != id && x.Code == normalizedCode, cancellationToken);

        if (duplicatedCode)
        {
            return Conflict("Ya existe una unidad con ese codigo dentro del edificio.");
        }

        var effectiveCompanyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar unidades.");
        }

        entity.CompanyId = effectiveCompanyId.Value;
        entity.BuildingId = request.BuildingId;
        entity.Code = normalizedCode;
        entity.Floor = request.Floor.Trim();
        entity.Coefficient = request.Coefficient;
        entity.IsActive = request.IsActive;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueCodeViolation(exception))
        {
            return Conflict("Ya existe una unidad con ese codigo dentro del edificio.");
        }

        return Ok(ToDto(entity, building.Name));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken, [FromQuery] bool permanent = false)
    {
        if (permanent) return await DeletePermanentlyAsync(id, cancellationToken);

        var entity = await dbContext.Units.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var hasResidents = await dbContext.UnitResidents
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.UnitId == entity.Id, cancellationToken);

        var hasCharges = await dbContext.ExpenseCharges
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.UnitId == entity.Id, cancellationToken);

        var hasPayments = await dbContext.Payments
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.UnitId == entity.Id, cancellationToken);

        var hasIndividualExpenses = await dbContext.BuildingExpenses
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.TargetUnitId == entity.Id, cancellationToken);

        if (hasResidents || hasCharges || hasPayments || hasIndividualExpenses)
        {
            return BadRequest("No se puede eliminar la unidad porque tiene residentes, cargos, pagos o gastos individuales asociados.");
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ─── Carga masiva desde Excel (solo SuperAdmin) ──────────────────────────

    private const long MaxImportSizeBytes = 2 * 1024 * 1024; // 2 MB

    // Edificios que se pueden elegir en la plantilla: todos los que tienen empresa (directa o por su condominio).
    private async Task<List<UnitImportService.BuildingRef>> LoadImportBuildingsAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => new
            {
                x.Id, x.Name, x.Code,
                CompanyId = x.CompanyId ?? (x.Condominium != null ? x.Condominium.CompanyId : null)
            })
            .ToListAsync(cancellationToken);

        var companyIds = rows.Where(r => r.CompanyId.HasValue).Select(r => r.CompanyId!.Value).Distinct().ToList();
        var companies = await dbContext.Companies
            .AsNoTracking()
            .Where(x => companyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return rows
            .Where(r => r.CompanyId.HasValue && companies.ContainsKey(r.CompanyId.Value))
            .Select(r => new UnitImportService.BuildingRef(r.Id, r.CompanyId!.Value, r.Name, r.Code, companies[r.CompanyId!.Value]))
            .ToList();
    }

    // Plantilla de Excel: una columna por dato de la unidad; el edificio se elige de una lista desplegable con todos los edificios.
    [HttpGet("import-template")]
    public async Task<IActionResult> DownloadImportTemplate(CancellationToken cancellationToken)
    {
        if (!accessScope.IsSuperAdmin) return Forbid();

        var buildings = await LoadImportBuildingsAsync(cancellationToken);
        if (buildings.Count == 0) return BadRequest("No hay edificios con empresa asignada para cargar unidades.");

        return File(
            UnitImportService.BuildTemplate(buildings),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "plantilla-unidades.xlsx");
    }

    // Sin "confirm" solo valida y devuelve la vista previa; con "confirm" guarda todo, y solo si no hay ningun error (todo o nada).
    [HttpPost("import")]
    [RequestSizeLimit(MaxImportSizeBytes)]
    public async Task<ActionResult<UnitImportResultDto>> Import(IFormFile file, [FromForm] bool confirm, CancellationToken cancellationToken)
    {
        if (!accessScope.IsSuperAdmin) return Forbid();
        if (file is null || file.Length == 0) return BadRequest("No se recibió ningún archivo.");
        if (file.Length > MaxImportSizeBytes) return BadRequest("El archivo no puede superar los 2 MB.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Solo se permiten archivos Excel (.xlsx).");

        List<UnitImportService.ParsedRow> parsed;
        await using (var stream = file.OpenReadStream())
        {
            var (rows, parseError) = UnitImportService.Parse(stream);
            if (rows is null) return BadRequest(parseError);
            parsed = rows;
        }

        if (parsed.Count == 0) return BadRequest("El archivo no tiene filas para importar: completá la plantilla desde la fila 5.");

        var buildings = await LoadImportBuildingsAsync(cancellationToken);
        var byLabel = buildings.ToDictionary(b => UnitImportService.NormalizeLabel(b.Label), b => b);

        var buildingIds = buildings.Select(b => b.Id).ToList();
        var existing = await dbContext.Units
            .AsNoTracking()
            .Where(x => !x.IsDeleted && buildingIds.Contains(x.BuildingId))
            .Select(x => new { x.BuildingId, x.Code, x.Coefficient })
            .ToListAsync(cancellationToken);
        var existingCodes = existing.Select(x => (x.BuildingId, x.Code.ToUpperInvariant())).ToHashSet();

        var validated = UnitImportService.Validate(parsed, byLabel, existingCodes);
        var valid = validated.Where(v => v.Error is null).ToList();

        var result = new UnitImportResultDto
        {
            Confirmed = confirm,
            TotalRows = validated.Count,
            ValidRows = valid.Count,
            ErrorRows = validated.Count - valid.Count,
            Rows = validated.Select(v => new UnitImportRowDto
            {
                RowNumber = v.Row.RowNumber,
                Building = v.Building?.Label ?? v.Row.Building,
                Code = v.Code,
                Floor = v.Floor,
                Coefficient = v.Coefficient,
                IsActive = v.IsActive,
                Error = v.Error
            }).ToList()
        };

        // Resumen por edificio: cuantas unidades se agregan y cuanto suman los coeficientes (suele ser 1).
        foreach (var group in valid.GroupBy(v => v.Building!))
        {
            var existingForBuilding = existing.Where(e => e.BuildingId == group.Key.Id).ToList();
            var total = existingForBuilding.Sum(e => e.Coefficient) + group.Sum(v => v.Coefficient);
            result.Buildings.Add(new UnitImportBuildingSummaryDto
            {
                Building = group.Key.Label,
                NewUnits = group.Count(),
                ExistingUnits = existingForBuilding.Count,
                CoefficientTotal = decimal.Round(total, 6),
                Warning = Math.Abs(total - 1m) > 0.0001m
                    ? $"Los coeficientes del edificio suman {total.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}; suelen sumar 1."
                    : null
            });
        }

        if (!confirm) return Ok(result);

        if (result.ErrorRows > 0)
        {
            result.Message = "No se guardó ninguna unidad: corregí las filas con errores y volvé a importar.";
            return Ok(result);
        }

        foreach (var v in valid)
        {
            dbContext.Units.Add(new Unit
            {
                CompanyId = v.Building!.CompanyId,
                BuildingId = v.Building.Id,
                Code = v.Code,
                Floor = v.Floor,
                Coefficient = v.Coefficient,
                IsActive = v.IsActive
            });
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueCodeViolation(exception))
        {
            return Conflict("Mientras se importaba se creó una unidad con el mismo código en un edificio. Volvé a validar el archivo.");
        }

        result.Created = valid.Count;
        result.Message = $"Se crearon {valid.Count} unidades.";
        return Ok(result);
    }

    // Eliminacion definitiva (solo SuperAdmin), para una unidad creada por error: se borra la fila de la base y sus vinculos con
    // propietarios y residentes. La eliminacion normal solo la marca como borrada y su codigo sigue reservado en el edificio, asi
    // que esa unidad no se podria volver a crear con el mismo codigo. Si la unidad ya tiene movimientos (cargos, pagos, facturas...)
    // no se borra: ese historial no se puede perder.
    private async Task<IActionResult> DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!accessScope.IsSuperAdmin) return Forbid();

        // Incluye las que ya estaban "eliminadas" (borrado logico): siguen ocupando el codigo.
        var entity = await dbContext.Units.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        var blockers = new List<string>();
        async Task Check(string label, Task<bool> exists)
        {
            if (await exists) blockers.Add(label);
        }

        await Check("cargos de expensas", dbContext.ExpenseCharges.AnyAsync(x => x.UnitId == id, cancellationToken));
        await Check("pagos", dbContext.Payments.AnyAsync(x => x.UnitId == id, cancellationToken));
        await Check("facturas", dbContext.Invoices.AnyAsync(x => x.UnitId == id, cancellationToken));
        await Check("notas de crédito", dbContext.CreditNotes.AnyAsync(x => x.UnitId == id, cancellationToken));
        await Check("comprobantes de pago de propietarios", dbContext.OwnerPaymentUnits.AnyAsync(x => x.UnitId == id, cancellationToken));
        await Check("saldos a favor", dbContext.OwnerCreditMovements.AnyAsync(x => x.UnitId == id, cancellationToken));
        await Check("gastos individuales", dbContext.BuildingExpenses.AnyAsync(x => x.TargetUnitId == id, cancellationToken));
        await Check("gastos recurrentes individuales", dbContext.RecurringBuildingExpenses.AnyAsync(x => x.TargetUnitId == id, cancellationToken));
        await Check("notas de crédito de proveedor", dbContext.BuildingExpenseCreditNoteAllocations.AnyAsync(x => x.UnitId == id, cancellationToken));
        await Check("reclamos", dbContext.Claims.AnyAsync(x => x.UnitId == id, cancellationToken));
        await Check("votos", dbContext.VoteCasts.AnyAsync(x => x.UnitId == id, cancellationToken));
        await Check("publicaciones del Marketplace", dbContext.MarketplaceListings.AnyAsync(x => x.UnitId == id, cancellationToken));
        await Check("reservas del Marketplace", dbContext.MarketplaceReservations.AnyAsync(x => x.UnitId == id, cancellationToken));
        await Check("notas de cambio de propietario", dbContext.MarketplaceHandoverNotes.AnyAsync(x => x.UnitId == id, cancellationToken));

        if (blockers.Count > 0)
        {
            return BadRequest(
                $"No se puede eliminar definitivamente la unidad {entity.Code}: tiene {string.Join(", ", blockers)} asociados y ese historial no se puede perder. " +
                "Si ya no se usa, desactivala en lugar de eliminarla.");
        }

        // Sus vinculos (aunque estén dados de baja) se borran con la unidad.
        dbContext.UnitOwners.RemoveRange(await dbContext.UnitOwners.Where(x => x.UnitId == id).ToListAsync(cancellationToken));
        dbContext.UnitResidents.RemoveRange(await dbContext.UnitResidents.Where(x => x.UnitId == id).ToListAsync(cancellationToken));
        dbContext.Units.Remove(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict("La unidad tiene datos asociados que impiden eliminarla definitivamente.");
        }

        return NoContent();
    }

    private static UnitDto ToDto(Unit entity, string buildingName) =>
        new()
        {
            Id = entity.Id,
            BuildingId = entity.BuildingId,
            BuildingName = buildingName,
            Code = entity.Code,
            Floor = entity.Floor,
            Coefficient = entity.Coefficient,
            IsActive = entity.IsActive,
            LateFeeExempt = entity.LateFeeExempt,
            LateFeeExemptReason = entity.LateFeeExemptReason
        };

    private static bool IsValidRequest(UnitUpsertRequest request, out string error)
    {
        if (request.BuildingId == Guid.Empty)
        {
            error = "No se puede crear una unidad sin seleccionar un edificio.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            error = "El codigo de la unidad es obligatorio.";
            return false;
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        if (!CodeRegex().IsMatch(normalizedCode))
        {
            error = "El codigo de la unidad solo puede contener letras, numeros y guiones medios.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Floor))
        {
            error = "El piso de la unidad es obligatorio.";
            return false;
        }

        if (request.Coefficient < 0m)
        {
            error = "El coeficiente no puede ser negativo.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsUniqueCodeViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        (sqlException.Number == 2601 || sqlException.Number == 2627) &&
        sqlException.Message.Contains("IX_Units_BuildingId_Code", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Z0-9]+(?:-[A-Z0-9]+)*$")]
    private static partial Regex CodeRegex();
}
