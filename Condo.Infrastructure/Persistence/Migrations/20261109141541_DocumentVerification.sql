-- ============================================================
-- Migracion EF: 20261109141541_DocumentVerification
-- QR de verificacion de la liquidacion de expensas: tabla DocumentVerifications (una fila por documento impreso con QR: token aleatorio
-- unico, tipo y id del documento, hash del contenido, copia de lo que muestra la pagina publica, y fechas de reemplazo o anulacion).
-- Solo agrega una tabla nueva con sus indices; no toca datos existentes. Las filas se crean solas al descargar el PDF de una liquidacion
-- (tambien las ya existentes), asi que no hace falta cargar datos.
-- Va DESPUES de 20261102114757_MultiCountryCore.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/DocumentVerification.sql
--
-- Es idempotente (revisa __EFMigrationsHistory) y verifica al final que la tabla y el indice unico del token existan de verdad; si algo
-- falta, aborta antes del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261109141541_DocumentVerification')
BEGIN
    CREATE TABLE [DocumentVerifications] (
        [Id] uniqueidentifier NOT NULL,
        [Token] nvarchar(32) NOT NULL,
        [DocumentType] nvarchar(30) NOT NULL,
        [DocumentId] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [ContentHash] nvarchar(64) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [BuildingName] nvarchar(200) NOT NULL,
        [AdministratorName] nvarchar(200) NULL,
        [PeriodName] nvarchar(100) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [IsPublished] bit NOT NULL,
        [IssuedAtUtc] datetime2 NOT NULL,
        [SupersededAtUtc] datetime2 NULL,
        [RevokedAtUtc] datetime2 NULL,
        [RevokedReason] nvarchar(300) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_DocumentVerifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DocumentVerifications_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261109141541_DocumentVerification')
BEGIN
    CREATE INDEX [IX_DocumentVerifications_CompanyId] ON [DocumentVerifications] ([CompanyId]);
    CREATE INDEX [IX_DocumentVerifications_DocumentType_DocumentId_ContentHash] ON [DocumentVerifications] ([DocumentType], [DocumentId], [ContentHash]);
    CREATE UNIQUE INDEX [IX_DocumentVerifications_Token] ON [DocumentVerifications] ([Token]);
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261109141541_DocumentVerification')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261109141541_DocumentVerification', N'8.0.8');
END;
GO

IF OBJECT_ID(N'DocumentVerifications', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_DocumentVerifications_Token' AND [object_id] = OBJECT_ID(N'DocumentVerifications') AND is_unique = 1)
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261109141541_DocumentVerification')
    RAISERROR(N'DocumentVerification: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'DocumentVerification OK' AS Resultado, (SELECT COUNT(*) FROM [DocumentVerifications]) AS Filas;
GO
