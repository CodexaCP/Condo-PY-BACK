# Guía: pagos online (Etapa 1, proveedor falso) — despliegue y pruebas

Fecha: 2026-10-20. Rama `feature/pagos-online` (BACK y WEB; el APP no tiene cambios). Antecedentes: [PROPUESTA_PAGOS_ONLINE.md](PROPUESTA_PAGOS_ONLINE.md), [ALCANCE_TECNICO_PAGOS_ONLINE_ETAPA1.md](ALCANCE_TECNICO_PAGOS_ONLINE_ETAPA1.md) y [GUIA_PRUEBAS_PAGOS_BANCARD_CONFIG.md](GUIA_PRUEBAS_PAGOS_BANCARD_CONFIG.md) (la configuración de conexiones, ya desplegable).

## 0. Qué es y qué no es esta etapa

**Es:** el cobro completo de punta a punta con un **proveedor falso**. Un propietario abre un cobro de sus comprobantes, el proveedor «avisa» el resultado por una dirección de callback, el pago se aplica **una sola vez** con la misma liquidación de siempre (pago aprobado, saldo a favor, recibo, borrador de factura, aviso) y el importe queda en una cuenta **«Cobros online por liquidar»** del edificio. El dinero no pasa por Condo-PY.

**No es todavía:**

- **No hay adaptador de Bancard.** No se cobra plata real. Falta programar Tpago y eso espera las respuestas de la sección 5.5 de la propuesta.
- **No hay pantalla del propietario en el APP.** El propietario todavía no puede tocar «Pagar ahora». Para probar, el recorrido del propietario se hace llamando a la API (sección 5).
- **No se registran liquidaciones del proveedor ni comisiones** (decisiones D-1 y D-2). La plata que queda «por liquidar» se pasa al banco con una **transferencia entre cuentas** de Finanzas.
- **No hay reversas** desde Condo-PY ni conciliación contra el portal del proveedor.

Todo arranca **apagado**: el cobro y el proveedor falso se encienden a propósito (sección 2). Con los interruptores apagados nada cambia para los propietarios.

## 1. Despliegue (en este orden)

### 1.1 Migración SQL (primero)

Archivo: `Condo.Infrastructure/Persistence/Migrations/20261020143213_OnlinePaymentIntents.sql`. Va **después** de `20261020111217_AccountReconcileOverride` (y de `20261012185825_PaymentConnections` si todavía no está). Se pega en el VPS como las anteriores:

```bash
cat > /tmp/OnlinePaymentIntents.sql << 'EOF'
-- (pegar el contenido del archivo .sql)
EOF
/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/OnlinePaymentIntents.sql
```

Debe terminar mostrando `OnlinePaymentIntents OK` con cuatro números (los ids de las tablas nuevas). Es idempotente (se puede correr dos veces) y **solo agrega**: cuatro tablas (`PaymentIntents`, `PaymentIntentItems`, `PaymentAttempts`, `PaymentEvents`), tres columnas nulas en `PaymentConnections` (titular), `BuildingPaymentConfigs.IsTest` (por defecto 0) y `OwnerPayments.PaymentIntentId` (nulo). No toca datos existentes. Se probó dos veces seguidas contra un SQL Server real.

### 1.2 Interruptores del servidor (todos apagados por defecto)

Se configuran como variables del servicio, junto a la clave maestra que ya existe (`Payments__MasterKey`, ver la guía de configuración):

```bash
systemctl edit condo-py-api
```

```ini
[Service]
# Solo en un servidor de PRUEBAS. En producción, no se define o se deja en false.
Environment="Payments__CollectionEnabled=true"
Environment="Payments__EnableFakeProvider=true"
# Opcional: minutos que tiene el propietario para pagar un cobro abierto (por defecto 30; mínimo 5).
Environment="Payments__IntentMinutes=30"
```

