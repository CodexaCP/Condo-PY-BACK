using Condo.Application.Models;
using Condo.Domain.Entities;

namespace Condo.Application.Abstractions;

public interface IExpenseSettlementDistributionService
{
    Task<ExpenseSettlementChargePreviewDto> PreviewAsync(
        ExpensePeriod period,
        ExpenseSettlement settlement,
        CancellationToken cancellationToken);
}
