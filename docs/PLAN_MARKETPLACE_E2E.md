# Marketplace de espacios temporales — Plan de implementación E2E

Base: [ESPECIFICACION_MARKETPLACE.md](ESPECIFICACION_MARKETPLACE.md) (las decisiones de negocio están cerradas ahí; este documento dice **cómo y en qué orden** se construye). Fecha: 2026-10-02.
Repos y rama de trabajo: `Condo-PY-BACK`, `Condo-PY-WEB`, `CondoPY-APP`, todos en la rama **`feature/marketplace`**.

> **Estado de las fases** (se actualiza al cerrar cada una):
> **Estado:** Fases 1 y 2 — **hechas y desplegadas** · Fases 3 a 9 — **hechas y commiteadas** (API con **550 pruebas verdes** con SQL Server real, 540 sin él; web y app compilan limpio). Las **migraciones** de las fases 5 y 7 ya están aplicadas en el VPS; la de la fase 8 (`MarketplaceHandoverNotes`) y el código de las fases 3 a 9 están **pendientes de desplegar**. Traen migración la 1, 2, 5, 7 y 8, cada una con su `.sql` (acumulado en `docs/sql/marketplace_acumulado.sql`). Guías: `DESPLIEGUE_MARKETPLACE.md` y `GUIA_PRUEBAS_MARKETPLACE.md`. Paso previo a la 7 (menús según el acceso real): hecho (ver 2.1).

---

## 0. Reglas de trabajo

- **Commits** solo a nombre de Tony, **sin** `Co-Authored-By`, **sin push**. Mensajes cortos en español, sin prefijos (estilo del historial: "Finanzas del edificio: rubro en las plantillas…"). Cada commit compila limpio.
- **Verificación** de cada fase: BACK `dotnet build Condo.Api/Condo.Api.csproj` sin errores ni advertencias nuevas; WEB `ng build` limpio; APP `ng build`/compilación limpia. **Tony prueba la interfaz él mismo**: no se levantan servidores de prueba ni el navegador integrado. La lógica del backend se verifica con **pruebas automáticas** (proyecto de pruebas, desde la fase 2).
- **Migraciones EF** a mano en el VPS (ver `vps_deploy`): cada migración trae su archivo `.sql` idempotente al lado (patrón `…OwnerPaymentWebChannel.sql`: cabecera con instrucciones, bloques con `IF NOT EXISTS` sobre `__EFMigrationsHistory`, verificación final antes del `COMMIT`). Se aplica **antes** de desplegar el código.
- **No se toca** el comportamiento de expensas/pagos existentes salvo lo estrictamente necesario (un nuevo origen de lote en `OwnerCreditMovement`). `OwnerPayment` no se reutiliza.
- Cada fase termina con un **criterio de salida** verificable y deja la rama en un estado que compila y no cambia nada para edificios sin el módulo habilitado.
- Nombres: prefijo **`Marketplace`** en entidades, DTOs, servicios, controladores y rutas (`api/marketplace/...`). Comentarios en español, breves, solo del porqué.

---

## 1. Arquitectura objetivo

### 1.1 Modelo de datos (BACK, SQL Server)
Reutiliza: `Building`, `Unit`, `UnitOwner` (titularidad), `UnitResident`, `ApplicationUser`, `OwnerCredit` + `OwnerCreditMovement` (saldo), `Notification` + `PushDispatcher`, `/api/uploads` (comprobantes), `QuestPDF`, `ClosedXML` (Excel), `PlanAccessPolicy`/`PlanRestrictionMiddleware`, `IUnitOverdueService`, patrón `FinanceModuleGate`, patrón de transacción `Serializable` de `AmenitiesController.Reserve`, patrón `BackgroundService`.

Entidades **nuevas** (todas con `CompanyId`, `BuildingId`, `IsDeleted`, fechas UTC; importes `decimal(18,2)`; estados guardados como texto):

