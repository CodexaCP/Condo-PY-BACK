using System.Security.Claims;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

// Carga masiva de propietarios desde Excel (SuperAdmin y Administrador de empresa). Es independiente de la carga de residentes.
// Sin "confirm" solo valida y devuelve la vista previa; con "confirm" guarda todo, y solo si no hay ningun error (todo o nada).
[ApiController]
[Authorize]
[Route("api/owners")]
public class OwnersImportController(
    ICondoDbContext dbContext, IAccessScopeService accessScope, IPasswordHasher passwordHasher,
    OwnerCreditService credits, MarketplaceHandoverService marketplaceHandover, ILogger<OwnersImportController> logger) : ControllerBase
{
    [HttpGet("import-template")]
    public async Task<IActionResult> DownloadTemplate([FromQuery] Guid? companyId, CancellationToken cancellationToken)
    {
        if (!PeopleImportData.CanImport(accessScope)) return Forbid();

        var (company, error) = await PeopleImportData.ResolveCompanyAsync(dbContext, accessScope, companyId, cancellationToken);
        if (company is null) return BadRequest(error);

        var (buildings, units) = await PeopleImportData.LoadBuildingsAndUnitsAsync(dbContext, accessScope, company.Value, cancellationToken);

        return File(
            OwnersImportService.BuildTemplate(buildings, units),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "plantilla-propietarios.xlsx");
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

        List<OwnersImportService.ParsedRow> parsed;
        await using (var stream = file.OpenReadStream())
        {
            var (rows, parseError) = OwnersImportService.Parse(stream);
            if (rows is null) return BadRequest(parseError);
            parsed = rows;
        }

        if (parsed.Count == 0) return BadRequest("El archivo no tiene filas para importar: completá la plantilla desde la fila 5.");

        var context = await LoadContextAsync(company.Value, cancellationToken);
        var validated = OwnersImportService.Validate(parsed, context);
        var valid = validated.Where(v => v.Error is null).ToList();

        var result = BuildResult(validated, valid, confirm);
        if (!confirm) return Ok(result);

        if (result.ErrorRows > 0)
        {
            result.Message = "No se guardó nada: corregí las filas con errores y volvé a importar.";
            return Ok(result);
        }

        // ── Alta de las cuentas nuevas y asignacion a las unidades, todo en un solo guardado ──
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var password = OwnersImportService.InitialPassword(DateTime.UtcNow);
        var passwordHash = passwordHasher.Hash(password);   // un solo hash: la contraseña inicial es la misma para todas

        var created = new Dictionary<string, ApplicationUser>();
        var newPrimaries = new List<(Guid UnitId, Guid OwnerId)>();

        foreach (var v in valid)
        {
            Guid ownerId;
            if (v.Existing is not null)
            {
                ownerId = v.Existing.Id;
            }
            else
            {
                if (!created.TryGetValue(v.DocKey, out var owner))
                {
                    owner = new ApplicationUser
                    {
                        CompanyId = company.Value,
                        FirstName = v.FirstName,
                        LastName = v.LastName,
                        FullName = v.FullName,
                        Username = v.Username,
                        Email = v.Email,
                        PasswordHash = passwordHash,
                        DocumentType = v.DocType,
                        DocumentNumber = v.DocNumber,
                        Phone = v.Phone,
                        Role = UserRole.Owner,
                        IsActive = true,
                        IsResident = false,
                        MustChangePassword = true
                    };
                    created[v.DocKey] = owner;
                    dbContext.ApplicationUsers.Add(owner);
                }

                ownerId = owner.Id;
            }

            if (v.Unit is null || v.AlreadyAssigned) continue;

            dbContext.UnitOwners.Add(new UnitOwner
            {
                CompanyId = v.Unit.CompanyId,
                UnitId = v.Unit.Id,
                OwnerId = ownerId,
                IsPrimary = v.IsPrimary,
                StartDate = today,
                OwnershipPercentage = v.Percentage
            });

            if (v.IsPrimary)
            {
                // Mismo traspaso que al asignar un principal a mano: el saldo a favor retenido de la unidad pasa al nuevo principal.
                await credits.TransferUnitLotsAsync(
                    v.Unit.Id, null, ownerId, company.Value,
                    $"Traspasado a {v.FullName} al asignarlo como propietario principal en la carga masiva del {DateTime.UtcNow:dd/MM/yyyy}.", cancellationToken);
                newPrimaries.Add((v.Unit.Id, ownerId));
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict("Mientras se importaba se creó otra cuenta con el mismo correo, usuario o documento. Volvé a validar el archivo.");
        }

        // Marketplace: si la unidad esperaba a su nuevo principal (nota de cambio), se completa. Es un aviso interno: si falla,
        // la carga no se pierde.
        foreach (var (unitId, ownerId) in newPrimaries)
        {
            try
            {
                await marketplaceHandover.OnPrimaryAssignedAsync(unitId, ownerId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "No se pudo completar la nota de cambio de propietario principal de la unidad {UnitId} en la carga masiva.", unitId);
            }
        }

        result.Created = true;
        result.InitialPassword = created.Count > 0 ? password : null;
        result.Message = $"Se crearon {created.Count} propietarios y {result.NewAssignments} asignaciones a unidades.";
        logger.LogInformation(
            "Carga masiva de propietarios: {People} personas nuevas y {Assignments} asignaciones en la empresa {CompanyId}, por el usuario {UserId}.",
            created.Count, result.NewAssignments, company.Value, User?.FindFirstValue(ClaimTypes.NameIdentifier));
        return Ok(result);
    }

    // Lo que ya hay en la empresa y la validacion necesita conocer.
    private async Task<OwnersImportService.Context> LoadContextAsync(Guid companyId, CancellationToken ct)
    {
        var (buildings, units) = await PeopleImportData.LoadBuildingsAndUnitsAsync(dbContext, accessScope, companyId, ct);

        var users = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.CompanyId == companyId)
            .Select(x => new { x.Id, x.FullName, x.Email, x.DocumentNumber, x.Role, x.Username })
            .ToListAsync(ct);

        var ownersByDocument = new Dictionary<string, OwnersImportService.ExistingPerson>();
        var usersByEmail = new Dictionary<string, OwnersImportService.ExistingPerson>();
        var usernames = new HashSet<string>();
        foreach (var u in users)
        {
            var person = new OwnersImportService.ExistingPerson(u.Id, u.FullName, u.Email, u.DocumentNumber, u.Role);
            usersByEmail.TryAdd(u.Email.Trim().ToLowerInvariant(), person);
            if (u.Role == UserRole.Owner && !string.IsNullOrWhiteSpace(u.DocumentNumber))
                ownersByDocument.TryAdd(PeopleImportCommon.NormalizeDocument(u.DocumentNumber), person);
            if (!string.IsNullOrWhiteSpace(u.Username)) usernames.Add(u.Username.Trim().ToLowerInvariant());
        }

        var unitIds = units.Select(u => u.Id).ToList();
        var links = await dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null && unitIds.Contains(x.UnitId))
            .Select(x => new { x.UnitId, x.OwnerId, x.OwnershipPercentage, x.IsPrimary })
            .ToListAsync(ct);
        var unitOwners = links
            .GroupBy(x => x.UnitId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<OwnersImportService.ExistingUnitOwner>)g
                    .Select(x => new OwnersImportService.ExistingUnitOwner(x.OwnerId, x.OwnershipPercentage, x.IsPrimary)).ToList());

        return new OwnersImportService.Context
        {
            BuildingsByLabel = PeopleImportData.ByLabel(buildings),
            Units = PeopleImportData.ByKey(units),
            OwnersByDocument = ownersByDocument,
            UsersByEmail = usersByEmail,
            Usernames = usernames,
            UnitOwners = unitOwners
        };
    }

    private static PeopleImportResultDto BuildResult(
        List<OwnersImportService.ValidatedRow> validated, List<OwnersImportService.ValidatedRow> valid, bool confirm)
    {
        var seenPeople = new HashSet<string>();
        var rows = new List<PeopleImportRowDto>();
        foreach (var v in validated)
        {
            var dto = new PeopleImportRowDto
            {
                RowNumber = v.Row.RowNumber,
                Name = v.FullName,
                Document = v.DocNumber ?? string.Empty,
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
            InitialPassword = valid.Any(v => v.Existing is null) ? OwnersImportService.InitialPassword(DateTime.UtcNow) : null,
            Rows = rows
        };
    }
}
