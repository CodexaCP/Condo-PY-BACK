using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Registro de un documento emitido que lleva QR de verificacion (hoy la liquidacion de expensas). El QR del papel contiene
// solo una direccion con el Token (aleatorio, no adivinable); la pagina publica busca esta fila y responde un resumen minimo:
// si el documento es autentico, de que edificio y periodo es y si sigue vigente.
//
// Se emite al descargar el PDF y es idempotente: mientras el contenido del documento no cambie se reutiliza el mismo Token;
// si cambia (la liquidacion se recalcula o se publica) la fila anterior queda reemplazada (SupersededAtUtc) y se emite una nueva,
// asi un papel viejo avisa que ya hay una version nueva. Guarda una copia (snapshot) de lo que se imprimio.
public class DocumentVerification : CompanyScopedEntity
{
    public const string TypeSettlement = "settlement";

    // 128 bits aleatorios en base64url (22 caracteres). Indice unico.
    public string Token { get; set; } = string.Empty;
    public string DocumentType { get; set; } = TypeSettlement;
    // Documento al que apunta (para la liquidacion, el periodo de expensas).
    public Guid DocumentId { get; set; }
    public Guid BuildingId { get; set; }

    // SHA-256 (hex) del contenido que se imprimio; sirve para saber si el documento cambio desde que se emitio.
    public string ContentHash { get; set; } = string.Empty;

    // Copia de lo que muestra la pagina publica (solo datos que ya estan en el papel de la cartelera).
    public string Title { get; set; } = string.Empty;
    public string BuildingName { get; set; } = string.Empty;
    public string? AdministratorName { get; set; }
    public string PeriodName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    // Falso = la liquidacion todavia era un borrador (no publicada) cuando se imprimio.
    public bool IsPublished { get; set; }
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;

    // Una version nueva del documento la reemplazo.
    public DateTime? SupersededAtUtc { get; set; }
    // Anulado a mano (error, retiro).
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedReason { get; set; }

    public Company? Company { get; set; }
}
