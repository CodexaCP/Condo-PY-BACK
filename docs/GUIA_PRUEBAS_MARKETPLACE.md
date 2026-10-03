# Marketplace de espacios temporales — Guía de pruebas de punta a punta

Para probar **todo** el marketplace a mano, rol por rol, con lo que tiene que pasar en cada paso. Complementa a `DESPLIEGUE_MARKETPLACE.md`
(cómo se instala) y a `ESPECIFICACION_MARKETPLACE.md` (las reglas de negocio). Marcá cada casilla `[ ]` a medida que lo verificás.

**Ejemplo numérico que se usa en toda la guía:** el propietario publica a **Gs. 20.000 por hora**; el comprador reserva **3 horas**.
Precio del espacio **60.000** + comisión de gestión 10 % **6.000** = el comprador paga **66.000**. El propietario recibe **60.000** (a su saldo a favor).

---

## 0. Qué necesitás

### 0.1 Un edificio de prueba (no uses uno real)
Idealmente un edificio dedicado (por ejemplo «Edificio Demo Marketplace») con **dos unidades** y, si es posible, **un segundo edificio sin el módulo** para comprobar los menús.

### 0.2 Personas de prueba (con celular o navegador separado cada una)

| Persona | Rol | Qué es en el edificio de prueba | Dónde la usás |
|---|---|---|---|
| **SuperAdmin** | SuperAdmin | Dueño de la plataforma | Web |
| **Encargado** | BuildingManager | Con acceso al edificio de prueba | Web y app |
| **Operador** | CompanyOperator | Con acceso al edificio de prueba | Web |
| **Ana** | Propietario | **Propietaria principal** de la unidad **302** (publica) | App |
| **Beto** | Propietario | Propietario principal de la unidad **101** (compra) | App |
| **Carla** | Residente | Residente de la unidad **101** (compra, **no** publica) | App |
| **Dani** | Propietario | Copropietario **no principal** de la 302 (no publica) | App |
| **Elena** | Propietario | De **otro edificio** (o empresa) para pruebas de aislamiento | App |

Se crean con *Usuarios*, *Propietarios*, *Residentes* y *Asignaciones* de la web. Los *push* solo llegan a celulares con la app instalada y sesión iniciada.

### 0.3 Los tiempos que mandan en el marketplace
| Qué | Cuánto | Cómo acelerarlo para probar |
|---|---|---|
| Plazo para pagar una reserva | **10 minutos** (cuenta regresiva visible) | Esperar, o dejarla vencer a propósito |
| Alertas al Encargado por un pago sin revisar | al subir, a los **15 min**, y luego **cada hora** | Atrasar `SubmittedAtUtc` (ver 0.4-C) |
| Aviso de inicio al comprador | al **empezar** la reserva | Adelantar la reserva (0.4-A) |
| Reclamo | desde que **empieza** hasta **24 h después de su fin** | 0.4-A |
| Acreditación al saldo del propietario | **24 h después del fin**, si no hay reclamo | Atrasar la reserva (0.4-B) |
| Alerta de reembolso vencido | a las **72 h** sin devolver | 0.4-D |
| Proceso de fondo | corre **cada 30 segundos** | Esperar medio minuto después de cada cambio |

### 0.4 Atajos de base de datos para «viajar en el tiempo» (SOLO en el edificio de prueba)
Se corren en el VPS con `sqlcmd` (ver `vps_deploy`), contra la base `CondoPY`. Cambiá `MP-00000001` por la referencia de la reserva (se ve en la app y en la web). Las fechas se guardan en UTC.

**A) La reserva ya empezó (en curso, empezó hace 1 hora):**
```sql
DECLARE @ref nvarchar(30) = 'MP-00000001', @inicio datetime2 = DATEADD(HOUR,-1,SYSUTCDATETIME());
UPDATE MarketplaceReservations SET EndsAtUtc = DATEADD(MINUTE, DATEDIFF(MINUTE, StartsAtUtc, EndsAtUtc), @inicio), StartsAtUtc = @inicio WHERE Reference = @ref;
```
**B) La reserva terminó hace más de 24 h (lista para acreditar), si dura 3 h:**
```sql
DECLARE @ref nvarchar(30) = 'MP-00000001', @inicio datetime2 = DATEADD(HOUR,-28,SYSUTCDATETIME());
UPDATE MarketplaceReservations SET EndsAtUtc = DATEADD(MINUTE, DATEDIFF(MINUTE, StartsAtUtc, EndsAtUtc), @inicio), StartsAtUtc = @inicio WHERE Reference = @ref;
```
**B2) Terminó hace 2 horas (dentro de la ventana de reclamo, todavía sin acreditar):** igual que B con `@inicio = DATEADD(HOUR,-5,SYSUTCDATETIME())`.

