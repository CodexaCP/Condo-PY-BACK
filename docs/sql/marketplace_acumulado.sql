-- ============================================================
-- Marketplace de espacios temporales — script ACUMULADO de base de datos (fases 1 a 8)
--
-- Contiene, en orden, las 5 migraciones del Marketplace. Cada una es idempotente (revisa __EFMigrationsHistory antes de actuar),
-- corre dentro de su propia transaccion y verifica al final que lo creado exista: si ya estaba aplicada no hace nada, y si algo falla
-- aborta y no queda nada a medias. Se puede correr completo aunque algunas ya esten aplicadas.
--
--   1. 20261002172053_MarketplaceModuleBase            (fase 1: habilitacion por edificio, plan, comision, datos para transferir)
--   2. 20261002174215_MarketplaceCore                  (fase 2: publicaciones, reservas, bloques, pagos, cuenta aparte, auditoria)
--   3. 20261002185716_MarketplacePaymentAlerts         (fase 5: alertas al revisor de pagos)
--   4. 20261002201507_MarketplaceCancellationsAndClaims(fase 7: reembolsos, reclamos, deudas por gestion, aviso de inicio)
--   5. 20261003105745_MarketplaceHandoverNotes         (fase 8: notas de cambio de propietario principal)
--
-- Prerrequisito: la migracion 20261002125524_OwnerPaymentWebChannel (pago por la web) ya aplicada.
--
-- CORRER EN EL VPS ANTES DE DESPLEGAR EL CODIGO, con -I (indices filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/marketplace_acumulado.sql
--
-- Despues, correr marketplace_verificacion.sql: debe mostrar "OK" en todas las filas.
-- ============================================================
GO

-- >>>>>>>>>> 20261002172053_MarketplaceModuleBase
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

-- >>>>>>>>>> 20261002174215_MarketplaceCore
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

-- >>>>>>>>>> 20261002185716_MarketplacePaymentAlerts
-- ============================================================
-- Migracion EF: 20261002185716_MarketplacePaymentAlerts
-- Marketplace de espacios temporales, fase 5: cuenta de las alertas al revisor de pagos (cuantas se mandaron y cuando fue la
-- ultima) en MarketplacePayments. Las alertas salen al subir el comprobante, a los 15 minutos y luego cada hora.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/MarketplacePaymentAlerts.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002185716_MarketplacePaymentAlerts'
)
BEGIN
    ALTER TABLE [MarketplacePayments] ADD [AlertCount] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002185716_MarketplacePaymentAlerts'
)
BEGIN
    ALTER TABLE [MarketplacePayments] ADD [LastAlertAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002185716_MarketplacePaymentAlerts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002185716_MarketplacePaymentAlerts', N'8.0.8');
END;
GO

IF COL_LENGTH(N'MarketplacePayments', N'AlertCount') IS NULL OR COL_LENGTH(N'MarketplacePayments', N'LastAlertAtUtc') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261002185716_MarketplacePaymentAlerts')
    RAISERROR(N'MarketplacePaymentAlerts: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'MarketplacePaymentAlerts OK' AS Resultado;
GO

-- >>>>>>>>>> 20261002201507_MarketplaceCancellationsAndClaims
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

-- >>>>>>>>>> 20261003105745_MarketplaceHandoverNotes
-- ============================================================
-- Migracion EF: 20261003105745_MarketplaceHandoverNotes
-- Marketplace de espacios temporales, fase 8: crea MarketplaceHandoverNotes, la nota interna que se genera cuando cambia el
-- propietario principal de una unidad con operaciones del marketplace abiertas (que paso, situacion al momento del cambio y quien
-- la leyo). Solo agrega una tabla nueva; no toca datos existentes.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/MarketplaceHandoverNotes.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003105745_MarketplaceHandoverNotes'
)
BEGIN
    CREATE TABLE [MarketplaceHandoverNotes] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [UnitId] uniqueidentifier NOT NULL,
        [PreviousOwnerId] uniqueidentifier NOT NULL,
        [NewOwnerId] uniqueidentifier NULL,
        [Trigger] nvarchar(20) NOT NULL,
        [Content] nvarchar(4000) NOT NULL,
        [ReservationIds] nvarchar(4000) NOT NULL,
        [ReservationCount] int NOT NULL,
        [ReadAtUtc] datetime2 NULL,
        [ReadByUserId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MarketplaceHandoverNotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MarketplaceHandoverNotes_ApplicationUsers_NewOwnerId] FOREIGN KEY ([NewOwnerId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceHandoverNotes_ApplicationUsers_PreviousOwnerId] FOREIGN KEY ([PreviousOwnerId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceHandoverNotes_ApplicationUsers_ReadByUserId] FOREIGN KEY ([ReadByUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceHandoverNotes_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceHandoverNotes_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MarketplaceHandoverNotes_Units_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Units] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003105745_MarketplaceHandoverNotes'
)
BEGIN
    CREATE INDEX [IX_MarketplaceHandoverNotes_BuildingId_ReadAtUtc_CreatedAtUtc] ON [MarketplaceHandoverNotes] ([BuildingId], [ReadAtUtc], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003105745_MarketplaceHandoverNotes'
)
BEGIN
    CREATE INDEX [IX_MarketplaceHandoverNotes_CompanyId] ON [MarketplaceHandoverNotes] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003105745_MarketplaceHandoverNotes'
)
BEGIN
    CREATE INDEX [IX_MarketplaceHandoverNotes_NewOwnerId] ON [MarketplaceHandoverNotes] ([NewOwnerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003105745_MarketplaceHandoverNotes'
)
BEGIN
    CREATE INDEX [IX_MarketplaceHandoverNotes_PreviousOwnerId] ON [MarketplaceHandoverNotes] ([PreviousOwnerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003105745_MarketplaceHandoverNotes'
)
BEGIN
    CREATE INDEX [IX_MarketplaceHandoverNotes_ReadByUserId] ON [MarketplaceHandoverNotes] ([ReadByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003105745_MarketplaceHandoverNotes'
)
BEGIN
    CREATE INDEX [IX_MarketplaceHandoverNotes_UnitId_CreatedAtUtc] ON [MarketplaceHandoverNotes] ([UnitId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003105745_MarketplaceHandoverNotes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261003105745_MarketplaceHandoverNotes', N'8.0.8');
END;
GO

IF OBJECT_ID(N'MarketplaceHandoverNotes') IS NULL
   OR COL_LENGTH(N'MarketplaceHandoverNotes', N'Content') IS NULL
   OR COL_LENGTH(N'MarketplaceHandoverNotes', N'ReservationIds') IS NULL
   OR COL_LENGTH(N'MarketplaceHandoverNotes', N'ReadAtUtc') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MarketplaceHandoverNotes_BuildingId_ReadAtUtc_CreatedAtUtc')
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261003105745_MarketplaceHandoverNotes')
    RAISERROR(N'MarketplaceHandoverNotes: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'MarketplaceHandoverNotes OK' AS Resultado;
GO