| Entidad | Para qué | Claves / restricciones |
|---|---|---|
| `Building` (+3 campos) | `MarketplaceEnabled`, `MarketplaceCommissionPercent` (def. 10), `MarketplaceTransferInfo` | — |
| `Plan` (+1 campo) | `IncludesMarketplace` | — |
| `MarketplaceListing` | Publicación: `UnitId`, `OwnerId` (principal al publicar), `Title`, `WindowStartUtc`, `WindowEndUtc`, `HourlyPrice`, `Status` (Active/Suspended/Closed) | Ventana de horas enteras; una unidad no puede tener dos publicaciones activas solapadas |
| `MarketplaceReservation` | La operación comercial. `ListingId`, `BuyerUserId`, `OwnerId` (**congelado**), `StartsAtUtc`, `EndsAtUtc`, `Hours`, **importes congelados** (`HourlyPrice`, `BaseAmount`, `CommissionPercent`, `CommissionAmount`, `TotalAmount`, `OwnerNetAmount`), `Status`, `ExpiresAtUtc`, `CreditStatus`, cancelación (quién, motivo, cuándo), respuesta al aviso de inicio, `RowVersion` | Un comprador, una reserva `PendingPayment` a la vez |
| `MarketplaceReservationSlot` | Un renglón por bloque de 30 min ocupado | **Índice único** `(ListingId, SlotStartUtc)` filtrado a reservas activas: la base impide la doble reserva |
| `MarketplacePayment` | Comprobante de la reserva: `ReservationId`, `ComprobanteUrl`, `Amount`, `Status` (Submitted/Approved/Rejected), revisor, motivo, fechas, `RowVersion` | Un pago `Approved` por reserva (índice único filtrado) |
| `MarketplaceAccountMovement` | Extracto de la cuenta aparte: `Kind` (PaymentIn, OwnerCredit, RefundOut, Adjustment), `Amount`, `ReservationId?`, concepto, quién | **Índice único** `(ReservationId, Kind)` para los automáticos: idempotencia |
| `MarketplaceRefund` | "Reembolso pendiente": monto, destinatario, motivo, `Status` (Pending/Returned), quién y cuándo devolvió, alerta a 72 h | Único por reserva |
| `MarketplaceClaim` | "Reportar un problema": quién, motivo, estado, resolución | Una abierta por reserva |
| `MarketplaceOwnerDebt` | Deuda por gestión del propietario que cancela | Se descuenta de su próxima acreditación |
| `MarketplaceEvent` | Auditoría + historial económico: usuario, fecha, acción, entidad, estado anterior/nuevo, IP, JSON con importes | Solo inserción |
| `MarketplaceHandoverNote` | Documento interno cuando cambia el propietario principal con operaciones abiertas | — |
| `OwnerCreditMovement` (+1 campo) | `MarketplaceReservationId` | **Índice único filtrado**: un lote por reserva |

Configuración de plataforma (no por edificio): `Marketplace:PaymentTimeoutMinutes = 10` en `appsettings` (fijo, no escrito en el código), más constantes de negocio nombradas (24 h de reclamo, 72 h de reembolso, alertas del revisor).

### 1.2 Máquinas de estados (transiciones explícitas; cualquier otra se rechaza)
- **Publicación:** `Active ⇄ Suspended` (automática si deja de ser el principal, o manual por el propietario/Encargado) → `Closed` (terminó la ventana o se canceló).
- **Reserva:** `PendingPayment → InReview → Confirmed → Completed`; ramas: `PendingPayment → Expired`, `PendingPayment|InReview → Rejected` (cierra), `PendingPayment|Confirmed → Cancelled` (comprador: antes del inicio; propietario: con motivo). Estado de acreditación aparte: `None → Pending → Credited`, con `Held` (reclamo) y `Reversed`.
- **Pago:** `Submitted → Approved | Rejected`.
- **Estados que ve la gente** (los 6 del principio de simplicidad): Esperando tu pago, En revisión, Confirmada, Finalizada, Cancelada, Vencida (y Rechazada se muestra como Cancelada con el motivo).

