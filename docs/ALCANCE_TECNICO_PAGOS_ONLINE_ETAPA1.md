# Alcance técnico: pagos online, Etapa 1

> **Actualización 2026-10-20 — implementado** (rama `feature/pagos-online`; cómo desplegar y probar: [GUIA_PRUEBAS_PAGOS_ONLINE_ETAPA1.md](GUIA_PRUEBAS_PAGOS_ONLINE_ETAPA1.md)). Lo que cambió respecto de este documento:
>
> - **D-7 resuelta: NO se crearon las tablas de liquidaciones** (`PaymentSettlements` / `PaymentSettlementItems`) ni `SettlementId` en el intento. La migración es `20261020143213_OnlinePaymentIntents`. Los campos de comisión y neto del intento quedan nulos y sin lógica (D-1/D-2).
> - **Sin índice nuevo para la cuenta en tránsito:** el índice único de `FinancialAccounts` `(BuildingId, Type)` para los tipos que no son banco ya impide una segunda cuenta `Clearing` por edificio.
> - **Transferencias:** desde que existen las transferencias entre cuentas (2026-10-19) la hipótesis H4 está resuelta: el neto pasa de la cuenta en tránsito al banco con una transferencia (`Clearing` solo puede ser origen). No hizo falta un concepto nuevo.
> - **D-3 resuelta:** `BuildingPaymentConfig.IsTest`; un cobro del simulador o de sandbox solo se aplica a la deuda en edificios de prueba, en los demás queda «simulado».
> - **D-4 resuelta:** importe cambiado → `ReviewRequired`; si el proveedor devuelve el dinero cuando no se le responde éxito (Tpago y el simulador) se le responde error.
> - **Saldo a favor:** el cobro online paga el total de los comprobantes y no usa el saldo a favor.
> - **Simulador:** el endpoint del simulador procesa el aviso firmado **en el mismo proceso** por el mismo código que el webhook (no hace una llamada HTTP a sí mismo).
> - **Proceso en segundo plano:** vence los cobros pasado su plazo; todavía no consulta al proveedor (el contrato `IPaymentProvider` no tiene `QueryAsync`; se suma con el adaptador de Tpago).
> - **Actor del sistema:** los borradores de factura de un pago online quedan a nombre de quien activó la conexión.
> - **Pendiente:** adaptador de Tpago, pantalla del propietario en el APP, filtro por IP de Bancard.

Fecha: 2026-10-12. **Documento de análisis para revisión. No se modificó código.** La implementación solo empieza cuando se apruebe este alcance y se resuelvan las decisiones de la sección 9.

Etapa 1 = **cobro con proveedor falso + cuenta en tránsito por edificio**, sin credenciales reales de Bancard ni liquidaciones bancarias. Antecedentes: [PROPUESTA_PAGOS_ONLINE.md](PROPUESTA_PAGOS_ONLINE.md) (diseño general) y [GUIA_PRUEBAS_PAGOS_BANCARD_CONFIG.md](GUIA_PRUEBAS_PAGOS_BANCARD_CONFIG.md) (configuración ya implementada).

---

## 1. Qué incluye y qué no

**Incluye**

1. Crear un cobro online (un edificio, comprobantes completos, del más antiguo al más nuevo) y su intento con el proveedor.
2. Recibir la confirmación del proveedor por un **aviso (webhook) idempotente** y aplicar el pago **una sola vez**, con la misma lógica de hoy (asignación a cargos, saldo a favor, recibo, borrador de factura, aviso al propietario).
3. Una **cuenta en tránsito por edificio** (derecho de cobro frente al proveedor, no dinero disponible) donde entran estos cobros.
4. Un **proveedor falso** que ejercita todo el camino (incluido el aviso firmado) sin dinero ni Bancard.
5. Registro de eventos (auditoría) y pantalla de administración para ver los cobros.
6. **Estructura preparada** para la Etapa 2 (liquidación que agrupa cobros de varios edificios).

**No incluye** (queda para etapas siguientes o pendiente de decisión)

- Adaptador real de Bancard, "Probar conexión", filtro de IP del proveedor.
- Registrar liquidaciones, comisiones y su tratamiento fiscal (**decisión pendiente documentada, sección 9**).
- Reversas, conciliación contra el portal del proveedor, pantalla de la app móvil (otro repositorio; aquí solo se define el contrato de la API).

