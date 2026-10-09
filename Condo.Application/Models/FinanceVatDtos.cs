namespace Condo.Application.Models;

// ── Libro de compras (IVA de los gastos) ──────────────────────────────────────

public class VatPurchaseRowDto
{
    public DateOnly Date { get; set; }
    public Guid ExpenseId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierRuc { get; set; }
    public string? Timbrado { get; set; }
    // Numero de la factura del proveedor; en una nota de credito, su numero.
    public string? DocumentNumber { get; set; }
    public bool IsCreditNote { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? LedgerCategoryCode { get; set; }
    public string? LedgerCategoryName { get; set; }
    // Total del comprobante con IVA incluido (negativo en una nota de credito).
    public decimal Total { get; set; }
    public decimal VatRate { get; set; }
    // Monto sin IVA (base gravada; en exento, el total) y IVA incluido.
    public decimal Base { get; set; }
    public decimal Vat { get; set; }
}

public class VatRateTotalsDto
{
    // 10, 5 o 0 (exento).
    public decimal Rate { get; set; }
    public int Count { get; set; }
    public decimal Total { get; set; }
    public decimal Base { get; set; }
    public decimal Vat { get; set; }
}

public class VatPurchasesBookDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public IReadOnlyList<VatPurchaseRowDto> Rows { get; set; } = [];
    // Siempre las tres tasas, en este orden: 10, 5 y exento.
    public IReadOnlyList<VatRateTotalsDto> TotalsByRate { get; set; } = [];
    public decimal GrandTotal { get; set; }
    public decimal GrandBase { get; set; }
    public decimal GrandVat { get; set; }
    // Gastos del rango sin tasa de IVA cuya cuenta no es "no corresponde": hay que clasificarlos para que entren al libro.
    public int UnclassifiedCount { get; set; }
    public decimal UnclassifiedTotal { get; set; }
}
