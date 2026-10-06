using Condo.Application.Models;
using Condo.Domain.Entities;

namespace Condo.Api.Services;

// Ficha ampliada del residente: validacion, copia a la entidad y al DTO.
public static class ResidentProfileRules
{
    public static string? Validate(ResidentUpsertRequest r, Resident? current)
    {
        var error =
            PersonRules.Max(r.EmergencyContactName, 200, "El contacto de emergencia") ??
            PersonRules.Max(r.Nationality, 60, "La nacionalidad") ??
            PersonRules.Max(r.LeaseUrl, 500, "El contrato") ??
            PersonRules.Max(r.LeaseFileName, 200, "El nombre del contrato") ??
            PersonRules.Max(r.DocumentType, 30, "El tipo de documento");
        if (error is not null) return error;

        // El documento se valida segun su tipo solo al crear o cuando cambia.
        var docType = PersonRules.NormalizeType(r.DocumentType);
        var docNumber = PersonRules.TrimOrNull(r.DocumentNumber);
        var changed = current is null || docType != PersonRules.NormalizeType(current.DocumentType)
                      || docNumber != PersonRules.TrimOrNull(current.DocumentNumber);
        if (changed && PersonRules.ValidateDocument(docType, docNumber) is { } docError) return docError;

        if (!PersonRules.IsValidPhone(PersonRules.NormalizePhone(r.EmergencyContactPhone)))
            return "El teléfono de emergencia no es válido (con el prefijo del país, ej. +595981123456).";

        if (r.BirthDate is { } birth && (birth < new DateOnly(1900, 1, 1) || birth > DateOnly.FromDateTime(DateTime.UtcNow)))
            return "La fecha de nacimiento no es válida.";

        if (r.LeaseEndDate is { } lease && lease < new DateOnly(1900, 1, 1))
            return "La fecha de vencimiento del contrato no es válida.";

        return null;
    }

    public static void Apply(Resident e, ResidentUpsertRequest r)
    {
        e.Relationship = r.Relationship;
        e.EmergencyContactName = PersonRules.TrimOrNull(r.EmergencyContactName);
        e.EmergencyContactPhone = PersonRules.NormalizePhone(r.EmergencyContactPhone);
        e.Nationality = PersonRules.TrimOrNull(r.Nationality);
        e.BirthDate = r.BirthDate;
        e.LeaseUrl = PersonRules.TrimOrNull(r.LeaseUrl);
        e.LeaseFileName = e.LeaseUrl is null ? null : PersonRules.TrimOrNull(r.LeaseFileName);
        e.LeaseEndDate = r.LeaseEndDate;
    }

    public static void Fill(ResidentProfileData target, Resident e)
    {
        target.Relationship = e.Relationship;
        target.EmergencyContactName = e.EmergencyContactName;
        target.EmergencyContactPhone = e.EmergencyContactPhone;
        target.Nationality = e.Nationality;
        target.BirthDate = e.BirthDate;
        target.LeaseUrl = e.LeaseUrl;
        target.LeaseFileName = e.LeaseFileName;
        target.LeaseEndDate = e.LeaseEndDate;
    }
}