### 1.3 Seguridad y aislamiento (transversal)
- **Servicio central** `MarketplaceScope`: "edificios que puedo ver" a partir de `UnitOwner`/`UnitResident` (usuario final) o `AccessScopeService` (personal). **Un solo edificio activo por consulta**; el edificio nunca viene "confiado" del cliente.
- Toda operación valida en el backend: módulo habilitado (`MarketplaceModuleGate`: edificio + plan), plan no vencido para escrituras del personal (ya lo hace el middleware), titularidad principal, estado y transición, precio y total (se recalculan en el servidor, nunca se aceptan del cliente), tenant/edificio.
- Idempotencia: índices únicos + `RowVersion` + transacciones; las operaciones financieras (aprobar pago, acreditar, devolver, descontar deuda) son re-ejecutables sin duplicar efectos.

### 1.4 Procesos en segundo plano (`MarketplaceBackgroundService`, ciclo corto, p. ej. 1 min)
Vence reservas sin pago a los 10 min · repite alertas al revisor (al subir, +15 min, cada hora) · avisa el inicio de la reserva · pasa a Completed al fin · acredita saldo (fin + 24 h sin reclamo) · alerta de reembolso a 72 h · suspende publicaciones de quien dejó de ser principal.

---

## 2. Fases

Cada fase indica: objetivo, trabajo por repo, migración, pruebas, **criterio de salida** y commits previstos.

### Fase 1 — Cimientos: habilitación y configuración por edificio  *(BACK + WEB)*
**Objetivo:** que el SuperAdmin pueda activar el marketplace por edificio, fijar su comisión y cargar los datos para transferir; que el plan lo incluya. Sin flujo para usuarios todavía.
- **BACK:** campos en `Building` y `Plan`; DTOs/`PlansController` (alta, edición, copia, lectura); `MarketplaceModuleGate` (mismo patrón que `FinanceModuleGate`); `MarketplaceAdminController` solo SuperAdmin (`GET api/marketplace/admin/buildings`, `PUT api/marketplace/admin/buildings/{id}`; activar exige plan que lo incluya; comisión 0–100 con 2 decimales; datos de transferencia ≤ 1000 caracteres; apagar conserva los datos); registro en `Program.cs`; migración `MarketplaceModuleBase` + `.sql`.
- **WEB:** `includesMarketplace` en el formulario de planes; página **"Marketplace"** (solo SuperAdmin) con la lista de edificios, estado del plan, interruptor, comisión y datos para transferir; ruta y entrada de menú.
- **APP:** sin cambios (solo rama).
- **Criterio de salida:** BACK y WEB compilan limpios; la migración y su `.sql` existen; edificios sin el módulo no cambian en nada.
- **Commits:** (1) BACK: campos, gate, API admin y migración; (2) WEB: plan y página de administración; (3) docs: especificación + plan.

