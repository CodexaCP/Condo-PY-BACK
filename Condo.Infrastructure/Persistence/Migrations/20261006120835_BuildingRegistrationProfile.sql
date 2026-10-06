-- ============================================================
-- Migracion EF: 20261006120835_BuildingRegistrationProfile
-- Ficha de registro del edificio: agrega a Buildings los datos generales, legales/registrales, fiscales (RUC, razon social...),
-- de cobranza (dia de vencimiento, dias de gracia, instrucciones de pago) y la zona horaria (todo opcional), y crea la tabla
-- BuildingBankAccounts (cuentas bancarias de cobro). Ademas copia a cada edificio el RUC, la razon social, la actividad
-- economica y la direccion del establecimiento de su timbrado de factura mas reciente, para no cargarlos de nuevo.
-- Va DESPUES de 20261005135732_AddAdvertisingModule.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/BuildingRegistrationProfile.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [AdministratorName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [AdministratorPhone] nvarchar(30) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [BylawsFileName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [BylawsUrl] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [CadastralAccount] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [City] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [DefaultDueDay] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [Department] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [EconomicActivity] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [EmergencyContactName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [EmergencyContactPhone] nvarchar(30) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [FincaNumber] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [FiscalAddress] nvarchar(300) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [FloorsCount] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [GraceDays] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [InvoiceEmail] nvarchar(160) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [Latitude] decimal(9,6) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [LegalEntityDate] date NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [LegalEntityNumber] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [LegalName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [LocationReference] nvarchar(300) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [LogoUrl] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [Longitude] decimal(9,6) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [Neighborhood] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [OfficeHours] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [PadronNumber] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [PaymentInstructions] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [PropertyType] nvarchar(30) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [Ruc] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [TaxpayerType] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [TimeZoneId] nvarchar(60) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [TowersCount] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [UnitsCount] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [VatRegime] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [WhatsAppPhone] nvarchar(30) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    ALTER TABLE [Buildings] ADD [YearBuilt] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    CREATE TABLE [BuildingBankAccounts] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [BankName] nvarchar(120) NOT NULL,
        [AccountType] nvarchar(20) NOT NULL,
        [AccountNumber] nvarchar(40) NOT NULL,
        [HolderName] nvarchar(200) NOT NULL,
        [HolderDocument] nvarchar(30) NULL,
        [Alias] nvarchar(60) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_BuildingBankAccounts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BuildingBankAccounts_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BuildingBankAccounts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    CREATE INDEX [IX_BuildingBankAccounts_BuildingId] ON [BuildingBankAccounts] ([BuildingId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    CREATE INDEX [IX_BuildingBankAccounts_CompanyId] ON [BuildingBankAccounts] ([CompanyId]);
END;
GO

-- Traspaso de datos fiscales: solo a edificios que todavia no tienen RUC, desde su timbrado de factura mas reciente.
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    UPDATE b
    SET b.[Ruc] = s.[Ruc],
        b.[LegalName] = s.[RazonSocial],
        b.[EconomicActivity] = NULLIF(s.[ActividadEconomica], N''),
        b.[FiscalAddress] = NULLIF(s.[DireccionEstablecimiento], N'')
    FROM [Buildings] b
    CROSS APPLY (
        SELECT TOP (1) x.[Ruc], x.[RazonSocial], x.[ActividadEconomica], x.[DireccionEstablecimiento]
        FROM [InvoiceSeries] x
        WHERE x.[BuildingId] = b.[Id] AND x.[IsDeleted] = 0 AND x.[DocumentType] = N'Invoice'
        ORDER BY x.[Activo] DESC, x.[CreatedAtUtc] DESC
    ) s
    WHERE b.[Ruc] IS NULL AND b.[IsDeleted] = 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006120835_BuildingRegistrationProfile', N'8.0.8');
END;
GO

IF COL_LENGTH(N'Buildings', N'Ruc') IS NULL
   OR COL_LENGTH(N'Buildings', N'FincaNumber') IS NULL
   OR COL_LENGTH(N'Buildings', N'DefaultDueDay') IS NULL
   OR COL_LENGTH(N'Buildings', N'TimeZoneId') IS NULL
   OR OBJECT_ID(N'BuildingBankAccounts', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261006120835_BuildingRegistrationProfile')
    RAISERROR(N'BuildingRegistrationProfile: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'BuildingRegistrationProfile OK' AS Resultado;
GO
