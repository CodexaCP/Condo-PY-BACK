namespace Condo.Application.Models;

// ── Proveedores de la empresa ─────────────────────────────────────────────────

public class SupplierDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Ruc { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    // Plazo de pago habitual en dias: al cargarle un gasto, el vencimiento se propone como fecha del gasto + plazo.
    public int? PaymentTermDays { get; set; }
    public bool IsActive { get; set; }
    // Gastos cargados con este proveedor (si tiene gastos no se elimina: se desactiva).
    public int ExpenseCount { get; set; }
}

public class SupplierUpsertRequest
{
    // Solo lo usa el SuperAdmin al crear (no tiene empresa propia); para el resto manda la empresa del usuario.
    public Guid? CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Ruc { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public int? PaymentTermDays { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SupplierPageDto
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool CanEdit { get; set; }
    public IReadOnlyList<SupplierDto> Items { get; set; } = [];
}