### Fase 2 — Núcleo de dominio, reglas puras y pruebas  *(BACK)*
**Objetivo:** el corazón del negocio, **sin pantallas**, con pruebas.
- Proyecto **`Condo.Tests`** (xUnit; SQLite en memoria para índices únicos e idempotencia; arnés opcional contra SQL Server real por variable de entorno para concurrencia `Serializable`).
- Entidades del núcleo hasta la fase 6 (`MarketplaceListing`, `MarketplaceReservation`, `MarketplaceReservationSlot`, `MarketplacePayment`, `MarketplaceAccountMovement`, `MarketplaceEvent` y el campo `MarketplaceReservationId` en `OwnerCreditMovement`), enums de estados, configuración EF, migración `MarketplaceCore` + `.sql`. **`MarketplaceRefund`, `MarketplaceClaim` y `MarketplaceOwnerDebt` se crean en la fase 7, y `MarketplaceHandoverNote` en la fase 8**, cada una con su migración, para no diseñar tablas por adelantado.
- Clases puras y probadas: `MarketplacePricing` (base = precio/hora × horas enteras; comisión sobre el total, **redondeo hacia arriba al guaraní**; neto propietario), `MarketplaceWindowRules` (desde/hasta en `:00`/`:30`, mismos minutos, horas enteras, futuro, cruce de medianoche), `MarketplaceStateMachine` (transiciones permitidas), generador de bloques de 30 min y solapamientos con límites semiabiertos (`19–22` no choca con `22–23`).
- `MarketplaceScope` (visibilidad por edificio/empresa) y `MarketplaceAudit` (escritura en `MarketplaceEvent`).
- **Pruebas:** cálculo (incl. redondeo), ventana, solapamientos, transiciones válidas/ inválidas, unicidad de bloques, aislamiento por empresa/edificio.
- **Criterio de salida:** pruebas verdes; nada expuesto por API todavía.

### Fase 3 — Publicaciones  *(BACK + APP)*
**Objetivo:** el propietario principal publica y gestiona sus publicaciones.
- **BACK:** `api/marketplace/listings` (crear, editar, suspender/reanudar, cerrar, listar las mías); valida titularidad **principal** de la unidad, módulo habilitado, ventana, que no se solape con otra publicación activa de la misma unidad, precio > 0. Suspensión automática si deja de ser principal. Eventos de auditoría.
- **APP:** sección **"Marketplace"** para el propietario: "Publicar" en tres pasos (unidad → fecha/hora desde-hasta en pasos de 30 min → precio por hora; sin avisos de comisión), "Mis publicaciones".
- **Pruebas:** residente no publica; copropietario no principal no publica; publicación ajena no se edita; solapamiento por unidad; precio y total nunca vienen del cliente.
- **Criterio de salida:** publicar y ver mis publicaciones de punta a punta.

### Fase 4 — Explorar y reservar (concurrencia y expiración)  *(BACK + APP)*
**Objetivo:** el comprador ve lo disponible de **su edificio**, elige desde/hasta y crea la reserva sin posibilidad de doble reserva.
- **BACK:** `GET listings` (solo edificio actual, publicaciones activas con disponibilidad calculada en el servidor), `POST reservations` (transacción `Serializable` + índice único de bloques; límite de **una** reserva `PendingPayment` por usuario; no reservar lo propio; precio congelado; `ExpiresAtUtc`); `MarketplaceBackgroundService` (vencimiento a 10 min, libera bloques).
- **APP:** tarjetas (título, horario, precio por hora), selector desde/hasta dentro de la ventana, desglose (precio, comisión por gestión, total), botón Reservar, cuenta regresiva.
- **Pruebas:** reserva normal, consecutiva, solapada parcial, dos usuarios a la vez, expiración libera el horario, no se paga una reserva vencida.
- **Criterio de salida:** reservar y ver cómo vence.

### Fase 5 — Pago y revisión  *(BACK + APP + WEB)*
**Objetivo:** pago con comprobante y revisión por el Encargado, con alertas.
- **BACK:** datos para transferir visibles **solo** en el pago de una reserva en curso; subir comprobante (para el plazo de 10 min y pasa a `InReview`); revisión (confirmar/rechazar con motivo; monto del comprobante = total esperado; rechazo cierra la reserva); aviso de mora de la unidad del comprador **solo** al revisor; alertas al revisor (push + notificación in-app); movimiento `PaymentIn` automático; idempotencia (aprobar dos veces no duplica).
- **APP:** pantalla de pago en una sola vista (cuenta regresiva, datos, adjuntar comprobante); estado de mi reserva; **sección del Encargado**: lista de pagos por revisar con confirmar/rechazar.
- **WEB:** pantalla de revisión para los roles que aprueban pagos.
- **Pruebas:** pago correcto, insuficiente, excedente, aprobado, rechazado, duplicado, comprobante tardío, plan vencido bloquea al personal, aislamiento entre edificios/empresas.

