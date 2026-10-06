using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

// Cliente al que se le factura una unidad: nombre, documento y datos de contacto.
public sealed record BillingClient(string? Name, string? DocumentType, string? Document, string? Address, string? Email);

// Define quien es el cliente de una factura. En borrador se toma del propietario principal vigente de la unidad (o, si no hay
// propietario, del residente actual). Al emitir la factura se guarda en ella (ApplySnapshot): desde entonces el documento
// fiscal no cambia aunque despues se edite o se reemplace al propietario.
public static class BillingClientResolver
{
    // Datos del propietario que intervienen: los personales y los de facturacion propios.
    private sealed record OwnerSource(
        Guid UnitId, bool IsPrimary, DateTime CreatedAtUtc,
        string FullName, string? DocumentType, string? DocumentNumber, string? Address, string? Email,
        PersonType? PersonType, string? LegalName,
        string? InvoiceName, string? InvoiceDocumentType, string? InvoiceDocument, string? InvoiceAddress, string? InvoiceEmail);

    // Con datos de facturacion propios (nombre y documento juntos) se factura a ellos; si no, a la razon social si es persona
    // juridica, y si no al propio propietario.
    public static BillingClient FromOwner(
        string fullName, string? documentType, string? documentNumber, string? address, string? email,
        PersonType? personType, string? legalName,
        string? invoiceName, string? invoiceDocumentType, string? invoiceDocument, string? invoiceAddress, string? invoiceEmail)
    {
        var hasOwnBilling = !string.IsNullOrWhiteSpace(invoiceName) && !string.IsNullOrWhiteSpace(invoiceDocument);
        if (hasOwnBilling)
        {
            return new BillingClient(
                invoiceName!.Trim(), PersonRules.NormalizeType(invoiceDocumentType), invoiceDocument!.Trim(),
                PersonRules.TrimOrNull(invoiceAddress) ?? PersonRules.TrimOrNull(address),
                PersonRules.TrimOrNull(invoiceEmail) ?? PersonRules.TrimOrNull(email));
        }

        var name = personType == PersonType.Legal && !string.IsNullOrWhiteSpace(legalName) ? legalName.Trim() : fullName;
        return new BillingClient(name, PersonRules.NormalizeType(documentType), PersonRules.TrimOrNull(documentNumber),
            PersonRules.TrimOrNull(address), PersonRules.TrimOrNull(email));
    }

    private static BillingClient FromOwner(OwnerSource o) =>
        FromOwner(o.FullName, o.DocumentType, o.DocumentNumber, o.Address, o.Email, o.PersonType, o.LegalName,
            o.InvoiceName, o.InvoiceDocumentType, o.InvoiceDocument, o.InvoiceAddress, o.InvoiceEmail);

    // Cliente actual de cada unidad (los que no tienen propietario ni residente no aparecen en el resultado).
    public static async Task<Dictionary<Guid, BillingClient>> LoadLiveAsync(
        ICondoDbContext dbContext, IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, BillingClient>();
        if (unitIds.Count == 0) return result;

        var owners = await dbContext.UnitOwners.AsNoTracking()
            .Where(x => !x.IsDeleted && unitIds.Contains(x.UnitId) && x.Owner != null)
            .Select(x => new OwnerSource(
                x.UnitId, x.IsPrimary, x.CreatedAtUtc,
                x.Owner!.FullName, x.Owner.DocumentType, x.Owner.DocumentNumber, x.Owner.Address, x.Owner.Email,
                x.Owner.PersonType, x.Owner.LegalName,
                x.Owner.InvoiceName, x.Owner.InvoiceDocumentType, x.Owner.InvoiceDocument, x.Owner.InvoiceAddress, x.Owner.InvoiceEmail))
            .ToListAsync(cancellationToken);

        foreach (var group in owners.GroupBy(o => o.UnitId))
        {
            var main = group.OrderByDescending(o => o.IsPrimary).ThenBy(o => o.CreatedAtUtc).First();
            result[group.Key] = FromOwner(main);
        }

        var withoutOwner = unitIds.Where(id => !result.ContainsKey(id)).ToList();
        if (withoutOwner.Count == 0) return result;

        var residents = await dbContext.UnitResidents.AsNoTracking()
            .Where(x => !x.IsDeleted && withoutOwner.Contains(x.UnitId) && x.EndDate == null && x.Resident != null)
            .Select(x => new { x.UnitId, x.CreatedAtUtc, x.Resident!.FullName, x.Resident.DocumentType, x.Resident.DocumentNumber, x.Resident.Email })
            .ToListAsync(cancellationToken);

        foreach (var group in residents.GroupBy(r => r.UnitId))
        {
            var first = group.OrderBy(r => r.CreatedAtUtc).First();
            result[group.Key] = new BillingClient(first.FullName, PersonRules.NormalizeType(first.DocumentType),
                PersonRules.TrimOrNull(first.DocumentNumber), null, PersonRules.TrimOrNull(first.Email));
        }

        return result;
    }

    public static async Task<BillingClient?> LoadLiveAsync(ICondoDbContext dbContext, Guid unitId, CancellationToken cancellationToken)
    {
        var all = await LoadLiveAsync(dbContext, new[] { unitId }, cancellationToken);
        return all.GetValueOrDefault(unitId);
    }

    // Cliente guardado en la factura al emitirla; null si todavia no se guardo (borrador).
    public static BillingClient? FromSnapshot(
        string? name, string? documentType, string? document, string? address, string? email) =>
        string.IsNullOrWhiteSpace(name) ? null : new BillingClient(name, documentType, document, address, email);

    // Guarda el cliente en la factura (al emitirla, o al completar las que se emitieron antes de existir este dato).
    public static void ApplySnapshot(Invoice invoice, BillingClient client, bool reconstructed = false)
    {
        invoice.ClientName = Clip(client.Name, 200);
        invoice.ClientDocumentType = Clip(client.DocumentType, 30);
        invoice.ClientDocument = Clip(client.Document, 40);
        invoice.ClientAddress = Clip(client.Address, 300);
        invoice.ClientEmail = Clip(client.Email, 160);
        invoice.ClientReconstructed = reconstructed;
    }

    // Cliente de una factura (para su PDF y detalle): el guardado al emitir; si no lo tiene (borrador), el actual de la unidad.
    public static async Task<(BillingClient? Client, bool Reconstructed)> LoadForInvoiceAsync(
        ICondoDbContext dbContext, Guid invoiceId, Guid unitId, CancellationToken cancellationToken)
    {
        var saved = await dbContext.Invoices.AsNoTracking()
            .Where(x => x.Id == invoiceId)
            .Select(x => new { x.ClientName, x.ClientDocumentType, x.ClientDocument, x.ClientAddress, x.ClientEmail, x.ClientReconstructed })
            .FirstOrDefaultAsync(cancellationToken);

        var snapshot = saved is null ? null : FromSnapshot(saved.ClientName, saved.ClientDocumentType, saved.ClientDocument, saved.ClientAddress, saved.ClientEmail);
        if (snapshot is not null) return (snapshot, saved!.ClientReconstructed);

        return (await LoadLiveAsync(dbContext, unitId, cancellationToken), false);
    }

    private static string? Clip(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= max ? v : v[..max];
    }
}
