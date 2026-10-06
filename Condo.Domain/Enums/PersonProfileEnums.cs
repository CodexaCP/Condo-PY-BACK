namespace Condo.Domain.Enums;

// Persona fisica o juridica (empresa): una unidad puede pertenecer a una sociedad, que se factura por su razon social.
public enum PersonType
{
    Natural = 1,
    Legal = 2
}

// Relacion del residente con la unidad.
public enum ResidentRelationship
{
    Tenant = 1,
    Family = 2,
    Employee = 3,
    Other = 4
}