### Fase 6 — Cuenta aparte y acreditación al saldo  *(BACK + WEB)*
**Objetivo:** la plata se refleja bien y el propietario cobra en su saldo.
- **BACK:** acreditación automática (reserva terminada + 24 h sin reclamo) como **nuevo lote** de `OwnerCreditMovement` con `MarketplaceReservationId` (idempotente) y `OwnerCredit.Amount`; movimiento `OwnerCredit` en el extracto; descuento de deuda por gestión pendiente; extracto por edificio y período con filtros; exportación a Excel; permisos (ver: SuperAdmin/CompanyAdmin/BuildingManager; editar: solo SuperAdmin); reversa de lote intacto.
- **WEB:** pantalla "Cuenta del marketplace" (extracto, filtros, Excel; edición manual solo SuperAdmin).
- **Pruebas:** propietario sin saldo / con saldo / acreditación doble / uso posterior del saldo en expensas (el flujo existente de `CoverWithCredit` sigue igual) / reversa.
- **Criterio de salida:** una reserva completa termina en saldo y en extracto, sin duplicados.

### 2.1 Paso previo a la fase 7 — Menús según el acceso real  *(BACK + WEB + APP, sin migración)*
**Regla (rige también para las fases 7 a 9):** un usuario solo ve una opción de menú si el backend se la va a atender: módulo habilitado **en ese edificio**, plan que lo incluye, rol con permiso y edificio dentro de su alcance. Toda pantalla nueva del marketplace entra al menú con esa condición.
- **BACK:** `GET api/marketplace/staff-buildings` (personal): edificios de su alcance con el módulo disponible y, por edificio, `canReviewPayments` (los 4 roles del personal), `canViewAccount` (SuperAdmin, Administrador de empresa y Encargado; no el Operador) y `canEditAccount` (solo SuperAdmin). Usuario final recibe lista vacía. Los roles que ven la cuenta quedan en una sola constante (`MarketplaceScope.AccountViewerRoles`), usada también por el extracto.
- **WEB:** `MarketplaceAccessService` (mismo patrón que `FinanceAccessService`); «Pagos del Marketplace» y «Cuenta del Marketplace» aparecen solo si hay un edificio donde corresponden; los selectores de edificio de ambas pantallas listan solo esos edificios (antes listaban todos y el elegido sin módulo respondía 403).
- **APP:** en la sección del Encargado, «Marketplace · pagos por revisar» aparece solo si el **edificio elegido** lo tiene disponible (se recalcula al cambiar de edificio y al entrar a «Más»). En la sección del propietario/residente, la pestaña «Pagos» y el atajo «Mis pagos» se muestran solo al rol Propietario (el backend responde 403 a un residente; ya estaba así antes del marketplace).
- **Pruebas:** 7 nuevas en `MarketplaceScopeTests` (alcance, módulo apagado, plan sin marketplace, rol sin cuenta, SuperAdmin, otra empresa, usuario final).
- **Pendiente conocido (no del marketplace):** las rutas de la web no tienen guard por rol/módulo; ocultar el menú no impide abrir la URL a mano (el backend igual responde 403 y las pantallas del marketplace muestran el aviso).

