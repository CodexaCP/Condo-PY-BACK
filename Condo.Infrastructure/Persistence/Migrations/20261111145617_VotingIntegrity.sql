-- ============================================================
-- Migracion EF: 20261111145617_VotingIntegrity
-- Votaciones, Fase 1 (integridad):
--   * Votes: quien abrio/cerro/anulo, anulacion con motivo, resultado congelado al cerrar (ResultJson + ResultHash SHA-256 y totales)
--     y RowVersion para que un voto y el cierre no se pisen.
--   * VoteCasts: canal del voto vigente (Source); los votos existentes quedan como 'Staff' (los cargo el personal).
--   * VoteCastEvents (tabla nueva): historial de solo insercion de cada voto, cambio y quita.
-- Solo agrega columnas nullable (o con valor por defecto), una tabla y sus indices; no modifica datos existentes. Las votaciones ya
-- cerradas quedan sin resultado congelado y se siguen mostrando calculadas, marcadas como tales.
-- Va DESPUES de 20261109150521_BuildingQrShowBankAccounts.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/VotingIntegrity.sql
--
-- Es idempotente (revisa __EFMigrationsHistory) y verifica al final que las columnas, la tabla y el historial existan de verdad; si
-- algo falta, aborta antes del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261111145617_VotingIntegrity')
BEGIN
    ALTER TABLE [Votes] ADD [CancelReason] nvarchar(500) NULL;
    ALTER TABLE [Votes] ADD [CancelledAtUtc] datetime2 NULL;
    ALTER TABLE [Votes] ADD [CancelledByUserId] uniqueidentifier NULL;
    ALTER TABLE [Votes] ADD [ClosedByUserId] uniqueidentifier NULL;
    ALTER TABLE [Votes] ADD [OpenedByUserId] uniqueidentifier NULL;
    ALTER TABLE [Votes] ADD [ResultHash] nvarchar(64) NULL;
    ALTER TABLE [Votes] ADD [ResultJson] nvarchar(max) NULL;
    ALTER TABLE [Votes] ADD [ResultParticipatingCoefficient] decimal(18,6) NULL;
    ALTER TABLE [Votes] ADD [ResultParticipatingUnits] int NULL;
    ALTER TABLE [Votes] ADD [ResultTotalCoefficient] decimal(18,6) NULL;
    ALTER TABLE [Votes] ADD [ResultTotalUnits] int NULL;
    ALTER TABLE [Votes] ADD [RowVersion] rowversion NOT NULL;
    ALTER TABLE [VoteCasts] ADD [Source] nvarchar(20) NOT NULL DEFAULT N'Staff';
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261111145617_VotingIntegrity')
BEGIN
    CREATE TABLE [VoteCastEvents] (
        [Id] uniqueidentifier NOT NULL,
        [VoteId] uniqueidentifier NOT NULL,
        [UnitId] uniqueidentifier NOT NULL,
        [Action] nvarchar(20) NOT NULL,
        [PreviousOptionId] uniqueidentifier NULL,
        [NewOptionId] uniqueidentifier NULL,
        [CoefficientWeight] decimal(18,6) NOT NULL,
        [Source] nvarchar(20) NOT NULL,
        [ActingUserId] uniqueidentifier NULL,
        [OwnerUserId] uniqueidentifier NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_VoteCastEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VoteCastEvents_ApplicationUsers_ActingUserId] FOREIGN KEY ([ActingUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_VoteCastEvents_ApplicationUsers_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_VoteCastEvents_Units_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Units] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_VoteCastEvents_VoteOptions_NewOptionId] FOREIGN KEY ([NewOptionId]) REFERENCES [VoteOptions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_VoteCastEvents_VoteOptions_PreviousOptionId] FOREIGN KEY ([PreviousOptionId]) REFERENCES [VoteOptions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_VoteCastEvents_Votes_VoteId] FOREIGN KEY ([VoteId]) REFERENCES [Votes] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261111145617_VotingIntegrity')
BEGIN
    CREATE INDEX [IX_Votes_CancelledByUserId] ON [Votes] ([CancelledByUserId]);
    CREATE INDEX [IX_Votes_ClosedByUserId] ON [Votes] ([ClosedByUserId]);
    CREATE INDEX [IX_Votes_OpenedByUserId] ON [Votes] ([OpenedByUserId]);
    CREATE INDEX [IX_VoteCastEvents_ActingUserId] ON [VoteCastEvents] ([ActingUserId]);
    CREATE INDEX [IX_VoteCastEvents_NewOptionId] ON [VoteCastEvents] ([NewOptionId]);
    CREATE INDEX [IX_VoteCastEvents_OwnerUserId] ON [VoteCastEvents] ([OwnerUserId]);
    CREATE INDEX [IX_VoteCastEvents_PreviousOptionId] ON [VoteCastEvents] ([PreviousOptionId]);
    CREATE INDEX [IX_VoteCastEvents_UnitId] ON [VoteCastEvents] ([UnitId]);
    CREATE INDEX [IX_VoteCastEvents_VoteId_UnitId_OccurredAtUtc] ON [VoteCastEvents] ([VoteId], [UnitId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261111145617_VotingIntegrity')
BEGIN
    ALTER TABLE [Votes] ADD CONSTRAINT [FK_Votes_ApplicationUsers_CancelledByUserId] FOREIGN KEY ([CancelledByUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION;
    ALTER TABLE [Votes] ADD CONSTRAINT [FK_Votes_ApplicationUsers_ClosedByUserId] FOREIGN KEY ([ClosedByUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION;
    ALTER TABLE [Votes] ADD CONSTRAINT [FK_Votes_ApplicationUsers_OpenedByUserId] FOREIGN KEY ([OpenedByUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261111145617_VotingIntegrity')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111145617_VotingIntegrity', N'8.0.8');
END;
GO

IF COL_LENGTH(N'Votes', N'ResultJson') IS NULL
   OR COL_LENGTH(N'Votes', N'RowVersion') IS NULL
   OR COL_LENGTH(N'Votes', N'CancelReason') IS NULL
   OR COL_LENGTH(N'VoteCasts', N'Source') IS NULL
   OR OBJECT_ID(N'VoteCastEvents', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_VoteCastEvents_VoteId_UnitId_OccurredAtUtc' AND [object_id] = OBJECT_ID(N'VoteCastEvents'))
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261111145617_VotingIntegrity')
    RAISERROR(N'VotingIntegrity: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'VotingIntegrity OK' AS Resultado,
       (SELECT COUNT(*) FROM [Votes]) AS Votaciones,
       (SELECT COUNT(*) FROM [VoteCasts] WHERE [Source] = N'Staff') AS VotosStaff,
       (SELECT COUNT(*) FROM [VoteCastEvents]) AS Eventos;
GO
