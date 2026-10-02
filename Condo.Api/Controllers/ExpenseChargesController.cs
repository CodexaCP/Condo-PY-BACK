using System.Linq.Expressions;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

// Los cargos ya no se crean ni se editan a mano: salen de la liquidacion (gastos, ingresos y aportes repartidos por
// unidad) y asi cada uno conserva su origen. Las correcciones despues de publicar van por nota de credito. Este
// controlador queda para consultar los cargos y para limpiar los cargos manuales anteriores (legacy) mientras el
// periodo siga en borrador.
[ApiController]
[Authorize]
[Route("api/expense-charges")]
public class ExpenseChargesController(ICondoDbContext dbContext, IAccessScopeService accessScope, ITenantContext tenantContext) : ControllerBase
{
    // Cargo manual (legacy): no viene de una liquidacion, ni es mora, ni es un ajuste/nota de credito.
    private static readonly Expression<Func<ExpenseCharge, bool>> IsManualCharge =
        x => x.SourceSettlementId == null && !x.IsLateFee && !x.IsReversal && x.SourceCreditNoteId == null;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExpenseChargeDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        [FromQuery] Guid? expensePeriodId,
        [FromQuery] Guid? unitId,
        CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.ExpenseCharges
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
                query = query.Where(x => accessibleBuildingIds.Contains(x.Unit!.BuildingId));
            }
        }

        if (buildingId.HasValue)
        {
            query = query.Where(x => x.Unit!.BuildingId == buildingId.Value);
        }

        if (expensePeriodId.HasValue)
        {
            query = query.Where(x => x.ExpensePeriodId == expensePeriodId.Value);
        }

        if (unitId.HasValue)
        {
            query = query.Where(x => x.UnitId == unitId.Value);
        }

        var charges = await query
            .OrderByDescending(x => x.ExpensePeriod!.Year)
            .ThenByDescending(x => x.ExpensePeriod!.Month)
            .ThenBy(x => x.Unit!.Code)
            .ThenBy(x => x.Concept)
            .Select(ProjectToDto())
            .ToListAsync(cancellationToken);

        return Ok(charges);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExpenseChargeDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var charge = await dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(ProjectToDto())
            .FirstOrDefaultAsync(cancellationToken);

        if (charge is null)
        {
            return NotFound();
        }

        return await accessScope.CanAccessBuildingAsync(charge.BuildingId, cancellationToken) ? Ok(charge) : Forbid();
    }

    // Solo limpia cargos manuales (legacy) de un periodo en borrador. Los cargos de liquidacion se quitan anulando
    // la liquidacion; los de mora y los ajustes no se tocan.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!CanCleanLegacyCharges()) return Forbid();

        var entity = await dbContext.ExpenseCharges
            .Where(x => !x.IsDeleted && x.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == entity.ExpensePeriodId, cancellationToken);

        if (period is null)
        {
            return BadRequest("El periodo de expensas no existe.");
        }

        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var isManual = await dbContext.ExpenseCharges.Where(x => x.Id == id).Where(IsManualCharge).AnyAsync(cancellationToken);
        if (!isManual)
        {
            return BadRequest("Solo se pueden eliminar cargos manuales anteriores. Los cargos de una liquidación se quitan anulando la liquidación, y las correcciones después de publicar se hacen con una nota de crédito.");
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Los cargos solo se pueden eliminar mientras el periodo esta en borrador.");
        }

        var hasPayments = await dbContext.PaymentAllocations.AnyAsync(a => !a.IsDeleted && a.ExpenseChargeId == id, cancellationToken);
        if (hasPayments)
        {
            return Conflict("Este cargo tiene pagos aplicados y no se puede eliminar.");
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Mismos roles que gestionan la liquidacion: Encargado de edificio, Administrador de empresa y SuperAdmin.
    private bool CanCleanLegacyCharges() =>
        tenantContext.IsSuperAdmin
        || string.Equals(tenantContext.Role, "CompanyAdmin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tenantContext.Role, "BuildingManager", StringComparison.OrdinalIgnoreCase);

    private Expression<Func<ExpenseCharge, ExpenseChargeDto>> ProjectToDto() =>
        x => new ExpenseChargeDto
        {
            Id = x.Id,
            CompanyId = x.CompanyId,
            ExpensePeriodId = x.ExpensePeriodId,
            ExpensePeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty,
            BuildingId = x.Unit != null ? x.Unit.BuildingId : Guid.Empty,
            BuildingName = x.Unit != null && x.Unit.Building != null ? x.Unit.Building.Name : string.Empty,
            UnitId = x.UnitId,
            UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
            ChargeType = x.ChargeType,
            SourceBuildingExpenseId = x.SourceBuildingExpenseId,
            SourceBuildingExpenseDescription = x.SourceBuildingExpense != null ? x.SourceBuildingExpense.Description : string.Empty,
            SourceSettlementId = x.SourceSettlementId,
            SourceSettlementName = x.SourceSettlement != null ? x.SourceSettlement.ExpensePeriod!.Name : string.Empty,
            IsLateFee = x.IsLateFee,
            IsManual = x.SourceSettlementId == null && !x.IsLateFee && !x.IsReversal && x.SourceCreditNoteId == null,
            Concept = x.Concept,
            Amount = x.Amount,
            Notes = x.Notes,
            IsReversal = x.IsReversal,
            ReversalOfChargeId = x.ReversalOfChargeId,
            IsReversed = dbContext.ExpenseCharges.Any(r => !r.IsDeleted && r.ReversalOfChargeId == x.Id),
            TotalAllocated = dbContext.PaymentAllocations.Where(a => !a.IsDeleted && a.ExpenseChargeId == x.Id).Sum(a => (decimal?)a.AllocatedAmount) ?? 0m,
            PendingAmount = x.Amount - (dbContext.PaymentAllocations.Where(a => !a.IsDeleted && a.ExpenseChargeId == x.Id).Sum(a => (decimal?)a.AllocatedAmount) ?? 0m)
        };
}
