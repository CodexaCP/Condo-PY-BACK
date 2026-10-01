-- ============================================================
-- Migracion EF: 20261001174227_FinanceBudget
-- Modulo Finanzas del edificio, fase 3: tabla BudgetLines (presupuesto mensual por rubro).
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/FinanceBudget.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001174227_FinanceBudget'
)
BEGIN
    CREATE TABLE [BudgetLines] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [CategoryId] uniqueidentifier NOT NULL,
        [Year] int NOT NULL,
        [Month] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_BudgetLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BudgetLines_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BudgetLines_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BudgetLines_LedgerCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [LedgerCategories] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001174227_FinanceBudget'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_BudgetLines_BuildingId_CategoryId_Year_Month] ON [BudgetLines] ([BuildingId], [CategoryId], [Year], [Month]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001174227_FinanceBudget'
)
BEGIN
    CREATE INDEX [IX_BudgetLines_BuildingId_Year_Month] ON [BudgetLines] ([BuildingId], [Year], [Month]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001174227_FinanceBudget'
)
BEGIN
    CREATE INDEX [IX_BudgetLines_CategoryId] ON [BudgetLines] ([CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001174227_FinanceBudget'
)
BEGIN
    CREATE INDEX [IX_BudgetLines_CompanyId] ON [BudgetLines] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001174227_FinanceBudget'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001174227_FinanceBudget', N'8.0.8');
END;
GO

IF OBJECT_ID(N'BudgetLines', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BudgetLines_BuildingId_CategoryId_Year_Month' AND object_id = OBJECT_ID(N'BudgetLines'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BudgetLines_BuildingId_Year_Month' AND object_id = OBJECT_ID(N'BudgetLines'))
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261001174227_FinanceBudget')
    RAISERROR(N'FinanceBudget: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'FinanceBudget OK' AS Resultado;
GO