---

## 2. Diagnóstico: qué existe hoy

| Pieza | Dónde está | Estado |
|---|---|---|
| Liquidación de un pago de propietario | `OwnerPaymentsController.SettlePaymentAsync` (privada, líneas ~1180-1282) | Hace todo el trabajo (crea un `Payment` por comprobante, sus `PaymentAllocation`, consume saldo a favor, valida cierre de mes y alcance). **Está atada al controlador y a la sesión del usuario.** |
| Comprobantes (unidad + período, todo o nada) | `ComprobanteService.LoadAsync` / `Cover` (tolerancia 0,5 Gs) | Reutilizable tal cual. |
| Referencia `PAY-aaaa-n` por empresa | `NextReferenceAsync` + reintento ante colisión | Reutilizable (hay que extraerla junto con la liquidación). |
| Borradores de factura al aprobar | `InvoiceDraftService.CreateDraftsFromOwnerPaymentAsync` | **Depende de `accessScope.CanAccessBuildingAsync`** (ver H2). |
| Aviso "pago aprobado" | `Notification` + `PushDispatcher.NotifyUserAsync` | Reutilizable. Se puede apagar por edificio. |
| Cierre contable de mes | `FinancePeriodGuard` | Reutilizable (recibe empresa y edificio, no usuario). |
| Libro de Finanzas (cuenta y rubro de un cobro) | `FinanceLedgerService` + `FinanceLedgerRules.ForPayment(método, clase, cuentas)` | Todo consumidor pasa por `GetBucketsAsync` y `GetRowsAsync`. Un cobro va a la caja si es efectivo, a la cuenta del fondo si es fondo de reserva y, si no, a la cuenta por defecto. |
| Tipos de cuenta financiera | `FinancialAccountType`: Cash, Bank, ReserveFund (guardado como texto, sin migración de datos al agregar valores) | Se usa en unos 12 archivos (sección 5). |
| Conexiones de pago y su configuración | `PaymentConnection`, `BuildingPaymentConfig`, `PaymentConnectionAudit`, `ISecretProtector` | **Hecho** (configuración). |
| Servicios en segundo plano | `AddHostedService` (mora, recordatorios, plan, marketplace, publicidad) | Patrón existente para el vencimiento de intentos. |
| Limitador de tasa | `GlobalLimiter` por usuario/IP (`MarketplaceRateLimiting`) | Aplica también a una ruta anónima: hay que revisar su partición para el aviso. |
| Pruebas de la liquidación | Solo indirectas (`ConfigCenterPhase2/3`: cierre de mes y mora) | **No hay un conjunto de pruebas propio de aprobar/registrar un pago.** |

---

## 3. Hallazgos que condicionan el diseño

**H1. La liquidación está atada a la sesión.** `SettlePaymentAsync` recibe `companyId` y `scope`, pero el controlador los obtiene de `tenantContext` y de `accessScope`. Un aviso de Bancard llega **sin sesión** (anónimo). Hay que extraer la liquidación a un servicio con un **contexto de ejecución explícito** (empresa, alcance opcional, usuario o "sistema"), de modo que el controlador siga haciendo exactamente lo mismo.

**H2. Los borradores de factura se omitirían en silencio.** `CreateDraftsFromOwnerPaymentAsync` salta cada unidad si `accessScope.CanAccessBuildingAsync` es falso, y en un aviso anónimo siempre lo es. Sin corregirlo, el pago se registraría pero **no se generarían las facturas borrador**. Requiere un parámetro "contexto de sistema" que omita esa verificación.

**H3. Un tipo de cuenta nuevo se confunde con "operativa" en varios lugares.** Hay código que pregunta `Type != ReserveFund` para decir "cuenta de caja o banco": `LedgerAccounts.Resolve` (podría elegir la cuenta en tránsito como cuenta por defecto), `BuildingConfigOverviewService` (contaría la cuenta en tránsito como caja/banco), `FinanceController` (la ofrecería para pagar gastos). Hay que revisarlos uno por uno (sección 5) y cubrirlos con pruebas.

**H4. El libro no tiene movimiento de transferencia entre cuentas.** Se arma solo desde pagos, ingresos, gastos y notas de crédito. La Etapa 1 no lo necesita (el cobro entra directo a la cuenta en tránsito). **La Etapa 2 sí**: pasar el neto de la cuenta en tránsito al banco sin que cuente como ingreso nuevo exige crear ese concepto. Queda anotado como requisito de diseño de la Etapa 2.

