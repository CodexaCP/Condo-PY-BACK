using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class CreditNoteLineDto
{
    public Guid Id { get; set; }
    public Guid ExpenseChargeId { get; set; }
    public string ChargeConcept { get; set; } = string.Empty;
    public ExpenseChargeType ChargeType { get; set; }
    public decimal ChargeAmount { get; set; }
    public decimal Amount { get; set; }
    public string? Concept { get; set; }
}

public class CreditNoteAttachmentDto
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public CreditNoteAttachmentKind Kind { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string? UploadedByName { get; set; }
    public DateTime UploadedAtUtc { get; set; }
}

public class CreditNoteDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid InvoiceId { get; set; }
    public string? InvoiceNumeroFormateado { get; set; }
    public decimal InvoiceMontoTotal { get; set; }

    public string Motivo { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public CreditNoteStatus Status { get; set; }

    public Guid CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? ApprovedByName { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public string? RejectedByName { get; set; }
    public string? VoidReason { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidedByName { get; set; }

    public CreditNoteFiscalDocumentType? FiscalDocumentType { get; set; }
    public string? FiscalNumero { get; set; }
    public string? FiscalTimbrado { get; set; }
    public string? FiscalCdc { get; set; }
    public DateTime? FiscalFechaEmisionUtc { get; set; }
    public string? FiscalEstado { get; set; }
    public string? FiscalObservaciones { get; set; }

    public List<CreditNoteLineDto> Lines { get; set; } = new();
    public List<CreditNoteAttachmentDto> Attachments { get; set; } = new();
}

public class AdjustableChargeDto
{
    public Guid ExpenseChargeId { get; set; }
    public string Concept { get; set; } = string.Empty;
    public ExpenseChargeType ChargeType { get; set; }
    public decimal Amount { get; set; }
    public decimal AlreadyAdjusted { get; set; }
    public decimal Adjustable { get; set; }
    public decimal AlreadyPaid { get; set; }
}

public class CreateCreditNoteLineRequest
{
    public Guid ExpenseChargeId { get; set; }
    public decimal Amount { get; set; }
    public string? Concept { get; set; }
}

public class CreateCreditNoteRequest
{
    public Guid InvoiceId { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public List<CreateCreditNoteLineRequest> Lines { get; set; } = new();
}

public class RejectCreditNoteRequest
{
    public string Motivo { get; set; } = string.Empty;
}

public class VoidCreditNoteRequest
{
    public string Motivo { get; set; } = string.Empty;
}

public class RegisterCreditNoteFiscalDataRequest
{
    public CreditNoteFiscalDocumentType? DocumentType { get; set; }
    public string? Numero { get; set; }
    public string? Timbrado { get; set; }
    public string? Cdc { get; set; }
    public DateTime? FechaEmisionUtc { get; set; }
    public string? Estado { get; set; }
    public string? Observaciones { get; set; }
}

public class AddCreditNoteAttachmentRequest
{
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public CreditNoteAttachmentKind Kind { get; set; } = CreditNoteAttachmentKind.Other;
}
