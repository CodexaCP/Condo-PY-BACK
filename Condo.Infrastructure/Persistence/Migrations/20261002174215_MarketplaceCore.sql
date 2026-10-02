-- ============================================================
-- Migracion EF: 20261002174215_MarketplaceCore
-- Marketplace de espacios temporales, nucleo: publicaciones, reservas, bloques de 30 min ocupados, pagos, extracto de la
-- cuenta aparte y auditoria (6 tablas nuevas) y el campo MarketplaceReservationId en OwnerCreditMovements. Los indices unicos
-- filtrados son la garantia en la base de datos contra la doble reserva, mas de una reserva esperando pago por comprador,
-- mas de un pago aprobado por reserva y asientos o lotes de saldo duplicados.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/MarketplaceCore.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    ALTER TABLE [OwnerCreditMovements] ADD [MarketplaceReservationId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE TABLE [MarketplaceEvents] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NULL,
        [TimestampUtc] datetime2 NOT NULL,
        [Action] nvarchar(60) NOT NULL,
        [EntityType] nvarchar(40) NOT NULL,
        [EntityId] uniqueidentifier NOT NULL,
        [FromStatus] nvarchar(40) NULL,
        [ToStatus] nvarchar(40) NULL,
        [IpAddress] nvarchar(64) NULL,
        [UserAgent] nvarchar(300) NULL,
        [DataJson] nvarchar(max) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MarketplaceEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MarketplaceEvents_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceEvents_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE TABLE [MarketplaceListings] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [UnitId] uniqueidentifier NOT NULL,
        [OwnerId] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [WindowStartUtc] datetime2 NOT NULL,
        [WindowEndUtc] datetime2 NOT NULL,
        [HourlyPrice] decimal(18,2) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [StatusReason] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MarketplaceListings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MarketplaceListings_ApplicationUsers_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceListings_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceListings_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceListings_Units_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Units] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE TABLE [MarketplaceReservations] (
        [Id] uniqueidentifier NOT NULL,
        [ListingId] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [UnitId] uniqueidentifier NOT NULL,
        [BuyerUserId] uniqueidentifier NOT NULL,
        [OwnerId] uniqueidentifier NOT NULL,
        [Reference] nvarchar(30) NOT NULL,
        [StartsAtUtc] datetime2 NOT NULL,
        [EndsAtUtc] datetime2 NOT NULL,
        [Hours] int NOT NULL,
        [HourlyPrice] decimal(18,2) NOT NULL,
        [BaseAmount] decimal(18,2) NOT NULL,
        [CommissionPercent] decimal(5,2) NOT NULL,
        [CommissionAmount] decimal(18,2) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [OwnerNetAmount] decimal(18,2) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [ExpiresAtUtc] datetime2 NULL,
        [CreditStatus] nvarchar(20) NOT NULL,
        [CreditedAtUtc] datetime2 NULL,
        [CancelledAtUtc] datetime2 NULL,
        [CancelledByUserId] uniqueidentifier NULL,
        [CancelledBy] nvarchar(20) NULL,
        [CancelReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MarketplaceReservations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MarketplaceReservations_ApplicationUsers_BuyerUserId] FOREIGN KEY ([BuyerUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceReservations_ApplicationUsers_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceReservations_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceReservations_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceReservations_MarketplaceListings_ListingId] FOREIGN KEY ([ListingId]) REFERENCES [MarketplaceListings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceReservations_Units_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Units] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE TABLE [MarketplaceAccountMovements] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [ReservationId] uniqueidentifier NULL,
        [Concept] nvarchar(300) NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MarketplaceAccountMovements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MarketplaceAccountMovements_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceAccountMovements_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceAccountMovements_MarketplaceReservations_ReservationId] FOREIGN KEY ([ReservationId]) REFERENCES [MarketplaceReservations] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE TABLE [MarketplacePayments] (
        [Id] uniqueidentifier NOT NULL,
        [ReservationId] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [BuyerUserId] uniqueidentifier NOT NULL,
        [ComprobanteUrl] nvarchar(500) NOT NULL,
        [ExpectedAmount] decimal(18,2) NOT NULL,
        [ReviewedAmount] decimal(18,2) NULL,
        [Status] nvarchar(20) NOT NULL,
        [SubmittedAtUtc] datetime2 NOT NULL,
        [ReviewedByUserId] uniqueidentifier NULL,
        [ReviewedAtUtc] datetime2 NULL,
        [RejectionReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MarketplacePayments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MarketplacePayments_ApplicationUsers_BuyerUserId] FOREIGN KEY ([BuyerUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplacePayments_ApplicationUsers_ReviewedByUserId] FOREIGN KEY ([ReviewedByUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplacePayments_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplacePayments_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplacePayments_MarketplaceReservations_ReservationId] FOREIGN KEY ([ReservationId]) REFERENCES [MarketplaceReservations] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE TABLE [MarketplaceReservationSlots] (
        [Id] uniqueidentifier NOT NULL,
        [ReservationId] uniqueidentifier NOT NULL,
        [ListingId] uniqueidentifier NOT NULL,
        [SlotStartUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MarketplaceReservationSlots] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MarketplaceReservationSlots_MarketplaceListings_ListingId] FOREIGN KEY ([ListingId]) REFERENCES [MarketplaceListings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceReservationSlots_MarketplaceReservations_ReservationId] FOREIGN KEY ([ReservationId]) REFERENCES [MarketplaceReservations] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OwnerCreditMovements_OneLotPerMarketplaceReservation] ON [OwnerCreditMovements] ([MarketplaceReservationId]) WHERE [MarketplaceReservationId] IS NOT NULL AND [Kind] = 'Generated' AND [IsDeleted] = 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceAccountMovements_BuildingId_OccurredAtUtc] ON [MarketplaceAccountMovements] ([BuildingId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceAccountMovements_CompanyId] ON [MarketplaceAccountMovements] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MarketplaceAccountMovements_OnePerReservationKind] ON [MarketplaceAccountMovements] ([ReservationId], [Kind]) WHERE [ReservationId] IS NOT NULL AND [Kind] <> 'Adjustment' AND [IsDeleted] = 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceEvents_BuildingId_TimestampUtc] ON [MarketplaceEvents] ([BuildingId], [TimestampUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceEvents_CompanyId] ON [MarketplaceEvents] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceEvents_EntityType_EntityId_TimestampUtc] ON [MarketplaceEvents] ([EntityType], [EntityId], [TimestampUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceListings_BuildingId_Status_WindowStartUtc] ON [MarketplaceListings] ([BuildingId], [Status], [WindowStartUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceListings_CompanyId] ON [MarketplaceListings] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceListings_OwnerId] ON [MarketplaceListings] ([OwnerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceListings_UnitId_WindowStartUtc] ON [MarketplaceListings] ([UnitId], [WindowStartUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplacePayments_BuildingId_Status_SubmittedAtUtc] ON [MarketplacePayments] ([BuildingId], [Status], [SubmittedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplacePayments_BuyerUserId] ON [MarketplacePayments] ([BuyerUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplacePayments_CompanyId] ON [MarketplacePayments] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MarketplacePayments_OneApprovedPerReservation] ON [MarketplacePayments] ([ReservationId]) WHERE [Status] = 'Approved' AND [IsDeleted] = 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplacePayments_ReservationId] ON [MarketplacePayments] ([ReservationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplacePayments_ReviewedByUserId] ON [MarketplacePayments] ([ReviewedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceReservations_BuildingId_Status_StartsAtUtc] ON [MarketplaceReservations] ([BuildingId], [Status], [StartsAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceReservations_BuyerUserId] ON [MarketplaceReservations] ([BuyerUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MarketplaceReservations_CompanyId_Reference] ON [MarketplaceReservations] ([CompanyId], [Reference]) WHERE [IsDeleted] = 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceReservations_ListingId_Status] ON [MarketplaceReservations] ([ListingId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MarketplaceReservations_OnePendingPerBuyer] ON [MarketplaceReservations] ([BuyerUserId]) WHERE [Status] = 'PendingPayment' AND [IsDeleted] = 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceReservations_OwnerId_CreditStatus] ON [MarketplaceReservations] ([OwnerId], [CreditStatus]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceReservations_Status_ExpiresAtUtc] ON [MarketplaceReservations] ([Status], [ExpiresAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceReservations_UnitId] ON [MarketplaceReservations] ([UnitId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MarketplaceReservationSlots_NoDoubleBooking] ON [MarketplaceReservationSlots] ([ListingId], [SlotStartUtc]) WHERE [IsDeleted] = 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    CREATE INDEX [IX_MarketplaceReservationSlots_ReservationId] ON [MarketplaceReservationSlots] ([ReservationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002174215_MarketplaceCore'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002174215_MarketplaceCore', N'8.0.8');
END;
GO

IF OBJECT_ID(N'MarketplaceListings') IS NULL OR OBJECT_ID(N'MarketplaceReservations') IS NULL OR OBJECT_ID(N'MarketplaceReservationSlots') IS NULL
   OR OBJECT_ID(N'MarketplacePayments') IS NULL OR OBJECT_ID(N'MarketplaceAccountMovements') IS NULL OR OBJECT_ID(N'MarketplaceEvents') IS NULL
   OR COL_LENGTH(N'OwnerCreditMovements', N'MarketplaceReservationId') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MarketplaceReservationSlots_NoDoubleBooking' AND is_unique = 1)
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MarketplaceReservations_OnePendingPerBuyer' AND is_unique = 1)
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MarketplacePayments_OneApprovedPerReservation' AND is_unique = 1)
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MarketplaceAccountMovements_OnePerReservationKind' AND is_unique = 1)
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OwnerCreditMovements_OneLotPerMarketplaceReservation' AND is_unique = 1)
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261002174215_MarketplaceCore')
    RAISERROR(N'MarketplaceCore: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'MarketplaceCore OK' AS Resultado;
GO
