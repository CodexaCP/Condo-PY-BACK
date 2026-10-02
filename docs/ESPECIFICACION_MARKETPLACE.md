# Marketplace de espacios temporales — Especificación en debate

Estado: **decisiones de negocio cerradas; plan E2E en [PLAN_MARKETPLACE_E2E.md](PLAN_MARKETPLACE_E2E.md); implementación en curso en la rama `feature/marketplace` de cada repo**. Última actualización: 2026-10-02 (quinta ronda de decisiones). Es el registro de lo hablado con Tony; se actualiza hasta que se diga "procesa" y se pase a plan de implementación.
Repos: `Condo-PY-BACK` (API .NET / SQL Server), `Condo-PY-WEB` (panel admin Angular), `CondoPY-APP` (Ionic: propietario/residente y sección del Encargado).

> **Regla para quien implemente:** las secciones 1 a 3 son decisiones cerradas. La sección 4 son decisiones abiertas: **preguntarlas, no asumirlas**. La sección 5 son mejoras ya acordadas como futuras. La sección 6 registra qué decisiones cambiaron. La sección 7 son los principios de simplicidad de uso, que mandan sobre cualquier detalle de pantalla.

---

## 1. Idea

Un propietario publica por un rato una unidad suya (p. ej. "Cochera 12") y otra persona del **mismo edificio** la reserva y paga por la plataforma. El propietario fija el precio que quiere recibir **por hora**; el sistema suma la comisión y calcula el total del comprador. No hay billetera nueva: **la ganancia del propietario va siempre a su saldo a favor (`OwnerCredit`)**. No existe el cobro en efectivo.

Ejemplo: 3 h × ₲20.000 = ₲60.000 base; comisión 10% = ₲6.000; el comprador paga ₲66.000; al propietario se le acreditan ₲60.000 de saldo a favor; los ₲6.000 son la comisión de la gestión.

---

## 2. Decisiones cerradas

### 2.1 Publicación y titularidad
- La publicación se hace sobre **una unidad** (el propietario elige una de sus unidades en un select; una por publicación) y lleva un **título libre** ("Cochera 12"). **No existe una entidad de "recurso" aparte ni "tipo de recurso"**: el título alcanza. **Sin imágenes.**
- Se publica la **unidad completa**: no hay dos publicaciones activas de la misma unidad en el mismo horario.
- Publica **solo el propietario principal** (`UnitOwner.IsPrimary`) de esa unidad. Si el cónyuge quiere publicar, usa el usuario del principal. La autorización se valida en el backend contra la relación actual `UnitOwner`.
- La ganancia es del propietario principal **vigente al crear la reserva** y **no cambia** aunque después cambie el principal. Si el principal cambia con operaciones abiertas, el sistema genera un **documento interno** (texto) que explica qué pasó y cuál es la situación actual; quien resuelva lo descarga al leerlo.
- Si el propietario **deja de ser el principal**, sus publicaciones sin reservar **se suspenden solas** (además se revalida en el backend al reservar).

### 2.2 Visibilidad y aislamiento
- Se ve **solo lo del edificio actual**, no de otros edificios ni de otras empresas. Quien tenga unidades en dos edificios cambia de edificio para ver el otro.
- Todo filtro de empresa/edificio se aplica en el backend (nunca confiar en lo que mande Angular).

### 2.3 Compradores
- Pueden comprar **propietarios y residentes** (ambos).

