using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Nota de credito interna: ajuste sobre una factura ya emitida. La factura original nunca se
// modifica ni se reemplaza; la NC registra por separado cuanto se reduce y por que, y recien al
// aprobarse genera los ExpenseCharge de reverso que bajan el saldo (ver CreditNoteLine).
public class CreditNote : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid UnitId { get; set; }
    public Guid InvoiceId { get; set; }

    public string Motivo { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public CreditNoteStatus Status { get; set; } = CreditNoteStatus.Draft;

    public Guid CreatedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public string RejectionReason { get; set; } = string.Empty;
    public DateTime? RejectedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public string VoidReason { get; set; } = string.Empty;
    public DateTime? VoidedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }

    // Documento fiscal oficial de la NC. Se puede registrar a mano en cualquier momento (papel o
    // electronica, sin relacion con InvoiceSeries), o numerar automaticamente con un timbrado
    // registrado como DocumentType=CreditNote (InvoiceSeriesId/Numero, mismo mecanismo que Invoice).
    // Ambos caminos escriben los mismos campos Fiscal*, por eso conviven sin duplicar datos.
    public Guid? InvoiceSeriesId { get; set; }
    public long? Numero { get; set; }
    public CreditNoteFiscalDocumentType? FiscalDocumentType { get; set; }
    public string? FiscalNumero { get; set; }
    public string? FiscalTimbrado { get; set; }
    public string? FiscalCdc { get; set; }
    public DateTime? FiscalFechaEmisionUtc { get; set; }
    public string? FiscalEstado { get; set; }
    public string? FiscalObservaciones { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public Unit? Unit { get; set; }
    public Invoice? Invoice { get; set; }
    public InvoiceSeries? Series { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }
    public ApplicationUser? ApprovedByUser { get; set; }
    public ApplicationUser? RejectedByUser { get; set; }
    public ApplicationUser? VoidedByUser { get; set; }
    public ICollection<CreditNoteLine> Lines { get; set; } = new List<CreditNoteLine>();
    public ICollection<CreditNoteAttachment> Attachments { get; set; } = new List<CreditNoteAttachment>();
}