**C) El comprobante lleva 16 minutos sin revisar (para ver el recordatorio):**
```sql
UPDATE MarketplacePayments SET SubmittedAtUtc = DATEADD(MINUTE,-16,SYSUTCDATETIME()), AlertCount = 1
WHERE ReservationId = (SELECT Id FROM MarketplaceReservations WHERE Reference = 'MP-00000001') AND Status = 'Submitted';
```
**D) El reembolso lleva 73 horas sin devolverse:**
```sql
UPDATE MarketplaceRefunds SET CreatedAtUtc = DATEADD(HOUR,-73,SYSUTCDATETIME())
WHERE ReservationId = (SELECT Id FROM MarketplaceReservations WHERE Reference = 'MP-00000001') AND Status = 'Pending';
```
Después de cada atajo, **esperá ~30 segundos** (proceso de fondo) y recargá la pantalla.

---

## 1. Preparación (SuperAdmin, web)

- [ ] **Planes** → el plan del edificio tiene tildado **«Incluye Marketplace»** (aparece la etiqueta verde en la lista).
- [ ] **Asignaciones** → el edificio de prueba tiene ese plan vigente.
- [ ] **Marketplace por edificio** → la lista muestra el edificio con su plan; «Configurar» → **Habilitar**, comisión **10**, datos para transferir (p. ej. «Banco X · Cuenta 123 · Titular: Administración · Alias: demo.edificio»). Guardar.
- [ ] Intentar habilitar un edificio **cuyo plan no incluye** el módulo → lo rechaza con un mensaje claro.
- [ ] Comisión fuera de 0–100 o datos para transferir de más de 1000 caracteres → los rechaza.

---

## 2. Menús según el acceso (lo primero que se ve)

Regla: **cada persona ve solo lo que el backend le va a atender** (módulo habilitado en *ese* edificio, plan que lo incluye, rol con permiso, edificio en su alcance).

| Quién | Dónde | Debe ver | NO debe ver |
|---|---|---|---|
| SuperAdmin | Web → Planes | «Marketplace por edificio»; «Cuenta del Marketplace» si hay edificio habilitado | — |
| Encargado (edificio **con** módulo) | Web → Amenities | «Pagos del Marketplace», «Reembolsos y reclamos», «Cuenta del Marketplace» | — |
| **Operador** (edificio con módulo) | Web → Amenities | «Pagos…», «Reembolsos y reclamos» | **«Cuenta del Marketplace»** |
| Encargado (edificio **sin** módulo) | Web y app | — | Nada de Marketplace |
| Encargado | App → Más | «Marketplace · pagos, reembolsos y reclamos» solo si el **edificio elegido** lo tiene (cambiá de edificio y comprobá que aparece/desaparece) | — |
| Ana, Beto, Carla (edificio con módulo) | App → Inicio | Atajo **Marketplace** | — |
| Elena (edificio sin módulo) | App → Inicio | — | Atajo Marketplace |
| **Carla (residente)** | App → barra inferior | Inicio, Expensas, Reclamos, Perfil | **«Pagos»** y «Mis pagos» (son solo del propietario) |
| Carla | App → Marketplace | Explorar y Mis reservas | **«Publicaciones»** y «Recibidas» (no puede publicar) |
| Dani (copropietario no principal) | App → Marketplace | Explorar y Mis reservas | «Publicaciones» |

- [ ] Cumplido todo el cuadro.
- [ ] **Apagar** el módulo del edificio (SuperAdmin) → en ~1 minuto los menús desaparecen para todos; volver a **encenderlo** → reaparecen. Los datos siguen ahí.

---

## 3. Publicar un espacio (Ana, app)

App → Marketplace → **Publicaciones** → «Publicar un espacio». Tres pasos.

- [ ] **Paso 1:** elegir la unidad (si tiene una sola, ya viene fija) y escribir el título «Cochera 12».
- [ ] **Paso 2:** fecha y hora **desde/hasta** en punto o y media; los **minutos de «desde» y «hasta» tienen que ser iguales** (19:30→22:30 vale; 19:00→22:30 no); la duración se muestra («3 horas»); puede cruzar la medianoche.
- [ ] **Paso 3:** precio por hora 20.000. Dice «Vas a recibir exactamente Gs. 20.000 por cada hora». **No se menciona la comisión** al publicar.
- [ ] «Publicar» → aparece en «Publicaciones» como **Activa**.
- Validaciones (todas deben **rechazar con un mensaje**):
  - [ ] Ventana en el pasado; menos de 1 hora; con fracciones (10:00→12:30); precio 0 o negativo; título vacío.
  - [ ] Una **segunda publicación de la misma unidad que se pisa** con la primera.
