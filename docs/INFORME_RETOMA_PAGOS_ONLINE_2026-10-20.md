# Informe de retoma: pagos online (Bancard)

Fecha: 2026-10-20. Rama `feature/pagos-online` creada en los tres repos. **Solo informe: no se tocó código.**

Documentos de base (ya en el repo): [PROPUESTA_PAGOS_ONLINE.md](PROPUESTA_PAGOS_ONLINE.md) (diseño general, 2026-10-05), [GUIA_PRUEBAS_PAGOS_BANCARD_CONFIG.md](GUIA_PRUEBAS_PAGOS_BANCARD_CONFIG.md) (configuración, 2026-10-12) y [ALCANCE_TECNICO_PAGOS_ONLINE_ETAPA1.md](ALCANCE_TECNICO_PAGOS_ONLINE_ETAPA1.md) (alcance de la Etapa 1, 2026-10-12, sin implementar).

---

## 1. Qué queremos

1. Que el propietario pague sus expensas con tarjeta desde la app o la web (**Pagar ahora**), y que el pago se aplique **solo**, sin que el personal lo apruebe.
2. Que **el dinero no pase por Condo-PY**: cada administración o consorcio tiene su propio comercio en Bancard y Condo-PY solo guarda credenciales y registra resultados.
3. Que el pago online termine en **la misma liquidación que hoy** (comprobante unidad + periodo, todo o nada, del cargo más antiguo al más nuevo, saldo a favor, recibo, borrador de factura, aviso).
4. Que sea **agnóstico del proveedor** (primer proveedor Bancard; otros después sin tocar el modelo de datos).
5. Que cada operación conserve edificio, unidad, propietario, titular del comercio, referencia externa e importe, y que sea **idempotente** (un aviso repetido no cobra dos veces).

## 2. Dónde estamos

### Hecho (config, 2026-10-12)

| Pieza | Estado |
|---|---|
| Entidades `PaymentConnection`, `BuildingPaymentConfig`, `PaymentConnectionAudit` + migración `20261012185825_PaymentConnections` (+ `.sql`) | Hecho y commiteado (BACK) |
| `ISecretProtector` (AES-GCM, clave maestra en `Payments__MasterKey` fuera de la base, `KeyId` para rotar) | Hecho |
| Catálogo en código (`PaymentProviderCatalog`: Bancard Tpago y vPOS) y `PaymentConnectionsController` (`api/payment-connections`) | Hecho |
| Sección "Pagos online" del Centro de configuración (WEB) | Hecho |
| 24 pruebas (`PaymentConnectionsTests`) y guía de pruebas | Hecho |
| Despliegue al VPS (SQL, clave maestra, backend, web) | **No confirmado**: la guía lo describe, no hay constancia de que se haya hecho |

### Diseñado pero NO implementado

- Todo el **cobro**: no existen `PaymentIntent`, `PaymentIntentItem`, `PaymentAttempt`, `PaymentEvent`, el callback `/api/payment-webhooks/{publicId}`, `IPaymentProvider`, el proveedor falso, ni el servicio en segundo plano de vencimiento.
- `SettlePaymentAsync` y `NextReferenceAsync` **siguen privados en `OwnerPaymentsController`** (verificado hoy). Extraerlos es el prerrequisito número uno.
- La **Etapa 1** (alcance del 2026-10-12): cobro con proveedor falso + **cuenta en tránsito por edificio** (`FinancialAccountType.Clearing`, `PaymentMethod.Online`), 6 tablas nuevas (2 reservadas para la Etapa 2) y 8 pasos de trabajo con un commit por paso.
- App móvil: nada (la rama `feature/pagos-online` del APP parte limpia de `feature/marketplace`).

### Decisiones (resueltas por Tony el 2026-10-20)

| # | Tema | Decisión |
|---|---|---|
| D-1/D-2 | Quién absorbe la comisión y su tratamiento fiscal | **No fijar nada**: campos de comisión y neto nulos, sin lógica; se define con el contador de cada cliente antes de la Etapa 2 |
| D-3 | Cómo probar sin liquidar deuda real | **Edificios marcados de prueba** (`BuildingPaymentConfig.IsTest`): el proveedor falso y el sandbox solo aplican pagos ahí; en reales queda "simulado" sin crear pago |
| D-4 | Importe que cambia entre cobro y confirmación (mora diaria) | **`ReviewRequired`**: no se aplica, se avisa a la administración y se devuelve el dinero |
| D-5 | Redondeo | Al guaraní entero, dentro de la tolerancia de 0,5 (default técnico, sin objeción) |
| D-6 | Titularidad del comercio | **Titular obligatorio y visible**: `HolderName` y `HolderTaxId` obligatorios para activar la conexión y copiados al cobro |
| D-7 | Tablas vacías de liquidaciones (Etapa 2) | **No crearlas ahora** (default técnico, sin objeción): migración más chica; se agregan en la Etapa 2 |

