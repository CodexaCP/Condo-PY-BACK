-- ============================================================
-- Migracion EF: 20261112161401_FinanceSettingsRowVersion
-- Libro Diario: version de fila (RowVersion) en FinanceSettings, para que dos personas que asientan a la vez no se pisen
-- «Asentado hasta» (el que llega segundo recibe un conflicto y reintenta).
-- Solo agrega una columna rowversion; no modifica datos existentes.
-- Va DESPUES de 20261111145617_VotingIntegrity.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/FinanceSettingsRowVersion.sql
--
-- Es idempotente (revisa __EFMigrationsHistory) y verifica al final que la columna exista de verdad; si falta, aborta antes del COMMIT.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261112161401_FinanceSettingsRowVersion')
BEGIN
    ALTER TABLE [FinanceSettings] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261112161401_FinanceSettingsRowVersion')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261112161401_FinanceSettingsRowVersion', N'8.0.8');
END;
GO

IF COL_LENGTH(N'FinanceSettings', N'RowVersion') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261112161401_FinanceSettingsRowVersion')
    RAISERROR(N'FinanceSettingsRowVersion: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'FinanceSettingsRowVersion OK' AS Resultado, (SELECT COUNT(*) FROM [FinanceSettings]) AS Edificios;
GO