- [ ] **Editar** (cambiar título/precio/ventana) → se guarda. **Pausar** → pasa a «Pausada» y deja de verse en Explorar. **Reanudar** → vuelve.
- [ ] **Carla y Dani** no pueden publicar (no tienen la solapa).

---

## 4. Explorar y reservar (Beto, app)

App → Marketplace → **Explorar**.

- [ ] Ve la tarjeta «Cochera 12 · Unidad 302 · Gs. 20.000 / hora» con la ventana, y «Ya reservado: …» si hay tramos ocupados.
- [ ] **Ana no ve su propia publicación** en Explorar (ni puede reservarla).
- [ ] «Reservar» → elegir **desde cuándo** y **cuántas horas** (solo opciones libres). Debajo: **Precio 60.000 · Comisión por gestión 6.000 (10 %) · Total 66.000**.
- [ ] «Reservar» → pasa a la pantalla de pago con **cuenta regresiva de 10 minutos**.
- [ ] Reservar una **segunda** vez mientras tiene una esperando pago → lo rechaza («Ya tenés una reserva esperando pago…»).
- [ ] **Dos personas a la vez:** Beto y Carla eligen **el mismo horario** y tocan «Reservar» casi juntos → **solo uno** lo consigue; el otro recibe «Ese horario se acaba de reservar».
- [ ] Una reserva **parcial** (2 de las 10 horas de la ventana) cobra solo esas horas.

### 4.1 Vencimiento
- [ ] Reservar y **no pagar durante 10 minutos** → la reserva pasa a **«Vencida»**, llega el aviso «Reserva vencida», el horario queda libre (otro puede reservarlo) y **ya no se puede pagar** ni subir comprobante.
- [ ] «Cancelar reserva» **antes de pagar** → se cancela sin costo ni reembolso.

---

## 5. Pagar y revisar (Beto → Encargado)

### 5.1 Beto paga
- [ ] En la pantalla de pago ve el **total a transferir (66.000)**, los **datos para transferir** (los que cargó el SuperAdmin) y la **referencia `MP-…`**. *Esos datos solo se ven acá, mientras corre el plazo.*
- [ ] Adjunta la captura de la transferencia y envía → estado **«En revisión»**; la cuenta regresiva se detiene y el horario **queda bloqueado**.
- [ ] Un comprobante que **no** sea de la plataforma (otra URL) → rechazado.

### 5.2 El Encargado revisa
- [ ] Le llega **push + notificación** «Pago de reserva por revisar» (y la lista de la web/app lo muestra).
- [ ] **Recordatorios:** aplicar el atajo **0.4-C** → llega «Recordatorio: pago de reserva por revisar». Siguen **cada hora** hasta resolverlo.
- [ ] Web → **Pagos del Marketplace** (o app → Más → Marketplace → **Pagos**): ve comprador, unidad, horario, importes y el comprobante.
- [ ] Si la unidad de Beto tiene **deuda atrasada**: aparece el aviso «Unidad con pagos atrasados» (**solo lo ve el revisor**; ni Ana ni otros vecinos).
- [ ] **Confirmar:** pide verificar que el comprobante muestre **exactamente 66.000**. Se confirma → Beto recibe «Reserva confirmada»; **Ana** recibe «Reservaron tu espacio».
  - [ ] Un monto distinto al total **no se puede confirmar**.
  - [ ] Confirmar **dos veces** (dos revisores a la vez o repetido) → el segundo recibe «Este pago ya fue revisado».
- [ ] **Rechazar** (otra reserva): exige **motivo**; la reserva se **cierra** («Cancelada» con el motivo para Beto), se libera el horario y **debe reservar de nuevo**.
- [ ] **Ana ve quién reservó** («Reservado por Beto, unidad 101») en **Recibidas**, sin más datos.
- [ ] **Cuenta del Marketplace** (web, Encargado/Admin/SuperAdmin): aparece **«Ingreso por reserva +66.000»**.

---

## 6. Cumplimiento y acreditación del saldo

