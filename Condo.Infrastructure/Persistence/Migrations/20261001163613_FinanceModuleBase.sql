-- ============================================================
-- Migracion EF: 20261001163613_FinanceModuleBase
-- Modulo "Finanzas del edificio", fase 1:
--   Plans.IncludesFinanceModule, Buildings.FinanceModuleEnabled,
--   tablas FinanceSettings, FinancialAccounts y LedgerCategories.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/FinanceModuleBase.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que las columnas, tablas e indices existan de verdad; si
-- algo falta, aborta antes del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    ALTER TABLE [Plans] ADD [IncludesFinanceModule] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    ALTER TABLE [Buildings] ADD [FinanceModuleEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    CREATE TABLE [FinanceSettings] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [FinanceStartDate] date NULL,
        [FiscalYearStartMonth] int NOT NULL,
        [SetupCompleted] bit NOT NULL,
        [SetupCompletedAtUtc] datetime2 NULL,
        [SetupCompletedByUserId] uniqueidentifier NULL,
        [EnabledAtUtc] datetime2 NULL,
        [EnabledByUserId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_FinanceSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FinanceSettings_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FinanceSettings_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    CREATE TABLE [FinancialAccounts] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [OpeningBalance] decimal(18,2) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_FinancialAccounts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FinancialAccounts_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FinancialAccounts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    CREATE TABLE [LedgerCategories] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [ParentId] uniqueidentifier NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [ExternalCode] nvarchar(50) NULL,
        [SystemKey] nvarchar(60) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_LedgerCategories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LedgerCategories_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LedgerCategories_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LedgerCategories_LedgerCategories_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [LedgerCategories] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    EXEC(N'UPDATE [Plans] SET [IncludesFinanceModule] = CAST(0 AS bit)
    WHERE [Id] = ''a0000000-0000-0000-0000-000000000001'';
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_FinanceSettings_BuildingId] ON [FinanceSettings] ([BuildingId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    CREATE INDEX [IX_FinanceSettings_CompanyId] ON [FinanceSettings] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_FinancialAccounts_BuildingId_Name] ON [FinancialAccounts] ([BuildingId], [Name]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_FinancialAccounts_BuildingId_Type] ON [FinancialAccounts] ([BuildingId], [Type]) WHERE [IsDeleted] = 0 AND [Type] <> ''Bank''');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    CREATE INDEX [IX_FinancialAccounts_CompanyId] ON [FinancialAccounts] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_LedgerCategories_BuildingId_Code] ON [LedgerCategories] ([BuildingId], [Code]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_LedgerCategories_BuildingId_SystemKey] ON [LedgerCategories] ([BuildingId], [SystemKey]) WHERE [IsDeleted] = 0 AND [SystemKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    CREATE INDEX [IX_LedgerCategories_CompanyId] ON [LedgerCategories] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    CREATE INDEX [IX_LedgerCategories_ParentId] ON [LedgerCategories] ([ParentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163613_FinanceModuleBase'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001163613_FinanceModuleBase', N'8.0.8');
END;
GO

IF COL_LENGTH(N'Plans', N'IncludesFinanceModule') IS NULL
   OR COL_LENGTH(N'Buildings', N'FinanceModuleEnabled') IS NULL
   OR OBJECT_ID(N'FinanceSettings', N'U') IS NULL
   OR OBJECT_ID(N'FinancialAccounts', N'U') IS NULL
   OR OBJECT_ID(N'LedgerCategories', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FinanceSettings_BuildingId' AND object_id = OBJECT_ID(N'FinanceSettings'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FinancialAccounts_BuildingId_Name' AND object_id = OBJECT_ID(N'FinancialAccounts'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FinancialAccounts_BuildingId_Type' AND object_id = OBJECT_ID(N'FinancialAccounts'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LedgerCategories_BuildingId_Code' AND object_id = OBJECT_ID(N'LedgerCategories'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LedgerCategories_BuildingId_SystemKey' AND object_id = OBJECT_ID(N'LedgerCategories'))
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261001163613_FinanceModuleBase')
    RAISERROR(N'FinanceModuleBase: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'FinanceModuleBase OK' AS Resultado,
       COL_LENGTH(N'Plans', N'IncludesFinanceModule') AS PlansColumna,
       COL_LENGTH(N'Buildings', N'FinanceModuleEnabled') AS BuildingsColumna,
       OBJECT_ID(N'FinanceSettings', N'U') AS FinanceSettings,
       OBJECT_ID(N'FinancialAccounts', N'U') AS FinancialAccounts,
       OBJECT_ID(N'LedgerCategories', N'U') AS LedgerCategories;
GO
