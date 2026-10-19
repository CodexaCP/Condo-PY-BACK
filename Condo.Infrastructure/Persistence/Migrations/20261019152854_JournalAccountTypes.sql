-- ============================================================
-- Migracion EF: 20261019152854_JournalAccountTypes
-- Finanzas, Libro Diario fase 4: columna JournalEntryLines.AccountType (clase de la cuenta del plan al asentar: Asset, Liability, Fund, Income o
-- Expense). Sirve para el cierre del ejercicio y los estados (balance general y de resultados). Es nula en lo asentado antes: se deduce del
-- id de la cuenta o de su codigo. Solo agrega una columna nula; no toca datos existentes.
-- Va DESPUES de 20261019125025_JournalReversals.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I y -b:
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/JournalAccountTypes.sql
--
-- Es idempotente (revisa __EFMigrationsHistory) y verifica al final que
-- la columna exista de verdad; si falta, aborta antes del COMMIT.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019152854_JournalAccountTypes'
)
BEGIN
    ALTER TABLE [JournalEntryLines] ADD [AccountType] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019152854_JournalAccountTypes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019152854_JournalAccountTypes', N'8.0.8');
END;
GO

IF COL_LENGTH(N'JournalEntryLines', N'AccountType') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261019152854_JournalAccountTypes')
    RAISERROR(N'JournalAccountTypes: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'JournalAccountTypes OK' AS Resultado, COL_LENGTH(N'JournalEntryLines', N'AccountType') AS Columna;
GO
