# Marketplace de espacios temporales — Guía de despliegue (fases 1 a 9)

Fecha: 2026-10-03. Rama de trabajo: `feature/marketplace` en los 3 repos (`Condo-PY-BACK`, `Condo-PY-WEB`, `CondoPY-APP`).
Datos del VPS y comandos base: ver la memoria `vps_deploy` (SSH `root@2.25.187.20`, servicio `condo-py-api`, repo en `/opt/condo-py-src`, base `CondoPY`).

> **Regla de oro:** primero la **base de datos**, después el **código**. Todo lo nuevo es *aditivo* (tablas y columnas nuevas): el código viejo
> sigue funcionando con la base nueva, pero el código nuevo **no** funciona con la base vieja.

---

## 0. Qué se despliega

| Pieza | Qué trae | Cómo |
|---|---|---|
| **Base de datos** | 5 migraciones del Marketplace (10 tablas nuevas y 11 columnas nuevas) | `docs/sql/marketplace_acumulado.sql` (idempotente) |
| **Backend (.NET)** | Todo el marketplace + límite de tasa en las escrituras | `git pull` + `dotnet publish` + `rsync` (paso 3) |
| **Web (Angular)** | Pantallas del Encargado y SuperAdmin | `ng build` y publicar como siempre |
| **App (Ionic)** | Sección del vecino y del Encargado | Nueva versión de la app |

El módulo **nace apagado**: ningún edificio ve nada hasta que el SuperAdmin lo habilita (paso 6). Desplegar el código no cambia nada para nadie.

---

## 1. Antes de empezar (5 minutos)

1. **Respaldo de la base** (en el VPS):
   ```bash
   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'ServerSA*2026' -C -Q "BACKUP DATABASE CondoPY TO DISK = N'/var/opt/mssql/data/CondoPY_pre_marketplace.bak' WITH INIT, COMPRESSION, CHECKSUM"
   ```
   (Si esa carpeta no existe o no tiene permiso, usá otra donde el usuario `mssql` pueda escribir.)
2. **Copia del backend actual**, por si hay que volver atrás:
   ```bash
   rm -rf /opt/condo-py-api.bak && cp -a /opt/condo-py-api /opt/condo-py-api.bak
   ```
3. **Rama del repo en el VPS:**
   ```bash
   cd /opt/condo-py-src && git branch --show-current && git status --short | head
   ```
   Tiene que ser `feature/marketplace` (o `main` si ya se mergeó) y estar limpio. Si dice otra rama, el `git pull` no trae el marketplace.
4. **Estado actual de la base** (qué falta aplicar): pegar `marketplace_verificacion.sql` (paso 2.3) *antes* de aplicar nada. Las filas `FALTA` son lo pendiente.

---

## 2. Base de datos

Los `.sql` se crean **pegándolos en el VPS** (el `scp` está bloqueado): `cat > /tmp/archivo.sql << 'EOF'` + contenido + `EOF`.

### 2.1 Aplicar el script acumulado
Archivo: `docs/sql/marketplace_acumulado.sql` (≈1.070 líneas; contiene las 5 migraciones en orden). Es **seguro correrlo completo aunque ya haya
migraciones aplicadas**: cada bloque revisa `__EFMigrationsHistory`, y cada migración verifica al final lo que creó y aborta sin dejar nada a medias.

```bash
/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'ServerSA*2026' -C -d CondoPY -I -b -i /tmp/marketplace_acumulado.sql
```
Debe imprimir, una por una: `MarketplaceModuleBase OK`, `MarketplaceCore OK`, `MarketplacePaymentAlerts OK`, `MarketplaceCancellationsAndClaims OK`,
`MarketplaceHandoverNotes OK`. **Siempre con `-I` y `-b`** (sin `-I` fallan los índices filtrados).

> Si solo falta la última (la fase 8), alcanza con `Condo.Infrastructure/Persistence/Migrations/20261003105745_MarketplaceHandoverNotes.sql`.

### 2.2 Prerrequisito
La migración `20261002125524_OwnerPaymentWebChannel` tiene que estar aplicada antes (ya lo está en producción).

