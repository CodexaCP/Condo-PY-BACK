using Condo.Api.Documents;
using Condo.Api.Services;
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
[Route("api/building-expenses")]
public class BuildingExpensesController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    IWebHostEnvironment env,
    IConfiguration configuration) : ControllerBase
{
    private static readonly string[] AllowedReceiptExtensions = [".pdf", ".jpg", ".jpeg", ".png"];
    private const long MaxReceiptSizeBytes = 10 * 1024 * 1024; // 10 MB

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BuildingExpenseDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        [FromQuery] Guid? expensePeriodId,
        CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

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

        if (expensePeriodId.HasValue)
        {
            query = query.Where(x => x.ExpensePeriodId == expensePeriodId.Value);
        }

        var items = await query
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.ExpensePeriod!.Year)
            .ThenByDescending(x => x.ExpensePeriod!.Month)
            .ThenBy(x => x.Building!.Name)
            .ThenBy(x => x.Description)
            .Select(x => new BuildingExpenseDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                ExpensePeriodId = x.ExpensePeriodId,
                ExpensePeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty,
                Category = x.Category,
                SupplierName = x.SupplierName,
                Description = x.Description,
                ExpenseDate = x.ExpenseDate,
                Amount = x.Amount,
                DistributionType = x.DistributionType,
                TargetUnitId = x.TargetUnitId,
                TargetUnitCode = x.TargetUnit != null ? x.TargetUnit.Code : string.Empty,
                Notes = x.Notes,
                HasReceipt = x.ReceiptStoredName != null,
                ReceiptFileName = x.ReceiptFileName
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BuildingExpenseDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => new BuildingExpenseDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                ExpensePeriodId = x.ExpensePeriodId,
                ExpensePeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty,
                Category = x.Category,
                SupplierName = x.SupplierName,
                Description = x.Description,
                ExpenseDate = x.ExpenseDate,
                Amount = x.Amount,
                DistributionType = x.DistributionType,
                TargetUnitId = x.TargetUnitId,
                TargetUnitCode = x.TargetUnit != null ? x.TargetUnit.Code : string.Empty,
                Notes = x.Notes,
                HasReceipt = x.ReceiptStoredName != null,
                ReceiptFileName = x.ReceiptFileName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        return await accessScope.CanAccessBuildingAsync(item.BuildingId, cancellationToken) ? Ok(item) : Forbid();
    }

    [HttpPost]
    public async Task<ActionResult<BuildingExpenseDto>> Create([FromBody] BuildingExpenseUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var context = await ValidateContextAsync(request, cancellationToken);
        if (context.Error is not null)
        {
            return context.Error;
        }

        if (!await accessScope.CanAccessBuildingAsync(request.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var effectiveCompanyId = context.Building!.CompanyId ?? context.Building!.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar gastos.");
        }

        var entity = new BuildingExpense
        {
            CompanyId = effectiveCompanyId.Value,
            BuildingId = request.BuildingId,
            ExpensePeriodId = request.ExpensePeriodId,
            Category = request.Category,
            SupplierName = request.SupplierName.Trim(),
            Description = request.Description.Trim(),
            ExpenseDate = request.ExpenseDate,
            Amount = request.Amount,
            DistributionType = request.DistributionType,
            TargetUnitId = request.DistributionType == BuildingExpenseDistributionType.IndividualUnit ? request.TargetUnitId : null,
            Notes = request.Notes.Trim()
        };

        dbContext.BuildingExpenses.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity, context.Building!, context.Period!, context.TargetUnit));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BuildingExpenseDto>> Update(Guid id, [FromBody] BuildingExpenseUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var entity = await dbContext.BuildingExpenses
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        // Se autoriza el gasto EXISTENTE (su edificio y su periodo actual), no solo el destino del request.
        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var currentPeriod = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == entity.ExpensePeriodId, cancellationToken);

        if (currentPeriod is null)
        {
            return BadRequest("El periodo de expensas asociado no existe.");
        }

        if (currentPeriod.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Los gastos del edificio solo se pueden gestionar mientras el periodo este en borrador.");
        }

        var context = await ValidateContextAsync(request, cancellationToken);
        if (context.Error is not null)
        {
            return context.Error;
        }

        if (!await accessScope.CanAccessBuildingAsync(request.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var effectiveCompanyId = context.Building!.CompanyId ?? context.Building!.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar gastos.");
        }

        entity.CompanyId = effectiveCompanyId.Value;
        entity.BuildingId = request.BuildingId;
        entity.ExpensePeriodId = request.ExpensePeriodId;
        entity.Category = request.Category;
        entity.SupplierName = request.SupplierName.Trim();
        entity.Description = request.Description.Trim();
        entity.ExpenseDate = request.ExpenseDate;
        entity.Amount = request.Amount;
        entity.DistributionType = request.DistributionType;
        entity.TargetUnitId = request.DistributionType == BuildingExpenseDistributionType.IndividualUnit ? request.TargetUnitId : null;
        entity.Notes = request.Notes.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(entity, context.Building!, context.Period!, context.TargetUnit));
    }

    private const long MaxImportSizeBytes = 2 * 1024 * 1024; // 2 MB

    private string ImportTemplateSecret => configuration["Jwt:Key"] ?? string.Empty;

    // La plantilla se genera para un edificio: lleva una marca cifrada con su empresa y su id, y solo se acepta al
    // importar en ese mismo edificio.
    [HttpGet("import-template")]
    public async Task<IActionResult> DownloadImportTemplate([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        if (buildingId == Guid.Empty) return BadRequest("El edificio es obligatorio.");
        if (!await accessScope.CanAccessBuildingAsync(buildingId, cancellationToken)) return Forbid();

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == buildingId, cancellationToken);
        if (building is null) return BadRequest("El edificio indicado no existe.");

        var companyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!companyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar gastos.");
        }

        var companyName = await dbContext.Companies
            .AsNoTracking()
            .Where(x => x.Id == companyId.Value)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var token = ExpenseImportTemplateToken.Create(ImportTemplateSecret, companyId.Value, building.Id);
        var fileName = $"plantilla-gastos-{new string(building.Name.Where(char.IsLetterOrDigit).ToArray())}.xlsx";
        return File(
            BuildingExpenseImportTemplateBuilder.Build(building.Name, companyName, token),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    // Carga masiva desde Excel: una fila = un gasto del periodo (fecha = primer dia del periodo, reparto por
    // coeficiente). Sin "confirm" solo valida y devuelve la vista previa; con "confirm" guarda todo o nada.
    [HttpPost("import")]
    [RequestSizeLimit(MaxImportSizeBytes)]
    public async Task<ActionResult<BuildingExpenseImportResultDto>> Import(
        IFormFile file,
        [FromForm] Guid buildingId,
        [FromForm] Guid expensePeriodId,
        [FromForm] bool confirm,
        [FromForm] bool replaceExisting,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0) return BadRequest("No se recibio ningun archivo.");
        if (file.Length > MaxImportSizeBytes) return BadRequest("El archivo no puede superar los 2 MB.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Solo se permiten archivos Excel (.xlsx).");
        }

        // El operador solo carga y edita; reemplazar elimina los gastos que ya estaban.
        if (replaceExisting && string.Equals(tenantContext.Role, "CompanyOperator", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "El operador no puede reemplazar los gastos del periodo.");
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expensePeriodId, cancellationToken);
        if (period is null) return BadRequest("El periodo de expensas indicado no existe.");
        if (period.BuildingId != buildingId) return BadRequest("El periodo de expensas debe pertenecer al edificio seleccionado.");
        if (!await accessScope.CanAccessBuildingAsync(buildingId, cancellationToken)) return Forbid();
        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Los gastos del edificio solo se pueden gestionar mientras el periodo este en borrador.");
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == buildingId, cancellationToken);
        var effectiveCompanyId = building?.CompanyId ?? building?.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar gastos.");
        }

        List<BuildingExpenseImportParser.ParsedRow> parsed;
        await using (var stream = file.OpenReadStream())
        {
            var (rows, token, parseError) = BuildingExpenseImportParser.Parse(stream);
            if (rows is null) return BadRequest(parseError);
            parsed = rows;

            // La plantilla tiene que haberse generado para este edificio y esta empresa.
            var marker = ExpenseImportTemplateToken.Read(ImportTemplateSecret, token);
            if (marker is null)
            {
                return BadRequest("El archivo no es una plantilla valida. Generá la plantilla desde la pantalla de gastos, para este edificio.");
            }

            if (marker.BuildingId != buildingId || marker.CompanyId != effectiveCompanyId.Value)
            {
                return BadRequest("Esta plantilla fue generada para otro edificio o empresa. Generá una nueva para este edificio.");
            }
        }

        var existing = await dbContext.BuildingExpenses
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId)
            .ToListAsync(cancellationToken);

        // Con "reemplazar" los existentes se eliminan, asi que no cuentan como duplicados.
        static string Key(string supplier, string description, decimal amount) =>
            $"{supplier.Trim().ToUpperInvariant()}|{description.Trim().ToUpperInvariant()}|{amount:0.00}";
        var existingKeys = replaceExisting
            ? new HashSet<string>()
            : existing.Select(x => Key(x.SupplierName, x.Description, x.Amount)).ToHashSet();
        var seenInFile = new HashSet<string>();

        var result = new BuildingExpenseImportResultDto { ExistingCount = existing.Count };
        var toCreate = new List<BuildingExpense>();

        foreach (var row in parsed)
        {
            var dto = new BuildingExpenseImportRowDto
            {
                RowNumber = row.RowNumber,
                Category = row.Category,
                Supplier = row.Supplier,
                Description = row.Description,
                Amount = row.Amount
            };

            string? error = null;
            BuildingExpenseCategory category = default;
            if (row.Category.Length == 0) error = "La categoria es obligatoria.";
            else if (!BuildingExpenseImportParser.TryResolveCategory(row.Category, out category)) error = $"Categoria desconocida: \"{row.Category}\". Ver la hoja Categorias de la plantilla.";
            else if (row.Description.Length == 0) error = "La descripcion es obligatoria.";
            else if (row.Description.Length > 200) error = "La descripcion no puede superar los 200 caracteres.";
            else if (row.Supplier.Length > 160) error = "El proveedor no puede superar los 160 caracteres.";
            else if (row.AmountError is not null) error = row.AmountError;
            else if (row.Amount is not > 0) error = "El monto debe ser mayor que cero.";

            if (error is not null)
            {
                dto.Status = "Error";
                dto.Message = error;
                result.ErrorCount++;
                result.Rows.Add(dto);
                continue;
            }

            dto.Category = CategoryLabels.ExpenseLabel(category);
            var key = Key(row.Supplier, row.Description, row.Amount!.Value);
            if (existingKeys.Contains(key))
            {
                dto.Status = "Duplicate";
                dto.Message = "Ya existe un gasto igual en el periodo: se omite.";
                result.DuplicateCount++;
            }
            else
            {
                if (!seenInFile.Add(key))
                {
                    dto.Status = "Warning";
                    dto.Message = "Fila repetida en el archivo (mismo proveedor, descripcion y monto): se importa igual.";
                    result.WarningCount++;
                }
                else
                {
                    result.OkCount++;
                }

                toCreate.Add(new BuildingExpense
                {
                    CompanyId = effectiveCompanyId.Value,
                    BuildingId = buildingId,
                    ExpensePeriodId = expensePeriodId,
                    Category = category,
                    SupplierName = row.Supplier,
                    Description = row.Description,
                    ExpenseDate = period.StartDate,
                    Amount = row.Amount!.Value,
                    DistributionType = BuildingExpenseDistributionType.ByCoefficient,
                    Notes = string.Empty
                });
            }

            result.Rows.Add(dto);
        }

        // Todo o nada: con un solo error no se guarda ninguna fila.
        if (!confirm || result.ErrorCount > 0 || (toCreate.Count == 0 && !replaceExisting))
        {
            return Ok(result);
        }

        if (replaceExisting)
        {
            foreach (var expense in existing) expense.IsDeleted = true;
            result.DeletedCount = existing.Count;
        }

        dbContext.BuildingExpenses.AddRange(toCreate);
        await dbContext.SaveChangesAsync(cancellationToken);

        result.Imported = true;
        result.ImportedCount = toCreate.Count;
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        // El operador solo carga, edita y adjunta comprobantes; no elimina gastos.
        if (string.Equals(tenantContext.Role, "CompanyOperator", StringComparison.OrdinalIgnoreCase)) return Forbid();

        var entity = await dbContext.BuildingExpenses
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == entity.ExpensePeriodId, cancellationToken);

        if (period is null)
        {
            return BadRequest("El periodo de expensas asociado no existe.");
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Los gastos del edificio solo se pueden eliminar mientras el periodo este en borrador.");
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<(ActionResult? Error, Building? Building, ExpensePeriod? Period, Unit? TargetUnit)> ValidateContextAsync(
        BuildingExpenseUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId, cancellationToken);

        if (building is null)
        {
            return (BadRequest("El edificio indicado no existe."), null, null, null);
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.ExpensePeriodId, cancellationToken);

        if (period is null)
        {
            return (BadRequest("El periodo de expensas indicado no existe."), null, null, null);
        }

        if (period.BuildingId != request.BuildingId)
        {
            return (BadRequest("El periodo de expensas debe pertenecer al edificio seleccionado."), null, null, null);
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return (BadRequest("Los gastos del edificio solo se pueden gestionar mientras el periodo este en borrador."), null, null, null);
        }

        if (request.ExpenseDate < period.StartDate || request.ExpenseDate > period.EndDate)
        {
            return (BadRequest("La fecha del gasto debe estar dentro del rango del periodo seleccionado."), null, null, null);
        }

        Unit? targetUnit = null;
        if (request.TargetUnitId.HasValue)
        {
            targetUnit = await dbContext.Units
                .AsNoTracking()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.IsActive && x.Id == request.TargetUnitId.Value, cancellationToken);

            if (targetUnit is null)
            {
                return (BadRequest("La unidad destino no existe o no esta activa."), null, null, null);
            }

            if (targetUnit.BuildingId != request.BuildingId)
            {
                return (BadRequest("La unidad destino debe pertenecer al edificio seleccionado."), null, null, null);
            }
        }

        return (null, building, period, targetUnit);
    }

    private static bool IsValidRequest(BuildingExpenseUpsertRequest request, out string error)
    {
        if (request.BuildingId == Guid.Empty)
        {
            error = "El edificio es obligatorio.";
            return false;
        }

        if (request.ExpensePeriodId == Guid.Empty)
        {
            error = "El periodo es obligatorio.";
            return false;
        }

        if (request.DistributionType == BuildingExpenseDistributionType.ManualGroup)
        {
            error = "La distribucion ManualGroup todavia no esta disponible como flujo operativo.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            error = "La descripcion es obligatoria.";
            return false;
        }

        if (request.Description.Trim().Length > 200)
        {
            error = "La descripcion no puede superar los 200 caracteres.";
            return false;
        }

        if (request.SupplierName.Trim().Length > 160)
        {
            error = "El proveedor no puede superar los 160 caracteres.";
            return false;
        }

        if (request.Notes.Trim().Length > 500)
        {
            error = "Las notas no pueden superar los 500 caracteres.";
            return false;
        }

        if (request.Amount <= 0)
        {
            error = "El monto debe ser mayor que cero.";
            return false;
        }

        if (request.DistributionType == BuildingExpenseDistributionType.IndividualUnit && !request.TargetUnitId.HasValue)
        {
            error = "La unidad destino es obligatoria cuando la distribucion es por unidad individual.";
            return false;
        }

        if (request.DistributionType != BuildingExpenseDistributionType.IndividualUnit && request.TargetUnitId.HasValue)
        {
            error = "La unidad destino solo se permite cuando la distribucion es por unidad individual.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    [HttpPost("{id:guid}/receipt")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<BuildingExpenseDto>> UploadReceipt(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("No se recibio ningun archivo.");
        }

        if (file.Length > MaxReceiptSizeBytes)
        {
            return BadRequest("El archivo no puede superar los 10 MB.");
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedReceiptExtensions.Contains(ext))
        {
            return BadRequest("Solo se permiten archivos PDF, JPG o PNG.");
        }

        var entity = await dbContext.BuildingExpenses
            .Include(x => x.Building).ThenInclude(b => b!.Condominium)
            .Include(x => x.ExpensePeriod)
            .Include(x => x.TargetUnit)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var uploadsDir = Path.Combine(env.ContentRootPath, "uploads", "receipts");
        Directory.CreateDirectory(uploadsDir);

        if (!string.IsNullOrEmpty(entity.ReceiptStoredName))
        {
            var oldPath = Path.Combine(uploadsDir, entity.ReceiptStoredName);
            if (System.IO.File.Exists(oldPath))
            {
                System.IO.File.Delete(oldPath);
            }
        }

        var storedName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploadsDir, storedName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        entity.ReceiptFileName = file.FileName;
        entity.ReceiptStoredName = storedName;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(entity, entity.Building!, entity.ExpensePeriod!, entity.TargetUnit));
    }

    [HttpGet("{id:guid}/receipt")]
    public async Task<IActionResult> DownloadReceipt(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.BuildingExpenses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken)
            && !await IsPresidentReviewingSettlementAsync(entity.BuildingId, entity.ExpensePeriodId, cancellationToken))
        {
            return Forbid();
        }

        if (string.IsNullOrEmpty(entity.ReceiptStoredName))
        {
            return NotFound("Este gasto no tiene comprobante adjunto.");
        }

        var filePath = Path.Combine(env.ContentRootPath, "uploads", "receipts", entity.ReceiptStoredName);
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound("El archivo del comprobante no se encontro en el servidor.");
        }

        var ext = Path.GetExtension(entity.ReceiptStoredName).ToLowerInvariant();
        var contentType = ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            _ => "image/jpeg"
        };

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath, cancellationToken);
        return File(fileBytes, contentType, entity.ReceiptFileName ?? entity.ReceiptStoredName);
    }

    [HttpDelete("{id:guid}/receipt")]
    public async Task<IActionResult> DeleteReceipt(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.BuildingExpenses
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        if (string.IsNullOrEmpty(entity.ReceiptStoredName))
        {
            return BadRequest("Este gasto no tiene comprobante adjunto.");
        }

        var filePath = Path.Combine(env.ContentRootPath, "uploads", "receipts", entity.ReceiptStoredName);
        if (System.IO.File.Exists(filePath))
        {
            System.IO.File.Delete(filePath);
        }

        entity.ReceiptFileName = null;
        entity.ReceiptStoredName = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<bool> IsPresidentReviewingSettlementAsync(Guid buildingId, Guid expensePeriodId, CancellationToken cancellationToken)
    {
        var isPresident = await dbContext.Buildings
            .AsNoTracking()
            .AnyAsync(x => x.Id == buildingId && x.PresidentUserId == tenantContext.UserId, cancellationToken);

        if (!isPresident) return false;

        return await dbContext.ExpenseSettlements
            .AsNoTracking()
            .AnyAsync(
                x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId
                     && x.Status == ExpenseSettlementStatus.Approved
                     && x.PresidentApprovedByUserId == null && x.PresidentRejectedByUserId == null,
                cancellationToken);
    }

    private static BuildingExpenseDto ToDto(BuildingExpense entity, Building building, ExpensePeriod period, Unit? targetUnit) =>
        new()
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
            BuildingId = entity.BuildingId,
            BuildingName = building.Name,
            ExpensePeriodId = entity.ExpensePeriodId,
            ExpensePeriodName = period.Name,
            Category = entity.Category,
            SupplierName = entity.SupplierName,
            Description = entity.Description,
            ExpenseDate = entity.ExpenseDate,
            Amount = entity.Amount,
            DistributionType = entity.DistributionType,
            TargetUnitId = entity.TargetUnitId,
            TargetUnitCode = targetUnit?.Code ?? string.Empty,
            Notes = entity.Notes,
            HasReceipt = entity.ReceiptStoredName != null,
            ReceiptFileName = entity.ReceiptFileName
        };
}
