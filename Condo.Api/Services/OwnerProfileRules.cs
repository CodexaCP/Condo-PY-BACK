using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Api.Services;

// Ficha ampliada del propietario: validacion, copia a la entidad y al DTO. El documento (y el de facturacion) se validan segun su
// tipo solo cuando cambian, para no bloquear la edicion de otros datos de un propietario cargado antes con otro formato.
public static class OwnerProfileRules
{
    public static string? Validate(OwnerUpsertRequest r, ApplicationUser? current)
    {
        var error =
            PersonRules.Max(r.LegalName, 200, "La razón social") ??
            PersonRules.Max(r.InvoiceName, 200, "El nombre de facturación") ??
            PersonRules.Max(r.InvoiceDocumentType, 30, "El tipo de documento de facturación") ??
            PersonRules.Max(r.InvoiceAddress, 300, "La dirección de facturación") ??
            PersonRules.Max(r.InvoiceEmail, 160, "El email de facturación") ??
            PersonRules.Max(r.Nationality, 60, "La nacionalidad") ??
            PersonRules.Max(r.DocumentType, 30, "El tipo de documento");
        if (error is not null) return error;

        // Documento personal
        var docType = PersonRules.NormalizeType(r.DocumentType);
        var docNumber = PersonRules.TrimOrNull(r.DocumentNumber);
        var docChanged = current is null || docType != PersonRules.NormalizeType(current.DocumentType)
                         || docNumber != PersonRules.TrimOrNull(current.DocumentNumber);
        if (docChanged && PersonRules.ValidateDocument(docType, docNumber) is { } docError) return docError;

        if (r.PersonType == PersonType.Legal && string.IsNullOrWhiteSpace(r.LegalName))
            return "La razón social es obligatoria para una persona jurídica.";

        // Datos de facturacion propios: nombre y documento juntos.
        var hasInvoiceName = !string.IsNullOrWhiteSpace(r.InvoiceName);
        var hasInvoiceDoc = !string.IsNullOrWhiteSpace(r.InvoiceDocument);
        if (hasInvoiceName != hasInvoiceDoc)
            return "Para facturar a otro nombre completá el nombre y el documento de facturación (o dejá los dos vacíos).";

        var invDocType = PersonRules.NormalizeType(r.InvoiceDocumentType);
        var invDocNumber = PersonRules.TrimOrNull(r.InvoiceDocument);
        var invChanged = current is null || invDocType != PersonRules.NormalizeType(current.InvoiceDocumentType)
                         || invDocNumber != PersonRules.TrimOrNull(current.InvoiceDocument);
        if (invChanged && PersonRules.ValidateDocument(invDocType, invDocNumber, "El documento de facturación") is { } invError)
            return invError;

        if (!PersonRules.IsValidEmail(r.InvoiceEmail)) return "El email de facturación no tiene un formato válido.";

        if (!PersonRules.IsValidPhone(PersonRules.NormalizePhone(r.SecondaryPhone)))
            return "El teléfono secundario no es válido (con el prefijo del país, ej. +595981123456).";
        if (!PersonRules.IsValidPhone(PersonRules.NormalizePhone(r.WhatsAppPhone)))
            return "El WhatsApp no es válido (con el prefijo del país, ej. +595981123456).";

        if (r.BirthDate is { } birth && (birth < new DateOnly(1900, 1, 1) || birth > DateOnly.FromDateTime(DateTime.UtcNow)))
            return "La fecha de nacimiento no es válida.";

        return null;
    }

    public static void Apply(ApplicationUser u, OwnerUpsertRequest r)
    {
        u.PersonType = r.PersonType;
        u.LegalName = PersonRules.TrimOrNull(r.LegalName);
        u.InvoiceName = PersonRules.TrimOrNull(r.InvoiceName);
        u.InvoiceDocumentType = PersonRules.NormalizeType(r.InvoiceDocumentType);
        u.InvoiceDocument = PersonRules.TrimOrNull(r.InvoiceDocument);
        u.InvoiceAddress = PersonRules.TrimOrNull(r.InvoiceAddress);
        u.InvoiceEmail = PersonRules.TrimOrNull(r.InvoiceEmail)?.ToLowerInvariant();
        u.SecondaryPhone = PersonRules.NormalizePhone(r.SecondaryPhone);
        u.WhatsAppPhone = PersonRules.NormalizePhone(r.WhatsAppPhone);
        u.Nationality = PersonRules.TrimOrNull(r.Nationality);
        u.BirthDate = r.BirthDate;
    }

    public static void Fill(OwnerProfileData target, ApplicationUser u)
    {
        target.PersonType = u.PersonType;
        target.LegalName = u.LegalName;
        target.InvoiceName = u.InvoiceName;
        target.InvoiceDocumentType = u.InvoiceDocumentType;
        target.InvoiceDocument = u.InvoiceDocument;
        target.InvoiceAddress = u.InvoiceAddress;
        target.InvoiceEmail = u.InvoiceEmail;
        target.SecondaryPhone = u.SecondaryPhone;
        target.WhatsAppPhone = u.WhatsAppPhone;
        target.Nationality = u.Nationality;
        target.BirthDate = u.BirthDate;
    }
}
