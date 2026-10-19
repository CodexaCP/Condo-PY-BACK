using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Asiento asentado en el Libro Diario de un edificio. A diferencia de los asientos sugeridos (que se arman al consultar), este queda
// guardado con un numero correlativo por edificio y ejercicio, sin huecos, y NO se edita ni se borra: un error se corrige con un
// contraasiento (Ley 1034/83, art. 79). Guarda una copia del codigo y nombre de cada cuenta al asentar, asi que seguir editando el plan de
// cuentas no cambia lo ya asentado. IsDeleted existe por la base comun pero nunca se usa.
public class JournalEntry : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }

    // Ejercicio en que cae la fecha del asiento (se identifica por el año en que empieza) y numero dentro de ese ejercicio.
    public int FiscalYear { get; set; }
    public int Number { get; set; }

    // Fecha contable y fecha en que ocurrio la operacion (distintas cuando se asienta algo atrasado).
    public DateOnly Date { get; set; }
    public DateOnly OperationDate { get; set; }

    public string Description { get; set; } = string.Empty;
    public string ThirdParty { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;

    public JournalEntryKind Kind { get; set; } = JournalEntryKind.Auto;

    // Origen del movimiento (tipo del libro de Finanzas, id y cuenta financiera). Nulos en los asientos que no vienen de un movimiento.
    public int? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? SourceAccountId { get; set; }
    // Version del asiento de ese origen: 0 el primero; los reasientos suben el numero.
    public int Revision { get; set; }

    // Asiento que este contraasiento invierte.
    public Guid? ReversesEntryId { get; set; }

    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }

    // Huella de las lineas, para detectar mas adelante si el movimiento de origen cambio despues de asentarlo.
    public string ContentHash { get; set; } = string.Empty;

    public DateTime PostedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? PostedByUserId { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
}