### 2.4 Precio, tiempo y comisión
- La reserva **puede empezar en `:00` o `:30`** (10:00, 10:30, 11:00…) y **dura siempre un número entero de horas, mínimo 1** (1 h, 2 h, 3 h; nunca 2 h 30). Por lo tanto también termina en `:00` o `:30`. La ocupación se guarda en bloques de 30 min.
- **La ventana publicada** es **una sola** por publicación: campos **fecha y hora "desde"** y **fecha y hora "hasta"** (puede cruzar medianoche, p. ej. hoy 20:00 → mañana 03:00; sin máximo de horas). Las horas se eligen en `:00` o `:30` y **desde y hasta deben tener los mismos minutos** (20:30 → 03:30 vale; 10:00 → 12:30 no), de modo que **la ventana siempre dura horas enteras** y nunca queda un tramo suelto de media hora.
- El propietario indica el **precio por hora**; el comprador puede reservar solo parte de la ventana publicada (p. ej. 2 de 10 horas) y **paga solo ese tiempo + la comisión de ese tiempo**. Como las horas son enteras, la base nunca tiene fracciones: solo se redondea la comisión.
- Comisión **configurable por edificio, solo por el SuperAdmin**; se **congela en la reserva** (base, comisión, total, neto propietario). Se calcula **una sola vez sobre el total de la reserva** (no por hora) y se **redondea siempre hacia arriba al guaraní entero**.
- El precio nunca se recalcula con la publicación actual: el histórico queda guardado en la operación.

### 2.5 Expiración
- Una reserva sin pago se vence a los **10 minutos, valor fijo para toda la plataforma** (no por edificio ni empresa; igual se guarda como configuración, no escrito en el código). El plazo corre solo hasta que el usuario sube el comprobante.
- Al vencer: se libera el horario, se **cierra la reserva y no se permite pagarla ni subir comprobante**; si el usuario aún quiere el espacio, crea una **reserva nueva**. Se avisa al usuario. (Cambia lo pedido al inicio, que mandaba los pagos tardíos a revisión: ver sección 6.) Si el usuario igual transfirió, **el dinero real queda fuera del sistema y la administración lo devuelve a mano**; la pantalla de pago muestra una **cuenta regresiva bien visible** para evitarlo. *(Aceptado.)*

### 2.6 Pagos
- Aprueban los **mismos roles que aprueban pagos hoy** (SuperAdmin, CompanyAdmin, CompanyOperator, BuildingManager, con su alcance de edificios). El flujo es idéntico al pago actual, **sin factura**. Comprobante adjunto; el PDF interno ("Comprobante interno de reserva") **no es documento fiscal** y se genera al pedirlo desde los importes congelados.
- **Después de subir el comprobante, el horario queda bloqueado** hasta que se revise. Mientras esté pendiente, quien debe revisar recibe en la app **alertas constantes** (insistentes) hasta que confirme o rechace. La pantalla de revisión debe permitir solo: ver el pago, confirmar o rechazar. El pago se puede seguir aprobando hasta el **fin de la reserva**; si termina sin revisarse, **lo resuelve el Encargado del edificio** (BuildingManager; el SuperAdmin es el dueño de la plataforma, no el operador del día a día). Alertas al revisor: al subir el comprobante, a los 15 minutos y luego cada hora hasta que se resuelva.
- **Si el Encargado rechaza el pago, la reserva se cierra** (con motivo obligatorio, visible para el comprador) y el comprador debe **crear otra**. No hay segundo plazo. Si ya había transferido dinero real, se resuelve a mano fuera del sistema, igual que en una reserva vencida.
- **No se aplica el bloqueo por mora** de amenities (`BlockOverdueAmenityReservations`) al marketplace. En su lugar, **la pantalla donde el Encargado (o quien aprueba) revisa el pago muestra un aviso si la unidad del comprador está en mora**, para que decida con ese dato. **Esa información no se muestra al publicador ni a otros vecinos** (es un dato sensible, y la mora se calcula por unidad, no por persona).
- Con el **plan del edificio vencido** (solo lectura/bloqueo) **no se pueden aprobar pagos del marketplace**, igual que hoy.
- El pago del marketplace es una **entidad nueva** con el mismo patrón que `OwnerPayment` (comprobante, revisión, estados), porque `OwnerPayment` exige coincidencia exacta con la deuda de expensas.

