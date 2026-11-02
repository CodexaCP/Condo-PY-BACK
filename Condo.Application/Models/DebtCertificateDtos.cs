namespace Condo.Application.Models;

public class DebtCertificateLineDto
{
    public string PeriodName { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public decimal Charged { get; set; }
    public decimal Paid { get; set; }
    public decimal Balance { get; set; }
}

/// <summary>
/// Certificado de deudas y créditos de una unidad: lo que el administrador debe poder expedir a pedido de un interesado (por ejemplo para
/// transmitir la unidad). Muestra lo adeudado por periodo y el saldo a favor, a la fecha de emision.
/// </summary>
public class DebtCertificateDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string BuildingName { get; set; } = string.Empty;
    public string BuildingAddress { get; set; } = string.Empty;
    public string? Jurisdiction { get; set; }
    public string? BuildingTaxId { get; set; }
    public string? BuildingLegalName { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? AdministratorName { get; set; }
    public string? AdministratorRegistryNumber { get; set; }
    public List<string> Owners { get; set; } = [];
    public DateTime IssuedAtUtc { get; set; }
    public string CurrencyCode { get; set; } = "PYG";
    public List<DebtCertificateLineDto> Lines { get; set; } = [];
    public decimal TotalDebt { get; set; }
    // Saldo a favor de los propietarios vigentes en la administracion (es del propietario, puede incluir lo generado por otras unidades).
    public decimal OwnerCreditBalance { get; set; }
    public bool HasDebt => TotalDebt > 0m;
}