### Fase 7 — Cancelaciones, reembolsos, reclamos y aviso de inicio  *(BACK + APP + WEB)* — **HECHA (2026-10-02)**
**Lo construido** (migración `MarketplaceCancellationsAndClaims` + `.sql`; entidades `MarketplaceRefund`, `MarketplaceClaim`, `MarketplaceOwnerDebt`; 4 campos de aviso de inicio en la reserva):
- **Cancelar una reserva pagada:** el comprador solo antes del inicio (se devuelve la base; la comisión no); el propietario con motivo obligatorio (se devuelve todo y asume la comisión: de su saldo a favor, consumiendo lotes, y lo que falte queda como **deuda por gestión**, que se descuenta de su próxima acreditación **en el mismo edificio**). Libera el horario, no acredita nada. `GET …/cancel-preview` calcula en el servidor lo que se avisa antes de confirmar. Cancelar sin pagar sigue igual (sin reembolso).
- **Reembolso pendiente** (único por reserva): lista del Encargado, "marcar devuelto" (una sola vez; asienta `RefundOut` en la cuenta), alerta única a las 72 h. El comprador ve "te lo devuelven hasta…".
- **Reclamo** ("Reportar un problema"): comprador o propietario, desde que empieza la reserva hasta 24 h después de su fin, mientras no se haya acreditado; retiene la acreditación (`Held`); un reclamo abierto por reserva. El Encargado resuelve **a favor del propietario** (se libera y se acredita) o **a favor del comprador** (decisión de Tony 2026-10-02: se devuelve todo, el propietario asume la comisión como si hubiera cancelado; una reserva en curso pasa a Cancelada).
- **Aviso de inicio:** el proceso de fondo avisa al comprador al empezar; "Sí, voy" / "No la voy a usar" (con motivo) solo se registra. El Encargado lo ve al resolver un reclamo.
- **Reservas recibidas:** el propietario ve quién reservó (nombre y unidad), lo que va a recibir y puede cancelar o reportar (`GET reservations/on-my-listings`).
- **Cuenta aparte:** nuevo movimiento `CancellationFee` (comisión descontada del saldo del propietario), y el resumen suma `PendingRefunds` (se resta de la ganancia de la gestión), `OwnerDebtsPending` y `TotalCancellationFees`; el Excel los incluye.
- **WEB:** pantalla «Reembolsos y reclamos» (reembolsos, reclamos, deudas por gestión) en el menú solo donde el módulo está disponible. **APP:** botones y avisos en «Mis reservas», solapa «Recibidas», y en la sección del Encargado las solapas Pagos / Reembolsos / Reclamos; los avisos abren la lista correcta.
- **Pruebas:** 108 nuevas (462 en total) sobre reglas puras, cancelaciones, reembolsos, reclamos, deudas, aviso de inicio, aislamiento y garantías de la base.
- **Límites conocidos:** solo cancela el propietario que figuraba al crear la reserva (si dejó de ser el principal, la vía es el reclamo); un propietario/Encargado no cancela una reserva "en revisión" (se confirma o rechaza); el Operador revisa y devuelve pero no ve la cuenta.

**Plan original de la fase:**
- Cancelación del comprador (antes del inicio; comisión no se devuelve; aviso previo), del propietario (motivo; devolución total; deuda por gestión), registro de **reembolso pendiente** con lista del Encargado, marca "devuelto" y alerta a 72 h; movimiento `RefundOut`.
- **Reclamo** ("Reportar un problema", 24 h tras el fin) que retiene la acreditación y avisa al Encargado, que lo resuelve.
- **Aviso de inicio** ("Sí, voy" / "No la voy a usar" con motivo, sin devolución automática).
- **Pruebas:** cancelación antes/después de acreditar, reembolso duplicado, deuda de gestión, reclamo que retiene, aviso sin respuesta.