**H5. Hoy no se previene el doble pago entre pagos declarados.** Dos pagos pendientes del mismo propietario pueden cubrir los mismos comprobantes; el segundo falla recién al aprobarse. Con pagos online es peor (el dinero ya se movió). Un intento abierto debe **reservar** sus comprobantes y bloquear tanto otro intento como el registro manual o el declarado por la app.

**H6. La mora cambia el importe sola.** Los recargos nacen por intervalo (hasta uno por día) y el proceso corre cada 6 horas. El total del comprobante puede cambiar entre que se crea el cobro y que llega la confirmación. Se resuelve con una **foto del comprobante al crear el cobro** y una **revalidación contra la deuda actual al confirmar** (sección 7). Qué hacer ante una diferencia es la decisión D-4.

**H7. Las pruebas corren sobre SQLite.** No hay secuencias SQL ni bloqueos de fila. El diseño de unicidad e idempotencia se apoya en **índices únicos filtrados** y en **actualizaciones condicionales de una sola instrucción**, que funcionan igual en SQLite y SQL Server (y se pueden probar).

**H8 (para la Etapa 3).** El aviso de Tpago se valida por IP de origen, pero el servicio corre detrás de un proxy y no hay `ForwardedHeaders` configurado. Fuera de esta etapa; queda anotado.

---

## 4. Decisiones de diseño de la Etapa 1

**4.1 La cuenta en tránsito.** Se agrega `FinancialAccountType.Clearing` (texto "Clearing", cabe en los 20 caracteres de la columna). Es una cuenta del edificio, **una por edificio**, creada por el sistema ("Cobros online por liquidar", saldo inicial 0), que no se puede crear, editar ni borrar a mano. Representa **un derecho de cobro frente al proveedor**: sus saldos se muestran en una línea propia ("Cobros online por liquidar") y **no** se suman a caja ni a banco disponibles. No se ofrece en conciliación bancaria (que ya acepta solo `Bank`) ni para pagar gastos.

**4.2 Cómo llega el cobro a esa cuenta.** Se agrega `PaymentMethod.Online` y `FinanceLedgerRules.ForPayment` envía los pagos con ese método a `LedgerAccounts.ClearingId` (si no hay, a la cuenta por defecto, para no perder el cobro). Así los dos consumidores del libro quedan intactos y el rubro sigue saliendo del cargo cubierto. *Alternativa descartada:* una columna `Payment.FinancialAccountId` que reemplaza la cuenta; es más flexible pero obliga a cambiar la agrupación del libro en tres lugares. Si en el futuro hiciera falta más de una cuenta en tránsito por edificio, se cambia entonces. El método `Online` no se acepta en el registro manual (validación explícita).

**4.3 Un solo pago por cobro (tres defensas).** (a) clave de deduplicación única por evento; (b) la transición `Pending → Confirmed` del intento se hace con **una actualización condicional** y solo quien "gana" liquida; (c) índice único filtrado `OwnerPayments.PaymentIntentId`. Un aviso repetido responde éxito y no hace nada. Se responde al proveedor **solo después de confirmar la transacción**.

**4.4 El titular del comercio.** Se agregan a la conexión `HolderName` y `HolderTaxId` (RUC o cédula), **obligatorios para activar**, y una nota opcional de la cuenta receptora. Al crear el cobro se **copian** al intento (foto), para que corregir el dato después no reescriba a nombre de quién se cobró.

**4.5 Qué conserva cada operación.** Edificio, unidad(es) (por ítem), propietario, conexión, titular (foto), referencia externa (`ProviderReference`), importe, estados y hora del proveedor.

**4.6 Proveedor falso y seguridad.**
- Se registra como un proveedor más del catálogo (`Fake`), **visible solo si `Payments:EnableFakeProvider=true`** (variable de entorno, apagada por defecto) y nunca en conexiones de Producción.
- Firma su aviso con la **clave privada de la conexión** (HMAC): así se ejercita el descifrado de secretos y la verificación del aviso por el mismo camino que usará Bancard.
- El simulador de la pantalla **no confirma por atajo**: envía el aviso al webhook real.
- **Regla de seguridad (D-3):** un cobro Sandbox/falso solo se **aplica a la deuda real** en edificios marcados explícitamente como de prueba (`BuildingPaymentConfig.IsTest`). En cualquier otro edificio, la confirmación queda registrada como evento y el cobro como "simulado", sin crear `Payment`. Sin esta regla un simulador en el VPS pagaría deuda real gratis.