| Variable | Qué hace | Por defecto |
|---|---|---|
| `Payments__CollectionEnabled` | Permite abrir cobros online. Apagado: la API responde «el pago online todavía no está habilitado» y la pantalla de configuración sigue funcionando. | `false` |
| `Payments__EnableFakeProvider` | Muestra el **Simulador de cobros** en el catálogo, deja crear y activar conexiones del simulador y habilita los endpoints del simulador. Nunca funciona en conexiones de Producción. | `false` |
| `Payments__IntentMinutes` | Plazo para pagar un cobro abierto. | `30` |
| `Payments__CallbackBaseUrl` | Dirección pública de la API para armar el callback (ya existía). | host de la llamada |

**En producción real dejá los dos primeros apagados** hasta que exista el adaptador de Bancard y lo hayas probado con un cliente piloto.

### 1.3 Backend y web

Despliegue normal (`/opt/condo-py-src` → publish → rsync) y del web. Orden: **SQL → interruptores → backend → web**. Al arrancar, el backend queda con un proceso en segundo plano que cada minuto cierra los cobros vencidos (no requiere configuración).

**Ventana de despliegue:** si el servicio se reinicia mientras llega un aviso, el proveedor real (Tpago) **revierte ese pago** y devuelve la plata al pagador. Es seguro (el dinero vuelve) pero deja mala experiencia: con un proveedor real, evitá desplegar en horas de cobro.

## 2. Preparar un entorno de prueba (una sola vez)

Hace falta un edificio con **Finanzas habilitado** (para ver la cuenta por liquidar) y un propietario con deuda publicada.

1. **Centro de configuración → Pagos online → Nueva conexión.** Proveedor «Simulador (solo pruebas)», modalidad «Simulador de cobros», ambiente **Sandbox**. Cargá una clave de firma cualquiera (texto largo) y el **titular** (por ejemplo «Administradora de Prueba S.A.», RUC `80000000-1`).
2. **Edificios:** asigná el edificio y marcalo **«de prueba»** (así los cobros SÍ se aplican a la deuda). Al asignarlo, si tiene Finanzas, se crea solo la cuenta **«Cobros online por liquidar»** (Finanzas (módulo) → Configuración → Cuentas: figura «La administra el sistema»).
3. **Activar** la conexión. Si falta el titular, no deja.
4. Encendé los dos interruptores de la sección 1.2 y reiniciá el servicio.

Para ver el caso «edificio real» (el cobro del simulador NO toca la deuda), repetí con otro edificio **sin** marcarlo de prueba.

## 3. Cómo probar el recorrido (sin APP)

Con el token del propietario (`Authorization: Bearer ...`) y la URL de la API:

```bash
# 1. Qué puede pagar (comprobantes con su acumulado, medio de pago, titular)
curl -s -H "Authorization: Bearer $TOKEN" $API/api/online-payments/options

# 2. Abrir el cobro "hasta" un comprobante (se incluyen todos los anteriores)
curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"buildingId":"...","throughUnitId":"...","throughExpensePeriodId":"..."}' $API/api/online-payments

# 3. Estado (la app lo consultará cada tanto mientras el propietario paga)
curl -s -H "Authorization: Bearer $TOKEN" $API/api/online-payments/<id>
```

El proveedor «responde» desde la web: **Cobros online** (menú «Períodos y cobros») → elegir el cobro → **Simular pago aprobado / rechazado / importe distinto**. Eso arma el aviso firmado y lo procesa por el mismo camino del webhook real (`POST /api/payment-webhooks/{publicId}`).

## 4. Qué probar

### 4.1 Camino feliz

1. Abrir un cobro de un comprobante: queda **En curso**, con el titular, el importe y un intento «Esperando el pago».
2. **Simular pago aprobado:** el cobro pasa a **Pagado**; en **Pagos → Hechos por el sistema** aparece un pago `PAY-aaaa-nnnnnn` con método «Pago online», aprobado; el propietario recibe «Tu pago online fue aprobado»; hay un **borrador de factura** por comprobante (Facturación); el comprobante ya no figura como deuda.
3. **Finanzas (módulo) → Tablero:** aparece «Cobros online por liquidar» con ese importe; **no** suma a «Saldo en caja» ni «Saldo bancario».
4. **Hasta un comprobante posterior:** con dos meses de deuda, abrir el cobro «hasta» el segundo incluye los dos (importe = suma, redondeada al guaraní).
5. **Liquidar:** Finanzas (módulo) → Operaciones → transferencia desde «Cobros online por liquidar» hacia el banco («Liquidación de cobros online»). La cuenta por liquidar solo puede ser **origen**, nunca destino.

