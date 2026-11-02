-- ============================================================
-- Migracion EF: 20261102114757_MultiCountryCore
-- Nucleo multipais (docs/ANALISIS_MULTIPAIS_NUCLEO.md). Solo agrega columnas con valores por defecto de Paraguay; no toca datos de otras tablas:
--   Companies: CountryCode (PY), CurrencyCode (PYG), LocaleCode (es-PY), TaxName (IVA), TaxGeneralRate (10), TaxReducedRate (5).
--   Buildings: Jurisdiction, AdministratorRegistryNumber, FiscalYearEndMonth (vacios) y SettlementApprovalMode (ManagerPresidentAdmin: la
--              cadena de aprobacion de siempre).
--   Votes:     MajorityRule (Simple) e IsInformative (0): las votaciones existentes se comportan igual.
-- Las empresas que ya existen quedan en Paraguay con IVA 10 % / 5 % (el UPDATE rellena las dos tasas, que admiten NULL).
-- Va DESPUES de 20261022113742_JournalSupportDocs.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I y -b:
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/MultiCountryCore.sql
--
-- Es idempotente (revisa __EFMigrationsHistory) y verifica al final que
-- las columnas existan de verdad; si faltan, aborta antes del COMMIT.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261102114757_MultiCountryCore')
BEGIN
    ALTER TABLE [Votes] ADD [IsInformative] bit NOT NULL DEFAULT CAST(0 AS bit);
    ALTER TABLE [Votes] ADD [MajorityRule] nvarchar(20) NOT NULL DEFAULT N'Simple';
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261102114757_MultiCountryCore')
BEGIN
    ALTER TABLE [Companies] ADD [CountryCode] nvarchar(2) NOT NULL DEFAULT N'PY';
    ALTER TABLE [Companies] ADD [CurrencyCode] nvarchar(3) NOT NULL DEFAULT N'PYG';
    ALTER TABLE [Companies] ADD [LocaleCode] nvarchar(10) NOT NULL DEFAULT N'es-PY';
    ALTER TABLE [Companies] ADD [TaxGeneralRate] decimal(5,2) NULL DEFAULT 10.0;
    ALTER TABLE [Companies] ADD [TaxName] nvarchar(30) NOT NULL DEFAULT N'IVA';
    ALTER TABLE [Companies] ADD [TaxReducedRate] decimal(5,2) NULL DEFAULT 5.0;
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261102114757_MultiCountryCore')
BEGIN
    ALTER TABLE [Buildings] ADD [AdministratorRegistryNumber] nvarchar(50) NULL;
    ALTER TABLE [Buildings] ADD [FiscalYearEndMonth] int NULL;
    ALTER TABLE [Buildings] ADD [Jurisdiction] nvarchar(100) NULL;
    ALTER TABLE [Buildings] ADD [SettlementApprovalMode] nvarchar(30) NOT NULL DEFAULT N'ManagerPresidentAdmin';
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261102114757_MultiCountryCore')
BEGIN
    EXEC(N'UPDATE [Companies] SET [TaxGeneralRate] = 10.0, [TaxReducedRate] = 5.0 WHERE [TaxGeneralRate] IS NULL AND [TaxReducedRate] IS NULL');
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261102114757_MultiCountryCore')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102114757_MultiCountryCore', N'8.0.8');
END;
GO

IF COL_LENGTH(N'Companies', N'CountryCode') IS NULL
   OR COL_LENGTH(N'Companies', N'CurrencyCode') IS NULL
   OR COL_LENGTH(N'Companies', N'TaxGeneralRate') IS NULL
   OR COL_LENGTH(N'Buildings', N'Jurisdiction') IS NULL
   OR COL_LENGTH(N'Buildings', N'SettlementApprovalMode') IS NULL
   OR COL_LENGTH(N'Votes', N'MajorityRule') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261102114757_MultiCountryCore')
    RAISERROR(N'MultiCountryCore: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'MultiCountryCore OK' AS Resultado,
       (SELECT COUNT(*) FROM [Companies] WHERE [CountryCode] = N'PY' AND [TaxGeneralRate] = 10.0) AS EmpresasParaguayConIva;
GO