## 3. Referencias oficiales de Bancard (releídas el 2026-10-20)

### 3.1 Tpago: API Reference (la única documentación técnica pública)

- **URL:** https://tpagodocs.bancard.com.py/ . **Versión vigente 3.16.0 (11-06-2026)**, igual a la que se usó en la propuesta; no hubo cambios. Colección Postman v1.2.0 publicada en el mismo sitio.
- Landing y producto: https://www.bancard.com.py/tpago · https://comercios.bancard.com.py/productos/payment-link
- Base producción `https://comercios.bancard.com.py` · sandbox `https://comercios.bancard.com.py:8888` (claves distintas).
- Crear link: `POST /external-commerce/api/0.1/commerces/{commerce_code}/branches/{commerce_branch_code}/links/generate-payment-link` con `amount` (entero, Gs), `description`, `reference_id` (opcional), `require_user_data` (opcional). Devuelve `link_url`, `link_alias`, `expiration_datetime`.
- Autenticación: `Authorization: Basic base64(pública:privada)`, HTTPS obligatorio.
- **Callback** (POST, TLS 1.2, **puerto 443 en producción**): IPs de origen publicadas: `190.128.218.209`, `190.128.232.10`, `190.104.129.98`, `200.85.46.226`. Debe responder `{"status":"success"}`; si no responde eso, hay excepción o timeout, **Tpago revierte el pago**. La v3.9.0 menciona "especificación de encriptación" del callback; **no está claro qué significa** (ver preguntas).
- Campos del callback: `payment.{link_alias, status, response_code ("00"=ok), amount, currency, ticket_number, authorization_code, date_time "DD/MM/YYYY HH:MM:SS", card_last_numbers, account_type (TC/TD), bin, payer{...}}`.
- Estados: 0 Confirmed, 1 Failed, 2 Pending, 3 Reversed, 4 Reverse pending, 5 Reverse failed (7 y 8 en desuso).
- Reversa: `PUT .../links/payments/revert/{payment_hook_alias}`, **solo el mismo día** y solo si está aprobado.
- Tarjetas de prueba (solo sandbox): Mastercard `5418 6301 1000 0014`, Visa `4907 8605 0000 0016` (aprueban); Infonet `8601 0100 0000 0013` (rechaza). Bloqueo antifraude: 7 rechazos en 24 h o 35 en 30 días bloquea la tarjeta 30 días en ese comercio.
- Soporte indicado en la propia documentación: support@tpago.com

**Dos hallazgos nuevos de esta relectura** (no estaban claros en la propuesta):

1. En la tabla de atributos y en el ejemplo del callback **no aparece `reference_id`** aunque la nota de versión 3.8.0 dice que el número de referencia se devuelve "como dato opcional". Hasta confirmarlo con Bancard, **el cruce del aviso con nuestro intento debe hacerse por `link_alias`** (que guardamos al crear el link), no por `reference_id`.
2. La tabla dice que `bin` son "los primeros 6 dígitos", pero el ejemplo trae 12 y cuando el pago no es aprobado vale `0`. Se mantiene la regla de no persistir el BIN.

No encontré en la documentación pública un endpoint para **anular un link** ni para **consultar un link por alias** que no sea el de pagos; sigue siendo una pregunta para Bancard.

### 3.2 vPOS 2.0: la documentación vigente NO es pública

- Producto: https://www.bancard.com.py/vpos (iframe embebido, tarjetas, débito, Zimple, cuotas, preautorización, 3D Secure opcional; **no enlaza documentación técnica**: solo videos, el Centro de ayuda y el Portal de Comercios).
- La guía técnica oficial vive en el **Portal de Comercios** (requiere cuenta de comercio). Referencias que sí se pueden leer:
  - Guía "Compra Simple v0.3.1" (alojada en afd.gov.py, **antigua**): https://www.afd.gov.py/userfiles/files/transparencia/ecommerce-bancard-compra-simple-version-0-3-1.pdf
  - Repositorio oficial de Bancard: https://github.com/Bancard/bancard-connectors (carpeta `vpos/`: `checkout/javascript` y `sdk/python`) y https://github.com/Bancard/bancard-checkout-js
  - SDK de terceros dicen estar verificados contra "eCommerce – Compra Simple **v1.23.1**" (versión más nueva que la que pudimos leer; no pude acceder a ella). Sandbox según SDK de terceros: `https://vpos.infonet.com.py:8888`, producción el mismo host sin puerto.