### Fase 8 — Documentos y trazabilidad  *(BACK + APP + WEB)* — **HECHA (2026-10-03)**
**Lo construido** (migración `MarketplaceHandoverNotes` + `.sql`, solo una tabla nueva):
- **Comprobante interno de reserva (PDF, QuestPDF, no fiscal con la franja en cada hoja):** `GET api/marketplace/reservations/{id}/receipt-pdf`. Se arma al pedirlo desde los importes congelados; solo con el pago confirmado. Cada parte recibe **su versión**: el personal ve todo (propietario, comprador, neto, comisión, total, quién confirmó, archivo del comprobante de transferencia y la imagen adjunta si existe); el comprador ve precio, comisión y total (sin nombre ni ganancia del propietario); el propietario ve quién reservó (nombre y unidad), precio por hora y lo que recibe (sin comisión ni total). Cada descarga queda en la auditoría (`receipt.generated`).
- **Nota de cambio de propietario principal** (`MarketplaceHandoverNote`): se genera desde `UnitOwnersController` (baja del principal). Suspende en el momento las publicaciones que ya no corresponden y, si le quedaron operaciones abiertas (pago en revisión, confirmadas, finalizadas sin acreditar, con reclamo abierto o con reembolso pendiente), guarda un texto con lo que pasó y la situación al momento del cambio y avisa al personal. Las reservas siguen a nombre del propietario original (su ganancia no pasa al nuevo). Si primero se da de baja al anterior y después se asigna el nuevo, la nota se completa sola (hasta 60 días). Se abre con la **situación actual** de cada operación; abrirla o descargarla la marca como leída (quién y cuándo). `GET handover-notes`, `GET handover-notes/{id}`, `GET handover-notes/{id}/pdf`. Un fallo al generarla no revierte el alta/baja del propietario.
- **Historial económico de la operación:** `GET reservations/{id}/history` (solo personal del edificio): publicación, reserva con importes, pago, confirmación, cancelación, reembolso, comisión asumida, deuda, reclamo, acreditación y movimientos de la cuenta, en orden, con quién lo hizo, armado desde `MarketplaceEvent`.
- **WEB:** ventana «Historial» (con botón al comprobante PDF) desde la cuenta del Marketplace y desde reembolsos y reclamos; solapa «Cambios de propietario» en «Reembolsos y reclamos». **APP:** «Comprobante (PDF)» en mis reservas y en las recibidas; solapa «Cambios» en la sección del Encargado; los avisos abren esa solapa.
- **Pruebas:** 33 nuevas (495 en total): PDF por tipo de parte, comprobante con imagen, aislamiento, historial, notas, ganchos del controlador y fallo tolerado.
- **Límites conocidos:** el contenido de cada versión del PDF no se verifica automáticamente (las pruebas comprueban que el PDF se genera para cada parte y que la parte correcta lo recibe); la nota solo cubre la **baja** del principal (hoy no hay un "cambiar principal" en un solo paso: son un alta y una baja); si el propietario anterior ya no pertenece al edificio, la cancelación o el reclamo de sus reservas lo resuelve la administración.

**Plan original de la fase:**
- **PDF "Comprobante interno de reserva"** (QuestPDF, generado al pedirlo desde importes congelados, **marcado como no fiscal**) ligado a publicación, reserva, pago, propietario, comprador, unidad, edificio y comprobante de transferencia.
- **Documento interno de cambio de propietario principal** (gancho en `UnitOwnersController`).
- Vista del **historial económico** de una operación (publicación → reserva → importes → pago → acreditación) desde `MarketplaceEvent`.

