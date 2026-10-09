using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Cierre de un mes calendario de un edificio (Centro de configuracion, seccion "Periodo y cierre"). Un mes cerrado no admite altas,
// ediciones ni bajas de los movimientos que lo alimentan (gastos, ingresos, pagos, notas de credito de proveedor y presupuesto).
// Reabrir no borra la fila: queda con quien reabrio, cuando y por que; cerrar de nuevo crea otra fila. Solo hay una fila vigente
// (sin reabrir) por edificio y mes.
public class FinancePeriodClosure : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    public DateTime ClosedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid ClosedByUserId { get; set; }

    public DateTime? ReopenedAtUtc { get; set; }
    public Guid? ReopenedByUserId { get; set; }
    public string? ReopenReason { get; set; }

    // Vigente = cerrado y no reabierto.
    public bool IsActive => ReopenedAtUtc is null;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
}
