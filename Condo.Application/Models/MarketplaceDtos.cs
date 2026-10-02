namespace Condo.Application.Models;

// ── Administracion del modulo (SuperAdmin) ────────────────────────────────────

// Fila del listado del SuperAdmin: todos los edificios con su plan, el interruptor y la configuracion del marketplace.
public class MarketplaceAdminBuildingDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string CondominiumName { get; set; } = string.Empty;
    public Guid? PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanStatus { get; set; } = string.Empty;
    public bool PlanIncludesMarketplace { get; set; }
    public bool ModuleEnabled { get; set; }
    // Habilitado y con plan que lo incluye: lo que realmente ven los usuarios del edificio.
    public bool ModuleAvailable { get; set; }
    public decimal CommissionPercent { get; set; }
    public string TransferInfo { get; set; } = string.Empty;
}

// Interruptor, comision de gestion y datos para transferir de un edificio. Los edita solo el SuperAdmin.
public class MarketplaceAdminUpdateRequest
{
    public bool Enabled { get; set; }
    public decimal CommissionPercent { get; set; }
    public string? TransferInfo { get; set; }
}
