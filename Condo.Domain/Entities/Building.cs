using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class Building : BaseEntity
{
    public Guid? CompanyId { get; set; }
    public Guid? CondominiumId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public string? ContactPhonePrefix { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public decimal? LateFeeRatePercentage { get; set; }
    public LateFeeFrequency? LateFeeFrequency { get; set; }
    public bool BlockOverdueAmenityReservations { get; set; } = false;

    // Propietario marcado como presidente del consorcio. Unico por edificio; firma la liquidacion
    // de expensas antes de que el CompanyAdmin la publique.
    public Guid? PresidentUserId { get; set; }
    public DateTime? PresidentAssignedAtUtc { get; set; }
    public Guid? PresidentAssignedByUserId { get; set; }

    // Modo de facturacion del edificio. Solo lo configura el superadmin (creacion/edicion del edificio).
    public InvoicingMode InvoicingMode { get; set; } = InvoicingMode.Preimpresa;

    // Modelos de documentos (factura, nota de credito y comprobante). Con UseStandardTemplates los PDF
    // salen con el diseno estandar de CONDOPY (colores de la marca); si no, el edificio adjunta sus
    // propios modelos, uno por concepto, y los PDF salen con el formato clasico preimpreso.
    public bool UseStandardTemplates { get; set; } = true;
    public string? InvoiceTemplateUrl { get; set; }
    public string? InvoiceTemplateFileName { get; set; }
    public string? CreditNoteTemplateUrl { get; set; }
    public string? CreditNoteTemplateFileName { get; set; }
    public string? ReceiptTemplateUrl { get; set; }
    public string? ReceiptTemplateFileName { get; set; }

    public Company? Company { get; set; }
    public Condominium? Condominium { get; set; }
    public ApplicationUser? PresidentUser { get; set; }
    public ICollection<Unit> Units { get; set; } = new List<Unit>();
    public ICollection<BuildingExpense> BuildingExpenses { get; set; } = new List<BuildingExpense>();
    public ICollection<BuildingIncome> BuildingIncomes { get; set; } = new List<BuildingIncome>();
    public ICollection<ExpenseSettlement> ExpenseSettlements { get; set; } = new List<ExpenseSettlement>();
    public ICollection<ExpensePeriod> ExpensePeriods { get; set; } = new List<ExpensePeriod>();
    public ICollection<UserBuildingAccess> UserAccesses { get; set; } = new List<UserBuildingAccess>();
}