### 2.3 Verificar que quedó bien (solo lectura)
Archivo: `docs/sql/marketplace_verificacion.sql`.
```bash
/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'ServerSA*2026' -C -d CondoPY -W -I -b -i /tmp/marketplace_verificacion.sql
```
Tiene que listar **35 filas en `OK`** (6 migraciones, 10 tablas, 11 columnas, 8 índices únicos) y terminar con `Faltantes = 0`.
Si `Faltantes` es mayor que cero, **no desplegar el código**.

*Validado el 2026-10-03 contra SQL Server 2019 (LocalDB): base migrada hasta `OwnerPaymentWebChannel` → script acumulado → verificación 35/35 → segunda
corrida sin errores → EF Core reconoce todas las migraciones como aplicadas.*

---

## 3. Backend

```bash
cd /opt/condo-py-src
git pull
dotnet publish Condo.Api/Condo.Api.csproj -c Release -o /opt/condo-py-publish

systemctl stop condo-py-api
rsync -a \
  --exclude 'appsettings.json' \
  --exclude 'appsettings.Development.json' \
  --exclude 'appsettings.Production.json' \
  --exclude 'wwwroot/uploads/' \
  /opt/condo-py-publish/ /opt/condo-py-api/
systemctl start condo-py-api
systemctl status condo-py-api --no-pager
```
Tiene que decir `Active: active (running)`. Si falla: `journalctl -u condo-py-api -n 80 --no-pager`.

### 3.1 Qué mirar en el arranque
```bash
journalctl -u condo-py-api --since "5 minutes ago" --no-pager | grep -i -E "error|marketplace|exception" | head
```
Sin errores. El proceso de fondo del marketplace corre cada 30 segundos y solo escribe en el log cuando hace algo
(`Marketplace: N reservas vencidas…`, `…acreditaciones al saldo`, `…alertas de pagos por revisar`, `…avisos de inicio`, `…reembolsos vencidos`).

### 3.2 Configuración (opcional)
Solo si querés cambiar el plazo para pagar (por defecto **10 minutos**, valor de toda la plataforma), en `appsettings.Production.json`:
```json
"Marketplace": { "PaymentTimeoutMinutes": 10 }
```
Los límites de tasa (60 escrituras/min, 12 acciones sensibles/min, 20 PDF/Excel/min, por usuario) están en `MarketplaceRateLimiting.cs`; un uso normal no se acerca.
Pasados los topes la API responde **429** con el mensaje «Hiciste demasiadas acciones seguidas. Esperá un momento e intentá de nuevo.».

---

## 4. Web
`ng build` en `Condo-PY-WEB` y publicar la salida como se publica siempre. Novedades: «Marketplace por edificio» (SuperAdmin), «Pagos del Marketplace»,
«Reembolsos y reclamos», «Cuenta del Marketplace» y el campo «Incluye Marketplace» en Planes. Todo aparece solo si el módulo está disponible.

## 5. App
Compilar y publicar una **nueva versión** de `CondoPY-APP`. El backend es compatible con versiones viejas de la app: simplemente no muestran las secciones nuevas.
(El archivo `android/app/release/baselineProfiles/*/app-release.dm` aparece modificado en el repo desde antes: no es parte de este trabajo.)

---

## 6. Encender el módulo (lanzamiento gradual)

1. **Planes** → editar (o crear) un plan y marcar **«Incluye Marketplace»**.
2. **Asignaciones** → que el edificio piloto tenga ese plan vigente.
3. **Marketplace por edificio** → «Configurar» el edificio piloto: **habilitar**, **comisión** (por defecto 10 %) y **datos para transferir** (banco, titular, cuenta, alias; solo los ve quien está pagando una reserva).
4. Recomendado: **un solo edificio piloto** durante unos días (ver la guía de pruebas, `docs/GUIA_PRUEBAS_MARKETPLACE.md`), y recién después habilitar el resto.

Para **apagarlo** en un edificio: el mismo interruptor. El módulo desaparece de menús y endpoints (403), y **se conservan todos los datos**.

---

## 7. Prueba rápida después de desplegar (10 minutos)

