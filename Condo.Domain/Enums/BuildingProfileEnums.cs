namespace Condo.Domain.Enums;

// Tipo de inmueble que se registra en la ficha del edificio.
public enum BuildingPropertyType
{
    Building = 1,
    Tower = 2,
    HorizontalCondominium = 3,
    GatedCommunity = 4,
    Other = 5
}

// Tipo de contribuyente ante la SET (datos fiscales del edificio).
public enum TaxpayerType
{
    Legal = 1,
    Natural = 2
}

// Regimen de IVA del contribuyente.
public enum VatRegime
{
    General = 1,
    Resimple = 2,
    Exempt = 3
}

public enum BankAccountType
{
    Checking = 1,
    Savings = 2
}
