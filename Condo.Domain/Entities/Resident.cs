using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class Resident : CompanyScopedEntity
{
    public string FullName { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? ApplicationUserId { get; set; }

    // Ficha ampliada del residente.
    public ResidentRelationship? Relationship { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Nationality { get; set; }
    public DateOnly? BirthDate { get; set; }
    // Contrato de alquiler (para inquilinos): documento adjunto y fecha de vencimiento.
    public string? LeaseUrl { get; set; }
    public string? LeaseFileName { get; set; }
    public DateOnly? LeaseEndDate { get; set; }

    public Company? Company { get; set; }
    public ApplicationUser? ApplicationUser { get; set; }
    public ICollection<UnitResident> UnitResidents { get; set; } = new List<UnitResident>();
}