**4.7 Estructura para la Etapa 2.** Se crean ahora, **vacías y sin código que las use**, `PaymentSettlements` y `PaymentSettlementItems` (liquidación que agrupa cobros de varios edificios con bruto, comisión, neto y reparto por consorcio). Cada intento ya lleva `CommissionAmount` y `NetAmount` nulos y `SettlementId` nulo. **No hay campo de quién asume la comisión ni de su tratamiento fiscal**: se agregan cuando exista la decisión documentada.

**4.8 Numeración.** `AttemptNumber` global por "máximo + 1" con índice único y reintento (igual que `NextReferenceAsync`), no con secuencia SQL (H7). `PaymentIntent.Number` igual.

---

## 5. Modelo de datos (una migración, solo agrega)

**Tablas nuevas** (todas con `Id`, fechas, borrado lógico y `CompanyId`)

| Tabla | Contenido principal | Índices / reglas |
|---|---|---|
| `PaymentIntents` | `BuildingId`, `OwnerId`, `ConnectionId`, `Number`, `Amount`, `Currency` (PYG), `Status` (Open, Paid, Closed, ReviewRequired, Reversed), `CloseReason`, `ReviewReason`, `ExpiresAtUtc`, `OwnerPaymentId`, `HolderName`, `HolderTaxId`, `IsSimulated` | `Number` único; por propietario y estado. |
| `PaymentIntentItems` | `IntentId`, `UnitId`, `ExpensePeriodId`, `Amount`, `SnapshotJson` (cargos y pendientes), `IsOpen` | **Único filtrado `(UnitId, ExpensePeriodId)` donde `IsOpen`**: un comprobante no puede estar en dos cobros abiertos. `IsOpen` se mantiene en la misma transacción que el estado del intento. |
| `PaymentAttempts` | `IntentId`, `ConnectionId`, `AttemptNumber`, `ProviderReference`, `ProviderSessionId`, `CheckoutUrl`, `ProviderTransactionId`, `AuthorizationCode`, `Status` (Created, Pending, Confirmed, Rejected, Cancelled, Expired, Reversed), `ProviderStatusRaw`, `Amount`, `ExpiresAtUtc`, `ConfirmedAtUtc`, `CardBrand`, `CardLast4`, **reservados:** `CommissionAmount?`, `NetAmount?`, `SettlementId?` | `AttemptNumber` único; `ProviderReference` único por conexión. |
| `PaymentEvents` | `ConnectionId`, `AttemptId?`, `Direction`, `Kind`, `DedupeKey`, `SignatureOk`, `Ip`, `PayloadJson` (redactado), `Outcome`, `Error`, `ReceivedAtUtc`, `ProcessedAtUtc` | **`DedupeKey` único filtrado** cuando existe. Solo inserción. |
| `PaymentSettlements` *(vacía)* | `ConnectionId`, `Reference`, `DepositDate`, `GrossTotal`, `CommissionTotal`, `NetTotal`, `Status`, `Notes` | Sin uso en Etapa 1. |
| `PaymentSettlementItems` *(vacía)* | `SettlementId`, `AttemptId`, `BuildingId`, `GrossAmount`, `CommissionAmount`, `NetAmount` | `AttemptId` único (un cobro se liquida una vez). |

**Columnas nuevas en tablas existentes**

| Tabla | Columna |
|---|---|
| `OwnerPayments` | `PaymentIntentId` (nulo) con **índice único filtrado**. |
| `PaymentConnections` | `HolderName`, `HolderTaxId`, `HolderBankNote` (obligatorios los dos primeros para activar). |
| `BuildingPaymentConfigs` | `IsTest` (falso por defecto). |
| `FinancialAccounts` | Índice único filtrado `(BuildingId)` donde el tipo es `Clearing`: una por edificio. |

**Valores nuevos de enumeraciones** (guardadas como texto; no hay migración de datos): `PaymentMethod.Online`, `OwnerPaymentChannel.Online` (la columna admite 10 caracteres: "Online" cabe), `FinancialAccountType.Clearing`.

---

