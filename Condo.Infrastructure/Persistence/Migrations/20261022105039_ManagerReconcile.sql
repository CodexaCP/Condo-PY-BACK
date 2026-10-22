-- ============================================================
-- Migracion EF: 20261022105039_ManagerReconcile
-- Finanzas, conciliacion bancaria desde la app: columna FinanceSettings.ManagerCanReconcile (bit NOT NULL, 0 por defecto). Con el interruptor
-- encendido, el Encargado del edificio tambien puede armar, cerrar y reabrir conciliaciones (antes solo SuperAdmin y Administrador de empresa).
-- Apagado por defecto: no cambia nada de lo existente.
-- Solo agrega una columna; no toca datos existentes. Va DESPUES de 20261021165658_SupplierCreditRecovery.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I y -b:
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/ManagerReconcile.sql
--
-- Es idempotente (revisa __EFMigrationsHistory) y verifica al final que
-- la columna exista de verdad; si falta, aborta antes del COMMIT.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105039_ManagerReconcile'
)
BEGIN
    ALTER TABLE [FinanceSettings] ADD [ManagerCanReconcile] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105039_ManagerReconcile'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261022105039_ManagerReconcile', N'8.0.8');
END;
GO

IF COL_LENGTH(N'FinanceSettings', N'ManagerCanReconcile') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261022105039_ManagerReconcile')
    RAISERROR(N'ManagerReconcile: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'ManagerReconcile OK' AS Resultado, COL_LENGTH(N'FinanceSettings', N'ManagerCanReconcile') AS Columna;
GO
