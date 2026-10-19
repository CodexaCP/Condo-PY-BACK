namespace Condo.Infrastructure.Persistence;

/// <summary>
/// Bloqueo a nivel de base de datos de las transferencias entre cuentas propias: no se borran, no se les cambia el importe, las cuentas, la
/// fecha ni el edificio, y una anulada no se reactiva. Lo unico que cambia en una transferencia es su anulacion (con motivo). Frenan hasta una
/// modificacion hecha a mano en la base: para una correccion excepcional hay que desactivar el trigger de forma deliberada (DISABLE TRIGGER).
/// </summary>
public static class FinanceTransferProtection
{
    public const string DeleteTrigger = "TR_FinanceTransfers_NoDelete";
    public const string UpdateTrigger = "TR_FinanceTransfers_Protected";

    public const string DeleteMessage = "Una operacion de Finanzas no se puede borrar: se anula con motivo.";
    public const string UpdateMessage = "Una operacion de Finanzas no se puede modificar: se anula con motivo y se carga de nuevo.";

    public static string CreateDeleteSql() =>
        $"CREATE TRIGGER [{DeleteTrigger}] ON [FinanceTransfers] INSTEAD OF DELETE AS BEGIN THROW 50000, N'{DeleteMessage}', 1; END";

    public static string CreateUpdateSql() =>
        $"CREATE TRIGGER [{UpdateTrigger}] ON [FinanceTransfers] AFTER UPDATE AS BEGIN "
        + "IF UPDATE([BuildingId]) OR UPDATE([CompanyId]) OR UPDATE([FromAccountId]) OR UPDATE([ToAccountId]) OR UPDATE([Amount]) "
        + "OR UPDATE([TransferDate]) OR UPDATE([CreatedByUserId]) "
        + $"THROW 50000, N'{UpdateMessage}', 1; "
        + "IF EXISTS (SELECT 1 FROM deleted d INNER JOIN inserted i ON i.[Id] = d.[Id] WHERE d.[Status] = 2 AND i.[Status] <> 2) "
        + $"THROW 50000, N'{UpdateMessage}', 1; "
        + "END";

    public static string DropSql(string trigger) => $"DROP TRIGGER IF EXISTS [{trigger}]";
}
