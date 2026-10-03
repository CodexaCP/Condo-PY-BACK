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
