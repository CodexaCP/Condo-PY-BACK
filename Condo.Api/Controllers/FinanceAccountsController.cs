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

        // Caja, bancos, fondo de reserva y cobros online por liquidar, y dentro de cada tipo por nombre (el orden de la enumeracion, no el del texto).
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

        if (request.Type == FinancialAccountType.Clearing)
        {
            return BadRequest(ClearingIsSystemMessage);
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
            IsActive = request.IsActive,
            ReconcileOverride = request.Reconciles
        };

        Db.FinancialAccounts.Add(entity);
        Audit.Add(companyId.Value, entity.BuildingId, ConfigSectionKeys.Chart, "Created",
            $"Se creó la cuenta «{entity.Name}» ({TypeLabel(entity.Type)}) con saldo inicial {entity.OpeningBalance:N0}.",
            "FinancialAccount", entity.Id);
        var conflict = await SaveOrConflictAsync(cancellationToken);
        return conflict ?? Ok(ToDto(entity));
    }

    private static string TypeLabel(FinancialAccountType type) => type switch
    {
        FinancialAccountType.Cash => "caja",
        FinancialAccountType.Bank => "banco",
        FinancialAccountType.ReserveFund => "fondo de reserva",
        FinancialAccountType.Clearing => "cobros online por liquidar",
        _ => type.ToString()
    };

    // La cuenta de cobros online por liquidar la crea y la mantiene el sistema (una por edificio): a mano no se crea, no se edita ni se elimina.
    private const string ClearingIsSystemMessage =
        "La cuenta de cobros online por liquidar la administra el sistema: no se crea, no se edita ni se elimina a mano. " +
        "Su saldo pasa al banco con una transferencia cuando el proveedor liquida.";

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

        if (entity.Type == FinancialAccountType.Clearing || request.Type == FinancialAccountType.Clearing)
        {
            return BadRequest(ClearingIsSystemMessage);
        }

        var error = await ValidateAsync(request, entity.BuildingId, entity.Id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        // Una cuenta con una conciliacion abierta sigue conciliandose: primero se cierra o se descarta.
        if (request.Reconciles == false && entity.Reconciles
            && await Db.BankReconciliations.AsNoTracking().AnyAsync(x => !x.IsDeleted && x.AccountId == entity.Id && x.Status == BankReconciliationStatus.Open, cancellationToken))
        {
            return Conflict("La cuenta tiene una conciliación abierta. Cerrala o descartala antes de dejar de conciliarla.");
        }

        var accountChanges = new List<ConfigChange>();
        var newName = request.Name.Trim();
        var newBalance = decimal.Round(request.OpeningBalance, 2);

        // Con meses cerrados el saldo inicial y el tipo de la cuenta no se tocan: moverian los saldos de esos meses.
        if ((entity.OpeningBalance != newBalance || entity.Type != request.Type)
            && await new FinancePeriodGuard(Db).AnyClosedAsync(entity.BuildingId, cancellationToken))
        {
            return FinancePeriodGuard.ClosedMonthsExistResponse("cambiar el saldo inicial o el tipo de una cuenta");
        }

        if (entity.Name != newName) accountChanges.Add(new ConfigChange("name", "Nombre", entity.Name, newName));
        if (entity.Type != request.Type) accountChanges.Add(new ConfigChange("type", "Tipo", TypeLabel(entity.Type), TypeLabel(request.Type)));
        if (entity.OpeningBalance != newBalance) accountChanges.Add(new ConfigChange("openingBalance", "Saldo inicial", $"{entity.OpeningBalance:N0}", $"{newBalance:N0}"));
        if (entity.IsActive != request.IsActive) accountChanges.Add(new ConfigChange("isActive", "Activa", entity.IsActive ? "Sí" : "No", request.IsActive ? "Sí" : "No"));
        var newReconciles = request.Reconciles ?? entity.Reconciles;
        if (entity.Reconciles != newReconciles) accountChanges.Add(new ConfigChange("reconciles", "Se concilia con extracto", entity.Reconciles ? "Sí" : "No", newReconciles ? "Sí" : "No"));
        if (accountChanges.Count > 0)
        {
            Audit.Add(entity.CompanyId, entity.BuildingId, ConfigSectionKeys.Chart, "Updated",
                $"Cuenta «{entity.Name}»: cambió {string.Join(", ", accountChanges.Select(x => x.Label))}.",
                "FinancialAccount", entity.Id, accountChanges);
        }

        entity.Name = newName;
        entity.Type = request.Type;
        entity.OpeningBalance = newBalance;
        entity.IsActive = request.IsActive;
        if (request.Reconciles.HasValue) entity.ReconcileOverride = request.Reconciles;

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

        if (entity.Type == FinancialAccountType.Clearing)
        {
            return BadRequest(ClearingIsSystemMessage);
        }

        // Con meses cerrados no se elimina una cuenta: sus saldos y movimientos ya estan cerrados.
        if (await new FinancePeriodGuard(Db).AnyClosedAsync(entity.BuildingId, cancellationToken))
        {
            return FinancePeriodGuard.ClosedMonthsExistResponse("eliminar una cuenta");
        }

        // Todavia no hay movimientos: la baja es libre. Desde la fase 2 una cuenta con movimientos solo se podra desactivar.
        entity.IsDeleted = true;
        Audit.Add(entity.CompanyId, entity.BuildingId, ConfigSectionKeys.Chart, "Deleted",
            $"Se eliminó la cuenta «{entity.Name}» ({TypeLabel(entity.Type)}).", "FinancialAccount", entity.Id);
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
        IsActive = x.IsActive,
        Reconciles = x.Reconciles
    };
}
