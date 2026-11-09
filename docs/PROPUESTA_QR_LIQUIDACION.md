# Propuesta: QR en la liquidación de expensas

Fecha: 2026-11-09 · Estado: **opción A implementada** (ver `GUIA_PRUEBAS_QR_LIQUIDACION.md`); B y C sin hacer · Origen: consulta del cuñado de Tony

## 0. Resumen

Hoy ningún PDF del sistema tiene QR (la búsqueda de `QR`, `QRCoder` y `ZXing` en el backend no devuelve nada). La liquidación (`SettlementPdfDocument`) se imprime en A4, se cuelga en la cartelera y se manda por WhatsApp.

Un QR en ese papel puede hacer **tres cosas distintas**, de menor a mayor riesgo y esfuerzo. Conviene entregarlas en este orden:

| # | QR | Para qué sirve | Mueve dinero | Depende de | Esfuerzo |
|---|----|----------------|--------------|------------|----------|
| A | **Verificar** | Comprobar que el papel es auténtico y está vigente | No | Nada | Bajo |
| B | **Ver mi cuenta** | Abrir en la app/web el comprobante de *mi* unidad | No | A | Bajo |
| C | **Pagar** | Pagar el comprobante con la app del banco o billetera | Sí | Pagos online (Bancard), decisiones abiertas | Alto |

**Recomendación:** hacer A y B ahora (seguros, rápidos, ya dan valor), y dejar C atado al proyecto de pagos online, porque un QR de pago bien hecho es lo mismo que el cobro online: un QR dinámico por comprobante que confirma el servidor.

> Pendiente de confirmar con el cuñado: si cuando dice «QR en la liquidación» se refiere a **pagar** (C, lo más probable en Paraguay: QR de banco o billetera) o a **verificar** (A). Con A y B cubrimos la segunda lectura sin riesgo y dejamos lista la base de la primera.

---

## 1. Opción A — QR de verificación (autenticidad)

### Idea
Cada liquidación emitida lleva un QR chico en una esquina. Al escanearlo, cualquier persona (sin usuario ni contraseña) ve una página pública que dice si el documento es auténtico, de qué edificio y período es, y desde cuándo vale. Sirve cuando alguien dice «esa liquidación está adulterada» o cuando el banco/inmobiliaria pide el **certificado de deudas** y quiere saber que no es un PDF editado.

### Cómo se ve para el usuario

**En el papel (esquina inferior derecha de la liquidación):**

```
 ┌──────────────────────────────────────────────┐
 │  LIQUIDACIÓN DE EXPENSAS · OCTUBRE 2026      │
 │  Edificio Torre Sol                          │
 │  ...conceptos, totales, firmas...            │
 │                                              │
 │  Emitida: 05/11/2026       ┌─────┐           │
 │  Vence:   15/11/2026       │ QR  │           │
 │                            └─────┘           │
 │                  Escaneá para verificar      │
 └──────────────────────────────────────────────┘
```

**Al escanear (página pública, sin login):**

```
 ✔ Documento auténtico
 Liquidación de expensas · Octubre 2026
 Edificio Torre Sol · Administra: Gestiones SA
 Emitida el 05/11/2026 · Total gastos comunes: ₲ 18.450.000
 Estado: VIGENTE
 [ Si el papel dice otro total, no es el documento original ]
```

Si se reemitió o se anuló: «⚠ Este documento fue reemplazado el 12/11/2026 por una versión nueva». Si el código no existe: «✖ No encontramos este documento».

Muestra **solo datos que ya están en el papel colgado en la cartelera** (edificio, período, total general, fecha). Nada de unidades ni montos individuales.

### Cómo se hace seguro
- El QR lleva solo una dirección fija `https://expenses.com.py/v/<token>`. El token es **aleatorio de 128 bits** (no un número correlativo), así no se pueden adivinar otros documentos.
- En la base se guarda el token, el tipo de documento, el id, la empresa, un **hash del contenido** y las fechas de emisión y anulación. La página pública solo lee esa fila y responde un resumen mínimo.
- Endpoint anónimo con **limitador de tasa** (ya existe el patrón) y sin enumeración: mismo mensaje si no existe o venció.
- Se puede **anular** un token (reemisión, error) y la página lo muestra.
- Dominio fijo: el QR nunca apunta a otro dominio; la página muestra siempre el dominio verificado.

### Dónde aplica
Liquidación (`SettlementPdfDocument`), certificado de deudas (`DebtCertificatePdfDocument`, el que más lo agradece), recibo de pago, informe para asamblea. Se puede activar por edificio con un interruptor, igual que `MarketplaceEnabled` y `AdsEnabled` (Centro de configuración del edificio).

### Puntos técnicos
- QuestPDF no dibuja QR solo: se agrega **QRCoder** (NuGet, licencia MIT) y se inserta como imagen/SVG.
- **Papel propio del edificio**: la liquidación se puede imprimir sobre un papel con diseño propio, con bloques calibrables. El QR debe ser un bloque más (`qr`) con posición, tamaño y la opción «no dibujar». Hay que agregarlo en `SettlementPdfDocument.cs` **y** en `FIELDS` de `settlement-calibration-page.component.ts` (los comentarios del código piden que coincidan).
- Una tabla nueva (`DocumentVerification`) = **una migración `.sql` manual para el VPS**.
- Una pantalla pública nueva en el WEB (`/v/:token`), en los tres idiomas que ya tiene el sitio (ES/PT/EN).

---

## 2. Opción B — QR «Ver mi cuenta» (por unidad)

### Idea
La liquidación individual de cada propietario (comprobante de su unidad) lleva un QR que abre la app o el web **directo en el comprobante de esa unidad**, con el detalle y el botón de pago (cuando exista).