### 4.2 Casos límite

| Caso | Qué debe pasar |
|---|---|
| Simular **dos veces** el mismo aviso | Responde éxito las dos veces, pero hay **un solo** pago. En el registro de eventos figura «Repetido (ignorado)». |
| **Importe distinto** | Se rechaza (409), el cobro sigue **En curso**, no se crea pago. Evento «Importe distinto». |
| **Pago rechazado** | Cobro **Cerrado** («El proveedor rechazó el pago»), comprobantes libres, el propietario puede abrir otro cobro (intento nuevo). |
| **Mora entre la apertura y la confirmación** (agregar un cargo al periodo con el cobro abierto) y simular aprobado | Cobro **Requiere revisión**, sin pago; el simulador (como Tpago) recibe error 409 y «devuelve el dinero»; se avisa a los administradores. Decisión D-4. |
| Comprobante **ya pagado** por otro camino mientras el cobro está abierto | Igual: **Requiere revisión** («alguno de los comprobantes ya estaba pagado»). |
| Cobro **vencido** (esperar el plazo o bajar `IntentMinutes`) | Se cierra solo («Venció el plazo para pagar»), comprobantes libres. Un aviso **tardío** igual se aplica si todo coincide; si otro cobro tomó el comprobante, va a revisión. |
| **Edificio real** (no marcado de prueba) con el simulador | El cobro queda **Simulado**: sin pago, sin cambio en la deuda ni en Finanzas. Decisión D-3. |
| **Pago declarado pendiente** del propietario | No puede abrir un cobro online (409) hasta que se resuelva. |
| **Cobro en curso** | El propietario no puede declarar otro pago desde la app y el personal no puede «Registrar pago» (409) mientras tanto. |
| Dos cobros del **mismo comprobante** a la vez | Solo se abre uno (409 «ya tenés un pago online en curso»). |
| **Firma inválida / sin firma** | 401, evento «Firma inválida», no cambia nada; un aviso legítimo posterior se procesa normal. |
| Conexión **desactivada** o sin titular | No se puede abrir cobro (409). |
| Simulador **apagado** en el servidor | El catálogo no lo ofrece, no se crean conexiones nuevas, no se pueden activar las existentes y el simulador responde 404. |

### 4.3 Permisos y alcance

- Solo el **propietario** abre y consulta sus cobros (nunca los de otro).
- El personal ve los cobros de **su alcance** (un encargado, solo sus edificios). Solo el **SuperAdmin** y el **Administrador de toda la empresa** ven el registro de eventos y usan el simulador.
- La cuenta por liquidar no se puede crear, editar ni borrar a mano, ni elegir como cuenta por defecto ni para pagar gastos.
- El método «Pago online» no se acepta al registrar un pago a mano, y un pago online no se revierte desde «Revertir pago».

### 4.4 Consultas útiles en la base

```sql
-- Últimos cobros y cómo terminaron
SELECT TOP 20 Number, Status, CloseReason, ReviewReason, Amount, IsSimulated, CreatedAtUtc FROM PaymentIntents ORDER BY Number DESC;

-- Registro de avisos (el contenido guardado está redactado: nunca lleva claves ni datos de tarjeta)
SELECT TOP 50 ReceivedAtUtc, Direction, Kind, Outcome, SignatureOk, Error FROM PaymentEvents ORDER BY ReceivedAtUtc DESC;

-- Un cobro no puede tener más de un pago (debe devolver 0 filas)
SELECT PaymentIntentId, COUNT(*) FROM OwnerPayments WHERE PaymentIntentId IS NOT NULL GROUP BY PaymentIntentId HAVING COUNT(*) > 1;

-- Comprobantes reservados por cobros abiertos
SELECT UnitId, ExpensePeriodId FROM PaymentIntentItems WHERE IsOpen = 1 AND IsDeleted = 0;
```

## 5. Contrato de la API (para la pantalla del APP, cuando se haga)

