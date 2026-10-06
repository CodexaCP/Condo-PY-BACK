-- ============================================================
-- Migracion EF: 20261006131419_PersonBillingProfile
-- 1) Facturas: guarda en la factura el cliente (nombre, documento, direccion, email) al emitirla, para que un documento fiscal
--    emitido no cambie aunque despues se edite o se reemplace al propietario. Las facturas Emitidas y Anuladas que ya existen
--    se completan con el cliente ACTUAL de su unidad (propietario principal vigente; si no hay, residente actual) y quedan
--    marcadas con ClientReconstructed = 1 (no es necesariamente quien era al emitirse).
-- 2) Propietarios (ApplicationUsers): persona fisica/juridica, razon social, datos de facturacion propios, telefono secundario,
--    WhatsApp, nacionalidad y fecha de nacimiento (todo opcional).
-- 3) Residentes: relacion con el propietario, contacto de emergencia, nacionalidad, fecha de nacimiento, contrato de alquiler.
-- 4) UnitOwners: porcentaje de titularidad, fecha de fin y motivo del cambio de propietario.
-- Va DESPUES de 20261006120835_BuildingRegistrationProfile.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/PersonBillingProfile.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261006131419_PersonBillingProfile')
BEGIN
    ALTER TABLE [UnitOwners] ADD
        [EndDate] date NULL,
        [OwnershipPercentage] decimal(5,2) NULL,
        [TransferReason] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261006131419_PersonBillingProfile')
BEGIN
    ALTER TABLE [Residents] ADD
        [BirthDate] date NULL,
        [EmergencyContactName] nvarchar(200) NULL,
        [EmergencyContactPhone] nvarchar(30) NULL,
        [LeaseEndDate] date NULL,
        [LeaseFileName] nvarchar(200) NULL,
        [LeaseUrl] nvarchar(500) NULL,
        [Nationality] nvarchar(60) NULL,
        [Relationship] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261006131419_PersonBillingProfile')
BEGIN
    ALTER TABLE [Invoices] ADD
        [ClientAddress] nvarchar(300) NULL,
        [ClientDocument] nvarchar(40) NULL,
        [ClientDocumentType] nvarchar(30) NULL,
        [ClientEmail] nvarchar(160) NULL,
        [ClientName] nvarchar(200) NULL,
        [ClientReconstructed] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261006131419_PersonBillingProfile')
BEGIN
    ALTER TABLE [ApplicationUsers] ADD
        [BirthDate] date NULL,
        [InvoiceAddress] nvarchar(300) NULL,
        [InvoiceDocument] nvarchar(40) NULL,
        [InvoiceDocumentType] nvarchar(30) NULL,
        [InvoiceEmail] nvarchar(160) NULL,
        [InvoiceName] nvarchar(200) NULL,
        [LegalName] nvarchar(200) NULL,
        [Nationality] nvarchar(60) NULL,
        [PersonType] nvarchar(20) NULL,
        [SecondaryPhone] nvarchar(30) NULL,
        [WhatsAppPhone] nvarchar(30) NULL;
END;
GO

-- Cliente de las facturas ya emitidas o anuladas: propietario principal vigente de la unidad (el de mas antiguo si hay
-- varios principales); si la unidad no tiene propietario, su residente actual. Las que no tengan a nadie quedan sin cliente.
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261006131419_PersonBillingProfile')
BEGIN
    UPDATE i
    SET i.[ClientName] = LEFT(c.[Name], 200),
        i.[ClientDocumentType] = LEFT(c.[DocType], 30),
        i.[ClientDocument] = LEFT(c.[Doc], 40),
        i.[ClientAddress] = LEFT(c.[Addr], 300),
        i.[ClientEmail] = LEFT(c.[Email], 160),
        i.[ClientReconstructed] = 1
    FROM [Invoices] i
    CROSS APPLY (
        SELECT TOP (1) x.[Name], x.[DocType], x.[Doc], x.[Addr], x.[Email]
        FROM (
            SELECT 1 AS [Src], uo.[IsPrimary] AS [P], uo.[CreatedAtUtc] AS [Cr],
                   u.[FullName] AS [Name], u.[DocumentType] AS [DocType], u.[DocumentNumber] AS [Doc],
                   u.[Address] AS [Addr], u.[Email] AS [Email]
            FROM [UnitOwners] uo
            INNER JOIN [ApplicationUsers] u ON u.[Id] = uo.[OwnerId]
            WHERE uo.[UnitId] = i.[UnitId] AND uo.[IsDeleted] = 0
            UNION ALL
            SELECT 2, CAST(0 AS bit), ur.[CreatedAtUtc],
                   r.[FullName], r.[DocumentType], r.[DocumentNumber],
                   CAST(NULL AS nvarchar(300)), r.[Email]
            FROM [UnitResidents] ur
            INNER JOIN [Residents] r ON r.[Id] = ur.[ResidentId]
            WHERE ur.[UnitId] = i.[UnitId] AND ur.[IsDeleted] = 0 AND ur.[EndDate] IS NULL
        ) x
        ORDER BY x.[Src], x.[P] DESC, x.[Cr]
    ) c
    WHERE i.[Status] IN (N'Issued', N'Voided') AND i.[ClientName] IS NULL;
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261006131419_PersonBillingProfile')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006131419_PersonBillingProfile', N'8.0.8');
END;
GO

IF COL_LENGTH(N'Invoices', N'ClientName') IS NULL
   OR COL_LENGTH(N'Invoices', N'ClientReconstructed') IS NULL
   OR COL_LENGTH(N'ApplicationUsers', N'InvoiceName') IS NULL
   OR COL_LENGTH(N'ApplicationUsers', N'PersonType') IS NULL
   OR COL_LENGTH(N'Residents', N'Relationship') IS NULL
   OR COL_LENGTH(N'UnitOwners', N'OwnershipPercentage') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261006131419_PersonBillingProfile')
    RAISERROR(N'PersonBillingProfile: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'PersonBillingProfile OK' AS Resultado,
       (SELECT COUNT(*) FROM [Invoices] WHERE [ClientReconstructed] = 1) AS FacturasCompletadas,
       (SELECT COUNT(*) FROM [Invoices] WHERE [Status] IN (N'Issued', N'Voided') AND [ClientName] IS NULL) AS FacturasSinCliente;
GO
