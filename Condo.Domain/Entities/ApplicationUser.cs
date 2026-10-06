using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class ApplicationUser : BaseEntity
{
    public Guid? CompanyId { get; set; }
    public Guid? CondominiumId { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    public string? DocumentType { get; set; }
    public string? DocumentNumber { get; set; }

    public string? PhonePrefix { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }

    public bool IsResident { get; set; } = false;
    public UserRole Role { get; set; } = UserRole.Resident;
    public string? SignatureUrl { get; set; }

    // Ficha ampliada del propietario: persona fisica o juridica, datos para facturarle y contacto adicional.
    public PersonType? PersonType { get; set; }
    public string? LegalName { get; set; }
    // Datos de facturacion propios (la factura sale a nombre de un tercero o de su empresa). InvoiceName e InvoiceDocument van juntos;
    // sin ellos se factura con los datos personales (o con la razon social si es persona juridica).
    public string? InvoiceName { get; set; }
    public string? InvoiceDocumentType { get; set; }
    public string? InvoiceDocument { get; set; }
    public string? InvoiceAddress { get; set; }
    public string? InvoiceEmail { get; set; }
    // Telefonos guardados completos, con el prefijo del pais (ej. +595981123456).
    public string? SecondaryPhone { get; set; }
    public string? WhatsAppPhone { get; set; }
    public string? Nationality { get; set; }
    public DateOnly? BirthDate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = true;
    public DateTime? LastLoginAtUtc { get; set; }

    public Company? Company { get; set; }
    public Condominium? Condominium { get; set; }
    public ICollection<UserBuildingAccess> BuildingAccesses { get; set; } = new List<UserBuildingAccess>();
}
