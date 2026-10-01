using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>Cuentas financieras del edificio (caja, bancos y fondo de reserva) con su saldo inicial a la fecha de arranque.</summary>
[Route("api/finance/accounts")]
public class FinanceAccountsController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate) : FinanceControllerBase(dbContext, accessScope, tenantContext, gate)
{
    private const int MaxNameLength = 200;
    private const decimal MaxOpeningBalance = 9_999_999_999_999m;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FinancialAccountDto>>> GetAll([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: false, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var accounts = await Db.FinancialAccounts.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .ToListAsync(cancellationToken);

        // Caja, bancos y fondo de reserva, y dentro de cada tipo por nombre (el orden de la enumeracion, no el del texto).
        return Ok(accounts.OrderBy(x => x.Type).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<FinancialAccountDto>> Create([FromBody] FinancialAccountUpsertRequest request, CancellationToken cancellationToken)
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

        var error = await ValidateAsync(request, request.BuildingId, null, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var entity = new FinancialAccount
        {
            CompanyId = companyId.Value,
            BuildingId = request.BuildingId,
            Name = request.Name.Trim(),
            Type = request.Type,
            OpeningBalance = decimal.Round(request.OpeningBalance, 2),
            IsActive = request.IsActive
        };

        Db.FinancialAccounts.Add(entity);
        var conflict = await SaveOrConflictAsync(cancellationToken);
        return conflict ?? Ok(ToDto(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<FinancialAccountDto>> Update(Guid id, [FromBody] FinancialAccountUpsertRequest request, CancellationToken cancellationToken)
    {
        var entity = await Db.FinancialAccounts.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var denied = await RequireModuleAsync(entity.BuildingId, write: true, cancellationToken, hideMissingAccess: true);
        if (denied is not null)
        {
            return denied;
        }

        var error = await ValidateAsync(request, entity.BuildingId, entity.Id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        entity.Name = request.Name.Trim();
        entity.Type = request.Type;
        entity.OpeningBalance = decimal.Round(request.OpeningBalance, 2);
        entity.IsActive = request.IsActive;

        var conflict = await SaveOrConflictAsync(cancellationToken);
        return conflict ?? Ok(ToDto(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await Db.FinancialAccounts.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var denied = await RequireModuleAsync(entity.BuildingId, write: true, cancellationToken, hideMissingAccess: true);
        if (denied is not null)
        {
            return denied;
        }

        // Todavia no hay movimientos: la baja es libre. Desde la fase 2 una cuenta con movimientos solo se podra desactivar.
        entity.IsDeleted = true;
        await Db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ActionResult?> ValidateAsync(
        FinancialAccountUpsertRequest request, Guid buildingId, Guid? excludeId, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return BadRequest("El nombre de la cuenta es obligatorio.");
        }

        if (name.Length > MaxNameLength)
        {
            return BadRequest($"El nombre no puede superar los {MaxNameLength} caracteres.");
        }

        if (!Enum.IsDefined(request.Type))
        {
            return BadRequest("El tipo de cuenta no es válido.");
        }

        if (request.OpeningBalance < 0)
        {
            return BadRequest("El saldo inicial no puede ser negativo.");
        }

        if (request.OpeningBalance > MaxOpeningBalance)
        {
            return BadRequest("El saldo inicial es demasiado grande.");
        }

        var others = Db.FinancialAccounts.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && (excludeId == null || x.Id != excludeId));

        var upperName = name.ToUpper();
        if (await others.AnyAsync(x => x.Name.ToUpper() == upperName, cancellationToken))
        {
            return Conflict("Ya existe una cuenta con ese nombre en este edificio.");
        }

        // Una sola caja y un solo fondo de reserva por edificio; los bancos pueden ser varios.
        if (request.Type != FinancialAccountType.Bank && await others.AnyAsync(x => x.Type == request.Type, cancellationToken))
        {
            return Conflict(request.Type == FinancialAccountType.Cash
                ? "Este edificio ya tiene una cuenta de caja. Editala o usá cuentas de banco para el resto."
                : "Este edificio ya tiene una cuenta de fondo de reserva.");
        }

        return null;
    }

    private static FinancialAccountDto ToDto(FinancialAccount x) => new()
    {
        Id = x.Id,
        BuildingId = x.BuildingId,
        Name = x.Name,
        Type = x.Type,
        OpeningBalance = x.OpeningBalance,
        IsActive = x.IsActive
    };
}