## 6. Cambios en lo que ya existe

| Archivo | Cambio |
|---|---|
| `OwnerPaymentsController` | Extraer `SettlePaymentAsync` y `NextReferenceAsync` a un servicio; **el controlador queda con el mismo comportamiento**. Rechazar `Register` y `Create` si el propietario tiene un cobro online abierto. `Reverse` no acepta el canal Online. |
| `InvoiceDraftService` | Parámetro para omitir la verificación de alcance cuando lo llama el sistema (H2). |
| `FinanceLedger.cs` | `LedgerAccounts.ClearingId`; `Resolve` excluye `Clearing` de la cuenta por defecto; `ForPayment` envía `Online` a la cuenta en tránsito. |
| `FinanceAccountsController` | Etiqueta del tipo; la cuenta en tránsito no se crea, edita ni borra a mano; una por edificio. |
| `FinanceController` | Excluir `Clearing` de las cuentas desde las que se paga un gasto (líneas ~275 y ~382). |
| `BuildingConfigOverviewService` | "Falta caja o banco" no cuenta `Clearing` (línea ~267). |
| `FinanceReportBuilder` / DTOs | Línea propia "Cobros online por liquidar" en saldos y tablero; no suma a caja ni banco (líneas ~56-58). |
| `AccountingEntriesService` | `Clearing` se mapea a una cuenta de activo del plan por `LedgerAccountRole` (el plan de cuentas del cliente es importable: no se puede suponer que exista una cuenta con cierto nombre). Sin mapeo, el asiento sale con aviso, como hoy con los bancos. |
| `FinanceExcelExporter`, informe de asamblea, recibos PDF | Etiquetas ("Cobros online por liquidar", "Pago online"). |
| `PaymentConnectionsController` | Titular obligatorio para activar; proveedor `Fake` bajo interruptor; al asignar un edificio se asegura su cuenta en tránsito (si el módulo Finanzas del edificio está disponible). |
| `Program.cs` | Registrar los servicios nuevos y el proceso en segundo plano. |
| Web | Etiquetas de método y canal, tipo de cuenta `Clearing` en el editor de cuentas (solo lectura), y las pantallas nuevas (sección 7.4). |

---

## 7. Código nuevo

**7.1 Servicios**

- `OwnerPaymentSettlementService`: la liquidación extraída, con `SettlementContext` (empresa, alcance opcional, usuario o sistema). Es la **única** que crea pagos, para la aprobación manual, el registro del personal y el cobro online.
- `OnlinePaymentService` (orquestador, no conoce a Bancard): crea el cobro, procesa eventos ya normalizados, vence y resuelve pendientes.
- `IPaymentProvider` (en `Condo.Application`): crear sesión, **verificar y normalizar un aviso**, consultar estado y declarar capacidades. Implementación `FakePaymentProvider`.
- `PaymentIntentMaintenanceService` (proceso en segundo plano, cada minuto): toma los intentos pendientes vencidos, consulta al proveedor y los resuelve o los marca vencidos.

**7.2 Endpoints**

| Ruta | Quién | Para qué |
|---|---|---|
| `GET api/online-payments/options` | Propietario | Por edificio: conexiones activas, titular y las opciones de pago ("hasta este mes") sobre sus comprobantes. |
| `POST api/online-payments` | Propietario | Crea cobro e intento; devuelve número, importe, titular, vencimiento y cómo pagar (redirección o sesión). |
| `GET api/online-payments/{id}` | Propietario | Estado para la consulta periódica de la app. |
| `POST api/payment-webhooks/{publicId}` | Anónimo (proveedor) | Aviso. Sin CORS, con su propio límite, siempre registra el evento. |
| `GET api/online-payments/admin` (+ `{id}`) | Personal con alcance | Lista y detalle con eventos; solo SuperAdmin y Administrador ven eventos crudos. |
| `POST api/payment-fake/{attemptNumber}/confirm` / `reject` | SuperAdmin y Administrador, solo con el proveedor falso encendido | Simulador: arma el aviso firmado y lo envía al webhook real. |

**7.3 La app móvil** vive en otro repositorio: aquí solo queda el contrato de los tres primeros endpoints. Con Tpago el proveedor no devuelve al usuario a la app, por lo que la consulta periódica y la notificación son parte del recorrido.

