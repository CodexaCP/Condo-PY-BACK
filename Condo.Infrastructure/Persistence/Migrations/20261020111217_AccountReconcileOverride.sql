-- ============================================================
-- Migracion EF: 20261020111217_AccountReconcileOverride
-- Finanzas, conciliacion bancaria: columna FinancialAccounts.ReconcileOverride (bit NULL). Deja que la empresa elija que cuentas se concilian
-- con un extracto. Nulo = lo de siempre segun el tipo (los bancos se concilian; la caja y el fondo de reserva no), asi que no cambia nada de lo
-- existente. Por ejemplo, un fondo de reserva que esta en una cuenta bancaria se puede marcar para conciliarlo.
-- Solo agrega una columna nula; no toca datos existentes. Va DESPUES de 20261019182431_FinanceTransfers.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I y -b:
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/AccountReconcileOverride.sql
--
-- Es idempotente (revisa __EFMigrationsHistory) y verifica al final que
-- la columna exista de verdad; si falta, aborta antes del COMMIT.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020111217_AccountReconcileOverride'
)
BEGIN
    ALTER TABLE [FinancialAccounts] ADD [ReconcileOverride] bit NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020111217_AccountReconcileOverride'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020111217_AccountReconcileOverride', N'8.0.8');
END;
GO

IF COL_LENGTH(N'FinancialAccounts', N'ReconcileOverride') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261020111217_AccountReconcileOverride')
    RAISERROR(N'AccountReconcileOverride: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'AccountReconcileOverride OK' AS Resultado, COL_LENGTH(N'FinancialAccounts', N'ReconcileOverride') AS Columna;
GO