- [ ] **Aviso de inicio:** atajo **0.4-A** → en ~30 s Beto recibe «Tu reserva empezó. ¿La vas a usar?»; en «Mis reservas» ve **«Sí, voy»** y **«No la voy a usar»** (esta última pide motivo).
  - [ ] «No la voy a usar» **no devuelve dinero** ni cambia lo que cobra Ana; solo queda registrado (lo ve el Encargado si hay un reclamo).
  - [ ] Si no responde: no pasa nada.
- [ ] La reserva pasa sola a **«Finalizada»** cuando termina.
- [ ] **Acreditación:** atajo **0.4-B** → en ~30 s Ana recibe «Saldo a favor acreditado» (**+60.000**).
  - [ ] Ana lo ve en su **saldo a favor** (app → Pagos → Enviar pago muestra el saldo). Solo ve **su saldo**, no un detalle aparte de ganancias.
  - [ ] **Cuenta del Marketplace:** nuevo renglón **«Acreditado al propietario −60.000»**; el saldo de la cuenta queda en **6.000** (la ganancia de la gestión).
  - [ ] **El saldo se usa como siempre:** Ana envía un comprobante de expensas y el saldo se aplica con la regla de siempre (no cambió).
  - [ ] Procesar de nuevo (esperar otro ciclo) **no acredita dos veces**.
- [ ] **Reversa (SuperAdmin):** en la cuenta, el renglón «Acreditado» trae «Revertir» **solo si el saldo sigue intacto**; pide motivo; el saldo de Ana baja 60.000 y la cuenta suma +60.000 («Ajuste manual»). Si Ana ya gastó ese saldo en expensas, **no deja** revertir.

---

## 7. Cancelaciones y reembolsos

### 7.1 Cancela el comprador (antes del inicio)
Reserva confirmada que **todavía no empezó**.
- [ ] Beto toca «Cancelar reserva» → **antes de confirmar** ve: «Se te devolverán **60.000**… se cobrará la comisión por la gestión (**6.000**) y no se devuelve».
- [ ] Confirma → «Cancelada»; el horario queda libre; **Ana no recibe nada** por esa reserva (el aviso le dice que se canceló).
- [ ] Beto ve «**Te devolvemos 60.000 hasta el [fecha +72 h]**».
- [ ] **Encargado:** llega «Reembolso pendiente»; web → *Reembolsos y reclamos* → solapa **Reembolsos**: ve comprador, monto, motivo, plazo.
- [ ] «Marcar devuelto» (después de devolver el dinero **fuera del sistema**) → queda quién y cuándo; Beto recibe «Reembolso realizado».
  - [ ] **Cuenta:** **«Devolución al comprador −60.000»**; el saldo vuelve a **6.000** (la comisión que no se devuelve).
  - [ ] Marcar devuelto **dos veces** → el segundo avisa que ya estaba devuelto; no se duplica la salida.
- [ ] **No** se puede cancelar una reserva **ya empezada** («usá Reportar un problema») ni una **en revisión**.
- [ ] Atajo **0.4-D** (73 h sin devolver) → el Encargado recibe **una** alerta «Reembolso vencido» (y no se repite).

### 7.2 Cancela el propietario
- [ ] Ana (App → Marketplace → **Recibidas**) → «Cancelar reserva» → ve «Se le devuelve todo (**66.000**) y vos asumís la comisión (**6.000**): se te descuenta de tu saldo a favor y, si no alcanza, queda como deuda…». **Pide motivo (obligatorio).**
- [ ] **Con saldo a favor ≥ 6.000:** el saldo de Ana baja 6.000; la cuenta registra **«Comisión por cancelación del propietario +6.000»**; no hay deuda.
- [ ] **Con saldo menor** (p. ej. 2.000): se descuentan 2.000 y **4.000 quedan como deuda por gestión**.
- [ ] **Sin saldo:** toda la comisión (6.000) queda como deuda.
- [ ] **Encargado:** web → *Reembolsos y reclamos* → solapa **Deudas por gestión** muestra la deuda (propietario, reserva, motivo, monto).
- [ ] Beto recibe el aviso «El propietario canceló tu reserva» con el motivo, y el reembolso **total (66.000)** queda pendiente.
- [ ] **Próxima acreditación de Ana (mismo edificio):** al acreditarse otra reserva de 60.000, se le acreditan **54.000** (descontando la deuda de 6.000); la deuda queda saldada y la notificación lo explica. Si la deuda fuera mayor que la ganancia, esa reserva se aplica a la deuda y no se acredita saldo.
- [ ] Una **deuda de otro edificio no se descuenta** en este.

