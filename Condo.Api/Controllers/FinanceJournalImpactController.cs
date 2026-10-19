using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Aviso previo a un hecho que cambia lo ya asentado: revertir un pago o deshacer la publicacion de un periodo. Responde cuantos asientos
/// vigentes del Libro Diario salen de ese pago o de los gastos e ingresos de ese periodo, para avisar que se va a generar un contraasiento.
/// Es solo informativo y nunca falla por Finanzas: sin el modulo, sin permiso o sin asientos responde «nada asentado».
/// </summary>
[Route("api/finance/journal/impact")]
public class FinanceJournalImpactController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    JournalPostingService journal) : FinanceControllerBase(dbContext, accessScope, tenantContext, gate)
{
    [HttpGet]
    public async Task<ActionResult<JournalImpactDto>> Get(
        [FromQuery] Guid? ownerPaymentId, [FromQuery] Guid? periodId, [FromQuery] Guid? expenseId, [FromQuery] Guid? transferId,
        CancellationToken cancellationToken)
    {
        if (!IsFinanceRole || new[] { ownerPaymentId, periodId, expenseId, transferId }.Count(x => x.HasValue) != 1) return Ok(new JournalImpactDto());

        if (transferId is { } transfer)
        {
            var building = await Db.FinanceTransfers.AsNoTracking()
                .Where(x => !x.IsDeleted && x.Id == transfer).Select(x => (Guid?)x.BuildingId).FirstOrDefaultAsync(cancellationToken);
            if (building is null) return Ok(new JournalImpactDto());

            var allowed = await UsableBuildingsAsync([building.Value], cancellationToken);
            return Ok(await WithPostedThroughAsync(
                await journal.ImpactAsync(allowed, LedgerSourceType.Transfer, [transfer], cancellationToken), allowed));
        }

        if (expenseId is { } expense)
        {
            var building = await Db.BuildingExpenses.AsNoTracking()
                .Where(x => !x.IsDeleted && x.Id == expense).Select(x => (Guid?)x.BuildingId).FirstOrDefaultAsync(cancellationToken);
            if (building is null) return Ok(new JournalImpactDto());

            var allowed = await UsableBuildingsAsync([building.Value], cancellationToken);
            return Ok(await WithPostedThroughAsync(
                await journal.ImpactAsync(allowed, LedgerSourceType.BuildingExpense, [expense], cancellationToken), allowed));
        }

        if (ownerPaymentId is { } paymentId)
        {
            var owner = await Db.OwnerPayments.AsNoTracking()
                .Where(x => !x.IsDeleted && x.Id == paymentId)
                .Select(x => new { x.CompanyId, x.Reference })
                .FirstOrDefaultAsync(cancellationToken);
            if (owner is null) return Ok(new JournalImpactDto());

            var payments = await Db.Payments.AsNoTracking()
                .Where(x => !x.IsDeleted && x.CompanyId == owner.CompanyId && x.Reference == owner.Reference)
                .Select(x => new { x.Id, BuildingId = x.Unit!.BuildingId })
                .ToListAsync(cancellationToken);
            var buildings = await UsableBuildingsAsync(payments.Select(p => p.BuildingId), cancellationToken);
            return Ok(await WithPostedThroughAsync(
                await journal.ImpactAsync(buildings, LedgerSourceType.OwnerPayment, payments.Select(p => p.Id).ToList(), cancellationToken), buildings));
        }

        var period = await Db.ExpensePeriods.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == periodId!.Value)
            .Select(x => new { x.BuildingId })
            .FirstOrDefaultAsync(cancellationToken);
        if (period is null) return Ok(new JournalImpactDto());

        var usable = await UsableBuildingsAsync([period.BuildingId], cancellationToken);
        if (usable.Count == 0) return Ok(new JournalImpactDto());

        var expenseIds = await Db.BuildingExpenses.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == periodId!.Value).Select(x => x.Id).ToListAsync(cancellationToken);
        var incomeIds = await Db.BuildingIncomes.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == periodId!.Value).Select(x => x.Id).ToListAsync(cancellationToken);
        var fromExpenses = await journal.ImpactAsync(usable, LedgerSourceType.BuildingExpense, expenseIds, cancellationToken);
        var fromIncomes = await journal.ImpactAsync(usable, LedgerSourceType.BuildingIncome, incomeIds, cancellationToken);

        var impact = new JournalImpactDto
        {
            Posted = fromExpenses.Posted || fromIncomes.Posted,
            PostedCount = fromExpenses.PostedCount + fromIncomes.PostedCount,
            Labels = fromExpenses.Labels.Concat(fromIncomes.Labels).Take(5).ToList()
        };
        return Ok(await WithPostedThroughAsync(impact, usable));
    }

    // Edificios del usuario con Finanzas disponible: los demas no tienen Libro Diario que avisar.
    private async Task<List<Guid>> UsableBuildingsAsync(IEnumerable<Guid> buildingIds, CancellationToken cancellationToken)
    {
        var usable = new List<Guid>();
        foreach (var id in buildingIds.Distinct())
        {
            if (id == Guid.Empty || !await AccessScope.CanAccessBuildingAsync(id, cancellationToken)) continue;
            if ((await Gate.GetStateAsync(id, cancellationToken)).IsAvailable) usable.Add(id);
        }

        return usable;
    }

    private async Task<JournalImpactDto> WithPostedThroughAsync(JournalImpactDto impact, IReadOnlyCollection<Guid> buildings)
    {
        if (!impact.Posted || buildings.Count == 0) return impact;

        impact.PostedThrough = await Db.FinanceSettings.AsNoTracking()
            .Where(x => !x.IsDeleted && buildings.Contains(x.BuildingId))
            .Select(x => x.JournalPostedThrough).MaxAsync();
        return impact;
    }
}
