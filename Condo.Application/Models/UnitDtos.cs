namespace Condo.Application.Models;

public class UnitUpsertRequest
{
    public Guid BuildingId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Floor { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UnitDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Floor { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public bool IsActive { get; set; }
    // Exoneracion de mora (Centro de configuracion): la unidad no acumula mora automatica mientras este marcada.
    public bool LateFeeExempt { get; set; }
    public string? LateFeeExemptReason { get; set; }
}

/// <summary>Cuantas unidades hay en el alcance (edificio elegido o todos), sin contar busqueda, piso ni estado: alimenta los contadores de los filtros.</summary>
public class UnitPageCountsDto
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Inactive { get; set; }
    public int LateFeeExempt { get; set; }
}

public class UnitPageDto : PagedResultDto<UnitDto>
{
    // Pisos del edificio elegido (vacio si se miran todos los edificios): son los atajos de filtro por piso.
    public IReadOnlyList<string> Floors { get; set; } = [];
    public UnitPageCountsDto Counts { get; set; } = new();
}
