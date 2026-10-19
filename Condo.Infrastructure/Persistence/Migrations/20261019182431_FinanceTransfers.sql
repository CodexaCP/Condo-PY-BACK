-- ============================================================
-- Migracion EF: 20261019182431_FinanceTransfers
-- Finanzas, operaciones guiadas: tabla FinanceTransfers (transferencias entre cuentas propias del edificio: deposito y retiro de efectivo,
-- aporte y retiro del fondo de reserva, transferencia entre bancos). No es ingreso ni gasto: mueve el saldo de cada cuenta.
-- Incluye dos triggers de proteccion: una operacion no se puede borrar, ni cambiarle importe, cuentas, fecha o edificio, ni reactivar una
-- anulada (solo se anula con motivo). Para una correccion excepcional hay que desactivarlos de forma deliberada (DISABLE TRIGGER) y dejar constancia.
-- Solo crea una tabla nueva; no toca datos existentes. Va DESPUES de 20261019152854_JournalAccountTypes.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I y -b:
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/FinanceTransfers.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que la tabla, los indices y los triggers existan de verdad;
-- si algo falta, aborta antes del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019182431_FinanceTransfers'
)
BEGIN
    CREATE TABLE [FinanceTransfers] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [FromAccountId] uniqueidentifier NOT NULL,
        [ToAccountId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [TransferDate] date NOT NULL,
        [Reference] nvarchar(100) NOT NULL,
        [Notes] nvarchar(500) NOT NULL,
        [SupportReference] nvarchar(500) NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [Status] int NOT NULL,
        [VoidedAtUtc] datetime2 NULL,
        [VoidedByUserId] uniqueidentifier NULL,
        [VoidReason] nvarchar(500) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_FinanceTransfers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FinanceTransfers_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FinanceTransfers_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FinanceTransfers_FinancialAccounts_FromAccountId] FOREIGN KEY ([FromAccountId]) REFERENCES [FinancialAccounts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FinanceTransfers_FinancialAccounts_ToAccountId] FOREIGN KEY ([ToAccountId]) REFERENCES [FinancialAccounts] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019182431_FinanceTransfers'
)
BEGIN
    CREATE INDEX [IX_FinanceTransfers_BuildingId_TransferDate] ON [FinanceTransfers] ([BuildingId], [TransferDate]);
    CREATE INDEX [IX_FinanceTransfers_CompanyId] ON [FinanceTransfers] ([CompanyId]);
    CREATE INDEX [IX_FinanceTransfers_FromAccountId] ON [FinanceTransfers] ([FromAccountId]);
    CREATE INDEX [IX_FinanceTransfers_ToAccountId] ON [FinanceTransfers] ([ToAccountId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019182431_FinanceTransfers'
)
BEGIN
    EXEC(N'CREATE TRIGGER [TR_FinanceTransfers_NoDelete] ON [FinanceTransfers] INSTEAD OF DELETE AS BEGIN THROW 50000, N''Una operacion de Finanzas no se puede borrar: se anula con motivo.'', 1; END');
    EXEC(N'CREATE TRIGGER [TR_FinanceTransfers_Protected] ON [FinanceTransfers] AFTER UPDATE AS BEGIN IF UPDATE([BuildingId]) OR UPDATE([CompanyId]) OR UPDATE([FromAccountId]) OR UPDATE([ToAccountId]) OR UPDATE([Amount]) OR UPDATE([TransferDate]) OR UPDATE([CreatedByUserId]) THROW 50000, N''Una operacion de Finanzas no se puede modificar: se anula con motivo y se carga de nuevo.'', 1; IF EXISTS (SELECT 1 FROM deleted d INNER JOIN inserted i ON i.[Id] = d.[Id] WHERE d.[Status] = 2 AND i.[Status] <> 2) THROW 50000, N''Una operacion de Finanzas no se puede modificar: se anula con motivo y se carga de nuevo.'', 1; END');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019182431_FinanceTransfers'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019182431_FinanceTransfers', N'8.0.8');
END;
GO

IF OBJECT_ID(N'FinanceTransfers', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FinanceTransfers_BuildingId_TransferDate' AND object_id = OBJECT_ID(N'FinanceTransfers'))
   OR NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_FinanceTransfers_NoDelete')
   OR NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_FinanceTransfers_Protected')
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261019182431_FinanceTransfers')
    RAISERROR(N'FinanceTransfers: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'FinanceTransfers OK' AS Resultado,
       (SELECT COUNT(*) FROM sys.triggers WHERE name IN (N'TR_FinanceTransfers_NoDelete', N'TR_FinanceTransfers_Protected')) AS Triggers;
GO