**7.4 Web (administración):** pantalla "Cobros online" (lista con estado, edificio, importe, referencia, titular y marca de simulado; detalle con eventos) y el simulador para SuperAdmin cuando el proveedor falso está encendido.

---

## 8. Flujo, estados y casos límite

**Crear el cobro.** Validar propietario y alcance → exigir **un solo edificio** → cargar los comprobantes con `ComprobanteService` y cubrir "hasta" el elegido (importe = suma, redondeado al guaraní, dentro de la tolerancia de 0,5) → exigir conexión activa del edificio, sin otro cobro abierto ni pago declarado pendiente → crear en **una transacción** el intento (`Open`), sus ítems con foto (`IsOpen`), el intento del proveedor (`Created` → `Pending`) y el evento → devolver cómo pagar.

**Procesar un aviso.** Guardar el evento (idempotente por `DedupeKey`) → verificar la firma y que el importe y la moneda coincidan con el intento → **ganar la transición** `Pending → Confirmed` → en una transacción: revalidar los comprobantes contra la deuda actual (misma cobertura exacta, cierre de mes, edificio) → liquidar con el servicio único (`OwnerPayment` aprobado con canal Online y método Online, `Payment` por comprobante, asignaciones, saldo a favor, recibo) → intento `Paid`, ítems `IsOpen=false` → confirmar la transacción → **recién entonces** responder éxito. Después: aviso al propietario y borradores de factura.

**Casos límite**

| Caso | Resultado |
|---|---|
| Mismo aviso dos veces | Se responde éxito; no se crea nada nuevo (evento duplicado, transición ya ganada, índice único de `PaymentIntentId`). |
| Dos avisos distintos del mismo intento a la vez | Uno gana; el otro responde éxito sin hacer nada. |
| Firma inválida, importe o moneda distintos | Evento rechazado y registrado; el intento no cambia. |
| El importe cambió por mora o por un pago del personal | **`ReviewRequired`** con motivo y aviso a la administración; no se aplica. *(Política por defecto; la confirma D-4.)* |
| El comprobante ya estaba pagado | `ReviewRequired`. |
| El mes está cerrado contablemente | `ReviewRequired`; el evento queda registrado. |
| Aviso después de vencido el intento | Si todo coincide todavía y los comprobantes siguen libres, se aplica; si no, `ReviewRequired`. |
| Rechazo del proveedor o vencimiento | Intento `Rejected`/`Expired`, intento cerrado, comprobantes liberados. Un reintento es un intento nuevo pedido por el usuario. |
| Falla técnica al aplicar | Se revierte la transacción, se responde error al proveedor (puede reintentar) y el evento queda con su error. |
| Edificio sin Finanzas habilitado | El pago se aplica igual (sin libro); no se crea cuenta en tránsito. |
| Edificio de prueba vs. real con proveedor falso | Ver 4.6: en uno real el cobro queda simulado, sin pago. |

---

## 9. Decisiones pendientes y qué queda fijado

**Pendientes de decisión documentada (no se programan reglas hasta tenerlas)**

| # | Tema | Estado en esta etapa |
|---|---|---|
| D-1 | Quién asume la comisión (consorcio / administradora / propietario) y su registro | **No se fija.** Los campos de comisión son nulos y no hay lógica. Decide el contador de cada cliente. |
| D-2 | Tratamiento fiscal de la comisión (¿IVA crédito?) | **No se fija.** |
| D-3 | Dónde y cómo se prueba a mano | Propuesta: proveedor falso solo en entornos de prueba o en un edificio marcado `IsTest`. |
| D-4 | Qué hacer si el importe cambia entre cobro y confirmación | Por defecto `ReviewRequired` (no se aplica y se avisa). Alternativas: acreditar la diferencia como saldo a favor, o frenar la mora mientras hay un cobro abierto. |
| D-5 | Redondeo | Se redondea al guaraní entero dentro de la tolerancia de 0,5 que ya usan los comprobantes. |
| D-6 | Titularidad del comercio y de la cuenta | Se **muestra** (dato obligatorio de la conexión); la resolución legal es del cliente. |
| D-7 | Estructura de la Etapa 2 | Tablas vacías creadas ahora (4.7). Se pueden crear recién en la Etapa 2 si se prefiere una migración más chica. |

