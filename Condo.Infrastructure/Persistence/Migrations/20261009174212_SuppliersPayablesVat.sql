-- ============================================================
-- Migracion EF: 20261009174212_SuppliersPayablesVat
-- Centro de configuracion del edificio, fase 4 (proveedores, cuentas por pagar e IVA de las compras):
--   Tabla Suppliers (proveedores de la empresa, compartidos entre sus edificios; el RUC es unico por empresa cuando se informa).
--   BuildingExpenses: SupplierId, InvoiceNumber, InvoiceTimbrado, DueDate, PaidAt, PaidFromAccountId, VatRate (todas opcionales).
--   LedgerCategories: VatTreatment (tratamiento de IVA de la cuenta de egresos, opcional).
-- Todo es opcional y queda vacio: los gastos existentes no tienen vencimiento ni pago cargados, asi que siguen contando en la caja en
-- su fecha (ningun saldo cambia). No toca datos existentes.
-- Va DESPUES de 20261009171429_LateFeePolicyAndNotices.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/SuppliersPayablesVat.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009174212_SuppliersPayablesVat'
)
BEGIN
    ALTER TABLE [LedgerCategories] ADD [VatTreatment] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009174212_SuppliersPayablesVat'
)
BEGIN
    ALTER TABLE [BuildingExpenses] ADD [DueDate] date NULL;
    ALTER TABLE [BuildingExpenses] ADD [InvoiceNumber] nvarchar(50) NULL;
    ALTER TABLE [BuildingExpenses] ADD [InvoiceTimbrado] nvarchar(20) NULL;
    ALTER TABLE [BuildingExpenses] ADD [PaidAt] date NULL;
    ALTER TABLE [BuildingExpenses] ADD [PaidFromAccountId] uniqueidentifier NULL;
    ALTER TABLE [BuildingExpenses] ADD [SupplierId] uniqueidentifier NULL;
    ALTER TABLE [BuildingExpenses] ADD [VatRate] decimal(5,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009174212_SuppliersPayablesVat'
)
BEGIN
    CREATE TABLE [Suppliers] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Ruc] nvarchar(20) NULL,
        [Phone] nvarchar(40) NULL,
        [Email] nvarchar(160) NULL,
        [Address] nvarchar(300) NULL,
        [PaymentTermDays] int NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Suppliers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Suppliers_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009174212_SuppliersPayablesVat'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenses_BuildingId_DueDate] ON [BuildingExpenses] ([BuildingId], [DueDate]);
    CREATE INDEX [IX_BuildingExpenses_PaidFromAccountId] ON [BuildingExpenses] ([PaidFromAccountId]);
    CREATE INDEX [IX_BuildingExpenses_SupplierId] ON [BuildingExpenses] ([SupplierId]);
    CREATE INDEX [IX_Suppliers_CompanyId_Name] ON [Suppliers] ([CompanyId], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009174212_SuppliersPayablesVat'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Suppliers_CompanyId_Ruc] ON [Suppliers] ([CompanyId], [Ruc]) WHERE [IsDeleted] = 0 AND [Ruc] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009174212_SuppliersPayablesVat'
)
BEGIN
    ALTER TABLE [BuildingExpenses] ADD CONSTRAINT [FK_BuildingExpenses_FinancialAccounts_PaidFromAccountId] FOREIGN KEY ([PaidFromAccountId]) REFERENCES [FinancialAccounts] ([Id]) ON DELETE NO ACTION;
    ALTER TABLE [BuildingExpenses] ADD CONSTRAINT [FK_BuildingExpenses_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009174212_SuppliersPayablesVat'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009174212_SuppliersPayablesVat', N'8.0.8');
END;
GO

IF COL_LENGTH(N'LedgerCategories', N'VatTreatment') IS NULL
   OR COL_LENGTH(N'BuildingExpenses', N'DueDate') IS NULL
   OR COL_LENGTH(N'BuildingExpenses', N'InvoiceNumber') IS NULL
   OR COL_LENGTH(N'BuildingExpenses', N'InvoiceTimbrado') IS NULL
   OR COL_LENGTH(N'BuildingExpenses', N'PaidAt') IS NULL
   OR COL_LENGTH(N'BuildingExpenses', N'PaidFromAccountId') IS NULL
   OR COL_LENGTH(N'BuildingExpenses', N'SupplierId') IS NULL
   OR COL_LENGTH(N'BuildingExpenses', N'VatRate') IS NULL
   OR OBJECT_ID(N'Suppliers', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Suppliers_CompanyId_Ruc' AND object_id = OBJECT_ID(N'Suppliers'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Suppliers_CompanyId_Name' AND object_id = OBJECT_ID(N'Suppliers'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingExpenses_BuildingId_DueDate' AND object_id = OBJECT_ID(N'BuildingExpenses'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingExpenses_PaidFromAccountId' AND object_id = OBJECT_ID(N'BuildingExpenses'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingExpenses_SupplierId' AND object_id = OBJECT_ID(N'BuildingExpenses'))
   OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BuildingExpenses_Suppliers_SupplierId')
   OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BuildingExpenses_FinancialAccounts_PaidFromAccountId')
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261009174212_SuppliersPayablesVat')
    RAISERROR(N'SuppliersPayablesVat: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'SuppliersPayablesVat OK' AS Resultado,
       COL_LENGTH(N'BuildingExpenses', N'DueDate') AS ColumnaGastos,
       COL_LENGTH(N'LedgerCategories', N'VatTreatment') AS ColumnaCuentas,
       OBJECT_ID(N'Suppliers', N'U') AS Tabla;
GO
