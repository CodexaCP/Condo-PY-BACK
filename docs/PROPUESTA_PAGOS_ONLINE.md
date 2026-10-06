# Propuesta técnica: pagos online de expensas en Condo-PY

Fecha del análisis: 2026-10-05. Documento de análisis y diseño. **No hay código ni migraciones asociadas.**

Convención de etiquetas en todo el documento:

- **[Confirmado]** lo publica el proveedor en una fuente oficial (se cita el enlace).
- **[Recomendación]** criterio de arquitectura/seguridad de Condo-PY, no lo exige el proveedor.
- **[A confirmar]** no pude verificarlo en una fuente pública oficial; hay que preguntarlo al proveedor.

---

## 0. Resumen ejecutivo

1. El objetivo "el dinero no pasa por Condo-PY" es alcanzable con Bancard (Tpago o vPOS) **siempre que cada cliente (administración o consorcio) tenga su propio comercio y sus propias credenciales**. Condo-PY solo guarda credenciales y registra resultados.
2. Lo más valioso que ya tenés es el concepto de **comprobante** (unidad + periodo, todo o nada, del más antiguo al más nuevo) y la **liquidación de un pago** (`SettlePaymentAsync`). El pago online debe terminar llamando a esa misma lógica. Hoy esa lógica está **privada dentro de `OwnerPaymentsController`**: extraerla a un servicio es el prerrequisito número uno.
3. La configuración de pagos debe colgar de la **empresa (administración)** y asignarse a **uno o varios edificios**. Un edificio con comercio propio es simplemente una conexión asignada a un solo edificio.
4. Separar tres cosas: **conexión** (credenciales del comercio), **intento de cobro** (lo que se le pide al proveedor) y **pago aplicado** (`OwnerPayment`/`Payment`, que ya existe).
5. Los dos mayores riesgos no son técnicos: (a) **a nombre de quién está el comercio** y (b) **qué hacer cuando el importe cambia entre que el propietario abre el pago y el proveedor confirma** (la mora genera un recargo nuevo por cada intervalo configurado, p. ej. uno por día, sin intervención de nadie).
6. Recomendación: preparar ahora la base agnóstica (sin una línea de Bancard), y programar el primer proveedor recién cuando Bancard conteste las preguntas de la sección 5.

---

## 1. Qué confirma Bancard (fuentes oficiales)

### 1.1 Tpago (link de pago + API)

