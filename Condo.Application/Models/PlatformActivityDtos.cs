namespace Condo.Application.Models;

// Actividad de la plataforma para el panel del SuperAdmin: altas y cambios de empresas, administradores, condominios y edificios.
public class PlatformActivityDto
{
    // Un dia por posicion (de mas viejo a hoy), con la cantidad de movimientos.
    public List<PlatformActivityDayDto> Days { get; set; } = new();
    // Ultimos movimientos, del mas reciente al mas viejo.
    public List<PlatformActivityItemDto> Items { get; set; } = new();
}

public class PlatformActivityDayDto
{
    public DateOnly Date { get; set; }
    public int Count { get; set; }
}

public class PlatformActivityItemDto
{
    public DateTime AtUtc { get; set; }
    // CompanyCreated, CompanyUpdated, AdminCreated, CondominiumCreated, BuildingCreated
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
