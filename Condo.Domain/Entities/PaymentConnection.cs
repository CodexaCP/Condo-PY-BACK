using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Conexion de una empresa con un comercio de un proveedor de pagos (por ejemplo Bancard Tpago o vPOS). Pertenece a la empresa y se asigna a
// uno o varios edificios (BuildingPaymentConfig). Condo-PY solo guarda las credenciales del comercio y registra resultados: el dinero no pasa
// por Condo-PY. Las claves secretas se guardan cifradas (SecretsCipher) con una clave maestra que vive fuera de la base; los datos que no
// son secretos (codigo de comercio, clave publica) van en SettingsJson.
public class PaymentConnection : CompanyScopedEntity
{
    // Nombre legible para distinguirla ("Bancard - Administradora Sol").
    public string Name { get; set; } = string.Empty;

    // Proveedor y modalidad: claves del catalogo en codigo (PaymentProviderCatalog), no una tabla.
    public string ProviderCode { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;

    public PaymentEnvironment Environment { get; set; } = PaymentEnvironment.Sandbox;
    public PaymentConnectionStatus Status { get; set; } = PaymentConnectionStatus.Draft;

    // Identificador aleatorio que va en la URL del callback del proveedor (identifica que credenciales usar sin adivinarse).
    public string PublicId { get; set; } = Guid.NewGuid().ToString("N");

    // JSON { campo: valor } con los datos no secretos del catalogo.
    public string? SettingsJson { get; set; }

    // JSON { campo: valor } cifrado con AES-GCM (base64) y la clave con que se cifro, para poder rotar la clave maestra.
    public string? SecretsCipher { get; set; }
    public string? KeyId { get; set; }

    // JSON { campo: "••••1234" } con el final de cada secreto, para mostrar que esta configurado sin devolverlo nunca.
    public string? SecretsFingerprint { get; set; }
    public DateTime? SecretsUpdatedAtUtc { get; set; }

    // Ultimo cambio de estado (quien y cuando).
    public DateTime? StatusChangedAtUtc { get; set; }
    public Guid? StatusChangedByUserId { get; set; }

    public Company? Company { get; set; }
    public ICollection<BuildingPaymentConfig> BuildingConfigs { get; set; } = new List<BuildingPaymentConfig>();
}
