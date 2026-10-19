-- ============================================================
-- Migracion EF: 20261019110927_AccountingBooks
-- Finanzas, libros contables: tabla AccountingBookSettings (una fila por edificio con el titular del libro -consorcio o empresa
-- administradora-, sus datos para el encabezado del Libro Diario, el contador y los datos de la rubrica). Es solo informacion: no mueve
-- saldos ni cambia el libro. Solo agrega una tabla y sus indices; no toca datos existentes.
-- Va DESPUES de 20261012185825_PaymentConnections.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/AccountingBooks.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que la tabla y los indices existan de verdad; si algo falta,
-- aborta antes del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019110927_AccountingBooks'
)
BEGIN
    CREATE TABLE [AccountingBookSettings] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [Holder] nvarchar(20) NOT NULL,
        [HolderLegalName] nvarchar(200) NULL,
        [HolderRuc] nvarchar(20) NULL,
        [HolderAddress] nvarchar(300) NULL,
        [AccountantName] nvarchar(200) NULL,
        [AccountantRegistration] nvarchar(30) NULL,
        [BookNumber] nvarchar(30) NULL,
        [RubricDate] date NULL,
        [AuthorizedFolios] int NULL,
        [UsedFolios] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_AccountingBookSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccountingBookSettings_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AccountingBookSettings_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019110927_AccountingBooks'
)
BEGIN
    CREATE INDEX [IX_AccountingBookSettings_CompanyId] ON [AccountingBookSettings] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019110927_AccountingBooks'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AccountingBookSettings_BuildingId] ON [AccountingBookSettings] ([BuildingId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019110927_AccountingBooks'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019110927_AccountingBooks', N'8.0.8');
END;
GO

IF OBJECT_ID(N'AccountingBookSettings', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AccountingBookSettings_BuildingId' AND object_id = OBJECT_ID(N'AccountingBookSettings'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AccountingBookSettings_CompanyId' AND object_id = OBJECT_ID(N'AccountingBookSettings'))
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261019110927_AccountingBooks')
    RAISERROR(N'AccountingBooks: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'AccountingBooks OK' AS Resultado,
       OBJECT_ID(N'AccountingBookSettings', N'U') AS Libros;
GO