| Ruta | Quién | Para qué |
|---|---|---|
| `GET api/online-payments/options` | Propietario | Por edificio: medios de pago activos (con titular y si es de prueba), comprobantes con su **acumulado** y, si no puede pagar ahora, el motivo (`blockedReason`). |
| `POST api/online-payments` | Propietario | Abre el cobro: `{ buildingId, connectionId?, throughUnitId, throughExpensePeriodId }`. Devuelve número, importe, titular, vencimiento y `checkoutUrl`. `connectionId` solo si el edificio tiene más de un medio. |
| `GET api/online-payments/{id}` | Propietario | Estado (`Open`, `Paid`, `Closed`, `ReviewRequired`) para la consulta periódica. |
| `POST api/payment-webhooks/{publicId}` | Proveedor | Aviso anónimo, sin CORS. Responde `{"status":"success"}` solo si el pago quedó registrado. |
| `GET api/online-payments/admin` (+ `/{id}`) | Personal | Lista y detalle. |
| `POST api/payment-fake/{n}/confirm` y `/reject` | SuperAdmin / Administrador | Simulador (solo con `Payments__EnableFakeProvider`). |

Códigos de error del `POST`: `collection_disabled`, `no_debt`, `comprobante_not_found`, `declared_payment_pending`, `payment_in_progress`, `no_connection`, `connection_required`, `holder_missing`, `provider_unavailable`, `secrets_unreadable`, `provider_error`. Con Tpago el proveedor **no devuelve al usuario a la app**: la pantalla de espera tiene que consultar el estado cada tanto (la app ya consulta cada 30 s) y apoyarse en la notificación push.

## 6. Reglas que quedaron fijadas

- **Un cobro = un edificio** (un solo comercio), con comprobantes completos del más antiguo al más nuevo. Sin pagos parciales. El saldo a favor del propietario no se usa en el cobro online.
- **Importe:** guaraní entero (redondeo a la unidad dentro de la tolerancia de 0,5 de los comprobantes).
- **Cambio de importe entre el cobro y la confirmación:** no se aplica, pasa a revisión y se avisa a la administración (D-4). Si el proveedor devuelve el dinero cuando no se le responde éxito (Tpago y el simulador) se lo rechaza para que lo devuelva; si no, la devolución queda manual.
- **Comisión y su tratamiento fiscal:** sin lógica; las columnas de comisión y neto del intento quedan vacías (D-1/D-2).
- **Titular del comercio:** obligatorio para activar; cada cobro guarda una copia al crearse.
- **Cobros de prueba:** solo se aplican a la deuda en edificios marcados «de prueba»; en los demás quedan simulados (D-3).

## 7. Cómo apagarlo

Sin tocar la base ni el código: quitá `Payments__CollectionEnabled` (o ponelo en `false`) y reiniciá. Nadie puede abrir cobros nuevos; los que estaban abiertos se cierran solos al vencer y un aviso en tránsito igual se procesa. Para sacar el simulador, quitá `Payments__EnableFakeProvider`: desaparece del catálogo y deja de responder, y las conexiones del simulador ya creadas no se pueden volver a activar.

## 8. Pendiente para cobrar plata real

1. Respuestas de Bancard a las preguntas de la sección 5.5 de la propuesta (en especial: si una plataforma puede integrar en nombre de muchos comercios, si el aviso de Tpago va firmado o cifrado, si el aviso trae `reference_id` y la política de reintentos). Hasta tenerlas, el cruce del aviso con el cobro se hace por `link_alias`.
2. Adaptador de Tpago (`IPaymentProvider`): crear el link, verificar el aviso por IP y TLS, y **consultar el estado del pago** antes de aplicarlo. El contrato todavía no tiene esa consulta; se agrega junto con el adaptador, y con ella el proceso de vencimiento podrá resolver los intentos sin aviso.
3. `ForwardedHeaders` en el proxy para filtrar por las cuatro IP de Bancard.
4. Pantalla de pago en el APP y piloto con **un** cliente.
5. Decisiones fiscales y de comisión con el contador de cada cliente (D-1 y D-2) antes de registrar liquidaciones.
