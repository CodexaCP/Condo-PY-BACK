-- ============================================================
-- Migracion EF: 20261001173551_FinanceLedgerDefaultAccount
-- Modulo Finanzas del edificio, fase 2: FinanceSettings.DefaultAccountId (cuenta por defecto del libro).
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/FinanceLedgerDefaultAccount.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001173551_FinanceLedgerDefaultAccount'
)
BEGIN
    ALTER TABLE [FinanceSettings] ADD [DefaultAccountId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001173551_FinanceLedgerDefaultAccount'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001173551_FinanceLedgerDefaultAccount', N'8.0.8');
END;
GO

IF COL_LENGTH(N'FinanceSettings', N'DefaultAccountId') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261001173551_FinanceLedgerDefaultAccount')
    RAISERROR(N'FinanceLedgerDefaultAccount: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'FinanceLedgerDefaultAccount OK' AS Resultado;
GO
