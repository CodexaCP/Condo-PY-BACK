namespace Condo.Infrastructure.Persistence;

/// <summary>
/// Bloqueo a nivel de base de datos del Libro Diario (Ley 1034/83, art. 79): los asientos y sus renglones se pueden insertar pero nunca
/// modificar ni borrar. Un error se corrige con un contraasiento. Los triggers frenan hasta una modificacion hecha a mano en la base: para
/// una correccion excepcional hay que desactivarlos de forma deliberada (DISABLE TRIGGER) y dejar constancia.
/// </summary>
public static class JournalImmutability
{
    public const string EntriesTrigger = "TR_JournalEntries_Immutable";
    public const string LinesTrigger = "TR_JournalEntryLines_Immutable";

    public const string Message = "El Libro Diario no se puede modificar ni borrar: un error se corrige con un contraasiento.";

    public static string CreateSql(string table, string trigger) =>
        $"CREATE TRIGGER [{trigger}] ON [{table}] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 50000, N'{Message}', 1; END";

    public static string DropSql(string trigger) => $"DROP TRIGGER IF EXISTS [{trigger}]";
}