---

## 8. «Reportar un problema» (reclamos)

- [ ] **Reserva en curso** (atajo 0.4-A) o **terminada hace menos de 24 h** (0.4-B2): Beto (o Ana) ve **«Reportar un problema»** → escribe qué pasó → enviado.
- [ ] La **acreditación queda retenida** (a Ana le aparece «retenida por un reclamo»); el **Encargado** recibe «Reclamo en una reserva»; la **otra parte** recibe un aviso neutro (sin el detalle).
- [ ] Hay **un solo reclamo abierto** por reserva: el otro lado recibe «Ya hay un reclamo abierto».
- [ ] **No** se puede reportar: antes de empezar, pasadas 24 h del fin, o si ya se acreditó.
- [ ] **Encargado resuelve** (web → Reclamos → «Resolver», o app → Reclamos): ve importes, partes y **lo que respondió Beto al aviso de inicio**.
  - **A favor del propietario:** no se devuelve nada; la acreditación se libera y se acredita al vencer la ventana. Ambas partes reciben la explicación.
  - **A favor del comprador:** se devuelve **todo (66.000)**; Ana **no cobra** y **asume la comisión** (saldo o deuda, como si hubiera cancelado); si la reserva seguía en curso pasa a «Cancelada». Ambos reciben la explicación; el Encargado ve el reembolso pendiente.
  - [ ] La explicación es **obligatoria**. Resolver dos veces no duplica nada.

---

## 9. Cambio de propietario principal

- [ ] Con una reserva **confirmada o pendiente de acreditar** de la unidad 302 (a nombre de Ana), un administrador **desvincula a Ana como propietaria principal** (web → Asignaciones → «Quitar») y, si corresponde, asigna a Dani como principal.
- [ ] Las **publicaciones de Ana pasan a «Pausada»** al instante («El propietario dejó de ser el principal»).
- [ ] El **Encargado** recibe «Cambió el propietario principal de una unidad». Web → *Reembolsos y reclamos* → **Cambios de propietario** (o app → Marketplace → **Cambios**): nota «Sin leer».
- [ ] Al **abrir** la nota: explica qué pasó («las reservas ya hechas conservan a Ana: a ella se le acredita…»), lista las operaciones abiertas **con su estado actual**, y queda **«Leída» por [nombre]**. Se puede **descargar en PDF**.
- [ ] Si se dio de baja primero y se asignó el nuevo después, la nota **se completa sola** («Actualización: se asignó a Dani…»).
- [ ] **Dar de baja a un copropietario que no es el principal** no genera nota.
- [ ] La ganancia de las reservas ya hechas **sigue siendo de Ana**, no pasa a Dani.

---

## 10. Documentos y trazabilidad

### 10.1 Comprobante interno de reserva (PDF)
Disponible **solo con el pago confirmado**. Botón «Comprobante (PDF)» en *Mis reservas* y *Recibidas* de la app, y en el *Historial* de la web. Cada parte ve **su versión**:
- [ ] **Beto (comprador):** precio, comisión por gestión y **total pagado**; **sin** el nombre ni la ganancia del propietario.
- [ ] **Ana (propietaria):** quién reservó (nombre y unidad), precio por hora y **lo que recibe**; **sin** la comisión ni el total.
- [ ] **Encargado/Admin/SuperAdmin:** todo (propietario, comprador, neto, comisión, total, quién confirmó, archivo del comprobante y la **imagen adjunta** si es una captura).
- [ ] Todas llevan la franja **«ESTE DOCUMENTO NO ES UN COMPROBANTE FISCAL…»** en cada hoja.
- [ ] Una reserva cancelada y reembolsada también lo genera (muestra cancelación y reembolso).
- [ ] Otra persona (Carla sin relación, Elena) pide el PDF de una reserva ajena → **no lo obtiene**.

### 10.2 Historial económico (web, personal)
Cuenta del Marketplace → botón **«Historial»** de un renglón (o desde Reembolsos/Reclamos).
- [ ] Muestra los **importes congelados** y una **línea de tiempo** en orden: reserva creada → comprobante enviado → pago confirmado → reserva confirmada → movimiento de cuenta → (aviso de inicio, cancelación, reembolso, comisión asumida, reclamo, acreditación según corresponda), con **quién** lo hizo («Sistema» para lo automático) y los montos.
- [ ] Desde ahí se abre el comprobante interno en PDF.

---

## 11. La cuenta aparte (web)

