using Condo.Domain.Enums;

namespace Condo.Application.Models;

// Ficha ampliada del residente (todo opcional).
public abstract class ResidentProfileData
{
    public ResidentRelationship? Relationship { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Nationality { get; set; }
    public DateOnly? BirthDate { get; set; }
    // Contrato de alquiler (inquilinos): documento adjunto y fecha de vencimiento.
    public string? LeaseUrl { get; set; }
    public string? LeaseFileName { get; set; }
    public DateOnly? LeaseEndDate { get; set; }
}

public class ResidentUpsertRequest : ResidentProfileData
{
    public Guid? CompanyId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ResidentDto : ResidentProfileData
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool IsActive { get; set; }
    public bool HasLinkedAccount { get; set; }
}
