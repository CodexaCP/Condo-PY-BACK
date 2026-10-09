namespace Condo.Application.Models;

// ── Cuentas por pagar (facturas de proveedores) ───────────────────────────────

public class PayableAgingBucketDto
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Total { get; set; }
}

public class PayablesSummaryDto
{
    // A pagar: con vencimiento y sin pago registrado.
    public int PendingCount { get; set; }
    public decimal PendingTotal { get; set; }
    // De lo anterior, lo que ya venció.
    public int OverdueCount { get; set; }
    public decimal OverdueTotal { get; set; }
    // Lo que vence en los proximos 7 dias (incluido hoy).
    public decimal DueNext7DaysTotal { get; set; }
    // Lo vencido por antiguedad (1-30, 31-60, 61-90 y mas de 90 dias).
    public IReadOnlyList<PayableAgingBucketDto> Aging { get; set; } = [];
}

public class PayableRowDto
{
    public Guid ExpenseId { get; set; }
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierRuc { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    // Dias de atraso si esta vencida y sin pagar; 0 en los demas casos.
    public int DaysOverdue { get; set; }
    public DateOnly? PaidAt { get; set; }
    public string? PaidFromAccountName { get; set; }
    public string? LedgerCategoryCode { get; set; }
    public string? LedgerCategoryName { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? VatAmount { get; set; }
}

public class PayablesPageDto
{
    public Guid BuildingId { get; set; }
    public PayablesSummaryDto Summary { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<PayableRowDto> Items { get; set; } = [];
}

// ── Cuentas por cobrar (deuda de los propietarios, por lo devengado) ───────────

public class ReceivablesSummaryDto
{
    // Lo cobrado a las unidades en los periodos publicados del edificio y lo que sigue pendiente.
    public decimal TotalCharged { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal TotalPending { get; set; }
    public decimal CollectionRatePercentage { get; set; }
    // Pendiente de periodos ya vencidos (morosidad) y cuantas unidades deben.
    public decimal OverdueAmount { get; set; }
    public int OverdueUnits { get; set; }
    public IReadOnlyList<PayableAgingBucketDto> Aging { get; set; } = [];
}
