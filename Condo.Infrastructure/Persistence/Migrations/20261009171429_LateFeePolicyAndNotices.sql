-- ============================================================
-- Migracion EF: 20261009171429_LateFeePolicyAndNotices
-- Centro de configuracion del edificio, fase 3 (politica de mora, fondos, avisos y umbral del presupuesto):
--   Buildings: LateFeeCapPercentage, LateFeeMinAmount, LateFeeAppliesToReserve/Extraordinary/Individual (por defecto 1: igual que hoy),
--              LateFeePolicyConfirmed, ReserveUsePolicy (por defecto FreeUse), ReserveUseThreshold, FundPolicyConfirmed.
--   Units: LateFeeExempt, LateFeeExemptReason, LateFeeExemptByUserId, LateFeeExemptAtUtc (exoneracion de mora por unidad).
--   FinanceSettings: BudgetWarnPercent (por defecto 10: el umbral fijo de hoy).
--   Tabla BuildingNoticeRules (reglas de avisos automaticos por edificio; sin filas rige el comportamiento actual).
-- Todos los valores por defecto reproducen el comportamiento actual: los edificios existentes no cambian. No toca datos existentes.
-- Va DESPUES de 20261009164951_PeriodClosing.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/LateFeePolicyAndNotices.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009171429_LateFeePolicyAndNotices'
)
BEGIN
    ALTER TABLE [Units] ADD [LateFeeExempt] bit NOT NULL DEFAULT CAST(0 AS bit);
    ALTER TABLE [Units] ADD [LateFeeExemptAtUtc] datetime2 NULL;
    ALTER TABLE [Units] ADD [LateFeeExemptByUserId] uniqueidentifier NULL;
    ALTER TABLE [Units] ADD [LateFeeExemptReason] nvarchar(300) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009171429_LateFeePolicyAndNotices'
)
BEGIN
    ALTER TABLE [FinanceSettings] ADD [BudgetWarnPercent] int NOT NULL DEFAULT 10;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009171429_LateFeePolicyAndNotices'
)
BEGIN
    ALTER TABLE [Buildings] ADD [FundPolicyConfirmed] bit NOT NULL DEFAULT CAST(0 AS bit);
    ALTER TABLE [Buildings] ADD [LateFeeAppliesToExtraordinary] bit NOT NULL DEFAULT CAST(1 AS bit);
    ALTER TABLE [Buildings] ADD [LateFeeAppliesToIndividual] bit NOT NULL DEFAULT CAST(1 AS bit);
    ALTER TABLE [Buildings] ADD [LateFeeAppliesToReserve] bit NOT NULL DEFAULT CAST(1 AS bit);
    ALTER TABLE [Buildings] ADD [LateFeeCapPercentage] decimal(7,2) NULL;
    ALTER TABLE [Buildings] ADD [LateFeeMinAmount] decimal(18,2) NULL;
    ALTER TABLE [Buildings] ADD [LateFeePolicyConfirmed] bit NOT NULL DEFAULT CAST(0 AS bit);
    ALTER TABLE [Buildings] ADD [ReserveUsePolicy] nvarchar(40) NOT NULL DEFAULT N'FreeUse';
    ALTER TABLE [Buildings] ADD [ReserveUseThreshold] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009171429_LateFeePolicyAndNotices'
)
BEGIN
    CREATE TABLE [BuildingNoticeRules] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [OffsetDays] int NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_BuildingNoticeRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BuildingNoticeRules_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BuildingNoticeRules_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009171429_LateFeePolicyAndNotices'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_BuildingNoticeRules_BuildingId_Kind] ON [BuildingNoticeRules] ([BuildingId], [Kind]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009171429_LateFeePolicyAndNotices'
)
BEGIN
    CREATE INDEX [IX_BuildingNoticeRules_CompanyId] ON [BuildingNoticeRules] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009171429_LateFeePolicyAndNotices'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009171429_LateFeePolicyAndNotices', N'8.0.8');
END;
GO

IF COL_LENGTH(N'Units', N'LateFeeExempt') IS NULL
   OR COL_LENGTH(N'Units', N'LateFeeExemptAtUtc') IS NULL
   OR COL_LENGTH(N'Units', N'LateFeeExemptByUserId') IS NULL
   OR COL_LENGTH(N'Units', N'LateFeeExemptReason') IS NULL
   OR COL_LENGTH(N'FinanceSettings', N'BudgetWarnPercent') IS NULL
   OR COL_LENGTH(N'Buildings', N'FundPolicyConfirmed') IS NULL
   OR COL_LENGTH(N'Buildings', N'LateFeeAppliesToExtraordinary') IS NULL
   OR COL_LENGTH(N'Buildings', N'LateFeeAppliesToIndividual') IS NULL
   OR COL_LENGTH(N'Buildings', N'LateFeeAppliesToReserve') IS NULL
   OR COL_LENGTH(N'Buildings', N'LateFeeCapPercentage') IS NULL
   OR COL_LENGTH(N'Buildings', N'LateFeeMinAmount') IS NULL
   OR COL_LENGTH(N'Buildings', N'LateFeePolicyConfirmed') IS NULL
   OR COL_LENGTH(N'Buildings', N'ReserveUsePolicy') IS NULL
   OR COL_LENGTH(N'Buildings', N'ReserveUseThreshold') IS NULL
   OR OBJECT_ID(N'BuildingNoticeRules', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingNoticeRules_BuildingId_Kind' AND object_id = OBJECT_ID(N'BuildingNoticeRules'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingNoticeRules_CompanyId' AND object_id = OBJECT_ID(N'BuildingNoticeRules'))
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261009171429_LateFeePolicyAndNotices')
    RAISERROR(N'LateFeePolicyAndNotices: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'LateFeePolicyAndNotices OK' AS Resultado,
       COL_LENGTH(N'Buildings', N'ReserveUsePolicy') AS ColumnaBuildings,
       COL_LENGTH(N'Units', N'LateFeeExempt') AS ColumnaUnits,
       OBJECT_ID(N'BuildingNoticeRules', N'U') AS Tabla;
GO
