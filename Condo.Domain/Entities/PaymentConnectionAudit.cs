using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Historial de cambios de las conexiones de pago: quien cambio que y cuando. Solo se agrega (nunca se edita ni se borra) y se guarda en la
// misma transaccion que el cambio. Nunca contiene una clave: de un secreto solo se registra que se reemplazo y su ultimo fragmento enmascarado.
public class PaymentConnectionAudit : CompanyScopedEntity
{
    public Guid ConnectionId { get; set; }

    // Que paso, en palabras cortas y estables: Created, Updated, SecretsReplaced, Assigned, Activated, Disabled, Deleted.
    public string Action { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;

    // Lista JSON de { field, label, before, after } con lo que cambio. Nula cuando el resumen alcanza.
    public string? ChangesJson { get; set; }

    public Guid UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string? Ip { get; set; }

    public Company? Company { get; set; }
    public PaymentConnection? Connection { get; set; }
}
