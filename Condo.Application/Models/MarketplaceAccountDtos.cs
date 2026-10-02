namespace Condo.Application.Models;

// ── Cuenta aparte del marketplace (por edificio) ──────────────────────────────

// Renglon del extracto. El importe lleva signo: ingresos suman, acreditaciones y devoluciones restan.
public class MarketplaceAccountRowDto
{
    public Guid Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    // PaymentIn | OwnerCredit | RefundOut | Adjustment
    public string Kind { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Concept { get; set; } = string.Empty;
    public Guid? ReservationId { get; set; }
    public string? Reference { get; set; }
    // Nulo = movimiento automatico del sistema.
    public string? CreatedByName { get; set; }
    // Solo el SuperAdmin: la acreditacion se puede revertir (el saldo del propietario sigue intacto).
    public bool CanReverse { get; set; }
}

public class MarketplaceAccountSummaryDto
{
    public decimal OpeningBalance { get; set; }
    public decimal TotalIn { get; set; }
    // Magnitudes positivas de lo acreditado a propietarios y de lo devuelto a compradores en el periodo.
    public decimal TotalCredited { get; set; }
    public decimal TotalRefunds { get; set; }
    public decimal TotalAdjustments { get; set; }
    public decimal ClosingBalance { get; set; }

    // Al dia de hoy (no dependen del periodo elegido):
    public decimal CurrentBalance { get; set; }
    // Parte del saldo que todavia le corresponde acreditar a los propietarios (reservas confirmadas o finalizadas sin acreditar).
    public decimal PendingToCredit { get; set; }
    // Lo que queda para la gestion: saldo actual menos lo pendiente de acreditar. Se reparte fuera del sistema.
    public decimal ManagementGain { get; set; }
}

public class MarketplaceStatementDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public MarketplaceAccountSummaryDto Summary { get; set; } = new();
    public List<MarketplaceAccountRowDto> Rows { get; set; } = [];
    // Solo el SuperAdmin puede cargar ajustes y revertir acreditaciones.
    public bool CanEdit { get; set; }
}

public class MarketplaceAdjustmentRequest
{
    public Guid BuildingId { get; set; }
    // Con signo: positivo suma a la cuenta, negativo resta. Guaranies enteros, distinto de cero.
    public decimal Amount { get; set; }
    public string Concept { get; set; } = string.Empty;
}

public class MarketplaceReverseCreditRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class MarketplaceReversalDto
{
    public Guid ReservationId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    // Saldo a favor del propietario despues de la reversa.
    public decimal OwnerBalance { get; set; }
}
