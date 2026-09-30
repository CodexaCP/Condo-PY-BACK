using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Api.Services;

public static class ChargeBaseQuery
{
    /// <summary>
    /// Base sobre la que se aplica el coeficiente de cada unidad ("Coeficiente X % de Y" en la factura): los cargos
    /// de expensa del periodo (ordinarios y de fondo de reserva) y las compensaciones por ingresos del reparto,
    /// sin el aporte extraordinario (que va aparte), la mora ni los cargos individuales.
    /// </summary>
    public static IQueryable<ExpenseCharge> CoefficientBase(this IQueryable<ExpenseCharge> charges, Guid expensePeriodId) =>
        charges.Where(x => !x.IsDeleted && !x.IsReversal && !x.IsLateFee
                           && x.ExpensePeriodId == expensePeriodId
                           && (x.ChargeType == ExpenseChargeType.Ordinary
                               || x.ChargeType == ExpenseChargeType.ReserveFund
                               || (x.ChargeType == ExpenseChargeType.Adjustment && x.SourceSettlementId != null)));
}
