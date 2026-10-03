-- ============================================================
-- Marketplace de espacios temporales — VERIFICACION de la base de datos (solo lectura, no modifica nada)
--
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -W -I -b -i /tmp/marketplace_verificacion.sql
--
-- Cada fila dice OK o FALTA. Si alguna dice FALTA, no desplegar el codigo: aplicar marketplace_acumulado.sql y volver a verificar.
-- ============================================================
SET NOCOUNT ON;

SELECT Tipo, Objeto, CASE WHEN Existe = 1 THEN 'OK' ELSE 'FALTA' END AS Estado
INTO #resultado
FROM (
    -- Migraciones registradas
    SELECT 'Migracion' AS Tipo, m.Nombre AS Objeto,
           CASE WHEN EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = m.Nombre) THEN 1 ELSE 0 END AS Existe
    FROM (VALUES
        (N'20261002125524_OwnerPaymentWebChannel'),
        (N'20261002172053_MarketplaceModuleBase'),
        (N'20261002174215_MarketplaceCore'),
        (N'20261002185716_MarketplacePaymentAlerts'),
        (N'20261002201507_MarketplaceCancellationsAndClaims'),
        (N'20261003105745_MarketplaceHandoverNotes')) AS m(Nombre)

    UNION ALL
    -- Tablas
    SELECT 'Tabla', t.Nombre, CASE WHEN OBJECT_ID(t.Nombre) IS NOT NULL THEN 1 ELSE 0 END
    FROM (VALUES
        (N'MarketplaceListings'), (N'MarketplaceReservations'), (N'MarketplaceReservationSlots'), (N'MarketplacePayments'),
        (N'MarketplaceAccountMovements'), (N'MarketplaceEvents'), (N'MarketplaceRefunds'), (N'MarketplaceClaims'),
        (N'MarketplaceOwnerDebts'), (N'MarketplaceHandoverNotes')) AS t(Nombre)

    UNION ALL
    -- Columnas agregadas a tablas que ya existian
    SELECT 'Columna', c.Tabla + '.' + c.Columna, CASE WHEN COL_LENGTH(c.Tabla, c.Columna) IS NOT NULL THEN 1 ELSE 0 END
    FROM (VALUES
        (N'Buildings', N'MarketplaceEnabled'), (N'Buildings', N'MarketplaceCommissionPercent'), (N'Buildings', N'MarketplaceTransferInfo'),
        (N'Plans', N'IncludesMarketplace'),
        (N'OwnerCreditMovements', N'MarketplaceReservationId'),
        (N'MarketplacePayments', N'AlertCount'), (N'MarketplacePayments', N'LastAlertAtUtc'),
        (N'MarketplaceReservations', N'StartNoticeSentAtUtc'), (N'MarketplaceReservations', N'StartResponse'),
        (N'MarketplaceReservations', N'StartResponseReason'), (N'MarketplaceReservations', N'StartResponseAtUtc')) AS c(Tabla, Columna)

    UNION ALL
    -- Indices unicos: son las garantias de integridad que viven en la base (doble reserva, doble pago, doble asiento, etc.)
    SELECT 'Indice unico', i.Nombre, CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = i.Nombre AND is_unique = 1) THEN 1 ELSE 0 END
    FROM (VALUES
        (N'IX_MarketplaceReservationSlots_NoDoubleBooking'),
        (N'IX_MarketplaceReservations_OnePendingPerBuyer'),
        (N'IX_MarketplacePayments_OneApprovedPerReservation'),
        (N'IX_MarketplaceAccountMovements_OnePerReservationKind'),
        (N'IX_OwnerCreditMovements_OneLotPerMarketplaceReservation'),
        (N'IX_MarketplaceRefunds_OnePerReservation'),
        (N'IX_MarketplaceClaims_OneOpenPerReservation'),
        (N'IX_MarketplaceOwnerDebts_OnePerReservation')) AS i(Nombre)
) AS r;

SELECT Tipo, Objeto, Estado FROM #resultado
ORDER BY CASE Tipo WHEN 'Migracion' THEN 1 WHEN 'Tabla' THEN 2 WHEN 'Columna' THEN 3 ELSE 4 END, Objeto;

-- Resumen: tiene que dar 0. Si es mayor, falta algo: no desplegar el codigo.
SELECT COUNT(*) AS Faltantes FROM #resultado WHERE Estado = 'FALTA';

DROP TABLE #resultado;