### 2.7 La cuenta aparte (por edificio)
- Hay **una cuenta aparte por edificio**, con todo trazado por edificio (la plataforma tiene empresas → condominios → edificios).
- **No es una cuenta bancaria real**: igual que hoy, solo **refleja entradas y salidas por cuenta y concepto**. **No es gasto, ingreso, caja ni fondo de reserva** del edificio y **no toca la contabilidad ni las Finanzas del edificio**. Lo que queda después de acreditar a los propietarios es la **comisión de la gestión: ganancias a repartir internas**.
- Movimientos: **entrada** = pago de reserva aprobado; **salida** = acreditación al saldo a favor del publicador. Todo automático y trazable.
- **Extracto** filtrable por período, con exportación a Excel. Lo **ven** SuperAdmin, CompanyAdmin y BuildingManager (con su alcance); el CompanyOperator no. **Solo el SuperAdmin puede editarlo** (movimientos manuales).
- El reparto de la comisión (CondoPY / administradora, p. ej. 50/50 o 70/30) **no lo calcula el sistema**: se saca el Excel del extracto y se negocia fuera.
- El diseño detallado de la cuenta (pantallas, movimientos exactos) se hace después.

### 2.8 Ganancia del propietario = solo saldo a favor
- **Se elimina el cobro por transferencia al propietario.** No hay estado de "liquidación", ni cuenta bancaria del propietario, ni pago manual a propietarios.
- Cuándo se acredita: pago aprobado, reserva confirmada, **fecha de fin cumplida** y **24 horas sin reclamos**. Entonces el neto del propietario (base, sin comisión) se acredita **automáticamente** a su `OwnerCredit` como un lote nuevo en `OwnerCreditMovement`, con la reserva de origen para que sea **trazable y reversible**.
- **El saldo se comporta exactamente como hoy**: se acumula, y se usa cuando el propietario envía su comprobante de pago de expensas, con la misma regla (monto + saldo cubren comprobantes completos exactos). No se enciende ninguna aplicación automática. Si el saldo es mayor que la deuda queda sin usar, y se **acepta así, como está hoy**.
- El propietario **solo ve su saldo** (no un detalle aparte de ganancias del marketplace).
- Reservas canceladas antes del uso o con reclamo abierto no acreditan.

### 2.9 Cancelaciones y reclamos
- **El comprador puede cancelar después de pagar, pero solo antes de que empiece el horario.** Una vez comenzada la hora, si no hay confirmación de uso ni aviso en contra, **el sistema asume que todo salió bien** y que se usó (se asume lo positivo).
- Al cancelar el comprador, **la comisión de gestión (10%) no se devuelve**: queda en la cuenta del edificio. La pantalla debe avisarlo antes de confirmar (mensaje tipo "se cobrará el 10% de comisión por la gestión de la reserva"). Lo único que se devuelve es la base, **a su saldo a favor, cuando se construya esa parte**. Se reconoce que un residente no propietario no podrá usar ese saldo hasta que exista "pagar con saldo" (sección 5).
- **Si cancela el propietario una reserva ya pagada:** debe indicar el **motivo**. Al comprador se le **devuelve todo, comisión incluida**, y **la comisión (10%) la asume el propietario que publicó**: se le descuenta de su saldo a favor o, si no alcanza, queda registrada como **deuda por gestión, visible para el Encargado, y se descuenta automáticamente de su próxima acreditación del marketplace**. No se suma al pago de expensas ni toca el cálculo de expensas.
- **Reclamos:** un único botón **"Reportar un problema"** con motivo, que pueden usar **comprador y propietario** durante las **24 horas posteriores al fin de la reserva**. Retiene la acreditación del saldo y avisa al Encargado, que lo resuelve. Cubre también el no-show.
- **Reembolso al comprador en la v1 ("reembolso pendiente"):** al cancelar se crea un registro con **monto** (solo la base si canceló el comprador; todo, comisión incluida, si canceló el propietario), **destinatario** y **motivo**. Aparece en una lista del **Encargado**, que devuelve el dinero **fuera del sistema** y lo marca **"devuelto"** (queda quién y cuándo). Al marcarlo, el extracto de la cuenta aparte registra solo la salida "devolución a comprador". El Encargado puede devolver **en cualquier momento, con un máximo de 72 horas** (no hay mínimo): al comprador se le informa que la devolución se hace "hasta en 72 horas", y si pasan 72 horas sin marcarla como devuelta, el Encargado recibe una alerta. Cuando exista "pagar con saldo", esto se reemplaza por un lote de saldo al comprador.
- **Revertir un saldo ya consumido en expensas:** solo lo hace **el SuperAdmin, a mano** (si el lote está intacto, se revierte sin problema).

