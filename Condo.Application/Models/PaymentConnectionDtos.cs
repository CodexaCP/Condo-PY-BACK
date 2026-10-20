namespace Condo.Application.Models;

public class PaymentFieldDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Secret { get; set; }
    public bool Required { get; set; }
    public int MaxLength { get; set; }
    public string? Help { get; set; }
}

public class PaymentProductDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<PaymentFieldDto> Fields { get; set; } = [];
    public bool SignedCallback { get; set; }
    public bool SupportsReversal { get; set; }
    public string ReversalNote { get; set; } = string.Empty;
    public bool EmbeddedCheckout { get; set; }
    public List<string> Notes { get; set; } = [];
}

public class PaymentProviderDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<PaymentProductDto> Products { get; set; } = [];
}

public class PaymentCatalogDto
{
    public List<PaymentProviderDto> Providers { get; set; } = [];

    // Hay clave maestra en el servidor. Sin ella no se pueden guardar claves privadas.
    public bool MasterKeyConfigured { get; set; }
}

public class PaymentConnectionBuildingDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    // Edificio de prueba: un cobro de sandbox o del simulador se aplica a la deuda solo aqui.
    public bool IsTest { get; set; }
}

public class PaymentConnectionDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProviderCode { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? StatusChangedAtUtc { get; set; }
    public List<PaymentConnectionBuildingDto> Buildings { get; set; } = [];

    // Solo para quienes pueden editar (el resto ve nombre, estado y edificios).
    public string? CallbackUrl { get; set; }
    public Dictionary<string, string>? Settings { get; set; }
    // Por cada secreto cargado, su final enmascarado (••••1234). Nunca el valor.
    public Dictionary<string, string>? SecretsMasked { get; set; }
    public DateTime? SecretsUpdatedAtUtc { get; set; }
    // Campos obligatorios que todavia no se cargaron: sin ellos no se puede activar.
    public List<string> MissingFields { get; set; } = [];

    // Titular del comercio: a nombre de quien se cobra (nombre y RUC o cedula) y una nota opcional de la cuenta receptora.
    // Obligatorios para activar. Solo para quienes pueden editar.
    public string? HolderName { get; set; }
    public string? HolderTaxId { get; set; }
    public string? HolderBankNote { get; set; }
}

public class PaymentConnectionListDto
{
    public List<PaymentConnectionDto> Items { get; set; } = [];
    public bool CanEdit { get; set; }
    public bool MasterKeyConfigured { get; set; }

    // El cobro online todavia no esta habilitado en la plataforma: la configuracion deja la conexion lista, pero nadie puede pagar aun.
    public bool CollectionEnabled { get; set; }
}

public class PaymentConnectionRequest
{
    public Guid? CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    // Solo al crear: el proveedor y la modalidad no se cambian despues (se crea otra conexion).
    public string ProviderCode { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string Environment { get; set; } = "Sandbox";
    // Campos no secretos del catalogo. Un valor vacio borra el dato.
    public Dictionary<string, string> Settings { get; set; } = [];
    // Campos secretos del catalogo. Vacio o ausente = se conserva el actual; con valor = se reemplaza.
    public Dictionary<string, string> Secrets { get; set; } = [];
    // Titular del comercio. Nulo = no se toca; vacio borra el dato.
    public string? HolderName { get; set; }
    public string? HolderTaxId { get; set; }
    public string? HolderBankNote { get; set; }
}

public class PaymentConnectionBuildingsRequest
{
    public List<Guid> BuildingIds { get; set; } = [];
    // De los edificios indicados, cuales son de prueba (donde un cobro de sandbox o del simulador SI se aplica a la deuda). Nulo = no se cambia.
    public List<Guid>? TestBuildingIds { get; set; }
}

public class PaymentConnectionAuditDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? ChangesJson { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
}