| # | Qué hacer | Resultado esperado |
|---|---|---|
| 1 | Entrar como SuperAdmin → menú *Planes* | Aparece «Marketplace por edificio» y «Cuenta del Marketplace» |
| 2 | Entrar como Encargado de un edificio **sin** el módulo | **No** aparece nada de Marketplace en el menú |
| 3 | Habilitar el edificio piloto y entrar como Encargado | Aparecen «Pagos del Marketplace», «Reembolsos y reclamos», «Cuenta del Marketplace» |
| 4 | Entrar a la app como propietario del piloto | El atajo «Marketplace» aparece en Inicio |
| 5 | Publicar un espacio de prueba (1 hora, precio simbólico) | Aparece en «Publicaciones» |
| 6 | Otro vecino lo ve en «Explorar» y toca «Reservar» | Muestra precio, comisión y total; cuenta regresiva de pago |
| 7 | `journalctl -u condo-py-api --since "10 minutes ago"` | Sin errores |

Después, seguir con la guía completa de pruebas.

---

## 8. Volver atrás

| Situación | Qué hacer |
|---|---|
| Algo raro con el módulo en un edificio | Apagar el interruptor de ese edificio (no se pierde nada) |
| El backend nuevo no arranca | `systemctl stop condo-py-api && rsync -a --delete --exclude 'appsettings*.json' --exclude 'wwwroot/uploads/' /opt/condo-py-api.bak/ /opt/condo-py-api/ && systemctl start condo-py-api` (el código viejo ignora las tablas nuevas) |
| Restaurar la base | Último recurso: restaurar el respaldo del paso 1 (se pierde lo escrito desde entonces). **No borrar las tablas del marketplace**: no hace falta |

Las migraciones **no se revierten**: son aditivas y el código anterior las ignora.

---

## 9. Seguimiento los primeros días (consultas de solo lectura)

```sql
-- Reembolsos pendientes (y cuántos pasaron las 72 horas)
SELECT COUNT(*) AS Pendientes, SUM(CASE WHEN CreatedAtUtc < DATEADD(HOUR,-72,SYSUTCDATETIME()) THEN 1 ELSE 0 END) AS Vencidos
FROM MarketplaceRefunds WHERE Status = 'Pending' AND IsDeleted = 0;

-- Reclamos abiertos y acreditaciones retenidas
SELECT COUNT(*) AS Abiertos FROM MarketplaceClaims WHERE Status = 'Open' AND IsDeleted = 0;

-- Pagos esperando revisión desde hace más de una hora
SELECT COUNT(*) FROM MarketplacePayments WHERE Status = 'Submitted' AND IsDeleted = 0 AND SubmittedAtUtc < DATEADD(HOUR,-1,SYSUTCDATETIME());

-- Conciliación de la cuenta aparte por edificio (el saldo es la suma de sus movimientos)
SELECT b.Name, SUM(m.Amount) AS Saldo FROM MarketplaceAccountMovements m JOIN Buildings b ON b.Id = m.BuildingId WHERE m.IsDeleted = 0 GROUP BY b.Name;
```

---

## 10. Pruebas automáticas (antes de cada despliegue)

```bash
dotnet test Condo.Tests                 # 540 pruebas con SQLite en memoria (10 de concurrencia se omiten solas)
```
Con SQL Server real (LocalDB) corren **las 550**, incluidas las 10 de concurrencia (reservas simultáneas, doble aprobación, doble acreditación, etc.):
```powershell
$env:CONDO_TEST_SQLSERVER = 'Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True'
dotnet test Condo.Tests
```
Cada ejecución crea y borra su propia base `CondoTests_*`.

---

## 11. Fuera de alcance de esta entrega (trabajo aparte, ya acordado)

- **Proteger los comprobantes subidos** (`/api/uploads`): hoy son URL pública (marketplace y expensas). Hay que cambiar app y web a descarga autenticada o URL firmada.
- **Pagar con saldo a favor** (comprador) y **reembolso de la base al saldo** del comprador.
- Contador de reembolsos/reclamos pendientes en las pestañas del Encargado (hoy se ve al entrar al Marketplace y llegan avisos).
- Las rutas de la web no tienen *guard* por rol/módulo: ocultar el menú no impide abrir la URL a mano (el backend igual responde 403).
