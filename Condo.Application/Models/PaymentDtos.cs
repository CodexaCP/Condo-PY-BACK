using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class PaymentUpsertRequest
{
    public Guid ExpensePeriodId { get; set; }
    public Guid UnitId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;
    public string Reference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public class PaymentDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}
