using Condo.Domain.Enums;

namespace Condo.Application.Models;

// Ficha ampliada del propietario (todo opcional): persona fisica o juridica, datos para facturarle y contacto adicional.
public abstract class OwnerProfileData
{
    public PersonType? PersonType { get; set; }
    // Razon social (persona juridica).
    public string? LegalName { get; set; }
    // Datos de facturacion propios: nombre y documento van juntos; sin ellos se factura con los datos personales.
    public string? InvoiceName { get; set; }
    public string? InvoiceDocumentType { get; set; }
    public string? InvoiceDocument { get; set; }
    public string? InvoiceAddress { get; set; }
    public string? InvoiceEmail { get; set; }
    // Telefonos completos con el prefijo del pais (+595981123456).
    public string? SecondaryPhone { get; set; }
    public string? WhatsAppPhone { get; set; }
    public string? Nationality { get; set; }
    public DateOnly? BirthDate { get; set; }
}

public class OwnerUpsertRequest : OwnerProfileData
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public string? DocumentNumber { get; set; }
    public string? PhonePrefix { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsResident { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public string? SignatureUrl { get; set; }
}

public class OwnerDto : OwnerProfileData
{
    public Guid Id { get; set; }
    public Guid? CompanyId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public string? DocumentNumber { get; set; }
    public string? PhonePrefix { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsResident { get; set; }
    public bool IsActive { get; set; }
    public string? SignatureUrl { get; set; }
    public List<OwnerPresidentBuildingDto> PresidentOfBuildings { get; set; } = new();
}

public class OwnerPresidentBuildingDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
}

public class OwnerEligibleBuildingDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public bool HasOtherPresident { get; set; }
    public string? OtherPresidentName { get; set; }
}

public class SetOwnerPresidentBuildingRequest
{
    public Guid? BuildingId { get; set; }
}
