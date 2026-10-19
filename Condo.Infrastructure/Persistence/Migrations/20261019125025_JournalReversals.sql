-- ============================================================
-- Migracion EF: 20261019125025_JournalReversals
-- Finanzas, Libro Diario fase 2 (contraasientos e inmutabilidad):
--  1) el indice unico del movimiento de origen pasa a usar OperationDate (fecha de la operacion) en lugar de Date (fecha contable), para que
--     las correcciones, que llevan otra fecha contable, no puedan duplicar un movimiento;
--  2) un asiento solo puede tener un contraasiento (indice unico filtrado sobre ReversesEntryId);
--  3) triggers que impiden UPDATE y DELETE en JournalEntries y JournalEntryLines (Ley 1034/83, art. 79: un error se corrige con un
--     contraasiento). Para una correccion excepcional hay que desactivarlos de forma deliberada: DISABLE TRIGGER ... y dejar constancia.
-- No toca datos. Va DESPUES de 20261019120048_JournalEntries.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/JournalReversals.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que los indices y los triggers existan de verdad; si algo
-- falta, aborta antes del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019125025_JournalReversals'
)
BEGIN
    DROP INDEX [IX_JournalEntries_BuildingId_SourceType_SourceId_Date_SourceAccountId_Revision] ON [JournalEntries];
    DROP INDEX [IX_JournalEntries_ReversesEntryId] ON [JournalEntries];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019125025_JournalReversals'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_JournalEntries_BuildingId_SourceType_SourceId_OperationDate_SourceAccountId_Revision] ON [JournalEntries] ([BuildingId], [SourceType], [SourceId], [OperationDate], [SourceAccountId], [Revision]) WHERE [SourceType] IS NOT NULL');
    EXEC(N'CREATE UNIQUE INDEX [IX_JournalEntries_ReversesEntryId] ON [JournalEntries] ([ReversesEntryId]) WHERE [ReversesEntryId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019125025_JournalReversals'
)
BEGIN
    EXEC(N'CREATE TRIGGER [TR_JournalEntries_Immutable] ON [JournalEntries] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 50000, N''El Libro Diario no se puede modificar ni borrar: un error se corrige con un contraasiento.'', 1; END');
    EXEC(N'CREATE TRIGGER [TR_JournalEntryLines_Immutable] ON [JournalEntryLines] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 50000, N''El Libro Diario no se puede modificar ni borrar: un error se corrige con un contraasiento.'', 1; END');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019125025_JournalReversals'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019125025_JournalReversals', N'8.0.8');
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_BuildingId_SourceType_SourceId_OperationDate_SourceAccountId_Revision' AND object_id = OBJECT_ID(N'JournalEntries'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_ReversesEntryId' AND object_id = OBJECT_ID(N'JournalEntries') AND is_unique = 1)
   OR EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_BuildingId_SourceType_SourceId_Date_SourceAccountId_Revision' AND object_id = OBJECT_ID(N'JournalEntries'))
   OR NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_JournalEntries_Immutable')
   OR NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_JournalEntryLines_Immutable')
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261019125025_JournalReversals')
    RAISERROR(N'JournalReversals: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'JournalReversals OK' AS Resultado,
       (SELECT COUNT(*) FROM sys.triggers WHERE name IN (N'TR_JournalEntries_Immutable', N'TR_JournalEntryLines_Immutable')) AS Triggers;
GO
