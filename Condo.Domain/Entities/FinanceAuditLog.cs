using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Historial de cambios de la configuracion de un edificio (Centro de configuracion): quien cambio que, cuando y con que valores
// antes y despues. Solo se agrega (nunca se edita ni se borra). Se guarda en la misma transaccion que el cambio que registra.
public class FinanceAuditLog : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }

    // Clave de la seccion del Centro (por ejemplo "identity", "late-fee", "chart"); ver ConfigSectionKeys.
    public string Section { get; set; } = string.Empty;

    // Que paso, en palabras cortas y estables (por ejemplo "Updated", "Created", "Deleted", "Enabled").
    public string Action { get; set; } = string.Empty;

    // Descripcion legible para el historial.
    public string Summary { get; set; } = string.Empty;

    // Entidad afectada, cuando el cambio es sobre un registro puntual (cuenta financiera, rubro, serie, etc.).
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    // Lista JSON de { field, label, before, after } con lo que cambio. Nula cuando el resumen alcanza.
    public string? ChangesJson { get; set; }

    // Quien lo hizo: el id y el correo quedan guardados al momento del cambio (el nombre se busca al leer).
    public Guid UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
}