**Fijado por este alcance:** el cobro y la liquidación bancaria son hechos distintos; la cuenta en tránsito es un derecho de cobro, no dinero disponible; un cobro es de un solo edificio; un pago se confirma una sola vez; el diseño soporta un comercio por consorcio y un comercio compartido por varios edificios sin mezclar contabilidades (el cobro y el titular quedan por edificio).

---

## 10. Pruebas automatizadas previstas

**A. Antes de tocar nada (caracterización de lo que ya existe)**
- Aprobar y registrar un pago: cantidad de `Payment`, asignaciones del más antiguo al más nuevo, saldo a favor, referencia `PAY-aaaa-n`, notificación y borradores de factura. Pago que no cubre comprobantes completos, fuera de alcance, en mes cerrado, concurrencia de referencias.
- Libro: un cobro por método y clase de cargo va a la cuenta y al rubro esperados (caja, fondo, por defecto).

**B. Extracción de la liquidación**
- Los resultados de A son idénticos antes y después.
- Liquidar con contexto de sistema (sin sesión ni alcance): crea el pago, **y los borradores de factura sí se generan** (H2).

**C. Cuenta en tránsito**
- `Online` entra a `Clearing`; sin cuenta en tránsito, a la por defecto.
- `Clearing` nunca es la cuenta por defecto, no cuenta como caja o banco, no se ofrece en conciliación ni para pagar gastos, no se crea ni edita a mano, es única por edificio.
- Saldos: aparece como "por liquidar" y no suma a disponible; el flujo de caja y el informe de asamblea no se rompen.
- Asientos sugeridos: con y sin mapeo de plan de cuentas.

**D. Cobro y aviso**
- Crear: un solo edificio; comprobantes completos del más antiguo; rechazo si hay cobro abierto, pago declarado pendiente o conexión inactiva; un comprobante no entra en dos cobros abiertos (índice).
- **Idempotencia:** aviso repetido, avisos simultáneos, evento duplicado: un solo `OwnerPayment` y un solo conjunto de `Payment` (incluye caso con SQL Server real cuando esté disponible).
- Verificación: firma inválida, importe o moneda distintos, conexión equivocada.
- Revalidación: importe cambiado por mora, comprobante ya pagado, mes cerrado → `ReviewRequired` sin crear pagos.
- Vencimiento: el proceso en segundo plano vence intentos y libera comprobantes; aviso tardío.
- Reintento después de un rechazo.
- Edificio real vs. `IsTest`: el cobro falso/sandbox no crea pago en uno real.
- Cada operación conserva edificio, unidad, titular (foto) y referencia; cambiar el titular después no altera cobros hechos.
- Permisos y alcance: el propietario solo ve y crea los suyos; el personal solo los de su alcance; eventos crudos solo para SuperAdmin y Administrador; webhook sin datos de otra empresa.

**E. Compatibilidad**
- Todas las pruebas actuales siguen pasando; la configuración de pagos (24 pruebas) no cambia.
- La migración solo agrega: prueba de que el modelo coincide con la migración y de que el `.sql` es idempotente.

---

## 11. Riesgos y orden de trabajo

**Riesgos principales:** la extracción de la liquidación (mitigado con A antes y B después), el efecto del nuevo tipo de cuenta en reportes (H3, prueba C), y el proveedor falso en producción (4.6, D-3).

**Orden sugerido, con un commit por paso y la suite completa verde entre pasos**

1. Pruebas de caracterización (A) sin cambiar código.
2. Extraer el servicio de liquidación y corregir H2 (B).
3. Tipo de cuenta `Clearing`, método y canal `Online`, y los ajustes de reportes (C).
4. Migración (tablas nuevas y columnas), con su `.sql`.
5. Orquestador, proveedor falso y webhook (D).
6. Proceso en segundo plano de vencimiento.
7. Web: pantalla de cobros online, simulador y etiquetas.
8. Guía de pruebas y de despliegue.

**Despliegue:** SQL primero, luego backend, luego web (como siempre). El cobro queda apagado (`CollectionEnabled=false` en la configuración) hasta que se habilite, y el proveedor falso apagado salvo que se encienda a propósito. Se puede revertir apagando esos interruptores sin tocar la base.

**Lo que este documento no pudo verificar:** la app móvil (otro repositorio), la documentación de vPOS 2.0 (requiere cuenta en el portal de Bancard) y si Bancard ofrece un reporte de liquidaciones por API. Ninguno bloquea la Etapa 1.
