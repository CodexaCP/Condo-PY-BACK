-- ============================================================
-- Migracion EF: 20261005135732_AddAdvertisingModule
-- Modulo de Publicidad (banners en la app): agrega Buildings.AdsEnabled (interruptor por edificio, apagado por defecto: ningun edificio
-- muestra anuncios hasta que el SuperAdmin lo active) y crea las tablas AdCampaigns y AdCampaignBuildings. No toca datos existentes.
-- Va DESPUES de 20261005123940_OwnerCreditMovementOnHold.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/AddAdvertisingModule.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005135732_AddAdvertisingModule'
)
BEGIN
    ALTER TABLE [Buildings] ADD [AdsEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005135732_AddAdvertisingModule'
)
BEGIN
    CREATE TABLE [AdCampaigns] (
        [Id] uniqueidentifier NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [AdvertiserName] nvarchar(200) NOT NULL,
        [Description] nvarchar(500) NULL,
        [CtaText] nvarchar(60) NOT NULL,
        [CtaUrl] nvarchar(500) NULL,
        [ImageUrl] nvarchar(500) NOT NULL,
        [Category] nvarchar(30) NOT NULL,
        [Position] int NOT NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NOT NULL,
        [MonthlyAmount] decimal(18,2) NULL,
        [IsActive] bit NOT NULL,
        [NotifyBeforeExpiry] bit NOT NULL,
        [ExpiryNotificationSent] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_AdCampaigns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AdCampaigns_ApplicationUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AdCampaigns_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005135732_AddAdvertisingModule'
)
BEGIN
    CREATE TABLE [AdCampaignBuildings] (
        [Id] uniqueidentifier NOT NULL,
        [AdCampaignId] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_AdCampaignBuildings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AdCampaignBuildings_AdCampaigns_AdCampaignId] FOREIGN KEY ([AdCampaignId]) REFERENCES [AdCampaigns] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AdCampaignBuildings_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005135732_AddAdvertisingModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AdCampaignBuildings_AdCampaignId_BuildingId] ON [AdCampaignBuildings] ([AdCampaignId], [BuildingId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005135732_AddAdvertisingModule'
)
BEGIN
    CREATE INDEX [IX_AdCampaignBuildings_BuildingId] ON [AdCampaignBuildings] ([BuildingId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005135732_AddAdvertisingModule'
)
BEGIN
    CREATE INDEX [IX_AdCampaigns_CompanyId_IsActive_StartDate_EndDate] ON [AdCampaigns] ([CompanyId], [IsActive], [StartDate], [EndDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005135732_AddAdvertisingModule'
)
BEGIN
    CREATE INDEX [IX_AdCampaigns_CreatedByUserId] ON [AdCampaigns] ([CreatedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005135732_AddAdvertisingModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005135732_AddAdvertisingModule', N'8.0.8');
END;
GO

IF COL_LENGTH(N'Buildings', N'AdsEnabled') IS NULL
   OR OBJECT_ID(N'AdCampaigns', N'U') IS NULL
   OR OBJECT_ID(N'AdCampaignBuildings', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261005135732_AddAdvertisingModule')
    RAISERROR(N'AddAdvertisingModule: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'AddAdvertisingModule OK' AS Resultado;
GO