### Cómo se ve para el usuario
1. Recibe el PDF de su unidad por WhatsApp o impreso.
2. Escanea el QR con la cámara.
3. Si tiene la app instalada, se abre en «Expensas octubre · Unidad 301»; si no, abre el web y le pide iniciar sesión.
4. Ve saldo, detalle y recibos. Si no es su unidad, ve «Este comprobante pertenece a otra cuenta».

```
 Expensas 10/2026 · Unidad 301
 Saldo pendiente: ₲ 350.000
 [ Ver detalle ]   [ Descargar recibo ]   [ Pagar ]  ← cuando exista C
```

### Cómo se hace seguro
- El QR **no contiene ni importe ni nombre**: solo un enlace opaco. Lo que se ve lo decide el servidor **después de iniciar sesión**, con las reglas de aislamiento de siempre (empresa, edificio, unidad).
- Si alguien reenvía el PDF de otro, solo ve la pantalla de login y no el contenido. Hay que tener cuidado con la **auditoría de aislamiento** pendiente: este endpoint debe validar que la unidad pertenezca al usuario, no solo al edificio.
- Reutiliza la tabla de A, con tipo `owner-statement`.

---

## 3. Opción C — QR de pago

### Idea
El comprobante lleva un QR que se escanea con la **app del banco o billetera** y paga el importe exacto. Es lo que la gente de Paraguay espera ver en una factura o liquidación.

### Por qué no se hace «ya»
- Un QR de pago **no es solo una imagen**: tiene que generarlo un proveedor (Bancard, o el QR interoperable que habilite el sistema de pagos del BCP; [A confirmar] qué estándar y qué bancos lo soportan) o un banco con el comercio a nombre del cliente.
- El importe de un comprobante **cambia con la mora** (se genera un recargo por intervalo). Un QR impreso con el importe del día de emisión queda desactualizado a la semana. La solución correcta es un QR **dinámico**: el servidor consulta el importe vigente en el momento de escanear. Ya está analizado en `PROPUESTA_PAGOS_ONLINE.md` (sección 5.9).
- La confirmación del pago la tiene que dar **el servidor** (webhook o consulta), nunca el navegador del propietario.
- Falta definir a nombre de quién está el comercio. Es una decisión abierta de Tony y del cliente.

### Camino realista, por etapas
1. **C1 – QR estático de cuenta (sin proveedor):** el QR contiene los datos de transferencia del edificio (banco, cuenta, titular, alias), que ya existen en la ficha del edificio (`BuildingBankAccount`). Sirve para copiar datos, pero **no confirma el pago ni fija importe**; hay que seguir informando el comprobante a mano. Solo conviene si el cuñado lo pide expresamente. [A confirmar] formato con un banco.
2. **C2 – QR dinámico por comprobante (con Bancard u otro):** al escanear se resuelve el importe vigente de ese comprobante, se crea el cobro y el pago se aplica por la lógica existente, del cargo más antiguo al más nuevo. Es el mismo trabajo que el cobro online.

### Cómo se ve para el usuario (C2)
```
 Escanea con tu app bancaria
 ┌─────┐   Expensas 10/2026 · Unidad 301
 │ QR  │   Importe a pagar hoy: ₲ 350.000
 └─────┘   Válido hasta las 23:59 de hoy
 Verificá que el destino sea: «Consorcio Torre Sol»
```
Después de pagar: «Pago recibido» y llega el recibo y el push, igual que hoy.

### Riesgo específico y cómo se cubre
- **QR falso pegado sobre el original** en la cartelera: la pantalla de pago muestra siempre el nombre del comercio/edificio verificado, y la liquidación impresa lleva también el QR de **verificación (A)** como contraste. Recomendación: el QR de pago solo va en el comprobante **individual** que recibe el propietario, no en el papel que se cuelga en la cartelera.
- Pagos duplicados: un cobro abierto por comprobante; si ya hay uno, se retoma.

---

## 4. Qué cambia para cada rol

| Rol | Con A y B | Con C |
|-----|-----------|-------|
| **Propietario** | Escanea y ve su cuenta o verifica el documento | Además paga desde su banco |
| **Encargado / presidente** | Nada que hacer; el PDF sale con QR | Ve el pago aplicado sin cargarlo a mano |
| **Administración (CompanyAdmin)** | Interruptor «Mostrar QR» por edificio | Configura el comercio en Pagos |
| **SuperAdmin (Tony)** | Calibra la posición del QR en papeles propios | Conexiones y claves |
| **Tercero (banco, inmobiliaria)** | Verifica el certificado de deudas sin llamar | — |

---

## 5. Decisiones para Tony y el cuñado

1. ¿El QR que pide el cuñado es de **pago** o de **verificación**? (Cambia la prioridad.)
2. ¿Se imprime también en el papel de la **cartelera** o solo en el comprobante individual? Recomendado: verificación en ambos, pago solo en el individual.
3. ¿Hay algún banco o billetera preferida en Paraguay (por QR interoperable) para investigar el estándar antes de C?
4. ¿Se activa **por edificio** con interruptor (recomendado) o para todos?

## 6. Si se aprueba A y B (alcance)

- BACK: QRCoder, tabla `DocumentVerification` con migración `.sql` para el VPS, servicio de emisión de tokens, endpoint anónimo con limitador, QR en liquidación, certificado de deudas y recibo, bloque `qr` calibrable, interruptor por edificio.
- WEB: página pública `/v/:token` en ES/PT/EN, bloque `qr` en la pantalla de calibración, interruptor en la configuración del edificio.
- APP: abrir el enlace del QR en la pantalla del comprobante (enlace directo), solo cuando Tony diga.
- Pruebas: token inexistente, anulado, reemitido, intento de enumeración, propietario de otra unidad, papel propio con QR desplazado.
