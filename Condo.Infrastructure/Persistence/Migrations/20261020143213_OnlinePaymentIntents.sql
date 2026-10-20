-- ============================================================
-- Migracion EF: 20261020143213_OnlinePaymentIntents
-- Pagos online, base del cobro: tablas PaymentIntents (cobro: que se quiere cobrar a un propietario), PaymentIntentItems (comprobantes
-- del cobro; un indice unico impide el mismo comprobante en dos cobros abiertos), PaymentAttempts (cada sesion con el proveedor de pagos)
-- y PaymentEvents (registro de auditoria de solo insercion; el indice unico de DedupeKey impide procesar dos veces el mismo aviso).
-- Columnas nuevas: PaymentConnections.HolderName / HolderTaxId / HolderBankNote (titular del comercio), BuildingPaymentConfigs.IsTest
-- (edificio de prueba, por defecto 0) y OwnerPayments.PaymentIntentId (nulo; unico: un cobro produce como maximo un pago).
-- Solo agrega tablas, columnas nulas (o con valor por defecto) e indices; no toca datos existentes ni cobra nada. El cobro sigue apagado
-- hasta que se programe el recorrido completo.
-- La cuenta de "cobros online por liquidar" no necesita indice nuevo: el indice de FinancialAccounts (BuildingId, Type) para los tipos que
-- no son banco ya impide una segunda.
-- Va DESPUES de 20261020111217_AccountReconcileOverride.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/OnlinePaymentIntents.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que tablas, columnas, indices y la clave foranea existan de
-- verdad; si algo falta, aborta antes del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020143213_OnlinePaymentIntents'
)
BEGIN
    ALTER TABLE [PaymentConnections] ADD [HolderBankNote] nvarchar(500) NULL;
    ALTER TABLE [PaymentConnections] ADD [HolderName] nvarchar(200) NULL;
    ALTER TABLE [PaymentConnections] ADD [HolderTaxId] nvarchar(30) NULL;
    ALTER TABLE [OwnerPayments] ADD [PaymentIntentId] uniqueidentifier NULL;
    ALTER TABLE [BuildingPaymentConfigs] ADD [IsTest] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020143213_OnlinePaymentIntents'
)
BEGIN
    CREATE TABLE [PaymentIntents] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [OwnerId] uniqueidentifier NOT NULL,
        [ConnectionId] uniqueidentifier NOT NULL,
        [Number] bigint NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [CloseReason] nvarchar(40) NULL,
        [ReviewReason] nvarchar(500) NULL,
        [ExpiresAtUtc] datetime2 NULL,
        [OwnerPaymentId] uniqueidentifier NULL,
        [HolderName] nvarchar(200) NOT NULL,
        [HolderTaxId] nvarchar(30) NOT NULL,
        [IsSimulated] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_PaymentIntents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentIntents_ApplicationUsers_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentIntents_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentIntents_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentIntents_PaymentConnections_ConnectionId] FOREIGN KEY ([ConnectionId]) REFERENCES [PaymentConnections] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020143213_OnlinePaymentIntents'
)
BEGIN
    CREATE TABLE [PaymentAttempts] (
        [Id] uniqueidentifier NOT NULL,
        [IntentId] uniqueidentifier NOT NULL,
        [ConnectionId] uniqueidentifier NOT NULL,
        [AttemptNumber] bigint NOT NULL,
        [ProviderReference] nvarchar(100) NOT NULL,
        [ProviderSessionId] nvarchar(100) NULL,
        [CheckoutUrl] nvarchar(1000) NULL,
        [ProviderTransactionId] nvarchar(100) NULL,
        [AuthorizationCode] nvarchar(50) NULL,
        [Status] nvarchar(20) NOT NULL,
        [ProviderStatusRaw] nvarchar(50) NULL,
        [Amount] decimal(18,2) NOT NULL,
        [ExpiresAtUtc] datetime2 NULL,
        [ConfirmedAtUtc] datetime2 NULL,
        [CardBrand] nvarchar(30) NULL,
        [CardLast4] nvarchar(4) NULL,
        [CommissionAmount] decimal(18,2) NULL,
        [NetAmount] decimal(18,2) NULL,
        [ReconciledAtUtc] datetime2 NULL,
        [ReconciledByUserId] uniqueidentifier NULL,
        [SettlementReference] nvarchar(100) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_PaymentAttempts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentAttempts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentAttempts_PaymentConnections_ConnectionId] FOREIGN KEY ([ConnectionId]) REFERENCES [PaymentConnections] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentAttempts_PaymentIntents_IntentId] FOREIGN KEY ([IntentId]) REFERENCES [PaymentIntents] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020143213_OnlinePaymentIntents'
)
BEGIN
    CREATE TABLE [PaymentIntentItems] (
        [Id] uniqueidentifier NOT NULL,
        [IntentId] uniqueidentifier NOT NULL,
        [UnitId] uniqueidentifier NOT NULL,
        [ExpensePeriodId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [SnapshotJson] nvarchar(max) NULL,
        [IsOpen] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_PaymentIntentItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentIntentItems_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentIntentItems_ExpensePeriods_ExpensePeriodId] FOREIGN KEY ([ExpensePeriodId]) REFERENCES [ExpensePeriods] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentIntentItems_PaymentIntents_IntentId] FOREIGN KEY ([IntentId]) REFERENCES [PaymentIntents] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentIntentItems_Units_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Units] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020143213_OnlinePaymentIntents'
)
BEGIN
    CREATE TABLE [PaymentEvents] (
        [Id] uniqueidentifier NOT NULL,
        [ConnectionId] uniqueidentifier NOT NULL,
        [AttemptId] uniqueidentifier NULL,
        [Direction] nvarchar(10) NOT NULL,
        [Kind] nvarchar(40) NOT NULL,
        [DedupeKey] nvarchar(200) NULL,
        [SignatureOk] bit NULL,
        [Ip] nvarchar(64) NULL,
        [PayloadJson] nvarchar(max) NULL,
        [Outcome] nvarchar(40) NOT NULL,
        [Error] nvarchar(1000) NULL,
        [ReceivedAtUtc] datetime2 NOT NULL,
        [ProcessedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_PaymentEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentEvents_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentEvents_PaymentAttempts_AttemptId] FOREIGN KEY ([AttemptId]) REFERENCES [PaymentAttempts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentEvents_PaymentConnections_ConnectionId] FOREIGN KEY ([ConnectionId]) REFERENCES [PaymentConnections] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020143213_OnlinePaymentIntents'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PaymentAttempts_AttemptNumber] ON [PaymentAttempts] ([AttemptNumber]);
    CREATE INDEX [IX_PaymentAttempts_CompanyId] ON [PaymentAttempts] ([CompanyId]);
    CREATE UNIQUE INDEX [IX_PaymentAttempts_ConnectionId_ProviderReference] ON [PaymentAttempts] ([ConnectionId], [ProviderReference]);
    CREATE INDEX [IX_PaymentAttempts_IntentId] ON [PaymentAttempts] ([IntentId]);
    CREATE INDEX [IX_PaymentAttempts_Status_ExpiresAtUtc] ON [PaymentAttempts] ([Status], [ExpiresAtUtc]);
    CREATE INDEX [IX_PaymentEvents_AttemptId] ON [PaymentEvents] ([AttemptId]);
    CREATE INDEX [IX_PaymentEvents_CompanyId] ON [PaymentEvents] ([CompanyId]);
    CREATE INDEX [IX_PaymentEvents_ConnectionId_ReceivedAtUtc] ON [PaymentEvents] ([ConnectionId], [ReceivedAtUtc]);
    CREATE INDEX [IX_PaymentIntentItems_CompanyId] ON [PaymentIntentItems] ([CompanyId]);
    CREATE INDEX [IX_PaymentIntentItems_ExpensePeriodId] ON [PaymentIntentItems] ([ExpensePeriodId]);
    CREATE INDEX [IX_PaymentIntentItems_IntentId] ON [PaymentIntentItems] ([IntentId]);
    CREATE INDEX [IX_PaymentIntents_BuildingId_CreatedAtUtc] ON [PaymentIntents] ([BuildingId], [CreatedAtUtc]);
    CREATE INDEX [IX_PaymentIntents_CompanyId] ON [PaymentIntents] ([CompanyId]);
    CREATE INDEX [IX_PaymentIntents_ConnectionId] ON [PaymentIntents] ([ConnectionId]);
    CREATE UNIQUE INDEX [IX_PaymentIntents_Number] ON [PaymentIntents] ([Number]);
    CREATE INDEX [IX_PaymentIntents_OwnerId_Status] ON [PaymentIntents] ([OwnerId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020143213_OnlinePaymentIntents'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OwnerPayments_PaymentIntentId] ON [OwnerPayments] ([PaymentIntentId]) WHERE [PaymentIntentId] IS NOT NULL');
    EXEC(N'CREATE UNIQUE INDEX [IX_PaymentAttempts_ConnectionId_ProviderSessionId] ON [PaymentAttempts] ([ConnectionId], [ProviderSessionId]) WHERE [ProviderSessionId] IS NOT NULL');
    EXEC(N'CREATE UNIQUE INDEX [IX_PaymentEvents_DedupeKey] ON [PaymentEvents] ([DedupeKey]) WHERE [DedupeKey] IS NOT NULL');
    EXEC(N'CREATE UNIQUE INDEX [IX_PaymentIntentItems_UnitId_ExpensePeriodId] ON [PaymentIntentItems] ([UnitId], [ExpensePeriodId]) WHERE [IsOpen] = 1 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020143213_OnlinePaymentIntents'
)
BEGIN
    ALTER TABLE [OwnerPayments] ADD CONSTRAINT [FK_OwnerPayments_PaymentIntents_PaymentIntentId] FOREIGN KEY ([PaymentIntentId]) REFERENCES [PaymentIntents] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020143213_OnlinePaymentIntents'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020143213_OnlinePaymentIntents', N'8.0.8');
END;
GO

IF OBJECT_ID(N'PaymentIntents', N'U') IS NULL
   OR OBJECT_ID(N'PaymentIntentItems', N'U') IS NULL
   OR OBJECT_ID(N'PaymentAttempts', N'U') IS NULL
   OR OBJECT_ID(N'PaymentEvents', N'U') IS NULL
   OR COL_LENGTH(N'PaymentConnections', N'HolderName') IS NULL
   OR COL_LENGTH(N'PaymentConnections', N'HolderTaxId') IS NULL
   OR COL_LENGTH(N'PaymentConnections', N'HolderBankNote') IS NULL
   OR COL_LENGTH(N'BuildingPaymentConfigs', N'IsTest') IS NULL
   OR COL_LENGTH(N'OwnerPayments', N'PaymentIntentId') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OwnerPayments_PaymentIntentId' AND object_id = OBJECT_ID(N'OwnerPayments'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentIntents_Number' AND object_id = OBJECT_ID(N'PaymentIntents'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentIntents_OwnerId_Status' AND object_id = OBJECT_ID(N'PaymentIntents'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentIntentItems_UnitId_ExpensePeriodId' AND object_id = OBJECT_ID(N'PaymentIntentItems'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentAttempts_AttemptNumber' AND object_id = OBJECT_ID(N'PaymentAttempts'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentAttempts_ConnectionId_ProviderReference' AND object_id = OBJECT_ID(N'PaymentAttempts'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentAttempts_ConnectionId_ProviderSessionId' AND object_id = OBJECT_ID(N'PaymentAttempts'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentAttempts_Status_ExpiresAtUtc' AND object_id = OBJECT_ID(N'PaymentAttempts'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentEvents_DedupeKey' AND object_id = OBJECT_ID(N'PaymentEvents'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentEvents_ConnectionId_ReceivedAtUtc' AND object_id = OBJECT_ID(N'PaymentEvents'))
   OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_OwnerPayments_PaymentIntents_PaymentIntentId')
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261020143213_OnlinePaymentIntents')
    RAISERROR(N'OnlinePaymentIntents: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'OnlinePaymentIntents OK' AS Resultado,
       OBJECT_ID(N'PaymentIntents', N'U') AS Cobros,
       OBJECT_ID(N'PaymentIntentItems', N'U') AS Comprobantes,
       OBJECT_ID(N'PaymentAttempts', N'U') AS Intentos,
       OBJECT_ID(N'PaymentEvents', N'U') AS Eventos;
GO
