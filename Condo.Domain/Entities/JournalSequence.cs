using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Ultimo numero de asiento usado por edificio y ejercicio. Se actualiza en la misma transaccion que inserta los asientos: si el guardado
// falla, el numero no se consume (sin huecos); si dos personas asientan a la vez, el indice unico de JournalEntry deja pasar a una sola.
public class JournalSequence : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public int FiscalYear { get; set; }
    public int LastNumber { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
}
