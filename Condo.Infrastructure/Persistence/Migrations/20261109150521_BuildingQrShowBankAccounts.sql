-- ============================================================
-- Migracion EF: 20261109150521_BuildingQrShowBankAccounts
-- Interruptor por edificio para mostrar las cuentas bancarias de cobro (datos para transferir) en la pagina publica a la que lleva el QR de
-- la liquidacion: columna Buildings.QrShowBankAccounts (bit, por defecto 0 = apagado). Solo agrega una columna con valor por defecto;
-- no toca datos existentes.
-- Va DESPUES de 20261109141541_DocumentVerification.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/BuildingQrShowBankAccounts.sql
--
-- Es idempotente (revisa __EFMigrationsHistory) y verifica al final que la columna exista; si falta, aborta antes del COMMIT.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261109150521_BuildingQrShowBankAccounts')
BEGIN
    ALTER TABLE [Buildings] ADD [QrShowBankAccounts] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261109150521_BuildingQrShowBankAccounts')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261109150521_BuildingQrShowBankAccounts', N'8.0.8');
END;
GO

IF COL_LENGTH(N'Buildings', N'QrShowBankAccounts') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261109150521_BuildingQrShowBankAccounts')
    RAISERROR(N'BuildingQrShowBankAccounts: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'BuildingQrShowBankAccounts OK' AS Resultado, (SELECT COUNT(*) FROM [Buildings] WHERE [QrShowBankAccounts] = 1) AS EdificiosConCuentasVisibles;
GO
