using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/buildings")]
public partial class BuildingsController(ICondoDbContext dbContext, IAccessScopeService accessScope, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BuildingDto>>> GetAll(CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
            {
                if (accessScope.CondominiumId.HasValue)
                {
                    var condId = accessScope.CondominiumId.Value;
                    query = query.Where(x => x.CondominiumId == condId);
                }
                else
                {
                    var cid = accessScope.CompanyId.Value;
                    query = query.Where(x => x.CompanyId == cid || (x.Condominium != null && x.Condominium.CompanyId == cid));
                }
            }
            else
            {
                query = query.Where(x => accessibleBuildingIds.Contains(x.Id));
            }
        }

        var buildings = await query
            .OrderBy(x => x.Name)
            .Select(x => new BuildingDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                CondominiumId = x.CondominiumId,
                CondominiumName = x.Condominium != null ? x.Condominium.Name : string.Empty,
                Name = x.Name,
                Code = x.Code,
                Address = x.Address,
                IsActive = x.IsActive,
                Description = x.Description,
                ContactPhonePrefix = x.ContactPhonePrefix,
                ContactPhone = x.ContactPhone,
                ContactEmail = x.ContactEmail,
                LateFeeRatePercentage = x.LateFeeRatePercentage,
                LateFeeFrequency = x.LateFeeFrequency,
                BlockOverdueAmenityReservations = x.BlockOverdueAmenityReservations,
                UseStandardTemplates = x.UseStandardTemplates,
                InvoiceTemplateUrl = x.InvoiceTemplateUrl,
                InvoiceTemplateFileName = x.InvoiceTemplateFileName,
                CreditNoteTemplateUrl = x.CreditNoteTemplateUrl,
                CreditNoteTemplateFileName = x.CreditNoteTemplateFileName,
                ReceiptTemplateUrl = x.ReceiptTemplateUrl,
                ReceiptTemplateFileName = x.ReceiptTemplateFileName
            })
            .ToListAsync(cancellationToken);

        return Ok(buildings);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BuildingDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!await accessScope.CanAccessBuildingAsync(id, cancellationToken))
        {
            return Forbid();
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => new BuildingDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                CondominiumId = x.CondominiumId,
                CondominiumName = x.Condominium != null ? x.Condominium.Name : string.Empty,
                Name = x.Name,
                Code = x.Code,
                Address = x.Address,
                IsActive = x.IsActive,
                Description = x.Description,
                ContactPhonePrefix = x.ContactPhonePrefix,
                ContactPhone = x.ContactPhone,
                ContactEmail = x.ContactEmail,
                LateFeeRatePercentage = x.LateFeeRatePercentage,
                LateFeeFrequency = x.LateFeeFrequency,
                BlockOverdueAmenityReservations = x.BlockOverdueAmenityReservations,
                UseStandardTemplates = x.UseStandardTemplates,
                InvoiceTemplateUrl = x.InvoiceTemplateUrl,
                InvoiceTemplateFileName = x.InvoiceTemplateFileName,
                CreditNoteTemplateUrl = x.CreditNoteTemplateUrl,
                CreditNoteTemplateFileName = x.CreditNoteTemplateFileName,
                ReceiptTemplateUrl = x.ReceiptTemplateUrl,
                ReceiptTemplateFileName = x.ReceiptTemplateFileName
            })
            .FirstOrDefaultAsync(cancellationToken);

        return building is null ? NotFound() : Ok(building);
    }

    [HttpPost]
    public async Task<ActionResult<BuildingDto>> Create([FromBody] BuildingUpsertRequest request, CancellationToken cancellationToken)
    {
        var companyId = accessScope.IsSuperAdmin ? request.CompanyId : accessScope.CompanyId;

        if (!accessScope.IsSuperAdmin && !companyId.HasValue)
        {
            return Forbid();
        }

        if (companyId.HasValue && !await accessScope.CanManageCompanyAsync(companyId.Value, cancellationToken))
        {
            return Forbid();
        }

        // Admin de condominio solo puede asociar edificios a su propio condominio
        if (accessScope.IsCompanyAdmin && accessScope.CondominiumId.HasValue
            && request.CondominiumId.HasValue
            && request.CondominiumId.Value != accessScope.CondominiumId.Value)
        {
            return Forbid();
        }

        var condominium = await ResolveCondominiumAsync(companyId, request.CondominiumId, cancellationToken);
        if (request.CondominiumId.HasValue && condominium is null)
        {
            return BadRequest("El condominio no existe o no pertenece a la empresa seleccionada.");
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var normalizedName = request.Name.Trim();
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var normalizedAddress = request.Address.Trim();

        if (companyId.HasValue)
        {
            var duplicatedCode = await dbContext.Buildings
                .AsNoTracking()
                .AnyAsync(x => !x.IsDeleted && x.CompanyId == companyId.Value && x.Code == normalizedCode, cancellationToken);

            if (duplicatedCode)
            {
                return Conflict("Ya existe un edificio con ese codigo dentro de la empresa.");
            }
        }

        var entity = new Building
        {
            CompanyId = companyId,
            CondominiumId = request.CondominiumId,
            Name = normalizedName,
            Code = normalizedCode,
            Address = normalizedAddress,
            IsActive = request.IsActive,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ContactPhonePrefix = string.IsNullOrWhiteSpace(request.ContactPhonePrefix) ? null : request.ContactPhonePrefix.Trim(),
            ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim(),
            ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim().ToLowerInvariant(),
            LateFeeRatePercentage = NormalizedLateFeeRate(request),
            LateFeeFrequency = NormalizedLateFeeRate(request).HasValue ? request.LateFeeFrequency : null,
            BlockOverdueAmenityReservations = request.BlockOverdueAmenityReservations
        };
        ApplyTemplates(entity, request);

        dbContext.Buildings.Add(entity);

        // Auto-assign demo plan — every new building starts with a 45-day free trial
        var demoPlanId = Guid.Parse("A0000000-0000-0000-0000-000000000001");
        var today = DateTime.UtcNow;
        dbContext.BuildingPlans.Add(new BuildingPlan
        {
            PlanId = demoPlanId,
            BuildingId = entity.Id,
            AssignmentScope = PlanAssignmentScope.Building,
            ScopeEntityId = entity.Id,
            StartDate = today,
            EndDate = today.AddDays(45),
            IsActive = true,
            IsArchived = false,
            AssignedById = tenantContext.UserId
        });

        var demoPlan = await dbContext.Plans.FirstOrDefaultAsync(x => x.Id == demoPlanId, cancellationToken);
        if (demoPlan is not null && !demoPlan.IsAssigned)
            demoPlan.IsAssigned = true;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueCodeViolation(exception))
        {
            return Conflict("Ya existe un edificio con ese codigo dentro de la empresa.");
        }

        entity.Condominium = condominium;
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BuildingDto>> Update(Guid id, [FromBody] BuildingUpsertRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Buildings
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        var effectiveCompanyId = await GetEffectiveCompanyIdAsync(entity, cancellationToken);
        var isBuildingManager = User.IsInRole("BuildingManager");
        if (isBuildingManager)
        {
            if (!await accessScope.CanAccessBuildingAsync(id, cancellationToken))
            {
                return Forbid();
            }
        }
        else if (effectiveCompanyId.HasValue)
        {
            if (!await accessScope.CanManageCompanyAsync(effectiveCompanyId.Value, cancellationToken))
            {
                return Forbid();
            }
        }
        else if (!accessScope.IsSuperAdmin)
        {
            return Forbid();
        }

        var requestedCondominiumId = isBuildingManager ? entity.CondominiumId : request.CondominiumId;
        var condominium = await ResolveCondominiumAsync(entity.CompanyId, requestedCondominiumId, cancellationToken);
        if (requestedCondominiumId.HasValue && condominium is null)
        {
            return BadRequest("El condominio no existe o no pertenece a la empresa seleccionada.");
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var normalizedName = request.Name.Trim();
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var normalizedAddress = request.Address.Trim();

        if (entity.CompanyId.HasValue)
        {
            var duplicatedCode = await dbContext.Buildings
                .AsNoTracking()
                .AnyAsync(x => !x.IsDeleted && x.CompanyId == entity.CompanyId.Value && x.Id != id && x.Code == normalizedCode, cancellationToken);

            if (duplicatedCode)
            {
                return Conflict("Ya existe un edificio con ese codigo dentro de la empresa.");
            }
        }

        var previousRate = entity.LateFeeRatePercentage;
        var previousFrequency = entity.LateFeeFrequency;
        var newRate = NormalizedLateFeeRate(request);
        var newFrequency = newRate.HasValue ? request.LateFeeFrequency : null;

        entity.CondominiumId = requestedCondominiumId;
        entity.Name = normalizedName;
        entity.Code = normalizedCode;
        entity.Address = normalizedAddress;
        if (!isBuildingManager)
        {
            entity.IsActive = request.IsActive;
        }
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.ContactPhonePrefix = string.IsNullOrWhiteSpace(request.ContactPhonePrefix) ? null : request.ContactPhonePrefix.Trim();
        entity.ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim();
        entity.ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim().ToLowerInvariant();
        entity.LateFeeRatePercentage = newRate;
        entity.LateFeeFrequency = newFrequency;
        entity.BlockOverdueAmenityReservations = request.BlockOverdueAmenityReservations;
        if (request.UseStandardTemplates.HasValue)
            ApplyTemplates(entity, request);

        var lateFeeChanged = previousRate != newRate || previousFrequency != newFrequency;
        if (lateFeeChanged && effectiveCompanyId.HasValue)
        {
            await QueueLateFeeChangeNotificationsAsync(
                entity, effectiveCompanyId.Value,
                previousRate, previousFrequency,
                newRate, newFrequency,
                cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueCodeViolation(exception))
        {
            return Conflict("Ya existe un edificio con ese codigo dentro de la empresa.");
        }

        entity.Condominium = condominium;
        return Ok(ToDto(entity));
    }

    private static decimal? NormalizedLateFeeRate(BuildingUpsertRequest request) =>
        request.LateFeeRatePercentage is > 0m ? decimal.Round(request.LateFeeRatePercentage.Value, 2) : null;

    private static string LateFeeConfigLabel(decimal? rate, LateFeeFrequency? frequency) =>
        rate.HasValue && frequency.HasValue
            ? $"{rate.Value:0.##}% {LateFeeFrequencyLabel(frequency.Value)}"
            : "sin mora";

    private static string LateFeeFrequencyLabel(LateFeeFrequency frequency) => frequency switch
    {
        LateFeeFrequency.Daily => "diario",
        LateFeeFrequency.Weekly => "semanal",
        LateFeeFrequency.Biweekly => "quincenal",
        _ => frequency.ToString()
    };

    private async Task QueueLateFeeChangeNotificationsAsync(
        Building building,
        Guid companyId,
        decimal? previousRate, LateFeeFrequency? previousFrequency,
        decimal? newRate, LateFeeFrequency? newFrequency,
        CancellationToken cancellationToken)
    {
        var actorId = tenantContext.UserId;
        var actorName = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => x.Id == actorId)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Un administrador";

        var recipientIds = new HashSet<Guid>();

        // Propietarios de unidades del edificio
        recipientIds.UnionWith(await dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == building.Id)
            .Select(x => x.OwnerId)
            .ToListAsync(cancellationToken));

        // Residentes activos (vinculados directamente por Id de usuario)
        recipientIds.UnionWith(await dbContext.UnitResidents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null
                     && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == building.Id
                     && x.Resident != null && !x.Resident.IsDeleted && x.Resident.ApplicationUserId != null)
            .Select(x => x.Resident!.ApplicationUserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken));

        // Managers con acceso al edificio
        recipientIds.UnionWith(await dbContext.UserBuildingAccesses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == building.Id)
            .Select(x => x.ApplicationUserId)
            .ToListAsync(cancellationToken));

        // Admins y operadores de la empresa
        recipientIds.UnionWith(await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.CompanyId == companyId
                     && (x.Role == UserRole.CompanyAdmin || x.Role == UserRole.CompanyOperator))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken));

        var previousLabel = LateFeeConfigLabel(previousRate, previousFrequency);
        var newLabel = LateFeeConfigLabel(newRate, newFrequency);
        var changedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        var body = $"{actorName} cambió el interés por mora del edificio {building.Name} de «{previousLabel}» a «{newLabel}» el {changedAt}.";

        foreach (var recipientId in recipientIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                CompanyId = companyId,
                RecipientId = recipientId,
                Type = NotificationType.LateFeeConfigChanged,
                Title = "Cambio en interés por mora",
                Body = body,
                EntityType = "Building",
                EntityId = building.Id
            });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Buildings.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var effectiveCompanyId = await GetEffectiveCompanyIdAsync(entity, cancellationToken);
        if (effectiveCompanyId.HasValue)
        {
            if (!await accessScope.CanManageCompanyAsync(effectiveCompanyId.Value, cancellationToken))
            {
                return Forbid();
            }
        }
        else if (!accessScope.IsSuperAdmin)
        {
            return Forbid();
        }

        var hasUnits = await dbContext.Units
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == entity.Id, cancellationToken);

        var hasPeriods = await dbContext.ExpensePeriods
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == entity.Id, cancellationToken);

        var hasAccesses = await dbContext.UserBuildingAccesses
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == entity.Id, cancellationToken);

        var hasExpenses = await dbContext.BuildingExpenses
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == entity.Id, cancellationToken);

        var hasIncomes = await dbContext.BuildingIncomes
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == entity.Id, cancellationToken);

        var hasSettlements = await dbContext.ExpenseSettlements
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == entity.Id, cancellationToken);

        if (hasUnits || hasPeriods || hasAccesses || hasExpenses || hasIncomes || hasSettlements)
        {
            return BadRequest("No se puede eliminar el edificio porque tiene unidades, periodos, accesos o movimientos asociados.");
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<Guid?> GetEffectiveCompanyIdAsync(Building entity, CancellationToken cancellationToken)
    {
        if (entity.CompanyId.HasValue) return entity.CompanyId;
        if (!entity.CondominiumId.HasValue) return null;

        return await dbContext.Condominiums
            .AsNoTracking()
            .Where(x => x.Id == entity.CondominiumId.Value && !x.IsDeleted)
            .Select(x => (Guid?)x.CompanyId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? ValidateRequest(BuildingUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name.Trim()))
        {
            return "El nombre es obligatorio.";
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return "El codigo es obligatorio.";
        }

        if (!CodeRegex().IsMatch(normalizedCode))
        {
            return "El codigo solo puede contener letras, numeros y guiones medios.";
        }

        if (string.IsNullOrWhiteSpace(request.Address.Trim()))
        {
            return "La direccion es obligatoria.";
        }

        if (request.LateFeeRatePercentage is < 0m or > 100m)
        {
            return "La tasa de interés por mora debe estar entre 0 y 100.";
        }

        if (request.LateFeeRatePercentage is > 0m && !request.LateFeeFrequency.HasValue)
        {
            return "Definí el incremento de la mora (diario, semanal o quincenal).";
        }

        if (request.UseStandardTemplates == false &&
            (string.IsNullOrWhiteSpace(request.InvoiceTemplateUrl) ||
             string.IsNullOrWhiteSpace(request.CreditNoteTemplateUrl) ||
             string.IsNullOrWhiteSpace(request.ReceiptTemplateUrl)))
        {
            return "Adjuntá los 3 modelos (factura, nota de crédito y comprobante) o marcá \"Usar modelos estándar de CONDOPY\".";
        }

        return null;
    }

    // Con modelos estandar no se guardan adjuntos: si el edificio vuelve al estandar se limpian los propios.
    private static void ApplyTemplates(Building entity, BuildingUpsertRequest request)
    {
        var standard = request.UseStandardTemplates ?? true;
        entity.UseStandardTemplates = standard;
        entity.InvoiceTemplateUrl = standard ? null : TrimOrNull(request.InvoiceTemplateUrl);
        entity.InvoiceTemplateFileName = standard ? null : TrimOrNull(request.InvoiceTemplateFileName);
        entity.CreditNoteTemplateUrl = standard ? null : TrimOrNull(request.CreditNoteTemplateUrl);
        entity.CreditNoteTemplateFileName = standard ? null : TrimOrNull(request.CreditNoteTemplateFileName);
        entity.ReceiptTemplateUrl = standard ? null : TrimOrNull(request.ReceiptTemplateUrl);
        entity.ReceiptTemplateFileName = standard ? null : TrimOrNull(request.ReceiptTemplateFileName);
    }

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsUniqueCodeViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        (sqlException.Number == 2601 || sqlException.Number == 2627) &&
        sqlException.Message.Contains("IX_Buildings_CompanyId_Code", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Z0-9]+(?:-[A-Z0-9]+)*$")]
    private static partial Regex CodeRegex();

    private Task<Condominium?> ResolveCondominiumAsync(Guid? companyId, Guid? condominiumId, CancellationToken cancellationToken)
    {
        if (!condominiumId.HasValue) return Task.FromResult<Condominium?>(null);

        var query = dbContext.Condominiums.Where(x => !x.IsDeleted && x.Id == condominiumId.Value);
        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    private static BuildingDto ToDto(Building entity) =>
        new()
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
            CondominiumId = entity.CondominiumId,
            CondominiumName = entity.Condominium != null ? entity.Condominium.Name : string.Empty,
            Name = entity.Name,
            Code = entity.Code,
            Address = entity.Address,
            IsActive = entity.IsActive,
            Description = entity.Description,
            ContactPhonePrefix = entity.ContactPhonePrefix,
            ContactPhone = entity.ContactPhone,
            ContactEmail = entity.ContactEmail,
            LateFeeRatePercentage = entity.LateFeeRatePercentage,
            LateFeeFrequency = entity.LateFeeFrequency,
            BlockOverdueAmenityReservations = entity.BlockOverdueAmenityReservations,
            UseStandardTemplates = entity.UseStandardTemplates,
            InvoiceTemplateUrl = entity.InvoiceTemplateUrl,
            InvoiceTemplateFileName = entity.InvoiceTemplateFileName,
            CreditNoteTemplateUrl = entity.CreditNoteTemplateUrl,
            CreditNoteTemplateFileName = entity.CreditNoteTemplateFileName,
            ReceiptTemplateUrl = entity.ReceiptTemplateUrl,
            ReceiptTemplateFileName = entity.ReceiptTemplateFileName
        };
}