### 2.10 Interfaces
- Propietario/residente: la **app**. Encargado: la **web** y también su **sección en la app** (la app ya tiene `pages/manager` con pagos y reservas y selector de edificio), donde se suma el marketplace.

### 2.10b Habilitación y pantallas
- **Habilitación igual que Finanzas:** el SuperAdmin activa el marketplace **por edificio** y el plan del edificio debe incluirlo (patrón `Building.FinanceModuleEnabled` + `Plan.IncludesFinanceModule`).
- **Datos para transferir:** un campo de texto **por edificio** (banco, titular, número, alias) que edita el SuperAdmin junto con la comisión. **Solo es visible para quien está haciendo una reserva, en la pantalla de pago**, cuando llega el momento de pagar; no es un dato que se muestre siempre. Para el futuro: podría ponerse opcional esa información en la ficha del propietario y del residente.
- **Aviso de inicio:** al empezar el horario, se avisa al comprador con **"Sí, voy"** y **"No la voy a usar"** (con motivo). **No hay devolución automática**: el motivo solo queda registrado y el propietario cobra normal. Si el comprador quiere su dinero usa **"Reportar un problema"** (el reclamo único) y lo resuelve el Encargado. Si no responde, no pasa nada (se asume que la usó).
- El **publicador ve quién reservó su espacio**: nombre y unidad (p. ej. "Reservado por Tony, unidad 5"), sin más datos.
- Quien **publica** solo ve el precio por hora que escribe (se le aclara que recibirá exactamente ese monto). El **comprador ve el desglose**: precio, **comisión por gestión** y total. La comisión se muestra con ese nombre.

### 2.11 Decisiones técnicas (las toma quien implementa, salvo objeción)
- Una publicación con reservas no puede cambiar su ventana ni su precio para las reservas ya hechas (el precio ya está congelado en cada reserva); los cambios solo rigen para reservas nuevas. Quitar una publicación con reservas pagadas equivale a cancelación del propietario (motivo + 10%).
- El propietario **no puede reservar su propia publicación**.
- Un usuario puede tener **como máximo una reserva en "Esperando tu pago"** a la vez (evita bloquear horarios).
- El Encargado **confirma o rechaza**; el sistema compara el monto del comprobante con el total esperado (no hay pagos parciales ni excedentes: si no coincide, se rechaza con motivo).
- Idempotencia con **índices únicos** (p. ej. un solo lote de saldo por reserva, un solo reembolso por reserva) y **control de versión de fila** (hoy no existe en el proyecto).
- **Tabla de auditoría/eventos propia** del marketplace, que sirve también de historial económico. Hoy solo existen `InvoiceAuditLog` y `CreditNoteAuditLog`, específicas.
- **Máquinas de estados separadas**: publicación, reserva y pago (la acreditación del saldo es un paso automático de la reserva, con su estado: pendiente / acreditado / retenido por reclamo / revertido). No mezclar estado de reserva con estado de pago.
- **Servicio central** de "edificios que puedo ver" para aislar (hoy `GetMyBuildingIdsAsync` está copiado en varios controladores y no hay filtros globales por empresa).
- Concurrencia: cada reserva ocupa **bloques de 30 min** guardados con **índice único (publicación, inicio de bloque)**, más transacción `Serializable` (patrón ya usado en `AmenitiesController.Reserve`, intervalos semiabiertos).
- Un proceso en segundo plano (patrón `BackgroundService` existente) para: vencer reservas sin pago, avisar el inicio de la reserva, repetir las alertas al revisor y acreditar el saldo cuando corresponde.

