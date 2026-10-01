using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Plan de cuentas del edificio: arbol de dos niveles (rubro y subrubro) con codigos editables. Nace de la plantilla
/// estandar al habilitar el modulo; los rubros de la plantilla se pueden renombrar, recodificar y desactivar, pero no
/// mover ni eliminar (el libro derivado los usa para mapear las categorias de gastos e ingresos).
/// </summary>
[Route("api/finance/categories")]
public class FinanceCategoriesController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate) : FinanceControllerBase(dbContext, accessScope, tenantContext, gate)
{
    private const int MaxCodeLength = 30;
    private const int MaxNameLength = 200;
    private const int MaxExternalCodeLength = 50;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LedgerCategoryDto>>> GetAll([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: false, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var items = await Db.LedgerCategories.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .ToListAsync(cancellationToken);

        var parentIds = items.Where(x => x.ParentId.HasValue).Select(x => x.ParentId!.Value).ToHashSet();

        return Ok(items
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .Select(x => ToDto(x, parentIds.Contains(x.Id)))
            .ToList());
    }

    [HttpPost]
    public async Task<ActionResult<LedgerCategoryDto>> Create([FromBody] LedgerCategoryUpsertRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(request.BuildingId, write: true, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var companyId = await ResolveCompanyIdAsync(request.BuildingId, cancellationToken);
        if (!companyId.HasValue)
        {
            return BadRequest(NoCompanyMessage);
        }

        var (error, parent) = await ValidateAsync(request, request.BuildingId, null, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var entity = new LedgerCategory
        {
            CompanyId = companyId.Value,
            BuildingId = request.BuildingId,
            ParentId = parent?.Id,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Type = parent?.Type ?? request.Type,
            ExternalCode = NormalizeExternalCode(request.ExternalCode),
            IsActive = request.IsActive
        };

        Db.LedgerCategories.Add(entity);
        var conflict = await SaveOrConflictAsync(cancellationToken);
        return conflict ?? Ok(ToDto(entity, false));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LedgerCategoryDto>> Update(Guid id, [FromBody] LedgerCategoryUpsertRequest request, CancellationToken cancellationToken)
    {
        var entity = await Db.LedgerCategories.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var denied = await RequireModuleAsync(entity.BuildingId, write: true, cancellationToken, hideMissingAccess: true);
        if (denied is not null)
        {
            return denied;
        }

        var hasChildren = await Db.LedgerCategories.AnyAsync(x => !x.IsDeleted && x.ParentId == entity.Id, cancellationToken);

        var (error, parent) = await ValidateAsync(request, entity.BuildingId, entity, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (entity.SystemKey is not null)
        {
            // Rubro de la plantilla: su lugar en el arbol y su tipo no cambian.
            if (request.ParentId != entity.ParentId || (parent is null && request.Type != entity.Type))
            {
                return BadRequest("Los rubros de la plantilla no se pueden mover ni cambiar de tipo. Podés renombrarlos, cambiarles el código o desactivarlos.");
            }
        }
        else
        {
            if (hasChildren && (request.ParentId.HasValue || request.Type != entity.Type))
            {
                return BadRequest("Este rubro tiene subrubros: no se le puede asignar un rubro padre ni cambiar su tipo.");
            }

            entity.ParentId = parent?.Id;
            entity.Type = parent?.Type ?? request.Type;
        }

        entity.Code = request.Code.Trim();
        entity.Name = request.Name.Trim();
        entity.ExternalCode = NormalizeExternalCode(request.ExternalCode);
        entity.IsActive = request.IsActive;

        var conflict = await SaveOrConflictAsync(cancellationToken);
        return conflict ?? Ok(ToDto(entity, hasChildren));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await Db.LedgerCategories.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var denied = await RequireModuleAsync(entity.BuildingId, write: true, cancellationToken, hideMissingAccess: true);
        if (denied is not null)
        {
            return denied;
        }

        if (entity.SystemKey is not null)
        {
            return BadRequest("Los rubros de la plantilla no se eliminan: si no los usás, desactivalos.");
        }

        if (await Db.LedgerCategories.AnyAsync(x => !x.IsDeleted && x.ParentId == entity.Id, cancellationToken))
        {
            return BadRequest("El rubro tiene subrubros. Eliminá o movés primero sus subrubros.");
        }

        // Todavia no hay movimientos ni presupuesto que lo usen. Desde las fases 2 y 3 un rubro usado solo se podra desactivar.
        entity.IsDeleted = true;
        await Db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Valida el pedido y resuelve el rubro padre. `current` es el rubro que se edita (null al crear).
    private async Task<(ActionResult? Error, LedgerCategory? Parent)> ValidateAsync(
        LedgerCategoryUpsertRequest request, Guid buildingId, LedgerCategory? current, CancellationToken cancellationToken)
    {
        var code = request.Code?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            return (BadRequest("El código del rubro es obligatorio."), null);
        }

        if (code.Length > MaxCodeLength)
        {
            return (BadRequest($"El código no puede superar los {MaxCodeLength} caracteres."), null);
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return (BadRequest("El nombre del rubro es obligatorio."), null);
        }

        if (name.Length > MaxNameLength)
        {
            return (BadRequest($"El nombre no puede superar los {MaxNameLength} caracteres."), null);
        }

        if ((request.ExternalCode?.Trim().Length ?? 0) > MaxExternalCodeLength)
        {
            return (BadRequest($"El código del contador no puede superar los {MaxExternalCodeLength} caracteres."), null);
        }

        if (!Enum.IsDefined(request.Type))
        {
            return (BadRequest("El tipo de rubro no es válido."), null);
        }

        LedgerCategory? parent = null;
        if (request.ParentId.HasValue)
        {
            if (current is not null && request.ParentId.Value == current.Id)
            {
                return (BadRequest("Un rubro no puede ser su propio padre."), null);
            }

            parent = await Db.LedgerCategories.AsNoTracking()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.ParentId.Value && x.BuildingId == buildingId, cancellationToken);
            if (parent is null)
            {
                return (BadRequest("El rubro padre no existe en este edificio."), null);
            }

            if (parent.ParentId.HasValue)
            {
                return (BadRequest("El plan de cuentas tiene dos niveles: el rubro padre no puede ser a su vez un subrubro."), null);
            }

            if (!parent.IsActive && (current is null || current.ParentId != parent.Id))
            {
                return (BadRequest("El rubro padre está desactivado."), null);
            }
        }

        var upperCode = code.ToUpper();
        var duplicated = await Db.LedgerCategories.AsNoTracking().AnyAsync(
            x => !x.IsDeleted && x.BuildingId == buildingId && x.Code.ToUpper() == upperCode
                 && (current == null || x.Id != current.Id),
            cancellationToken);
        if (duplicated)
        {
            return (Conflict("Ya existe un rubro con ese código en este edificio."), null);
        }

        return (null, parent);
    }

    private static string? NormalizeExternalCode(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static LedgerCategoryDto ToDto(LedgerCategory x, bool hasChildren) => new()
    {
        Id = x.Id,
        BuildingId = x.BuildingId,
        ParentId = x.ParentId,
        Code = x.Code,
        Name = x.Name,
        Type = x.Type,
        ExternalCode = x.ExternalCode,
        SystemKey = x.SystemKey,
        IsActive = x.IsActive,
        IsTemplate = x.SystemKey is not null,
        HasChildren = hasChildren
    };
}
