namespace Condo.Application.Models;

public class UnitOwnerDto
{
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateOnly StartDate { get; set; }
    // Porcentaje de titularidad (copropietarios); vacio = no informado.
    public decimal? OwnershipPercentage { get; set; }
    // Al asignar el nuevo propietario principal: saldo a favor de la unidad que se le traspaso (0 si no habia).
    public decimal TransferredCredit { get; set; }
}

// Resultado de dar de baja a un propietario: el saldo a favor de la unidad que quedo retenido (a la espera del nuevo propietario
// principal) o que paso directo a otro propietario principal que sigue en la unidad.
public class UnitOwnerRemovalDto
{
    public decimal HeldCredit { get; set; }
    public decimal TransferredCredit { get; set; }
}

// Lo que pasaria al dar de baja a un propietario, sin hacerlo: si se puede (sin deuda pendiente) y que pasa con el saldo a favor.
public class UnitOwnerRemovalPreviewDto
{
    public string UnitCode { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    // Es el unico propietario principal de la unidad: al quitarlo la unidad cambia de manos y se exige que no haya deuda.
    public bool IsLastPrimary { get; set; }
    public bool CanRemove { get; set; }
    public decimal PendingDebt { get; set; }
    public List<string> DebtPeriods { get; set; } = new();
    // Saldo a favor de la unidad que quedaria retenido hasta asignar el nuevo propietario principal.
    public decimal CreditToHold { get; set; }
    // Saldo a favor de la unidad que pasaria directo al otro propietario principal que sigue en la unidad.
    public decimal CreditToTransfer { get; set; }
    public string? Message { get; set; }
}

public class CreateUnitOwnerRequest
{
    public Guid UnitId { get; set; }
    public Guid OwnerId { get; set; }
    public bool IsPrimary { get; set; }
    public DateOnly StartDate { get; set; }
    public decimal? OwnershipPercentage { get; set; }
}

public class UpdateUnitOwnershipRequest
{
    public decimal? OwnershipPercentage { get; set; }
}