---

## 3. Hallazgos del código que condicionan el diseño

| Tema | Hallazgo |
|---|---|
| Amenities existentes | `Amenity`/`AmenityReservation`: precio fijo, estados `PendingPayment/PendingReview/Confirmed/Rejected/Cancelled`, comprobante, revisión del administrador. **`PendingPayment` bloquea el horario sin vencimiento**: defecto actual que conviene corregir aparte. Solo se cancela automáticamente por mora. |
| Saldo a favor | `OwnerCredit` = un monto por (propietario, empresa). `OwnerCreditMovement` lleva **lotes** (`Generated`, con `RemainingAmount`) y **aplicaciones** (`Applied`). Ya hay un origen distinto del pago (nota de crédito, `CreditNoteId`); el marketplace sería un tercer origen (`MarketplaceReservationId`). |
| Cómo se consume el saldo | Solo al pagar expensas (`CoverWithCredit`): primero se prueba el monto solo; **solo si no cierra exacto** se suma el saldo, y **monto + saldo debe cubrir comprobantes completos exactos**. La aplicación automática al publicar período y la manual están **apagadas** (`OwnerCreditFeature.Enabled = false`). **Verificado:** un saldo mayor que la deuda pendiente **no se puede consumir**. Decidido aceptarlo como está. |
| Saldo entre edificios | El saldo es por empresa, no por edificio: una ganancia del edificio A puede aplicarse a expensas del edificio B de la misma empresa, aunque la cuenta aparte sea por edificio. |
| Datos para transferir | La app de pago de expensas **no muestra datos bancarios**: el propietario solo adjunta la captura de su transferencia. No hay entidad en el backend con cuenta/alias de cobro. Para el marketplace hace falta decidir de dónde salen (ver 2.10b). |
| Auditoría | No hay auditoría genérica. |
| Comprobantes | `/api/uploads` guarda en `wwwroot/uploads/GUID.ext` y se sirve **sin autenticación**. |
| Aprobación de pagos | `CanManagePayments()` es por rol; no hay interruptor por edificio. |
| Habilitación por edificio | Existe el patrón `Building.FinanceModuleEnabled` + `Plan.IncludesFinanceModule` (`FinanceModuleGate`). |
| Contador del edificio | Si el comprador transfiere a una cuenta bancaria real del edificio, esos depósitos no están en Finanzas del edificio; el Excel del extracto del marketplace es el respaldo para explicarlos. |

---

## 4. Decisiones abiertas (preguntar, no asumir)

**No quedan decisiones de negocio abiertas.** Las dudas técnicas se resuelven fase por fase en [PLAN_MARKETPLACE_E2E.md](PLAN_MARKETPLACE_E2E.md).

---

## 5. Pendientes y mejoras acordadas para más adelante

- **Pagar con el propio saldo a favor** (el comprador usa su `OwnerCredit` para reservar): marcado como mejora pendiente.
- **Reembolso de la base al saldo del comprador** (ver 4.1): "cuando hagamos esa parte".
- **Proteger los comprobantes subidos** (marketplace y también los de expensas): hoy son URL pública. Decidido que se protegen, pero se deja **pendiente como trabajo aparte**. Al proteger, hay que cambiar app y web (las imágenes protegidas no se muestran con un enlace simple: descarga autenticada o URL firmada), decidir qué hacer con los archivos y URL viejas y si se protegen también firmas y otras imágenes subidas.
- **Diseño específico de la cuenta aparte** (extracto, movimientos, Excel).
- **Política de cancelación** configurable (congelada en la reserva al crearla).
- **Disponibilidad recurrente** y varias ventanas por publicación.
- **Datos bancarios opcionales en la ficha del propietario y del residente** (idea para el futuro).
- **Cobro en efectivo al propietario** (transferencia): descartado por ahora por la complejidad; podría reconsiderarse.

---

## 6. Cambios de decisión (historial)

