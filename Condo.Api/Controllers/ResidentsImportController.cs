using System.Security.Claims;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

// Carga masiva de residentes desde Excel (SuperAdmin y Administrador de empresa). Es independiente de la carga de propietarios.
// Sin "confirm" solo valida y devuelve la vista previa; con "confirm" guarda todo, y solo si no hay ningun error (todo o nada).
[ApiController]
[Authorize]
[Route("api/residents")]
public class ResidentsImportController(
    ICondoDbContext dbContext, IAccessScopeService accessScope, ILogger<ResidentsImportController> logger) : ControllerBase
{
    [HttpGet("import-template")]
    public async Task<IActionResult> DownloadTemplate([FromQuery] Guid? companyId, CancellationToken cancellationToken)
    {
        if (!PeopleImportData.CanImport(accessScope)) return Forbid();

        var (company, error) = await PeopleImportData.ResolveCompanyAsync(dbContext, accessScope, companyId, cancellationToken);
        if (company is null) return BadRequest(error);

        var (buildings, units) = await PeopleImportData.LoadBuildingsAndUnitsAsync(dbContext, accessScope, company.Value, cancellationToken);

        return File(
            ResidentsImportService.BuildTemplate(buildings, units),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "plantilla-residentes.xlsx");
    }

    [HttpPost("import")]
    [RequestSizeLimit(PeopleImportCommon.MaxFileSizeBytes)]
    public async Task<ActionResult<PeopleImportResultDto>> Import(
        IFormFile file, [FromForm] bool confirm, [FromForm] Guid? companyId, CancellationToken cancellationToken)
    {
        if (!PeopleImportData.CanImport(accessScope)) return Forbid();
        if (file is null || file.Length == 0) return BadRequest("No se recibió ningún archivo.");
        if (file.Length > PeopleImportCommon.MaxFileSizeBytes) return BadRequest("El archivo no puede superar los 2 MB.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Solo se permiten archivos Excel (.xlsx).");

        var (company, companyError) = await PeopleImportData.ResolveCompanyAsync(dbContext, accessScope, companyId, cancellationToken);
        if (company is null) return BadRequest(companyError);

        List<ResidentsImportService.ParsedRow> parsed;
        await using (var stream = file.OpenReadStream())
        {
            var (rows, parseError) = ResidentsImportService.Parse(stream);
            if (rows is null) return BadRequest(parseError);
            parsed = rows;
        }

        if (parsed.Count == 0) return BadRequest("El archivo no tiene filas para importar: completá la plantilla desde la fila 5.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var (buildings, units) = await PeopleImportData.LoadBuildingsAndUnitsAsync(dbContext, accessScope, company.Value, cancellationToken);

        var existingResidents = await dbContext.Residents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.CompanyId == company.Value)
            .Select(x => new { x.Id, x.FullName, x.DocumentNumber })
            .ToListAsync(cancellationToken);
        var residentsByDocument = new Dictionary<string, ResidentsImportService.ExistingResident>();
        foreach (var r in existingResidents)
            residentsByDocument.TryAdd(PeopleImportCommon.NormalizeDocument(r.DocumentNumber), new ResidentsImportService.ExistingResident(r.Id, r.FullName));

        var unitIds = units.Select(u => u.Id).ToList();
        var links = await dbContext.UnitResidents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && unitIds.Contains(x.UnitId))
            .Select(x => new { x.UnitId, x.ResidentId, x.IsPrimary, x.StartDate, x.EndDate })
            .ToListAsync(cancellationToken);
        var assignments = links
            .GroupBy(x => x.UnitId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ResidentsImportService.ExistingAssignment>)g
                    .Select(x => new ResidentsImportService.ExistingAssignment(x.ResidentId, x.IsPrimary, x.StartDate, x.EndDate)).ToList());

        var context = new ResidentsImportService.Context
        {
            BuildingsByLabel = PeopleImportData.ByLabel(buildings),
            Units = PeopleImportData.ByKey(units),
            ResidentsByDocument = residentsByDocument,
            UnitAssignments = assignments,
            Today = today
        };

        var validated = ResidentsImportService.Validate(parsed, context);
        var valid = validated.Where(v => v.Error is null).ToList();

        var result = BuildResult(validated, valid, confirm);
        if (!confirm) return Ok(result);

        if (result.ErrorRows > 0)
        {
            result.Message = "No se guardó nada: corregí las filas con errores y volvé a importar.";
            return Ok(result);
        }

        // Si el correo del residente ya es de una cuenta de la empresa, la ficha queda vinculada (igual que en el alta manual).
        var userIdsByEmail = new Dictionary<string, Guid>();
        var accounts = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.CompanyId == company.Value)
            .Select(x => new { x.Id, x.Email })
            .ToListAsync(cancellationToken);
        foreach (var a in accounts) userIdsByEmail.TryAdd(a.Email.Trim().ToLowerInvariant(), a.Id);

        var created = new Dictionary<string, Resident>();
        foreach (var v in valid)
        {
            Guid residentId;
            if (v.Existing is not null)
            {
                residentId = v.Existing.Id;
            }
            else
            {
                if (!created.TryGetValue(v.DocKey, out var resident))
                {
                    resident = new Resident
                    {
                        CompanyId = company.Value,
                        FullName = v.FullName,
                        DocumentType = v.DocType,
                        DocumentNumber = v.DocNumber,
                        Email = v.Email,
                        PhoneNumber = v.Phone,
                        IsOwner = false,
                        IsActive = true,
                        ApplicationUserId = userIdsByEmail.TryGetValue(v.Email, out var userId) ? userId : null,
                        Relationship = v.Relationship,
                        LeaseEndDate = v.LeaseEnd
                    };
                    created[v.DocKey] = resident;
                    dbContext.Residents.Add(resident);
                }

                residentId = resident.Id;
            }

            if (v.Unit is null || v.AlreadyAssigned) continue;

            dbContext.UnitResidents.Add(new UnitResident
            {
                CompanyId = v.Unit.CompanyId,
                UnitId = v.Unit.Id,
                ResidentId = residentId,
                IsPrimary = v.IsPrimary,
                StartDate = v.StartDate
            });
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict("Mientras se importaba se creó otro residente con el mismo documento o una asignación igual. Volvé a validar el archivo.");
        }

        result.Created = true;
        result.Message = $"Se crearon {created.Count} residentes y {result.NewAssignments} asignaciones a unidades.";
        logger.LogInformation(
            "Carga masiva de residentes: {People} personas nuevas y {Assignments} asignaciones en la empresa {CompanyId}, por el usuario {UserId}.",
            created.Count, result.NewAssignments, company.Value, User?.FindFirstValue(ClaimTypes.NameIdentifier));
        return Ok(result);
    }

    private static PeopleImportResultDto BuildResult(
        List<ResidentsImportService.ValidatedRow> validated, List<ResidentsImportService.ValidatedRow> valid, bool confirm)
    {
        var seenPeople = new HashSet<string>();
        var rows = new List<PeopleImportRowDto>();
        foreach (var v in validated)
        {
            var dto = new PeopleImportRowDto
            {
                RowNumber = v.Row.RowNumber,
                Name = v.FullName,
                Document = v.DocNumber,
                Email = v.Email,
                Building = v.Building?.Label ?? v.Row.Building,
                Unit = v.Unit?.Code ?? v.Row.Unit,
                Error = v.Error
            };

            if (v.Error is null)
            {
                var personKey = v.Existing is not null ? v.Existing.Id.ToString() : "new:" + v.DocKey;
                var first = seenPeople.Add(personKey);
                dto.Person = !first ? "Misma persona" : v.Existing is not null ? "Ya existe" : "Nueva";
                dto.Assignment = v.Unit is null ? "Sin unidad" : v.AlreadyAssigned ? "Ya estaba asignada" : "Se asigna";
            }

            rows.Add(dto);
        }

        return new PeopleImportResultDto
        {
            Confirmed = confirm,
            TotalRows = validated.Count,
            ValidRows = valid.Count,
            ErrorRows = validated.Count - valid.Count,
            NewPeople = valid.Where(v => v.Existing is null).Select(v => v.DocKey).Distinct().Count(),
            ExistingPeople = valid.Where(v => v.Existing is not null).Select(v => v.Existing!.Id).Distinct().Count(),
            NewAssignments = valid.Count(v => v.Unit is not null && !v.AlreadyAssigned),
            AlreadyAssigned = valid.Count(v => v.Unit is not null && v.AlreadyAssigned),
            Rows = rows
        };
    }
}
