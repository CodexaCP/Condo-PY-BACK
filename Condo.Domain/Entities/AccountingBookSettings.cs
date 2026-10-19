using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Configuracion de los libros contables de un edificio (una por edificio): quien es el titular, sus datos para el encabezado del Libro
// Diario, el contador y los datos de la rubrica del Registro Publico de Comercio (Ley 1034/83, arts. 77 y 78). El Diario es siempre uno por
// edificio, aunque el titular sea la empresa administradora. Los datos del titular son opcionales: vacios, se usan los del consorcio
// (ficha del edificio) o el nombre de la empresa. Es solo informacion: no mueve saldos ni cambia el libro.
public class AccountingBookSettings : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }

    public AccountingBookHolder Holder { get; set; } = AccountingBookHolder.Consortium;

    // Datos del titular que pisan los de la ficha del edificio (consorcio) o de la empresa (administradora).
    public string? HolderLegalName { get; set; }
    public string? HolderRuc { get; set; }
    public string? HolderAddress { get; set; }

    // Contador matriculado (art. 77).
    public string? AccountantName { get; set; }
    public string? AccountantRegistration { get; set; }

    // Rubrica (art. 78): se carga a mano, el tramite es externo.
    public string? BookNumber { get; set; }
    public DateOnly? RubricDate { get; set; }
    public int? AuthorizedFolios { get; set; }
    public int? UsedFolios { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
}