Web → **Cuenta del Marketplace** (Administrador, Encargado, SuperAdmin; **no** el Operador).
- [ ] Extracto por **período** (por defecto el mes) con filtros de fecha; saldo inicial/final del período.
- [ ] Tarjetas: **saldo actual**, **pendiente de acreditar**, **reembolsos pendientes de devolver**, **deudas por gestión por descontar** y **ganancia de la gestión (a repartir)** = saldo − pendiente de acreditar − reembolsos pendientes.
- [ ] **Exportar a Excel**: hoja «Resumen» y hoja «Movimientos» (con los tipos en español). *Es el respaldo para repartir la comisión fuera del sistema.*
- [ ] Solo el **SuperAdmin** ve «Agregar ajuste» (importe con signo + concepto obligatorio) y «Revertir».
- [ ] **Conciliación:** el saldo de la cuenta = suma de sus movimientos (ver consulta en `DESPLIEGUE_MARKETPLACE.md`, sección 9).

Ejemplo de cómo debe quedar una reserva completa (66.000 de ingreso → 60.000 acreditados): **6.000** de ganancia de la gestión.

---

## 12. Seguridad (pruebas manuales)

- [ ] **Elena** (otro edificio/empresa) **no ve** el Marketplace del edificio de prueba ni sus publicaciones; el Encargado del *otro* edificio no ve sus pagos ni sus reclamos.
- [ ] **Beto y Carla no ven** las pantallas del personal (web) aunque conozcan la URL (el backend responde 403/404).
- [ ] **Solo el SuperAdmin** carga ajustes manuales y revierte acreditaciones (el Administrador de empresa y el Encargado no).
- [ ] **El precio nunca viene del cliente:** el desglose lo calcula siempre el servidor.
- [ ] **Límite de tasa:** tocar «Reservar»/«Confirmar» muchas veces seguidas (más de 12 acciones sensibles en un minuto) → «Hiciste demasiadas acciones seguidas. Esperá un momento…» (se recupera solo en segundos). Un uso normal no lo dispara.
- [ ] **Plan vencido** del edificio: el Encargado ve el sistema en solo lectura y **no puede confirmar ni rechazar** pagos (igual que en expensas).

---

## 13. Pruebas automáticas (complemento)

`dotnet test Condo.Tests` → **540 correctas** (SQLite en memoria). Con SQL Server real (variable `CONDO_TEST_SQLSERVER`, ver `DESPLIEGUE_MARKETPLACE.md` §10) → **550 correctas**, incluidas:
- **Aislamiento:** más de 300 intentos de acceso indebido (otra empresa, otro edificio, vecino ajeno, usuario final en pantallas del personal, personal en lo del SuperAdmin, token de otra empresa) sobre **todas** las operaciones; en cada una se exige 403/404 y que la base quede idéntica. (Se comprobó que la prueba falla si se rompe un chequeo a propósito.)
- **Concurrencia en SQL Server real:** 8 compradores reservando el mismo horario, doble confirmación, cancelar vs. cancelar, dos reclamos, resolver para ambos lados, 3 «devuelto» simultáneos, 4 acreditaciones simultáneas → siempre gana uno, los importes se asientan una sola vez y no hay errores sin controlar.
- Límite de tasa montado en un servidor HTTP real en memoria (429 + `Retry-After`), PDF para cada tipo de parte, historial, notas, deudas, reclamos, reembolsos y aviso de inicio.

---

## 14. Lista final de aceptación

- [ ] §1 Preparación · [ ] §2 Menús · [ ] §3 Publicar · [ ] §4 Reservar (+ vencimiento) · [ ] §5 Pago y revisión
- [ ] §6 Aviso de inicio y acreditación · [ ] §7 Cancelaciones y reembolsos · [ ] §8 Reclamos · [ ] §9 Cambio de principal
- [ ] §10 Documentos e historial · [ ] §11 Cuenta aparte y Excel · [ ] §12 Seguridad
- [ ] Los números cuadran: ingresos − acreditaciones − devoluciones + comisiones asumidas = saldo de la cuenta.
- [ ] Sin errores en `journalctl -u condo-py-api` durante toda la prueba.

## 15. Lo que **no** está (por decisión)
Pagar con saldo a favor, reembolso al saldo del comprador, cobro en efectivo al propietario, política de cancelación configurable, disponibilidad recurrente, imágenes en las publicaciones y protección de los comprobantes subidos (hoy son URL pública): están registrados como mejoras futuras en la especificación.