Fuentes: [bancard.com.py/tpago](https://www.bancard.com.py/tpago), [comercios.bancard.com.py/productos/payment-link](https://comercios.bancard.com.py/productos/payment-link), [tpagodocs.bancard.com.py](https://tpagodocs.bancard.com.py/) (API Reference v3.16.0, fecha 11-06-2026, y su colección Postman v1.2.0 publicada en el mismo sitio). La documentación bloquea descargas automáticas (403); la leí abriéndola en un navegador.

**[Confirmado]**

- Tpago genera **links de pago**; ofrece "Integración API Tpago" para conectar el sistema administrativo del comercio.
- Medios: tarjetas de crédito, débito, cuotas y QR. Acreditación: débito al instante, crédito en 48 h (página de Tpago).
- API REST, **solo guaraníes** (monto entero). Base producción `https://comercios.bancard.com.py`, sandbox `https://comercios.bancard.com.py:8888` con **claves distintas** a producción.
- Crear link: `POST /external-commerce/api/0.1/commerces/{commerce_code}/branches/{commerce_branch_code}/links/generate-payment-link` con `amount`, `description`, `reference_id` (opcional, "para vincular con tu sistema") y `require_user_data`. Responde `link_url` (lo que se comparte), `link_alias`, `expiration_datetime`, `commerce_id`, `commerce_branch_id`.
- **Los parámetros documentados no incluyen URL de retorno** ni expiración configurable: el usuario paga en la página de Tpago y el sistema se entera por el callback.
- Autenticación: **Basic** con `clave_pública:clave_privada` en base64, claves administradas en el portal Tpago; HTTPS obligatorio; "no compartir las claves en repositorios ni en código del cliente".
- **Callback**: POST con `payment.{link_alias, status, response_code, amount, currency, ticket_number, authorization_code, date_time, commerce_name, branch_name, payer{...}, card_last_numbers, account_type, bin, ...}`. URL configurable en el portal Tpago, **TLS 1.2**, **puerto 443 obligatorio en producción**, lista de **4 IPs** de Tpago para filtrar.
- El comercio debe responder JSON `{"status":"success"}`. **Si no responde eso, hay excepción o timeout, Tpago revierte el pago y devuelve el dinero al usuario.**
- El `reference_id` se devuelve en el callback solo si se lo **pide como parámetro opcional** (endpoints `callback-keys`; nota de versión 3.8.0).
- Estados oficiales: 0 Confirmed, 1 Failed, 2 Pending, 3 Reversed, 4 Reverse pending, 5 Reverse failed (7 y 8 en desuso).
- Reversa: `PUT .../links/payments/revert/{payment_hook_alias}`, **solo el mismo día** del pago y solo si está aprobado.
- Consulta: pagos por `link_alias` (`get_payments_by_link_alias`, solo para links creados por API), históricos y reportes de suscripciones.
- Hay **suscripciones** por API (`generate-subscription-link`: día de débito, periodicidad, pausar, reintentar pagos fallidos, pago adicional) y migración "DBA" por CSV. Es la vía de **débito automático con tarjeta**.
- Bloqueo anti-fraude: 7 rechazos de la misma tarjeta en 24 h o 35 en 30 días la bloquea 30 días en ese comercio.
- Requisitos de adhesión: RUC activo, cédula, cuenta bancaria; para S.A./S.R.L. constitución y acta autenticadas, poderes, cédulas de firmantes y **cuenta a nombre de la sociedad**.
- Precio publicado: "Gs 22.900 (IVA incl.)" **sin aclarar si es mensual, único o por alta**. Comisión por transacción: no la vi publicada.

**[A confirmar con Bancard]**: si el callback va firmado (la documentación solo describe filtro por IP y TLS), si hay callback por reversas, política de reintentos del callback, timeout exacto, si un comercio con varias sucursales (`commerce_branch_code`) sirve para modelar un edificio por sucursal, y si las credenciales son por comercio o por sucursal.

Observación: el ejemplo de callback trae `bin` de 12 dígitos. [Recomendación] No persistir el BIN completo (ver sección 3.5).

### 1.2 vPOS 2.0

Fuentes: [bancard.com.py/vpos](https://www.bancard.com.py/vpos), guía oficial "Integración con eCommerce Bancard – Compra Simple v0.3.1" (copia alojada en [afd.gov.py](https://www.afd.gov.py/userfiles/files/transparencia/ecommerce-bancard-compra-simple-version-0-3-1.pdf); **es una versión vieja**), SDK oficial [Bancard/bancard-connectors](https://github.com/Bancard/bancard-connectors) y [Bancard/bancard-checkout-js](https://github.com/Bancard/bancard-checkout-js). La documentación vigente de vPOS 2.0 está en el portal de comercios (requiere cuenta): **no pude leerla**.

**[Confirmado]**

- vPOS 2.0 = "botón de pago" para app/web: **iframe embebido** con colores/logo personalizables; pagos ocasionales y con tarjeta registrada; crédito, débito, Zimple, cuotas, preautorización, 3D Secure opcional; cumple PCI DSS. Precio publicado "Gs 74.900 IVA incl." (período no aclarado).
- Flujo (SDK actual y guía 0.3.1): el comercio llama `single_buy` con un `shop_process_id` propio (entero), recibe un `process_id`, muestra el formulario con `Bancard.Checkout.createForm()` y **vPOS llama a la URL de confirmación del comercio** (`single_buy_confirm`). Esa llamada "es el único medio por el cual el portal tiene certeza del pago"; la página de retorno **no** prueba nada.
- Autenticación: clave pública/privada; **la privada nunca viaja, se manda como token MD5**: `md5(privada + shop_process_id + monto + moneda)`; confirmación: `md5(privada + shop_process_id + "confirm" + monto + moneda)`; consulta: `... + "get_confirmation"`; rollback: `... + "rollback" + "0.00"`. Monto con dos decimales.
- Entornos: producción `https://vpos.infonet.com.py`, staging `:8888`. Claves, URL de confirmación y trazas se administran en el portal de comercios.
- El comercio debe responder 200 en 60 s. Si inició el pago y **no recibe confirmación en ~10 min**, debe consultar `get_confirmation` y, si no se pagó, hacer `rollback`.
- Rollback solo antes de que la operación esté "cuponada"; después, reversa manual por el área comercial de Bancard.
- **Monitoreo**: Bancard hace un POST con JSON vacío a la URL de confirmación **cada 5 minutos**. El endpoint debe tolerarlo sin error.
- Para habilitar la integración hay que completar un **checklist de pruebas** en el portal (single_buy, confirmación, consulta, rollback).
- Reglas de UI: mostrar fecha/hora, nº de pedido, importe, descripción de respuesta y código de autorización; **no mostrar** `response_code`, respuesta extendida ni información de seguridad. Debe haber sección de contacto. **Prohibido guardar datos de tarjeta.**

**[A confirmar]**: versión vigente de la API de vPOS 2.0, IPs de origen de la confirmación, política de reintentos, si la confirmación trae la clave pública (para identificar al comercio), y si el iframe funciona dentro del WebView de Capacitor de tu app.

### 1.3 Otras modalidades de Bancard relevantes (solo en el menú oficial)

- **Débito Automático**: cobros recurrentes con tarjeta de crédito ([página](https://www.bancard.com.py/debito-automatico)). En la API de Tpago esto son las suscripciones.
- **Infonet Cobranzas**: red de bocas de pago de servicios ("ofrecé pago de servicios"). Modelo distinto (consulta de deuda + notificación de pago). **[A confirmar]** si existe para administradoras y qué integración técnica tiene.
- **QR**, **POS**, **Orden Telefónica**, **Pix**: son de venta en local o fuera de alcance para este caso.

---

## 2. Comparativa de proveedores (los que tienen sentido para Condo-PY)

| | Bancard Tpago | Bancard vPOS 2.0 | Pagopar (hoy uPay, ueno) | Dinelco (Bepsa) | Transferencia / QR SIP (BCP) |
|---|---|---|---|---|---|
| Modalidad | Link de pago (redirección) | Iframe embebido | Checkout redirigido + links | Checkout, Link, QR | Transferencia instantánea / QR interoperable |
| Pagos online | Sí | Sí | Sí | Sí (Checkout) | Sí, pero sin API de cobro pública |
| API | Sí, REST ([docs](https://tpagodocs.bancard.com.py/)) | Sí, REST (guía en el portal) | Sí, REST ([docs](https://soporte.pagopar.com/portal/es/kb/articles/version-ingles-api-integraci%C3%B3n-de-medios-de-pagos)) | **No encontré documentación pública** | No aplica |
| Callback/webhook | Sí; sin firma documentada; IP + TLS 1.2 + 443 | Sí; token MD5 con clave privada | Sí; token `sha1(privada + hash_pedido)`; reintenta cada 10 min hasta recibir 200 | [A confirmar] | No |
| Referencia propia | `reference_id` (opcional, hay que registrarlo en el callback); `link_alias` siempre | `shop_process_id` (entero propio) | `id_pedido_comercio` (alfanumérico, único entre desarrollo y producción) | [A confirmar] | Texto libre del banco (débil) |
| Dinero directo al comercio | Sí: liquida Bancard al comercio (débito inmediato, crédito 48 h) | Igual | Se acredita en la cuenta del comercio (ueno u otro banco/billetera) tras la liquidación de uPay; **uPay actúa de intermediario** | [A confirmar] | Sí (va a la cuenta) |
| Medios | Tarjetas, cuotas, QR | Tarjetas, Zimple, cuotas | Tarjetas, QR, billeteras, transferencia, bocas de cobranza (Aquí Pago, Pago Express, Wepa), Pix | Tarjetas (Visa, Mastercard, Cabal), billeteras, QR | Transferencia/QR |
| Costos publicados | Gs 22.900 IVA incl. (período no aclarado); comisión no publicada | Gs 74.900 IVA incl. (período no aclarado); comisión no publicada | [uPay](https://upay.com.py/pagopar/): débito 2 %, crédito local 2,9 %, crédito internacional 3 %, QR 2 %; liquidación 24/48 h hábiles. Fuentes de terceros más viejas hablaban de 5,5–7 % + cuota mensual | No publicados | Sin costo de pasarela (según banco) |
| Requisitos | RUC, cuenta bancaria; sociedades con documentos autenticados y cuenta a nombre de la sociedad | Igual | [A confirmar] | [A confirmar] | Cuenta bancaria |
| Multiempresa SaaS | Credenciales por comercio; checklist de pruebas por comercio en vPOS | Igual | Claves por comercio | [A confirmar] | Reconciliación manual |

Notas de fuentes: Dinelco y BCP vienen de [su sitio](https://www.dinelco.com.py), notas de prensa ([Infonegocios](https://infonegocios.com.py/plus/dinelco-lo-hace-diferente-estreno-link-y-pasarela-de-pago-pagos-qr-y-portal-de-comercios)) y del [BCP](https://www.bcp.gov.py/web/institucional/w/bcp-actualiza-el-reglamento-del-sipap-y-eleva-el-limite-de-las-transferencias-instantaneas-a-g-10-millones) (transferencias instantáneas hasta Gs 10 millones desde marzo 2026; QR interoperable anunciado por el BCP para 2026). Son datos secundarios; confirmar antes de decidir.

**Lectura para Condo-PY**

- Bancard es el primer proveedor lógico (el que más vas a encontrar en tus clientes). Para expensas, un **link/redirección alcanza**; el iframe de vPOS aporta tarjetas guardadas y preautorización que no necesitás todavía.
- Pagopar sirve cuando un cliente quiere **bocas de cobranza y billeteras** sin contratar cada una. Pero Pagopar es un agregador: el dinero pasa por él antes de llegar al comercio. Sigue sin pasar por Condo-PY.
- Transferencia y QR no tienen confirmación automática pública: se mantienen en el flujo manual actual (declarar + revisar).
- **No** recomiendo que Condo-PY use una cuenta propia de pasarela para cobrar por sus clientes: contradice tu objetivo y trae implicancias legales/tributarias.

---

## 3. Diseño propuesto

### 3.1 Jerarquía de credenciales: empresa, no edificio

Tu modelo ya es `Company → Condominium (opcional) → Building → Unit`, y casi todo lleva `CompanyId`. **[Recomendación]**:

- La **conexión de pago pertenece a la empresa**, y se **asigna** a uno o varios edificios mediante una tabla de asignación.
- Cubre los dos casos: una administración con un único comercio para todos sus edificios (una conexión, N asignaciones) y un consorcio con comercio propio (una conexión, 1 asignación).
- Evita pedir credenciales al crear el edificio: sin filas de configuración el estado es "No configurado".
- Regla de integridad: la conexión y el edificio deben ser de la misma empresa.
- Regla de negocio clave: **un cobro online = un solo edificio** (un solo comercio). Hoy `OwnerPayment` admite unidades de varios edificios; con pago online, si un propietario tiene unidades en dos edificios con comercios distintos, paga en dos operaciones.
- Quién configura: **CompanyAdmin** (la administración es la titular del comercio) y SuperAdmin como soporte. Encargado/Operador solo ven el estado. Un interruptor por edificio operado por SuperAdmin (como `MarketplaceEnabled`) permite cobrar el módulo como parte de un plan, si querés.

### 3.2 Identificación del pago (referencias)

**[Recomendación]** cuatro identificadores con funciones distintas:

| Identificador | Quién lo genera | Para qué | Se manda al proveedor |
|---|---|---|---|
| `PaymentIntent.Id` (Guid) | Condo-PY | Clave interna | No |
| `PaymentIntent.Number` (secuencia numérica global) | Condo-PY | Código legible de soporte ("CP-000123") | No |
| `PaymentAttempt.ProviderReference` (número de intento de una **secuencia global única**) | Condo-PY | `shop_process_id` de vPOS, `reference_id` de Tpago, `id_pedido_comercio` de Pagopar | **Sí** |
| ID del proveedor (`link_alias`/`process_id`, `ticket_number`, `authorization_code`, `hook_alias`) | Proveedor | Auditoría y conciliación | Lo recibimos |

Por qué una secuencia numérica global y no el `PAY-aaaa-n` actual:

- vPOS pide `shop_process_id` entero y Pagopar exige unicidad también entre desarrollo y producción; una secuencia global lo cumple siempre.
- `PAY-aaaa-n` es **por empresa** y se asigna al aprobar; sigue siendo la referencia del recibo (la generás al confirmar, como hoy).
- **No** codificar edificio, unidad, nombre ni monto dentro de la referencia: viaja a terceros y es adivinable. Se resuelve por búsqueda en base de datos.
- La descripción que ve el pagador es texto aparte (vPOS limita a 100 caracteres): "Expensas 09/2026 - Edificio X - Unidad 301".

Qué se congela en el momento de crear el cobro (**snapshot**, igual que `Invoice.DetalleSnapshotJson`): edificio, propietario, importe total, y por cada comprobante unidad + periodo + importe + líneas (cargos y pendientes). Al confirmarse, la auditoría puede responder "qué edificio, quién, qué comprobante, qué liquidación, qué importe, qué transacción".

### 3.3 Estados

Se separan **tres dimensiones**: el negocio (intención), el proveedor (intento) y la conciliación (flag aparte). No hacen falta "Iniciado" ni "En proceso": ningún proveedor informa si el usuario abrió la página; es estado interno y no aporta.

**PaymentIntent (lo que Condo-PY quiere cobrar)**

| Estado | Significado |
|---|---|
| `Open` | Hay un intento vigente esperando pago |
| `Paid` | Confirmado y aplicado (existe `OwnerPayment` aprobado) |
| `Closed` | Terminó sin pago (motivo: rechazado, cancelado o vencido) |
| `ReviewRequired` | El proveedor cobró pero no se pudo aplicar (importe cambió, comprobante ya pagado, etc.) |
| `Reversed` | Estuvo pagado y se revirtió |

**PaymentAttempt (cada sesión con el proveedor)**

`Created` → `Pending` → `Confirmed` | `Rejected` | `Cancelled` | `Expired` → (`Reversed`). Mapeo a Tpago: Confirmed=0, Failed=1→Rejected, Pending=2, Reversed=3. `Reverse pending`/`Reverse failed` (4 y 5) se agregan **solo cuando implementes la reversa desde Condo-PY**.

**Conciliación** no es un estado de pago: es `ReconciledAtUtc/By` en el intento ("pendiente de conciliación" = pagado y sin marcar). Mezclarlo con el estado del pago obliga a duplicar estados.

"Error" no es un estado: un fallo técnico es un **evento** en el registro (y se reintenta). Si el proveedor no pudo crear la sesión, el intento queda `Rejected` con motivo.

### 3.4 Qué se guarda y dónde

| Concepto | Dónde vive |
|---|---|
| Configuración del proveedor | `PaymentConnection` (empresa) y `BuildingPaymentConfig` (asignación) |
| Transacción de un pago | `PaymentIntent` + `PaymentIntentItem` + `PaymentAttempt` + `PaymentEvent` |
| Comprobante/deuda de expensa | **No cambia**: se calcula de `ExpenseCharge` − `PaymentAllocation`. El pago online termina creando `OwnerPayment` + `Payment` + `PaymentAllocation` por el mismo camino que hoy |

### 3.5 Seguridad

**Requisitos confirmados por el proveedor**

- HTTPS obligatorio; claves nunca en código cliente ni repositorios (Tpago).
- Callback: TLS 1.2, puerto 443 en producción, filtro por IPs de Tpago (Tpago).
- Responder `{"status":"success"}` solo si el pago quedó registrado; de lo contrario Tpago revierte (Tpago). vPOS: responder 200 en 60 s.
- Token MD5 con clave privada que no viaja (vPOS).
- La página de resultado no debe mostrar códigos internos de respuesta; el comercio no debe guardar datos de tarjeta (vPOS).
- Sandbox y producción con claves y URLs separadas (ambos).
- Reversa de Tpago solo el mismo día; rollback de vPOS solo antes del cupón.

**Recomendaciones de Condo-PY**

1. **Secretos cifrados en la base** (AES-GCM) con una clave maestra **fuera de la base** (variable de entorno del servicio systemd, no en `appsettings.json` ni en el repo), con `KeyId` para poder rotar. Hoy los secretos (JWT, Resend) están en texto plano en configuración y el único AES-GCM existente es el token de plantilla de gastos: no hay infraestructura de secretos que reutilizar.
2. **Campos de solo escritura**: la API nunca devuelve una clave; el frontend ve "••••1234" y "configurada el dd/mm". Reemplazar = cargar de nuevo.
3. **El frontend nunca calcula importes, referencias ni habla con el proveedor.** Pide "pagar estos comprobantes"; el backend recalcula el total y devuelve una URL o un token de sesión.
4. **El retorno del navegador no prueba el pago.** Solo cuenta el callback verificado (o la consulta servidor a servidor).
5. **Verificación del callback por capas.** vPOS: validar token MD5 (comparación en tiempo constante) y monto/moneda. Tpago (sin firma documentada): filtro de IP (cuidado: detrás del proxy hay que configurar `ForwardedHeaders` con proxies conocidos, igual que pasó con el limitador de tasa) y **confirmar consultando al proveedor** antes de aplicar el pago. Siempre: comparar monto y moneda contra el intento congelado.
6. **URL de callback por conexión con identificador aleatorio** (`.../payment-webhooks/{publicId}`): identifica qué credenciales usar, porque el callback de Tpago no trae el código de comercio. Ruta anónima, fuera del límite de tasa por usuario y sin CORS.
7. **Idempotencia**: clave de deduplicación única por evento (proveedor + ID de transacción + estado); transición de estado monótona (un estado terminal no retrocede, salvo `Confirmed → Reversed`); procesar dentro de **una transacción de base** con bloqueo del intento (ya usás `RowVersion`); **responder éxito solo después del commit**; los duplicados se responden OK sin reaplicar.
8. **Prevención de doble pago**: índice único filtrado que impide dos intentos abiertos sobre el mismo comprobante; el registro manual del personal debe rechazar si hay un cobro online abierto del propietario (ya lo hacés con los pagos pendientes de la app); al aplicar, se **revalida la cobertura exacta** dentro de la transacción.
9. **Reintentos**: nunca reintentar automáticamente un pago rechazado (el bloqueo por tarjeta de Tpago penaliza). Un reintento es un intento nuevo, pedido por el usuario, y solo cuando el anterior es terminal.
10. **Registro de auditoría de solo inserción** (`PaymentEvent`), con el patrón de `MarketplaceEvent`: quién, cuándo, IP, estado anterior y posterior, importes; payload crudo con datos de tarjeta **redactados**. No persistir BIN completo ni nada más que marca y últimos 4 dígitos.
11. **Reversas**: respetar lo que ya existe (no se revierte un pago con factura emitida ni con saldo a favor usado). El proveedor decide si se puede revertir (Tpago: mismo día). Pasado ese plazo, queda como devolución manual a tramitar con Bancard y el intento se marca para seguimiento.
12. **Sandbox vs producción**: son **conexiones distintas** (campo de ambiente, insignia visible). [Decisión] Un cobro de sandbox **no debe liquidar deuda real**; limitarlo a edificios de prueba o dejarlo en modo simulación.
13. **Datos personales**: el callback trae nombre, RUC, correo y celular del pagador. Aplicar minimización y retención acotada; la ley de protección de datos 7593/2025 ([resumen](https://olartemoure.com/en/paraguay-approves-law-7593-2025/)) será exigible en unos dos años.
14. **Disponibilidad**: reiniciar el servicio durante un callback hace que Tpago **revierta ese pago**. Es seguro (el dinero vuelve) pero deja mala experiencia: evitar despliegues en horas pico.

### 3.6 Experiencia dentro de Condo-PY

**Administración: Configuración → Pagos** (solo CompanyAdmin y SuperAdmin; el resto lo ve en lectura)

```
Bancard — Tpago
  Estado: No configurado                       [ Configurar ]

Bancard — vPOS
  Estado: Activo · Producción · 3 edificios    [ Ver configuración ] [ Probar conexión ] [ Desactivar ]
```

Estados de una conexión: `No configurado` → `Pendiente de validación` (cargada, sin prueba exitosa o sin certificar en Bancard) → `Activo` → `Desactivado`. Asistente de alta: (1) elegir proveedor y modalidad, (2) ambiente, (3) credenciales (solo escritura), (4) copiar la **URL de callback** y la lista de IPs para pegarlas en el portal del proveedor, (5) probar conexión, (6) elegir edificios, (7) activar. "Probar conexión" depende de qué ofrezca cada API: [A confirmar] (una consulta con alias inexistente distingue credenciales inválidas de "no encontrado").

**Propietario**

```
Expensas 09/2026 · Unidad 301 · Edificio X
Saldo pendiente: ₲350.000          [ Pagar ahora ]
```

1. Elegir comprobantes. Se respeta tu regla: **solo comprobantes completos, del más antiguo al más nuevo** (se puede pagar "hasta este mes"). Sin pagos parciales, que además chocarían con tu regla del comprobante completo.
2. Pantalla de confirmación con el importe exacto y los comprobantes. Si hay un cobro abierto del mismo comprobante: "Tenés un pago en curso" (retomar o esperar).
3. Medio de pago: lista de conexiones activas del edificio. Con un solo proveedor se omite este paso.
4. Redirección a la página del proveedor (Tpago) o formulario embebido (vPOS).
5. **Como Tpago no devuelve al usuario a tu app, la pantalla de espera consulta el estado** (ya tenés consulta cada 30 s y push): "Estamos verificando tu pago. Si ya pagaste, no vuelvas a pagar".
6. Confirmación: recibo y comprobante actualizado, que salen del flujo actual (`receipt-pdf`, notificación, borrador de factura).

### 3.7 Conciliación (sin convertirte en contabilidad)

Tres puntas: **pago en Condo-PY ↔ transacción del proveedor ↔ acreditación bancaria**.

- Condo-PY ya tiene la primera y guarda de la segunda: `ticket_number`, código de autorización, fecha y hora del proveedor, importe bruto, ambiente.
- Pantalla simple: pagos online confirmados por rango de fechas, exportables a Excel, con importe, ticket, autorización y un marcador **Conciliado** (usuario, fecha, referencia de la acreditación). Comisión y fecha de acreditación son campos opcionales que se completan a mano o por importación futura.
- Fuentes de contraste que Bancard sí ofrece: el portal de comercios (monitoreo de transacciones) y, en Tpago, consultas por API.
- Importar extractos o conciliar contra el banco queda fuera.

---

## 4. Lo que ya existe en tu sistema (hallazgos del código)

Reutilizable:

- Jerarquía `Company → Condominium → Building → Unit` y `CompanyId` casi en todas las tablas. **No hay filtros globales de tenant** (0 `HasQueryFilter`): el aislamiento se escribe a mano en cada consulta; el webhook, que no tiene sesión, tendrá que acotar por la empresa de la conexión explícitamente.
- Comprobante calculado (`ComprobanteService`): unidad + periodo, cobertura exacta, del más antiguo al más nuevo, tolerancia de ½ guaraní.
- `OwnerPayment` (con `Channel` App/Web y `Method`, incluyendo `Card`) → `Payment` + `PaymentAllocation`; `Reference` `PAY-aaaa-n` por empresa con índice único; borradores de factura; notificaciones y push; recibo en PDF; saldo a favor (`OwnerCredit`), que absorbe diferencias de importe.
- Reversa de pagos del canal Web con sus reglas (factura emitida, saldo usado).
- Patrones: servicios en segundo plano (`MarketplaceMaintenanceService`), auditoría de solo inserción (`MarketplaceEvent`), `RowVersion`, limitador de tasa, interruptores por edificio y por plan (`MarketplaceEnabled`, `AdsEnabled`, `Plan.IncludesMarketplace`), `HttpClient` tipado (Resend).

Faltantes o puntos de fricción:

1. **`SettlePaymentAsync`, `CoverWithCredit` y `NextReferenceAsync` son privados del controlador** (1.388 líneas). Un cobro online confirmado por un callback anónimo no puede llamarlos. Hay que extraerlos a un servicio con una prueba que demuestre que el comportamiento actual no cambia.
2. `ITenantContext` y varios servicios asumen un usuario autenticado (por ejemplo la factura exige `CreatedByUserId`): para el callback se necesita un actor "sistema".
3. No hay infraestructura de cifrado de secretos ni de rutas anónimas con verificación.
4. El comprobante **no tiene identidad persistente** (se calcula): el cobro debe guardar la pareja (unidad, periodo) con un snapshot.
5. `OwnerPaymentChannel` solo tiene App y Web: falta `Online`.
6. La **mora crea un cargo nuevo por cada intervalo configurado en el edificio** (diario, semanal o quincenal; `LateFeeAccrualService`). El proceso solo *revisa* cada 6 horas, pero con intervalo diario genera un recargo por día, en la primera revisión posterior a la medianoche (hora del servidor). El total de un comprobante vencido puede cambiar entre la creación del cobro y la confirmación, sobre todo cerca del cambio de día.
7. Hay un solo VPS con proxy: confirmar que el backend sea alcanzable por HTTPS en el 443 con TLS ≥ 1.2 desde internet, y poder identificar la IP real del llamante.

---

## 5. Las diez conclusiones pedidas

### 5.1 Qué ya debería existir

Ver sección 4. En resumen: jerarquía empresa/edificio, comprobante todo-o-nada, liquidación de pagos con asignación a cargos, saldo a favor, facturación en borrador, notificaciones, auditoría tipo `MarketplaceEvent`, y patrones de servicios en segundo plano e interruptores por edificio.

### 5.2 Qué agregar ahora (base agnóstica, sin una línea de Bancard)

1. Extraer la liquidación de pagos a un servicio propio y cubrirla con pruebas (comportamiento idéntico).
2. Entidades nuevas (sección 5.6): conexión, asignación, cobro, ítems, intento, evento. Más `OwnerPayment.PaymentIntentId` y `OwnerPaymentChannel.Online`.
3. Servicio de protección de secretos con clave maestra fuera de la base.
4. Interfaz de proveedor y un **proveedor falso** para pruebas (permite desarrollar y probar el flujo completo, incluido el callback, sin Bancard).
5. Endpoint genérico de callback con registro de eventos e idempotencia, y la pantalla Configuración → Pagos mostrando solo estados.
6. Servicio en segundo plano que resuelve intentos pendientes pasados X minutos (consulta y, si corresponde, rollback/expiración).

### 5.3 Qué NO implementar todavía

- Adaptadores de Pagopar, Dinelco o bancos.
- Débito automático/suscripciones, tarjetas guardadas, preautorización, cuotas.
- Pagos parciales o pagos mezclando edificios con comercios distintos.
- Una tabla `PaymentProvider` en la base: alcanza un **catálogo en código** (cada proveedor declara sus campos de credencial y capacidades; un interruptor de SuperAdmin lo habilita). Tampoco una tabla `PaymentMethod`: el medio (tarjeta, QR, billetera) lo elige el usuario dentro de la página del proveedor.
- Reversas desde la interfaz, conciliación bancaria automática, importadores de extractos.
- Cobrar los planes de Condo-PY (`BuildingPlanPayment`) por pasarela: ahí **sí** pasaría dinero por vos; es otro producto.
- Modelo de "consulta de deuda" para redes de cobranza (Infonet Cobranzas, bocas): solo cuando exista un cliente que lo pida.

### 5.4 Qué debe proporcionar el cliente para activar Bancard

Para **Condo-PY** (datos técnicos, cargados en Configuración → Pagos):

- Qué entidad es el comercio (empresa administradora o consorcio) y a qué edificios aplica.
- Producto contratado: Tpago API o vPOS 2.0.
- Tpago: `commerce_code`, `commerce_branch_code`, clave pública y privada (sandbox y producción por separado). vPOS: clave pública y privada (sandbox y producción).
- Registrar en el portal de Bancard la **URL de callback** que genera Condo-PY (con puerto 443) y, si corresponde, permitir las IPs.
- Persona de contacto técnica y resultado de las pruebas de sandbox.

Para **Bancard** (documentación legal; Condo-PY no debería recolectarla): RUC activo, cédula, cuenta bancaria **a nombre del titular** (de la sociedad si es S.A./S.R.L.), estatutos/constitución y acta autenticados, poderes y cédulas de los firmantes, formulario de adhesión y el anexo de acceso al portal de comercios.

### 5.5 Qué preguntar directamente a Bancard

1. ¿Una plataforma SaaS puede integrar en nombre de muchos comercios? ¿Hay certificación a nivel plataforma o cada comercio debe pasar su propio checklist (vPOS)? ¿Qué firma Condo-PY, si firma algo?
2. Versión vigente de la API de vPOS 2.0 y acceso a su documentación y entorno de staging.
3. Tpago: ¿el callback va firmado o autenticado? ¿Cuál es la política de reintentos y el timeout? ¿Hay callback ante reversas? ¿Las credenciales son por comercio o por sucursal? ¿Una sucursal por edificio es un uso válido?
4. IPs de origen de la confirmación de vPOS y si identifica el comercio.
5. Tpago: duración del link y si se puede fijar o anular; por qué el `bin` llega con 12 dígitos.
6. Costos reales: ¿los Gs 22.900 / 74.900 son mensuales, únicos o por alta? Comisión por transacción (débito, crédito, cuotas, QR).
7. ¿Puede el comercio trasladar la comisión al pagador? (condiciones de Bancard).
8. Plazo y procedimiento de devolución cuando ya pasó el día (Tpago) o ya está cuponada la operación (vPOS).
9. ¿Existe un modelo de cobranza por consulta de deuda (Infonet Cobranzas) para administradoras de edificios?
10. ¿Funciona el iframe de vPOS dentro de un WebView de Capacitor?

### 5.6 Modelo de datos recomendado

Seis tablas nuevas y dos cambios menores. Todas con `Id`, fechas y borrado lógico como el resto.

**`PaymentConnection`**: conexión con el comercio (pertenece a la empresa)

- `CompanyId`, `Name` (alias legible), `ProviderCode` (Bancard, Pagopar…), `ProductCode` (Tpago, VPos…), `Environment` (Sandbox/Production), `Status` (Draft, PendingValidation, Active, Disabled).
- `PublicId` (aleatorio, va en la URL del callback).
- `SettingsJson` (datos no secretos: código de comercio, sucursal, clave pública).
- `SecretsCipher`, `KeyId`, `SecretsFingerprint` (últimos caracteres para mostrar), `LastTestAtUtc`, `LastTestResult`.
- Quién y cuándo activó o desactivó. `RowVersion`.

**`BuildingPaymentConfig`**: asignación a edificios

- `CompanyId`, `BuildingId`, `ConnectionId`, `IsEnabled`, `SortOrder`. Único (`BuildingId`, `ConnectionId`). Validar misma empresa.

**`PaymentIntent`**: lo que se quiere cobrar

- `CompanyId`, `BuildingId`, `OwnerId`, `ConnectionId` (la usada), `Number` (secuencia), `Amount` (entero), `Currency` (PYG), `Status`, `ExpiresAtUtc`, `OwnerPaymentId` (resultado), `ReviewReason`, `RowVersion`.

**`PaymentIntentItem`**: comprobantes incluidos

- `IntentId`, `UnitId`, `ExpensePeriodId`, `Amount`, `SnapshotJson` (cargos y pendientes del momento). Índice único filtrado que impide el mismo comprobante en dos intentos abiertos.

**`PaymentAttempt`**: sesión con el proveedor

- `IntentId`, `ConnectionId`, `AttemptNumber` (secuencia global), `ProviderReference` (lo que enviamos), `ProviderSessionId` (`link_alias`/`process_id`), `CheckoutUrl`, `ProviderTransactionId` (`ticket_number`), `AuthorizationCode`, `Status`, `ProviderStatusRaw`, `ResponseCode`, `Amount`, `ExpiresAtUtc`, `ConfirmedAtUtc` (hora del proveedor), `CardBrand`/`CardLast4`, `ReconciledAtUtc/By`, `SettlementReference`, `RowVersion`.

**`PaymentEvent`**: auditoría de solo inserción

- `CompanyId`, `ConnectionId`, `AttemptId?`, `Direction`, `Kind` (create, callback, query, reverse…), `DedupeKey` (único cuando existe), `SignatureOk`, `Ip`, `PayloadJson` (redactado), `Outcome`, `Error`, `ReceivedAtUtc`, `ProcessedAtUtc`.

**Cambios en lo existente**: `OwnerPayment.PaymentIntentId` (nulo) y `OwnerPaymentChannel.Online`. Opcional: `Building.OnlinePaymentsEnabled` y `Plan.IncludesOnlinePayments` si querés comercializarlo como módulo.

### 5.7 Flujo completo de pago

1. El propietario elige comprobantes (completos, del más antiguo en adelante) y toca **Pagar ahora**.
2. El backend valida pertenencia y alcance, **recalcula** el total, exige un solo edificio, verifica que ese edificio tenga una conexión activa, que no haya otro cobro abierto del mismo comprobante ni un pago manual pendiente del propietario.
3. Crea `PaymentIntent` (`Open`) + ítems con snapshot + `PaymentAttempt` (`Created`) con su número de secuencia.
4. El proveedor crea la sesión; el intento pasa a `Pending` y se devuelve la URL (Tpago) o el identificador de sesión (vPOS).
5. El propietario paga en la página del proveedor.
6. El proveedor llama al callback de la conexión. El backend: guarda el evento crudo (idempotente), verifica autenticidad y monto/moneda, bloquea el intento y el intent.
7. Si está `Confirmed` y el comprobante sigue coincidiendo: ejecuta la **misma liquidación que hoy** (crea `OwnerPayment` aprobado con canal `Online`, `Payment` + asignaciones, notificación, borrador de factura), `Intent = Paid`. Si algo no coincide: `ReviewRequired` y aviso a la administración.
8. Hace commit y recién entonces responde éxito al proveedor.
9. La app consulta el estado y recibe la notificación: muestra confirmación y recibo.
10. Seguridad por tiempo: un servicio en segundo plano toma los intentos `Pending` vencidos, consulta al proveedor y los resuelve (vPOS recomienda esperar ~10 min; después consultar y hacer rollback si no hubo pago).
11. Conciliación: el personal revisa los pagos online del periodo contra el portal del proveedor y el banco y los marca como conciliados.

### 5.8 Arquitectura para múltiples proveedores

Dos servicios sin conocimiento del proveedor y una interfaz que cada proveedor implementa:

- **Orquestador de cobros** (`OnlinePaymentService`): crea el cobro y el intento, procesa eventos ya normalizados, vence y resuelve pendientes. No conoce Bancard.
- **Liquidación de pagos** (extraída del controlador): la usan la aprobación manual y la confirmación online.
- **`IPaymentProvider`** (en `Condo.Application`, implementaciones en `Condo.Infrastructure`): crear sesión de cobro; **verificar y normalizar un callback** a un evento común (referencia, estado, importe, moneda, IDs del proveedor, fecha); consultar estado; revertir; probar credenciales; y declarar **capacidades** (¿tiene reversa?, ¿solo el mismo día?, ¿embebido o redirección?, ¿callback firmado?).
- **Catálogo en código** de proveedores y modalidades con los campos de credencial que cada uno pide; la pantalla de configuración se dibuja a partir de ese catálogo.
- Un único endpoint de callback (`/api/payment-webhooks/{publicId}`) que resuelve la conexión y delega en el proveedor correspondiente.

Agregar Pagopar u otro es: una clase que implementa la interfaz, una entrada en el catálogo y pruebas. No toca el modelo de datos. Los proveedores que no tienen API de confirmación (transferencia, QR) siguen en el flujo manual actual de declarar y revisar.

### 5.9 Riesgos y decisiones a resolver antes de programar

1. **Titularidad del comercio.** Bancard exige que la cuenta esté a nombre del titular/sociedad. Si una administración cobra las expensas de varios consorcios en una cuenta propia, se mezcla el dinero de terceros. Es una decisión legal/contable de cada cliente (consultar al contador); Condo-PY debe mostrar claramente a nombre de quién se cobra.
2. **Cambio de importe entre el cobro y la confirmación** (recargo de mora del día siguiente, notas de crédito, pago manual del personal en el medio). Opciones: (a) `ReviewRequired` y devolver el dinero, (b) acreditar la diferencia como saldo a favor, (c) bloquear la acumulación de mora mientras hay un cobro abierto. [Decisión] mi sugerencia: **(a) por defecto con aviso inmediato a la administración**, porque la reversa de Tpago solo es válida el mismo día. Un caso a definir: ¿qué hacer con un pago tardío sobre un cobro que el usuario ya canceló? Tpago no documenta cómo anular un link.
3. **Redondeo.** Tpago exige monto entero; tus cargos pueden tener decimales (tolerancia actual de ½ guaraní). Hay que decidir el redondeo del total.
4. **Comisiones**: ¿las absorbe el edificio o se trasladan al propietario? Confirmar si Bancard lo permite. Mi sugerencia: sin recargo en la primera versión.
5. **Sandbox**: decidir que nunca liquide deuda real.
6. **Certificación por comercio**: si vPOS exige un checklist por comercio, el alta de cada cliente tiene un paso manual. Condiciona la promesa comercial y el soporte.
7. **Gestión de claves en un solo VPS.** La clave maestra no puede viajar en el mismo respaldo que la base.
8. **Facturación**: hoy el pago aprobado genera borradores y la emisión es manual (papel preimpreso). Con pagos online sin intervención conviene definir con cada cliente cuándo se emite la factura.
9. **Disponibilidad del callback**: un reinicio del servicio revierte pagos de Tpago. Definir ventanas de despliegue.
10. **Un solo comercio por cobro**: propietarios con unidades en edificios de comercios distintos pagan en varias operaciones.
11. **Aplicación móvil**: Tpago no devuelve al usuario a tu app; la experiencia depende de la consulta periódica y el push. Validar el iframe de vPOS en Capacitor.
12. **Ley de datos personales 7593/2025**: política de retención de los datos del pagador.

### 5.10 Recomendación final

Prepará ahora la **base agnóstica**: extraer la liquidación a un servicio, agregar las seis tablas, el servicio de secretos, la interfaz de proveedor con un proveedor falso, el endpoint de callback idempotente y la pantalla Configuración → Pagos en estado "No configurado". Es poco código nuevo, no depende de Bancard, y deja el sistema listo para cualquier proveedor.

No programes ningún adaptador real hasta tener las respuestas de la sección 5.5. Mi orden sugerido para el **primer proveedor**: **Bancard Tpago** (redirección con link, un endpoint para crear y un callback), siempre que Bancard confirme que el acceso por API está incluido para tus clientes; si no, vPOS 2.0. Probalo con **un cliente piloto** antes de generalizar, y resolvé antes las decisiones 1 y 2 de la sección 5.9, porque cambian reglas de negocio y no solo código.

---

## Fuentes

Oficiales de Bancard: [Tpago](https://www.bancard.com.py/tpago) · [vPOS](https://www.bancard.com.py/vpos) · [Débito Automático](https://www.bancard.com.py/debito-automatico) · [Portal de comercios: link de pago](https://comercios.bancard.com.py/productos/payment-link) · [Tpago API Reference](https://tpagodocs.bancard.com.py/) y su [colección Postman](https://tpagodocs.bancard.com.py/tpago-api-postman-collection_v1.2.0.json) · [Guía vPOS Compra Simple v0.3.1](https://www.afd.gov.py/userfiles/files/transparencia/ecommerce-bancard-compra-simple-version-0-3-1.pdf) (copia antigua) · SDK [bancard-connectors](https://github.com/Bancard/bancard-connectors) y [bancard-checkout-js](https://github.com/Bancard/bancard-checkout-js).

Otros proveedores: [Pagopar, documentación de la API](https://soporte.pagopar.com/portal/es/kb/articles/version-ingles-api-integraci%C3%B3n-de-medios-de-pagos) · [uPay/Pagopar](https://upay.com.py/pagopar/) · [Dinelco](https://www.dinelco.com.py) · [Infonegocios sobre Dinelco](https://infonegocios.com.py/plus/dinelco-lo-hace-diferente-estreno-link-y-pasarela-de-pago-pagos-qr-y-portal-de-comercios) (secundaria) · [BCP, transferencias instantáneas](https://www.bcp.gov.py/web/institucional/w/bcp-actualiza-el-reglamento-del-sipap-y-eleva-el-limite-de-las-transferencias-instantaneas-a-g-10-millones) · [Ley 7593/2025](https://olartemoure.com/en/paraguay-approves-law-7593-2025/) (secundaria).

Limitaciones de esta investigación: no pude leer la documentación de vPOS 2.0 que Bancard publica dentro del portal de comercios (requiere cuenta); la guía v0.3.1 es antigua aunque el SDK oficial actual usa los mismos endpoints y fórmulas de token. Los precios de Bancard figuran sin período. Dinelco no publica API.