### Fase 9 — Endurecimiento y entrega  *(BACK + WEB + APP)* — **HECHA (2026-10-03)**
**Lo construido** (sin migración nueva):
- **Aislamiento (auditoría):** `MarketplaceIsolationMatrixTests`: más de 300 intentos de acceso indebido sobre **todas** las operaciones que reciben un id o un edificio (otra empresa, otro edificio de la misma empresa, vecino sin relación, usuario final en pantallas del personal, personal en lo del SuperAdmin, token que dice otra empresa, módulo apagado). En cada una se exige 403/404 —nunca un 409 que revele estados— y que la base quede **idéntica** (sin cambios, sin auditoría, sin avisos). Además: precio/total nunca del cliente, comprobante solo de la propia plataforma, aprobación solo con el monto exacto, panel de administración solo del SuperAdmin y todos los controladores con `[Authorize]` y sin acciones anónimas (por reflexión). Se comprobó que la matriz **falla** si se rompe un chequeo a propósito. Resultado de la revisión: no se encontraron fugas entre empresas, edificios o usuarios.
- **Concurrencia contra SQL Server real:** `MarketplaceConcurrencySqlServerTests` (10 pruebas, variable de entorno `CONDO_TEST_SQLSERVER`, p. ej. LocalDB; se omiten solas sin ella): 8 compradores sobre el mismo horario, un comprador con 2 reservas, doble confirmación, aprobar vs. rechazar, cancelar comprador vs. propietario, dos reclamos, resolver para ambos lados, 3 «devuelto», 4 acreditaciones y acreditar vs. cancelar. Corren con los mismos reintentos que producción. Se repitieron 3 veces sin fallas intermitentes.
- **Límite de tasa de las escrituras** (`MarketplaceRateLimiting`, con `UseRateLimiter` después de autenticar): por **usuario** y tipo de acción en ventana de 1 minuto: 60 escrituras comunes, **12 acciones sensibles** (reservar, pagar, cancelar, reclamar, confirmar, rechazar, devolver, resolver), 20 PDF/Excel; lecturas y lo que no es del marketplace no se limitan. Pasado el tope: **429** con `{ error: "marketplace_rate_limited", message }` y `Retry-After`. Probado también montado en un servidor HTTP en memoria, donde se detectó y corrigió que faltaba el `Retry-After`.
- **Entrega:** `docs/sql/marketplace_acumulado.sql` (5 migraciones en orden, idempotente), `docs/sql/marketplace_verificacion.sql` (35 comprobaciones de solo lectura), `docs/DESPLIEGUE_MARKETPLACE.md` y `docs/GUIA_PRUEBAS_MARKETPLACE.md`. El camino se **validó contra SQL Server 2019**: base migrada hasta `OwnerPaymentWebChannel` → acumulado → verificación 35/35 → segunda corrida sin errores → EF reconoce todo como aplicado.
- **Trabajo aparte (no bloquea):** proteger los comprobantes subidos (`/api/uploads`), «pagar con saldo», contador de pendientes del Encargado en las pestañas y *guards* de rutas en la web.

**Plan original de la fase:**
- Pruebas de seguridad (empresa A→B, edificio A→B, residente que publica, edición ajena, precio manipulado, IDOR en todos los endpoints), concurrencia contra SQL Server real, revisión del servicio de aislamiento, límites de tasa en escritura.
- Script `.sql` acumulado y guía de despliegue (primero migraciones, luego código) para el VPS.
- **Trabajo aparte (no bloquea):** proteger los comprobantes subidos (`/api/uploads`) y "pagar con saldo".

---

## 3. Riesgos conocidos y cómo se manejan

| Riesgo | Manejo |
|---|---|
| Aislamiento manual por controlador (no hay filtros globales) | Servicio central `MarketplaceScope` + pruebas de aislamiento en cada fase |
| Saldo mayor que la deuda no se consume (regla actual de `CoverWithCredit`) | Aceptado por Tony; se documenta; no se modifica el motor de expensas |
| Plan vencido bloquea al Encargado (no puede aprobar) | Aceptado; las reservas en revisión expiran por resolución manual al terminar |
| 10 min fijos vs. transferencia real | Cuenta regresiva visible; transferencia a reserva vencida se devuelve a mano |
| Migraciones manuales en el VPS | `.sql` idempotente con verificación final, aplicado antes del código |
| Comprobantes en URL pública | Trabajo aparte ya acordado |
| Proceso en segundo plano caído | Los bloques se calculan también "perezosamente" (una reserva vencida no bloquea aunque el proceso no haya corrido) |

---

## 4. Orden y dependencias

`1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9`. El BACK de cada fase se hace primero y luego las pantallas. Las fases 1 y 2 no cambian nada visible para los usuarios; desde la 3 el marketplace solo existe en edificios con el módulo habilitado.