- **2026-10-02:** se descarta el cobro por transferencia al propietario (la idea original tenía "Opción 1: recibir el dinero" con estados de liquidación). La ganancia va **solo a saldo a favor, automática y trazable**. Se eliminan la entidad/máquina de liquidación y la cuenta bancaria del propietario.
- **2026-10-02:** la comisión deja de ser "ingreso de la plataforma" en el sistema: es parte de las ganancias a repartir internas, en la cuenta aparte por edificio, y se reparte fuera del sistema.
- **2026-10-02:** el momento de acreditar pasa de "al aprobar el pago" a "aprobado + reserva terminada + 24 h sin reclamos".
- **2026-10-02:** **horas enteras → bloques de 30 minutos** con mínimo 1 hora.
- **2026-10-02:** la idea original mandaba el pago tardío de una reserva vencida a revisión; ahora **una reserva vencida se cierra y no admite pago**.
- **2026-10-02:** la cuenta aparte pasa de "cuenta que recibe el dinero" a **extracto contable, no bancario**.
- **2026-10-02:** se permite **cancelar después de pagar solo antes de que empiece el horario**; después se asume que se usó. El propietario que cancela debe dar motivo y asume el 10% de comisión.
- **2026-10-02:** el revisor de pagos sin resolver es el **Encargado del edificio**, no el SuperAdmin; el SuperAdmin solo interviene a mano en reversas de saldo consumido y movimientos manuales de la cuenta.
- **2026-10-02:** la duración de la reserva pasa a **horas enteras** (inicio en `:00` o `:30`), no cualquier múltiplo de 30 min.
- **2026-10-02:** la deuda del propietario que cancela se cobra **descontándola de su próxima acreditación**, no en el pago de expensas.
- **2026-10-02:** la ventana publicada tiene desde/hasta con los mismos minutos (`:00` o `:30`) y siempre dura horas enteras.
- **2026-10-02:** un pago rechazado **cierra la reserva** (no hay segundo plazo). El bloqueo por mora de amenities **no se aplica** al marketplace; se avisa al publicador (detalle abierto).
- **2026-10-02:** los datos para transferir son **por edificio** y solo se ven en la pantalla de pago de una reserva en curso.
- **2026-10-02:** el aviso de mora del comprador lo ve **solo el Encargado/quien aprueba el pago**, no el publicador (dato sensible). El publicador ve nombre y unidad de quien reservó.
- **2026-10-02:** reembolso v1 = registro "reembolso pendiente" que el Encargado devuelve a mano y marca como devuelto; sin plazo mínimo y con **máximo 72 horas** (alerta al Encargado si se pasa).

---

## 7. Principios de simplicidad (mandan sobre cualquier detalle)

1. **Pocos pasos.** Publicar = elegir unidad → elegir fecha y desde/hasta → poner precio por hora. Quien publica solo ve su precio por hora; el comprador ve el desglose (ver 2.10b).
2. **El comprador ve primero lo disponible**: tarjetas con título, horario y precio por hora; elige desde/hasta; ve el desglose (precio, servicio, total) antes de reservar.
3. **Pago guiado en una sola pantalla**: cuenta regresiva de 10 minutos bien visible, datos para transferir, botón para adjuntar el comprobante.
4. **Estados en lenguaje humano y pocos**: "Esperando tu pago", "En revisión", "Confirmada", "Finalizada", "Cancelada", "Vencida". Los estados internos más finos no se muestran.
5. **Cero detección automática compleja**: sin estado "en uso", sin no-show automático; un único mecanismo de reclamo.
6. **El Encargado resuelve desde el celular** con lo mínimo: ver el pago, confirmar o rechazar; alerta insistente hasta que lo haga.
7. **Avisar antes, no después**: la comisión no devuelta se explica antes de cancelar; la cuenta regresiva se ve antes de que venza.
8. **Reusar lo que la gente ya conoce**: mismas pantallas, colores y flujo de comprobante que el pago de expensas; el saldo se ve donde ya se ve.
