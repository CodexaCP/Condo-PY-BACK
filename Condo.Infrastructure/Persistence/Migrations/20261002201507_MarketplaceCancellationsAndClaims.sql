-- ============================================================
-- Migracion EF: 20261002201507_MarketplaceCancellationsAndClaims
-- Marketplace de espacios temporales, fase 7: cancelaciones, reembolsos pendientes, reclamos ("Reportar un problema"), deuda por
-- gestion del propietario y aviso de inicio. Crea MarketplaceRefunds, MarketplaceClaims y MarketplaceOwnerDebts (con sus indices
-- unicos filtrados: un reembolso y una deuda por reserva, un reclamo abierto por reserva) y agrega a MarketplaceReservations las
-- columnas del aviso de inicio (StartNoticeSentAtUtc, StartResponse, StartResponseReason, StartResponseAtUtc).
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/MarketplaceCancellationsAndClaims.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    ALTER TABLE [MarketplaceReservations] ADD [StartNoticeSentAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    ALTER TABLE [MarketplaceReservations] ADD [StartResponse] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    ALTER TABLE [MarketplaceReservations] ADD [StartResponseAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    ALTER TABLE [MarketplaceReservations] ADD [StartResponseReason] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE TABLE [MarketplaceClaims] (
        [Id] uniqueidentifier NOT NULL,
        [ReservationId] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [OpenedByUserId] uniqueidentifier NOT NULL,
        [OpenedBy] nvarchar(20) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Resolution] nvarchar(20) NULL,
        [ResolutionNote] nvarchar(500) NULL,
        [ResolvedByUserId] uniqueidentifier NULL,
        [ResolvedAtUtc] datetime2 NULL,
        [RowVersion] rowversion NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MarketplaceClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MarketplaceClaims_ApplicationUsers_OpenedByUserId] FOREIGN KEY ([OpenedByUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceClaims_ApplicationUsers_ResolvedByUserId] FOREIGN KEY ([ResolvedByUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceClaims_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceClaims_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceClaims_MarketplaceReservations_ReservationId] FOREIGN KEY ([ReservationId]) REFERENCES [MarketplaceReservations] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE TABLE [MarketplaceOwnerDebts] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [OwnerId] uniqueidentifier NOT NULL,
        [ReservationId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [PaidAmount] decimal(18,2) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [SettledAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MarketplaceOwnerDebts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MarketplaceOwnerDebts_ApplicationUsers_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceOwnerDebts_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceOwnerDebts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceOwnerDebts_MarketplaceReservations_ReservationId] FOREIGN KEY ([ReservationId]) REFERENCES [MarketplaceReservations] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE TABLE [MarketplaceRefunds] (
        [Id] uniqueidentifier NOT NULL,
        [ReservationId] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [RecipientUserId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Origin] nvarchar(20) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [ReturnedAtUtc] datetime2 NULL,
        [ReturnedByUserId] uniqueidentifier NULL,
        [OverdueAlertSentAtUtc] datetime2 NULL,
        [RowVersion] rowversion NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MarketplaceRefunds] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MarketplaceRefunds_ApplicationUsers_RecipientUserId] FOREIGN KEY ([RecipientUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceRefunds_ApplicationUsers_ReturnedByUserId] FOREIGN KEY ([ReturnedByUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceRefunds_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceRefunds_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceRefunds_MarketplaceReservations_ReservationId] FOREIGN KEY ([ReservationId]) REFERENCES [MarketplaceReservations] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE INDEX [IX_MarketplaceReservations_Status_StartNoticeSentAtUtc_StartsAtUtc] ON [MarketplaceReservations] ([Status], [StartNoticeSentAtUtc], [StartsAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE INDEX [IX_MarketplaceClaims_BuildingId_Status_CreatedAtUtc] ON [MarketplaceClaims] ([BuildingId], [Status], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE INDEX [IX_MarketplaceClaims_CompanyId] ON [MarketplaceClaims] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_MarketplaceClaims_OneOpenPerReservation] ON [MarketplaceClaims] ([ReservationId]) WHERE [Status] = ''Open'' AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE INDEX [IX_MarketplaceClaims_OpenedByUserId] ON [MarketplaceClaims] ([OpenedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE INDEX [IX_MarketplaceClaims_ResolvedByUserId] ON [MarketplaceClaims] ([ResolvedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE INDEX [IX_MarketplaceOwnerDebts_BuildingId] ON [MarketplaceOwnerDebts] ([BuildingId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE INDEX [IX_MarketplaceOwnerDebts_CompanyId] ON [MarketplaceOwnerDebts] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_MarketplaceOwnerDebts_OnePerReservation] ON [MarketplaceOwnerDebts] ([ReservationId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE INDEX [IX_MarketplaceOwnerDebts_OwnerId_BuildingId_SettledAtUtc] ON [MarketplaceOwnerDebts] ([OwnerId], [BuildingId], [SettledAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE INDEX [IX_MarketplaceRefunds_BuildingId_Status_CreatedAtUtc] ON [MarketplaceRefunds] ([BuildingId], [Status], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE INDEX [IX_MarketplaceRefunds_CompanyId] ON [MarketplaceRefunds] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_MarketplaceRefunds_OnePerReservation] ON [MarketplaceRefunds] ([ReservationId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE INDEX [IX_MarketplaceRefunds_RecipientUserId] ON [MarketplaceRefunds] ([RecipientUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    CREATE INDEX [IX_MarketplaceRefunds_ReturnedByUserId] ON [MarketplaceRefunds] ([ReturnedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002201507_MarketplaceCancellationsAndClaims', N'8.0.8');
END;
GO

IF OBJECT_ID(N'MarketplaceRefunds') IS NULL OR OBJECT_ID(N'MarketplaceClaims') IS NULL OR OBJECT_ID(N'MarketplaceOwnerDebts') IS NULL
   OR COL_LENGTH(N'MarketplaceReservations', N'StartNoticeSentAtUtc') IS NULL
   OR COL_LENGTH(N'MarketplaceReservations', N'StartResponse') IS NULL
   OR COL_LENGTH(N'MarketplaceReservations', N'StartResponseReason') IS NULL
   OR COL_LENGTH(N'MarketplaceReservations', N'StartResponseAtUtc') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MarketplaceRefunds_OnePerReservation' AND is_unique = 1)
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MarketplaceClaims_OneOpenPerReservation' AND is_unique = 1)
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MarketplaceOwnerDebts_OnePerReservation' AND is_unique = 1)
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261002201507_MarketplaceCancellationsAndClaims')
    RAISERROR(N'MarketplaceCancellationsAndClaims: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'MarketplaceCancellationsAndClaims OK' AS Resultado;
GO
