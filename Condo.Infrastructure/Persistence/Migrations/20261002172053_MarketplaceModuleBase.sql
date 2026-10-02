-- ============================================================
-- Migracion EF: 20261002172053_MarketplaceModuleBase
-- Marketplace de espacios temporales, fase 1: interruptor, comision de gestion y datos para transferir en Buildings,
-- y marca "incluye Marketplace" en Plans. Los edificios existentes quedan apagados con comision 10; los planes
-- existentes quedan sin el modulo.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/MarketplaceModuleBase.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002172053_MarketplaceModuleBase'
)
BEGIN
    ALTER TABLE [Plans] ADD [IncludesMarketplace] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002172053_MarketplaceModuleBase'
)
BEGIN
    ALTER TABLE [Buildings] ADD [MarketplaceCommissionPercent] decimal(5,2) NOT NULL DEFAULT 10.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002172053_MarketplaceModuleBase'
)
BEGIN
    ALTER TABLE [Buildings] ADD [MarketplaceEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002172053_MarketplaceModuleBase'
)
BEGIN
    ALTER TABLE [Buildings] ADD [MarketplaceTransferInfo] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002172053_MarketplaceModuleBase'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002172053_MarketplaceModuleBase', N'8.0.8');
END;
GO

IF COL_LENGTH(N'Plans', N'IncludesMarketplace') IS NULL OR COL_LENGTH(N'Buildings', N'MarketplaceCommissionPercent') IS NULL OR COL_LENGTH(N'Buildings', N'MarketplaceEnabled') IS NULL OR COL_LENGTH(N'Buildings', N'MarketplaceTransferInfo') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261002172053_MarketplaceModuleBase')
    RAISERROR(N'MarketplaceModuleBase: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'MarketplaceModuleBase OK' AS Resultado;
GO