- Lo que sabemos (de la guía 0.3.1 y el SDK): `single_buy` con `shop_process_id`, token `md5(privada + shop_process_id + monto + moneda)`, confirmación por POST de vPOS a nuestra URL (**es el único medio de certeza del pago**), monitoreo cada 5 minutos con JSON vacío, `get_confirmation` y `rollback`, checklist de pruebas obligatorio para habilitar la integración.
- Contacto comercial/técnico de Bancard: cac@bancard.com.py · WhatsApp 0982 80 04 04 · (021) 416 1000 · Av. Brasilia 765, Asunción.

### 3.3 Requisitos de alta (oficiales)

RUC activo, cédula, cuenta bancaria **a nombre del titular**; para S.A./S.R.L. constitución y acta autenticadas, poderes, cédulas de firmantes y cuenta a nombre de la sociedad. Precios publicados sin aclarar período: Tpago Gs 22.900 IVA incl.; vPOS Gs 74.900 IVA incl. Acreditación: débito al instante, crédito en 48 h.

## 4. Qué se puede hacer sin esperar a Bancard y qué no

| Se puede (base agnóstica, Etapa 1) | No se puede todavía (depende de Bancard) |
|---|---|
| Extraer la liquidación a un servicio con pruebas de caracterización | Adaptador real de Tpago/vPOS |
| Cuenta en tránsito, método y canal `Online` | "Probar conexión" |
| 6 tablas, orquestador, `IPaymentProvider`, proveedor falso | Registrar la URL de callback en el portal de Bancard |
| Webhook idempotente y vencimiento en segundo plano | Filtro de IP detrás del proxy (`ForwardedHeaders`, hallazgo H8) |
| Pantalla web de cobros y simulador | Pantalla de la app móvil con Tpago (el usuario no vuelve a la app) |

## 5. Preguntas a Bancard (consolidadas, orden de importancia)

1. ¿Una plataforma SaaS puede integrar en nombre de muchos comercios? ¿Hay certificación por plataforma o cada comercio pasa su checklist?
2. Tpago: ¿el callback va firmado/cifrado (nota 3.9.0)? ¿Política de reintentos y timeout exacto? ¿Hay callback por reversas?
3. Tpago: ¿se devuelve `reference_id` en el callback y cómo se activa (nota 3.8.0)? ¿Credenciales por comercio o por sucursal? ¿Una sucursal por edificio es válida?
4. Tpago: vigencia del link, ¿se puede fijar o anular? ¿Por qué `bin` llega con 12 dígitos?
5. vPOS: acceso a la guía vigente (v1.23.x) y al staging; IPs de origen de la confirmación; ¿funciona el iframe dentro del WebView de Capacitor?
6. Costos: ¿Gs 22.900 / 74.900 son mensuales, únicos o por alta? Comisión por transacción y si se puede trasladar al pagador.
7. Devoluciones fuera de plazo (Tpago: después del día; vPOS: después del cupón).
8. ¿Existe cobranza por consulta de deuda (Infonet Cobranzas) para administradoras?

## 6. Orden de trabajo propuesto para esta rama

1. Pruebas de caracterización de aprobar/registrar un pago (sin cambiar código).
2. Extraer `OwnerPaymentSettlementService` y corregir H2 (borradores de factura sin sesión).
3. `Clearing` / `Online` y ajustes de reportes.
4. Migración + `.sql`.
5. Orquestador, proveedor falso y webhook.
6. Vencimiento en segundo plano.
7. Web: cobros online y simulador.
8. Guía de pruebas.

Después de eso, recién el **adaptador Tpago** (el más simple: un POST para crear el link y un callback), con un cliente piloto, una vez contestadas las preguntas 1–3.

## 7. Estado de las ramas (2026-10-20)

| Repo | Rama | Base | Cambios locales |
|---|---|---|---|
| Condo-PY-BACK | `feature/pagos-online` | `main` (6abfc0b) | ninguno |
| Condo-PY-WEB | `feature/pagos-online` | `main` (58a4898) | ninguno |
| CondoPY-APP | `feature/pagos-online` | `feature/marketplace` (c6ae7f5) | ninguno (se descartaron 5 archivos generados de Android) |
