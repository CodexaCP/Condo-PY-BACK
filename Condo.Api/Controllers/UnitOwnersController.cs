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
[Route("api/unit-owners")]
public class UnitOwnersController(
    ICondoDbContext dbContext, IAccessScopeService accessScope, IOwnerResidencySyncService residencySync,
    MarketplaceHandoverService marketplaceHandover, ILogger<UnitOwnersController> logger,
    OwnerCreditService credits, PushDispatcher push) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UnitOwnerDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
                query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            else
                query = query.Where(x => accessibleBuildingIds.Contains(x.Unit!.BuildingId));
        }

        if (buildingId.HasValue)
            query = query.Where(x => x.Unit!.BuildingId == buildingId.Value);

        var items = await query
            .OrderBy(x => x.Unit!.Building!.Name)
            .ThenBy(x => x.Unit!.Code)
            .ThenByDescending(x => x.IsPrimary)
            .Select(x => new UnitOwnerDto
            {
                Id = x.Id,
                UnitId = x.UnitId,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                BuildingId = x.Unit != null ? x.Unit.BuildingId : Guid.Empty,
                BuildingName = x.Unit != null && x.Unit.Building != null ? x.Unit.Building.Name : string.Empty,
                OwnerId = x.OwnerId,
                OwnerName = x.Owner != null ? x.Owner.FullName : string.Empty,
                IsPrimary = x.IsPrimary,
                StartDate = x.StartDate
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<UnitOwnerDto>> Create(
        [FromBody] CreateUnitOwnerRequest request,
        CancellationToken cancellationToken)
    {
        var unit = await dbContext.Units
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.UnitId, cancellationToken);

        if (unit is null) return NotFound("Unidad no encontrada.");

        if (!await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
            return Forbid();

        // Se permite vincular como propietario a cualquier cuenta Owner o Resident: una misma
        // persona puede ser propietaria de una unidad y residente de otra (o de la misma), sin
        // necesidad de cambiarle el rol base de la cuenta.
        var owner = await dbContext.ApplicationUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.OwnerId
                     && (x.Role == UserRole.Owner || x.Role == UserRole.Resident), cancellationToken);

        if (owner is null) return NotFound("Propietario no encontrado.");

        var companyId = unit.Building?.CompanyId ?? accessScope.CompanyId ?? Guid.Empty;

        // Crítico: el propietario debe pertenecer a la misma empresa que la unidad — sin este
        // chequeo se podría vincular una unidad a un usuario de otra empresa administradora,
        // dándole acceso a expensas, pagos, reclamos, comunicados y amenities ajenos.
        if (owner.CompanyId != companyId)
            return BadRequest("El propietario no pertenece a la misma empresa que la unidad.");

        var duplicate = await dbContext.UnitOwners
            .AnyAsync(x => !x.IsDeleted && x.UnitId == request.UnitId && x.OwnerId == request.OwnerId, cancellationToken);

        if (duplicate) return Conflict("Este propietario ya esta asignado a esta unidad.");

        var entity = new UnitOwner
        {
            UnitId = request.UnitId,
            OwnerId = request.OwnerId,
            IsPrimary = request.IsPrimary,
            StartDate = request.StartDate,
            CompanyId = companyId
        };

        // Otro propietario principal ya vigente: si el nuevo tambien es principal, la titularidad no queda sin principal.
        var hadOtherPrimary = request.IsPrimary && await dbContext.UnitOwners
            .AnyAsync(x => !x.IsDeleted && x.UnitId == request.UnitId && x.IsPrimary, cancellationToken);

        dbContext.UnitOwners.Add(entity);

        // Cambio de propietario: el saldo a favor de la unidad que quedo retenido pasa al nuevo propietario principal (en la misma
        // transaccion que el alta).
        var pendingPushes = new List<(Guid RecipientId, string Title, string Body, Guid UnitId)>();
        var transferred = 0m;
        if (request.IsPrimary && !hadOtherPrimary)
        {
            transferred = await credits.TransferUnitLotsAsync(
                request.UnitId, null, request.OwnerId, companyId,
                $"Traspasado a {owner.FullName} al asignarlo como propietario principal el {DateTime.UtcNow:dd/MM/yyyy}.", cancellationToken);
            if (transferred > 0m)
                AddTransferNotice(pendingPushes, companyId, request.OwnerId, unit.Id, unit.Code, transferred, unit.Building?.Name);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await SendTransferPushesAsync(pendingPushes);

        if (owner.IsResident)
            await residencySync.SyncAsync(owner, companyId, cancellationToken);

        // Marketplace: si primero se dio de baja al principal anterior, la nota de cambio queda esperando a este nuevo principal.
        if (request.IsPrimary && !hadOtherPrimary)
        {
            try
            {
                await marketplaceHandover.OnPrimaryAssignedAsync(request.UnitId, request.OwnerId, cancellationToken);
            }
            catch (Exception ex)
            {
                // La nota es un aviso interno: si falla, el alta del propietario no se pierde.
                logger.LogError(ex, "No se pudo completar la nota de cambio de propietario principal de la unidad {UnitId}.", request.UnitId);
            }
        }

        return Ok(new UnitOwnerDto
        {
            Id = entity.Id,
            UnitId = entity.UnitId,
            UnitCode = unit.Code,
            BuildingId = unit.BuildingId,
            BuildingName = unit.Building?.Name ?? string.Empty,
            OwnerId = entity.OwnerId,
            OwnerName = owner.FullName,
            IsPrimary = entity.IsPrimary,
            StartDate = entity.StartDate,
            TransferredCredit = transferred
        });
    }

    // Que pasaria al dar de baja a este propietario, sin hacerlo: sirve para avisar de la deuda o del saldo antes de confirmar.
    [HttpGet("{id:guid}/removal-preview")]
    public async Task<ActionResult<UnitOwnerRemovalPreviewDto>> RemovalPreview(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.UnitOwners.AsNoTracking()
            .Include(x => x.Owner)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        var unit = await dbContext.Units.AsNoTracking().FirstOrDefaultAsync(x => x.Id == entity.UnitId, cancellationToken);
        if (unit is null || !await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken)) return Forbid();

        var preview = new UnitOwnerRemovalPreviewDto
        {
            UnitCode = unit.Code,
            OwnerName = entity.Owner?.FullName ?? string.Empty,
            IsPrimary = entity.IsPrimary,
            CanRemove = true
        };
        if (!entity.IsPrimary) return Ok(preview);

        var remainingPrimary = await FindRemainingPrimaryAsync(entity, cancellationToken);
        preview.IsLastPrimary = remainingPrimary is null;

        if (remainingPrimary is null)
        {
            var debt = await GetUnitDebtAsync(unit, cancellationToken);
            preview.PendingDebt = debt.Total;
            preview.DebtPeriods = debt.Periods;
            if (debt.Total > 0m)
            {
                preview.CanRemove = false;
                preview.Message = DebtMessage(unit.Code, debt);
            }
        }

        var lots = await dbContext.OwnerCreditMovements.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Kind == OwnerCreditMovementKind.Generated && x.UnitId == unit.Id && x.OwnerId == entity.OwnerId
                        && x.CompanyId == unit.CompanyId && x.RemainingAmount > 0 && !x.OnHold)
            .Select(x => x.RemainingAmount)
            .ToListAsync(cancellationToken);
        var credit = lots.Sum();
        if (remainingPrimary is null) preview.CreditToHold = credit;
        else preview.CreditToTransfer = credit;

        return Ok(preview);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.UnitOwners
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null) return NotFound();

        var unit = await dbContext.Units
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == entity.UnitId, cancellationToken);

        if (unit is null || !await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
            return Forbid();

        var wasPrimary = entity.IsPrimary;
        var removedOwnerId = entity.OwnerId;
        var unitId = entity.UnitId;

        // Cambio de propietario: si es el unico propietario principal, la unidad cambia de manos y se exige liquidar antes toda su
        // deuda pendiente. Si queda otro principal, la unidad sigue con titular y no hace falta.
        Guid? remainingPrimary = null;
        if (wasPrimary)
        {
            remainingPrimary = await FindRemainingPrimaryAsync(entity, cancellationToken);
            if (remainingPrimary is null)
            {
                var debt = await GetUnitDebtAsync(unit, cancellationToken);
                if (debt.Total > 0m) return BadRequest(DebtMessage(unit.Code, debt));
            }
        }

        entity.IsDeleted = true;

        // El saldo a favor que vino de esta unidad: queda retenido hasta que haya nuevo propietario principal, o pasa directo al otro
        // principal que sigue (en la misma transaccion que la baja).
        var result = new UnitOwnerRemovalDto();
        var pendingPushes = new List<(Guid RecipientId, string Title, string Body, Guid UnitId)>();
        if (wasPrimary)
        {
            var removedName = await dbContext.ApplicationUsers.AsNoTracking()
                .Where(x => x.Id == removedOwnerId).Select(x => x.FullName).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

            if (remainingPrimary is { } next)
            {
                result.TransferredCredit = await credits.TransferUnitLotsAsync(
                    unitId, removedOwnerId, next, unit.CompanyId,
                    $"Traspasado al propietario principal que sigue en la unidad al darse de baja a {removedName} el {DateTime.UtcNow:dd/MM/yyyy}.", cancellationToken);
                if (result.TransferredCredit > 0m)
                    AddTransferNotice(pendingPushes, unit.CompanyId, next, unit.Id, unit.Code, result.TransferredCredit, null);
            }
            else
            {
                result.HeldCredit = await credits.HoldUnitLotsAsync(
                    unitId, removedOwnerId, unit.CompanyId,
                    $"Retenido: se dio de baja a {removedName} de la unidad el {DateTime.UtcNow:dd/MM/yyyy}; pasa al nuevo propietario principal.", cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await SendTransferPushesAsync(pendingPushes);

        // Marketplace: si se desvinculo al propietario principal, sus publicaciones se suspenden en el momento y, si le quedaron
        // reservas abiertas, se genera la nota interna para el personal (con quien pasa a ser el principal, si ya hay otro).
        if (wasPrimary)
        {
            try
            {
                var currentPrimary = await dbContext.UnitOwners
                    .AsNoTracking()
                    .Where(x => !x.IsDeleted && x.UnitId == unitId && x.IsPrimary)
                    .Select(x => (Guid?)x.OwnerId)
                    .FirstOrDefaultAsync(cancellationToken);
                await marketplaceHandover.OnPrimaryRemovedAsync(unitId, removedOwnerId, currentPrimary, cancellationToken);
            }
            catch (Exception ex)
            {
                // La nota es un aviso interno: si falla, la baja del propietario no se revierte.
                logger.LogError(ex, "No se pudo generar la nota de cambio de propietario principal de la unidad {UnitId}.", unitId);
            }
        }

        // 204 como siempre; 200 con el detalle solo si el saldo a favor de la unidad se retuvo o paso a otro propietario.
        return result.HeldCredit > 0m || result.TransferredCredit > 0m ? Ok(result) : NoContent();
    }

    // ── Cambio de propietario: deuda y saldo a favor de la unidad ─────────────

    // Otro propietario principal vigente de la unidad (el mas antiguo), si queda alguno despues de quitar a este.
    private async Task<Guid?> FindRemainingPrimaryAsync(UnitOwner removing, CancellationToken ct) =>
        await dbContext.UnitOwners.AsNoTracking()
            .Where(x => !x.IsDeleted && x.UnitId == removing.UnitId && x.IsPrimary && x.Id != removing.Id)
            .OrderBy(x => x.StartDate)
            .Select(x => (Guid?)x.OwnerId)
            .FirstOrDefaultAsync(ct);

    private sealed record UnitDebt(decimal Total, List<string> Periods);

    // Deuda pendiente de la unidad: todo lo que le falta pagar de los periodos publicados (expensas y mora), con la misma cuenta que usa
    // el pago de propietarios (reversos por nota de credito y pagos sin aplicar incluidos).
    private async Task<UnitDebt> GetUnitDebtAsync(Unit unit, CancellationToken ct)
    {
        var (charges, pending) = await credits.LoadPendingChargesAsync(new[] { unit.Id }, unit.CompanyId, false, ct);
        var open = charges.Where(c => pending.GetValueOrDefault(c.Id) > 0m).ToList();
        var total = open.Select(c => pending[c.Id]).Sum();
        var periods = open
            .OrderBy(c => c.ExpensePeriod?.Year).ThenBy(c => c.ExpensePeriod?.Month)
            .Select(c => c.ExpensePeriod?.Name ?? string.Empty)
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();
        return new UnitDebt(total, periods);
    }

    private static string DebtMessage(string unitCode, UnitDebt debt) =>
        $"No se puede dar de baja al propietario principal de la unidad {unitCode}: la unidad tiene una deuda pendiente de " +
        $"{MarketplaceNotices.Gs(debt.Total)}" + (debt.Periods.Count > 0 ? $" ({string.Join(", ", debt.Periods)})" : string.Empty) +
        ". Liquidá la deuda antes de cambiar de propietario.";

    private void AddTransferNotice(
        List<(Guid RecipientId, string Title, string Body, Guid UnitId)> pushes, Guid companyId, Guid recipientId,
        Guid unitId, string unitCode, decimal amount, string? buildingName)
    {
        var title = $"Saldo a favor de la unidad {unitCode}";
        var body = $"Se te traspasó {MarketplaceNotices.Gs(amount)} de saldo a favor que tenía la unidad {unitCode}" +
                   (string.IsNullOrWhiteSpace(buildingName) ? string.Empty : $" ({buildingName})") +
                   ". Se aplica en tu próximo pago.";

        dbContext.Notifications.Add(new Notification
        {
            CompanyId = companyId,
            RecipientId = recipientId,
            Type = NotificationType.SupplierCreditApplied,
            Title = OwnerCreditService.Clip(title, 200),
            Body = OwnerCreditService.Clip(body, 1000),
            EntityType = "Unit",
            EntityId = unitId
        });
        pushes.Add((recipientId, title, body, unitId));
    }

    private async Task SendTransferPushesAsync(IEnumerable<(Guid RecipientId, string Title, string Body, Guid UnitId)> pushes)
    {
        foreach (var p in pushes)
            await push.NotifyUserAsync(p.RecipientId, p.Title, p.Body, "Unit", p.UnitId, CancellationToken.None, nameof(NotificationType.SupplierCreditApplied));
    }
}
