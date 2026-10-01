# Especificación — Sección "Encargado" en la app CondoPY (Android)

Estado: **propuesta, sin implementar**. Fecha: 2026-10-01.
Repos involucrados: `CondoPY-APP` (Ionic 8 + Angular 20 + Capacitor 8), `Condo-PY-BACK` (.NET API), `Condo-PY-WEB` (solo referencia).

---

## 1. Objetivo

Darle al **Encargado de edificio** (`BuildingManager`) una sección dentro de la misma app Android donde pueda **ver el estado del edificio** y resolver desde el celular las **tareas cortas y frecuentes**, con botones predefinidos. Lo largo y delicado (liquidaciones, facturación, configuración) sigue en la web.

### 1.1 Alcance por fases

| Fase | Contenido |
|---|---|
| **1 (MVP)** | Inicio con resumen · Pagos de propietarios (revisar / aprobar / rechazar) · Reclamos (cambiar estado) · Reservas (aprobar / rechazar) · Notificaciones con navegación · Pantalla de plan vencido (enviar comprobante) · Control de versión de la app |
| **2** | Morosos con llamar / WhatsApp / recordatorio · Comunicado rápido con plantillas · Gasto rápido con foto de factura |
| **Fuera de alcance** | Cierre y publicación de liquidaciones, facturas y timbrados, notas de crédito, importación por Excel, usuarios, edificios y unidades, planes (SuperAdmin), reportes contables |

### 1.2 Roles

- **Fase 1:** `BuildingManager`.
- El mismo shell puede habilitarse sin cambios de backend para `CompanyOperator` y `CompanyAdmin` (los endpoints ya les dan acceso). Decisión abierta, ver sección 10.
- `Owner`, `Resident` y `Porter` mantienen la app actual (`/area`).

---

## 2. Hallazgos del código que condicionan el diseño

Verificado leyendo el código actual (2026-10-01).

1. **El flujo de pagos de propietarios tiene 3 pasos, no 2.** `Pending` → `PUT /owner-payments/{id}/review` con el **monto verificado** → `UnderReview` → `PUT /approve`. Rechazar (`/reject`) exige motivo (máx. 500 caracteres) y vale en `Pending` y `UnderReview`. El `review` falla con 400 si el monto no cubre exactamente los comprobantes abiertos (considerando el crédito del propietario). Aprobar además genera borradores de factura.
2. **`CanProcess` en el pago.** Un pago puede incluir unidades de edificios que el Encargado no tiene asignados: lo puede ver, pero no procesar (`canProcess = false`, y los `PUT` responden 403 con `OutOfScopeMessage`). La app debe respetarlo.
3. **Reclamos solo cambian de estado** (`Pendiente`, `EnProceso`, `Resuelto`). No existe campo de respuesta o comentario. Un "responder" requeriría cambio de backend (fase 2 opcional).
4. **`GET /api/dashboard/summary` no sirve para el celular.** Es acumulado histórico, no acepta `buildingId`, carga todos los cargos y pagos en memoria, y para `CompanyAdmin` usa toda la empresa aunque esté acotado a un condominio. Hace falta un endpoint liviano nuevo (sección 6.2).
5. **`GET /api/owner-payments` no filtra por edificio** (solo `status` y `ownerId`) ni pagina. Se necesita `buildingId` y paginación.
6. **Gastos (`POST /api/building-expenses`)** exigen `ExpensePeriodId` del mismo edificio, período en **Borrador** y fecha dentro del rango del período. El "gasto rápido" debe elegir el período borrador vigente o avisar que no hay.
7. **`GET /api/expense-periods` no filtra** por edificio ni por estado (y para `CompanyAdmin` devuelve toda la empresa). Se necesita filtro.
8. **Comunicados:** `AnnouncementsController` es `[Authorize]` sin roles. Hoy lo protege que el alcance por edificio de un propietario es vacío, pero conviene fijar los roles explícitamente.
9. **El shell actual de la app (`/area`) es de propietario/residente** (Inicio, Expensas, Pagos, Reclamos, Mi perfil) y las rutas `/area/payments` y `/area/claims` son pantallas de propietario. El Encargado necesita **su propio shell** (`/manager`) para no mezclar.
10. **Nombres:** la app muestra `Porter` como "Encargado" (`dashboard.page.ts`, `profile.page.ts`). En el sistema el Encargado de edificio es `BuildingManager`. Hay que corregir las etiquetas antes de mostrar menús por rol.
11. **Login:** `LoginResponse` incluye `role`, `companyId`, `scopeLabel`. No incluye la lista de edificios; se obtiene de `GET /api/buildings`.
12. **Restricción por plan vencido (implementada 2026-10-01):** la API responde `403` con `{ "error": "plan_read_only" | "plan_blocked", "message": "..." }` a Administrador de empresa, Operador y Encargado. La app móvil **no** lo maneja todavía (el interceptor solo trata 401).
13. **Producción:** `environment.prod.ts` apunta a `https://api.tramiya.com.py/api`. `environment.ts` apunta a un túnel de Cloudflare de desarrollo: verificar que el APK publicado se compile con `prod`.

---

## 3. Arquitectura en la app

### 3.1 Redirección por rol

Hoy `''` redirige a `area`. Cambia a:

```
'' → RoleRedirectGuard
       BuildingManager (y roles habilitados) → /manager
       Owner / Resident / Porter             → /area   (sin cambios)
```

- `RoleRedirectGuard` lee `auth.getUser().role`.
- `ManagerGuard` (canActivate de `/manager`): sesión válida + `mustChangePassword` falso + rol habilitado. Si no, redirige a `/area` o `/login`.
- `AuthGuard` actual se reutiliza dentro.

### 3.2 Estructura de carpetas nueva

```
src/app/pages/manager/
  manager.module.ts            (shell con ion-tabs)
  manager.page.html/ts
  dashboard/                   (M2)
  payments/                    (M3 lista, M4 detalle)
  claims/                      (M5)
  reservations/                (M6)
  delinquency/                 (M7)  fase 2
  announce/                    (M8)  fase 2
  quick-expense/               (M9)  fase 2
  plan/                        (M11)
src/app/core/
  manager-api.service.ts       (endpoints de la sección)
  building-context.service.ts  (edificio seleccionado)
  version.service.ts           (control de versión)
```

### 3.3 Tab bar del Encargado

| Tab | Ruta | Icono (ionicons) | Badge |
|---|---|---|---|
| Inicio | `/manager/dashboard` | `grid-outline` | — |
| Pagos | `/manager/payments` | `wallet-outline` | pagos por revisar |
| Reclamos | `/manager/claims` | `chatbubble-ellipses-outline` | reclamos pendientes |
| Reservas | `/manager/reservations` | `calendar-outline` | reservas por revisar |
| Más | `/manager/more` | `ellipsis-horizontal-outline` | — |

"Más" agrupa: Morosos, Comunicado, Gasto rápido (fase 2), Notificaciones, Mi plan, Mi perfil.

### 3.4 Contexto de edificio

- `BuildingContextService` guarda `selectedBuildingId` en `localStorage` (clave `condopy_manager_building`).
- Carga `GET /api/buildings` al entrar. Si hay un solo edificio, se selecciona solo y no se muestra el selector. Si hay varios, aparece un chip arriba (`ion-select` en `ion-popover`) en todas las pantallas del shell.
- Todos los pedidos de la sección envían `buildingId` explícito.
- Si el edificio guardado ya no está en la lista, se elige el primero.

### 3.5 Interceptor (cambio en `auth.interceptor.ts`)

Agregar manejo de `403` con `error.error.error`:
- `plan_blocked` → navegar a `/manager/plan` (pantalla M11), con toast una sola vez cada 4 s.
- `plan_read_only` → toast "El plan está vencido: modo solo lectura", sin navegar.

---

## 4. Pantallas

Convenciones: Ionic (`ion-header`, `ion-content`, `ion-refresher` en todas las listas, `ion-skeleton-text` al cargar). Montos en guaraníes con separador de miles (`1.250.000 Gs.`). Fechas `dd/MM/yyyy`. Todas las acciones que modifican piden confirmación (`AlertController`) y muestran toast de resultado.

### M0 — Login y redirección
- Reutiliza `/login`. Tras el login exitoso, `RoleRedirectGuard` decide el destino.
- Registrar token push igual que hoy (`PushService.init()`).

### M1 — Selector de edificio
- Componente `building-switcher` (chip en el header del shell).
- Datos: `GET /api/buildings`.
- Estados: un edificio (oculto), varios (popover con lista), error (reintentar).

### M2 — Inicio (`/manager/dashboard`)
**Objetivo:** ver de un vistazo qué requiere atención.

Datos: `GET /api/manager/summary?buildingId=` (nuevo, 6.2) y `GET /api/building-plans/my-plan`.

Contenido, de arriba abajo:
1. Saludo + chip de edificio + campana de notificaciones con contador (`GET /notifications/unread-count`, refresco cada 30 s como hoy).
2. **Banner de plan** (solo si el plan no está `Active`): `ExpiringSoon` amarillo "Tu plan vence en N días"; `Expired` naranja "Plan vencido, quedan N días de gracia"; `ReadOnly` rojo "Solo lectura, bloqueo en N días"; `Blocked` rojo y botón "Regularizar" → M11.
3. **Tarjetas de atención** (tocables, llevan a la lista filtrada): Pagos por revisar · Reclamos pendientes · Reservas por revisar.
4. **Indicadores del mes:** cobrado vs. emitido (% de cobranza) · saldo vencido · unidades morosas.
5. **Acciones rápidas** (grilla de botones): Revisar pagos · Reclamos · Reservas · Morosos (f2) · Comunicado (f2) · Gasto (f2).
6. Si el plan está `ReadOnly`, las acciones de escritura se muestran deshabilitadas con candado.

Estados: cargando (skeleton), error (mensaje + reintentar), sin datos (ceros).

### M3 — Pagos por revisar (`/manager/payments`)
**Datos:** `GET /api/owner-payments?status=&buildingId=&page=&pageSize=` (filtro y paginación nuevos, 6.3).

- Segmentos: **Por revisar** (`Pending`) · **En revisión** (`UnderReview`) · **Resueltos** (`Approved`, `Rejected`).
- Fila: propietario, unidades ("Torre A · 3B"), monto declarado, fecha, referencia, etiqueta de estado. Icono de candado si `canProcess = false`.
- Scroll infinito (`ion-infinite-scroll`), pull-to-refresh.
- Vacío: "No hay pagos por revisar".

### M4 — Detalle de pago (`/manager/payments/:id`)
**Datos:** `GET /api/owner-payments/{id}` (incluye `units`, `comprobanteUrl`, `canProcess`, `applications` si está aprobado).

Contenido:
- **Foto del comprobante** (`comprobanteUrl`), tocable para ampliar con zoom. Si es PDF, abrir en el visor del sistema.
- Datos: propietario, referencia, fecha de pago, monto declarado, unidades con monto asignado.
- Si `canProcess = false`: aviso "Este pago incluye unidades de otros edificios. Lo debe procesar un Administrador de empresa" y **sin botones de acción**.

Acciones según estado (solo si `canProcess` y plan no restringido):

| Estado | Botones | Llamada |
|---|---|---|
| `Pending` | **Verificar monto** (campo precargado con `declaredAmount`, editable) | `PUT /owner-payments/{id}/review` `{ reviewedAmount }` |
| `Pending` / `UnderReview` | **Rechazar** (pide motivo, 1–500 caracteres) | `PUT /owner-payments/{id}/reject` `{ rejectionReason }` |
| `UnderReview` | **Aprobar** | `PUT /owner-payments/{id}/approve` |

- **Atajo "Verificar y aprobar":** una sola confirmación que llama `review` y luego `approve` en secuencia. Si `review` funciona y `approve` falla, el pago queda en `UnderReview`; recargar y mostrar el error exacto.
- Los errores 400 del backend (por ejemplo monto que no coincide) se muestran tal cual en un `ion-alert`, con la sugerencia de rechazar.
- Tras actuar: toast, volver a la lista y refrescar contadores.

### M5 — Reclamos (`/manager/claims`)
**Datos:** `GET /api/claims?buildingId=&status=`.

- Segmentos: **Pendientes** · **En proceso** · **Resueltos**.
- Fila: categoría, unidad, resumen de la descripción, quién lo creó, fecha.
- Detalle en `ion-modal`: descripción completa, datos del reclamante y **botones de estado**: "Pasar a En proceso" / "Marcar Resuelto" / "Reabrir (Pendiente)".
- Llamada: `PATCH /api/claims/{id}/status` `{ status: "Pendiente" | "EnProceso" | "Resuelto" }`.
- El backend notifica al reclamante por campanita y push.
- (Fase 2 opcional: nota de respuesta, requiere cambio de backend; ver 6.6.)

### M6 — Reservas (`/manager/reservations`)
**Datos:** `GET /api/amenities/reservations?buildingId=&status=`.

- Segmentos: **Por revisar** (`PendingReview` + `PendingPayment`) · **Confirmadas** · **Rechazadas**.
- Fila: área común, rango horario, reservante, precio, estado.
- Detalle: foto del comprobante (`comprobanteUrl`) si existe, notas del reservante.
- Acciones: **Aprobar** y **Rechazar** (motivo opcional) → `POST /api/amenities/reservations/{id}/review` `{ approve, rejectionReason }`.
- Aviso al aprobar: "Se publicará un comunicado de que el área queda reservada en ese horario" (el backend lo crea automáticamente).
- Error "La reserva ya fue procesada": refrescar la lista.

### M7 — Morosos (`/manager/delinquency`) — fase 2
**Datos:** `GET /api/morosity?buildingId=&page=&pageSize=&agingBucket=`.

- Cabecera con el resumen: unidades en mora, monto total vencido y los tramos 0–30, 31–60, 61–90 y +90.
- Filtro por tramo (chips). Lista agrupada **por unidad en el cliente** (el endpoint devuelve una fila por unidad y período): unidad, responsable, deuda total, días máximos de atraso.
- Detalle de unidad: períodos adeudados y tres botones:
  - **Llamar** (`tel:` con `responsiblePhone`, o `ownerPhone` si está vacío)
  - **WhatsApp** (`https://wa.me/<número sin +>?text=<mensaje prellenado>`)
  - **Enviar recordatorio por correo** → `POST /api/morosity/send-reminders?buildingId=&unitId=` (el parámetro `unitId` ya existe, así que se envía solo a esa unidad). Respuesta `{ emailsSent, unitsSkippedNoEmail }`; si es 0 enviados, avisar "La unidad no tiene correo cargado".
- El envío masivo no va en el celular en esta versión (evita enviar 500 correos con un toque).

### M8 — Comunicado rápido (`/manager/announce`) — fase 2
**Datos:** `GET /api/announcements?buildingId=` (últimos publicados) y `POST /api/announcements`.

- Plantillas predefinidas (en la app, sin backend): *Corte de agua programado*, *Corte de energía*, *Mantenimiento del ascensor*, *Fumigación*, *Asamblea / convocatoria*, *Aviso general*. Cada una con título, texto con campos a completar (fecha, horario) y categoría (`Mantenimiento`, `Seguridad`, `Convocatoria`, `General`).
- Flujo: elegir plantilla → completar campos → **vista previa** → "Publicar" (confirmación: "Se enviará a todos los residentes y propietarios del edificio").
- Llamada: `POST /api/announcements` `{ buildingId, title, body, category, expiresAt?, isActive: true }`. El backend notifica a los usuarios del edificio.
- Validaciones del backend: título ≤ 200 caracteres, texto obligatorio, categoría de la lista `General | Mantenimiento | Seguridad | Financiero | Convocatoria | Otro`.
- Lista inferior de los últimos comunicados con botón "Desactivar" (`PUT /announcements/{id}` con `isActive=false`). No se ofrece eliminar desde el celular (decisión 3).

### M9 — Gasto rápido (`/manager/quick-expense`) — fase 2
**Datos:** `GET /api/expense-periods?buildingId=&status=Draft` (filtro nuevo, 6.5), `POST /api/building-expenses`, `POST /api/building-expenses/{id}/receipt`.

- Campos: categoría (lista), proveedor, descripción, monto, fecha (por defecto hoy), **foto de la factura** (cámara o galería), "Pagado por fondo de reserva" (interruptor).
- El período es el **borrador** del edificio cuyo rango contiene la fecha. Si no existe, pantalla vacía: "No hay un período abierto para esta fecha. Crealo desde la web."
- Distribución por defecto `ByCoefficient` (campo oculto). No se ofrece distribución individual en el celular.
- Flujo en dos llamadas: crear el gasto y luego subir la foto (JPG o PNG, máx. 10 MB). Si la foto falla, el gasto queda creado y se ofrece "Reintentar subida".
- Plugin necesario: `@capacitor/camera` (hoy no está instalado; requiere reconstruir el APK).

### M10 — Notificaciones (`/manager/notifications`)
- Reutiliza la pantalla existente (`/area/notifications`) montada también bajo el shell del Encargado.
- Ampliar `resolveNotificationRoute` (en `notifications.service.ts`) con el contexto de rol:

| `entityType` / `type` | Destino Encargado |
|---|---|
| `OwnerPayment` (`OwnerPaymentSubmitted`) | `/manager/payments/{entityId}` |
| `Claim` (`ClaimCreated`) | `/manager/claims` (abre el reclamo) |
| `AmenityReservation` (`AmenityReservationCreated`) | `/manager/reservations` |
| `BuildingPlan` (`PlanExpiringSoon`, `PlanExpired`, `PlanSuspended`) | `/manager/plan` |

- Las rutas de propietario no cambian: el destino depende del rol del usuario logueado.

### M11 — Mi plan / plan vencido (`/manager/plan`)
**Datos:** `GET /api/building-plans/my-plan`. Es la **única pantalla utilizable con el plan bloqueado**.

- Tarjeta con plan, edificio, vencimiento, estado y días restantes (mismo criterio que la web: `Active`, `ExpiringSoon`, `Expired`, `ReadOnly`, `Blocked`).
- Mensajes por estado (mismos textos de la web).
- Botón **Enviar comprobante de pago**: monto, fecha, foto (`POST /api/uploads` y luego `POST /api/building-plan-payments` con `buildingPlanId`, `declaredAmount`, `paymentDate`, `comprobanteUrl`).
- Muestra si ya hay un comprobante pendiente de revisión (solo se permite uno).
- Con el plan `Blocked`, el shell oculta las demás tabs y deja solo esta pantalla y "Cerrar sesión".

### M12 — Mi perfil / contraseña
- Reutiliza `profile` y `change-password` existentes. Corregir las etiquetas de rol (hallazgo 10).

---

## 5. Estados de plan y qué se habilita

| Estado del plan | Lectura | Acciones de escritura | Pantalla |
|---|---|---|---|
| `Active` / `ExpiringSoon` | sí | sí | todas (con banner si vence) |
| `Expired` (gracia) | sí | sí | todas, banner naranja |
| `ReadOnly` | sí | **no** (botones deshabilitados con candado; el backend igual responde 403) | todas, solo "Plan" permite enviar |
| `Blocked` | **no** | **no** | solo M11 |

La app lee el estado de `my-plan` al entrar y lo refresca al volver a primer plano. La seguridad real es del backend; la app solo evita ofrecer botones inútiles.

---

## 6. Endpoints

### 6.1 Existentes que se usan sin cambios

| Pantalla | Método y ruta | Uso |
|---|---|---|
| M0 | `POST /api/auth/login` | inicio de sesión |
| M1 | `GET /api/buildings` | edificios del alcance |
| M2, M10 | `GET /api/notifications/unread-count`, `GET /api/notifications`, `PUT /api/notifications/{id}/read`, `PUT /api/notifications/read-all` | campanita |
| — | `POST /api/devices/register`, `POST /api/devices/unregister` | token push |
| M4 | `GET /api/owner-payments/{id}` | detalle |
| M4 | `PUT /api/owner-payments/{id}/review` `{ reviewedAmount }` | verificar monto |
| M4 | `PUT /api/owner-payments/{id}/approve` | aprobar |
| M4 | `PUT /api/owner-payments/{id}/reject` `{ rejectionReason }` | rechazar |
| M5 | `GET /api/claims?buildingId&status` | lista |
| M5 | `PATCH /api/claims/{id}/status` `{ status }` | cambiar estado |
| M6 | `GET /api/amenities/reservations?buildingId&status` | lista |
| M6 | `POST /api/amenities/reservations/{id}/review` `{ approve, rejectionReason }` | aprobar / rechazar |
| M7 | `GET /api/morosity?buildingId&agingBucket&page&pageSize` | reporte |
| M7 | `POST /api/morosity/send-reminders?buildingId&unitId` | recordatorio por unidad |
| M8 | `GET/POST/PUT/DELETE /api/announcements` | comunicados |
| M9 | `POST /api/building-expenses`, `POST /api/building-expenses/{id}/receipt` | gasto y factura |
| M11 | `GET /api/building-plans/my-plan`, `POST /api/building-plan-payments`, `POST /api/uploads` | plan |

### 6.2 Nuevo: resumen liviano del edificio

`GET /api/manager/summary?buildingId={guid}`
Roles: `BuildingManager`, `CompanyOperator`, `CompanyAdmin` (y `SuperAdmin`). Valida `CanAccessBuildingAsync(buildingId)`.

Respuesta:

```json
{
  "buildingId": "…",
  "buildingName": "Edificio Hampton",
  "pendingOwnerPayments": 3,
  "underReviewOwnerPayments": 1,
  "pendingClaims": 2,
  "inProgressClaims": 1,
  "pendingReservations": 1,
  "currentPeriod": { "id": "…", "name": "Octubre 2026", "status": "Draft", "dueDate": "2026-10-10" },
  "currentPeriodCharged": 18500000,
  "currentPeriodCollected": 12300000,
  "collectionRatePercentage": 66.5,
  "overdueBalance": 4200000,
  "unitsInArrears": 7,
  "plan": { "name": "Plan Mensual", "status": "ExpiringSoon", "endDate": "2026-10-15", "daysUntilExpiry": 14, "daysUntilBlocked": null }
}
```

Reglas: todo acotado a **un** edificio; período vigente = el del mes actual (o el último publicado si no hay); consultas agregadas en base de datos, sin cargar tablas completas en memoria; mismos criterios de saldo vencido que `MorosityController` para que los números coincidan.

### 6.3 Modificar: `GET /api/owner-payments`

Agregar parámetros opcionales: `buildingId` (filtra por unidades del edificio, dentro del alcance), `page` y `pageSize` (por defecto 1 y 25, máximo 100). El `status` acepta lista separada por comas (`Pending,UnderReview`). Sin los parámetros nuevos el comportamiento actual no cambia (la web sigue funcionando).

### 6.4 Modificar: `GET /api/amenities/reservations`

Agregar `from` y `to` (fechas) para "reservas de hoy / de la semana". Opcional en fase 1.

### 6.5 Modificar: `GET /api/expense-periods`

Agregar `buildingId` y `status`. Corregir de paso que para `CompanyAdmin` acotado a un condominio no devuelva toda la empresa (mismo criterio de alcance que el resto de los endpoints). Necesario para M9.

### 6.6 Respuesta en reclamos — descartada

Decisión 4: los reclamos solo cambian de estado. No hay columna nueva ni migración.

### 6.7 Nuevo: control de versión de la app

`GET /api/app/version` (anónimo)

```json
{ "minVersionCode": 2, "latestVersionCode": 3, "apkUrl": "https://tramiya.com.py/downloads/condopy.apk", "message": "Hay una versión nueva." }
```

- La app compara su `versionCode` (plugin `@capacitor/app` → `App.getInfo()`) al abrir y al volver a primer plano.
- Si `versionCode < minVersionCode`: pantalla bloqueante "Actualizá la app" con botón de descarga.
- Si `< latestVersionCode`: aviso descartable.
- Los valores se leen de configuración del VPS (`App:MinVersionCode`, `App:LatestVersionCode`), sin base de datos.
- Necesario porque el APK se distribuye fuera de Play Store y no se actualiza solo.

### 6.8 Endurecer: `AnnouncementsController`

Agregar `[Authorize(Roles = "SuperAdmin,CompanyAdmin,CompanyOperator,BuildingManager")]` en `POST`, `PUT`, `DELETE` y `broadcast`. Los `GET` quedan abiertos al alcance actual (los usa la app de propietarios).

---

## 7. Modelos TypeScript (en `core/models.ts`)

```ts
export type ManagerPlanStatus = 'Active' | 'ExpiringSoon' | 'Expired' | 'ReadOnly' | 'Blocked' | 'Archived';

export interface ManagerSummary {
  buildingId: string;
  buildingName: string;
  pendingOwnerPayments: number;
  underReviewOwnerPayments: number;
  pendingClaims: number;
  inProgressClaims: number;
  pendingReservations: number;
  currentPeriod: { id: string; name: string; status: string; dueDate: string } | null;
  currentPeriodCharged: number;
  currentPeriodCollected: number;
  collectionRatePercentage: number;
  overdueBalance: number;
  unitsInArrears: number;
  plan: { name: string; status: ManagerPlanStatus; endDate: string; daysUntilExpiry: number; daysUntilBlocked: number | null } | null;
}

export interface OwnerPayment {
  id: string; ownerId: string; ownerFullName: string;
  paymentDate: string; comprobanteUrl: string;
  declaredAmount: number; reviewedAmount: number | null;
  status: 'Pending' | 'UnderReview' | 'Approved' | 'Rejected';
  reference: string; rejectionReason: string;
  units: { unitId: string; unitCode: string; buildingName: string; allocatedAmount: number }[];
  canProcess: boolean;
}

export interface AppVersionInfo { minVersionCode: number; latestVersionCode: number; apkUrl: string; message?: string; }
```

---

## 8. Seguridad y auditoría

- **El backend manda.** Cada endpoint ya valida rol y alcance por edificio (`GetAccessibleBuildingIdsAsync`, `CanAccessBuildingAsync`). La app no reemplaza esas validaciones.
- **Confirmación** antes de aprobar, rechazar, publicar comunicado y enviar recordatorios.
- **Trazabilidad ya existente:** `ReviewedByUserId` en pagos y reservas, `ResolvedByUserId` en reclamos, `CreatedByUserId` en comunicados y gastos. No se requiere tabla de auditoría nueva.
- **Sesión:** el 401 cierra sesión (ya implementado). No guardar datos sensibles fuera de `localStorage` / `sessionStorage` existentes.
- **Comprobantes:** se muestran por URL con el esquema actual de archivos; no se cachean en disco de forma persistente.

---

## 9. Orden de implementación y criterios de aceptación

### Fase 1
1. **Backend:** `GET /api/manager/summary`, filtros en `owner-payments` y `expense-periods`, `GET /api/app/version`, roles en comunicados. Despliegue sin migraciones SQL.
2. **App:** guards por rol, shell `/manager`, `BuildingContextService`, interceptor con `plan_*`, etiquetas de rol corregidas.
3. **App:** M2 Inicio, M3/M4 Pagos, M5 Reclamos, M6 Reservas, M10 Notificaciones, M11 Plan, control de versión.
4. **Compilar APK de release con `environment.prod`, subir `versionCode`, publicar en la landing.**

Criterios de aceptación (ejemplos):
- Un Encargado con 2 edificios ve el selector; con 1 no.
- Aprobar un pago desde la app deja el mismo resultado que desde la web (estado `Approved`, notificación al propietario, borradores de factura).
- Un pago con `canProcess = false` no muestra botones de acción.
- Con el plan en `ReadOnly`, tocar "Aprobar" no es posible (botón deshabilitado) y, si se fuerza, el 403 muestra el aviso sin cerrar la app.
- Con el plan en `Blocked`, solo se accede a M11 y se puede enviar el comprobante.
- Con un `versionCode` menor al mínimo, la app muestra la pantalla de actualización.
- Un Propietario sigue entrando a `/area` y no ve nada del shell del Encargado.

### Fase 2
M7 Morosos, M8 Comunicado rápido, M9 Gasto rápido (incluye `@capacitor/camera`).

### Esfuerzo (orden de magnitud, a confirmar)
Fase 1: ~1,5 a 2 semanas (backend ~2 días, app ~8 días). Fase 2: ~1 semana.

---

## 10. Decisiones tomadas (2026-10-01)

| # | Tema | Decisión |
|---|---|---|
| 1 | Roles | **Solo `BuildingManager`.** Abrir a Operador o Administrador queda para después y no requiere cambios de backend. |
| 2 | Revisión de pagos | **Atajo "Verificar y aprobar" en un toque** (una confirmación, dos llamadas seguidas) + botón Rechazar con motivo. |
| 3 | Comunicados | El Encargado **solo puede desactivarlos**, no eliminarlos. Eliminar queda para la web. |
| 4 | Reclamos | **Solo cambio de estado**, sin nota de respuesta. Se descarta la sección 6.6 (no hay migración). |
| 5 | Distribución | **Solo descarga directa** desde `tramiya.com.py`, con control de versión mínima dentro de la app. |
| 6 | Avisos push al Encargado | **Verificado:** ya se envían (campanita y push). Ver abajo. |

### Verificación de la decisión 6 (código actual)

- **Pago enviado** (`OwnerPaymentsController`, `Create`): notifica a `BuildingManager` y `CompanyOperator` con acceso a algún edificio del pago, y a `CompanyAdmin` de la empresa (o del condominio, si está acotado). Usa `NotifyUsersAsync` (push).
- **Reclamo creado** (`MeController`): notifica a todos los usuarios con acceso activo al edificio. Push incluido.
- **Reserva solicitada** (`AmenitiesController`): notifica a todos los usuarios con acceso activo al edificio. Push incluido.
- **Pendiente en la app:** `resolveNotificationRoute` solo conoce `OwnerPayment` (hacia `/area/payments/:id`, pantalla de propietario), `SettlementPendingPresidentReview` y `ExpensePeriod`. Para el Encargado hay que agregar el destino por rol (tabla de M10). Hoy, tocar el push de un pago llevaría a la pantalla equivocada.

### Cambios que estas decisiones producen en el documento

- M8: se elimina el botón "Eliminar"; queda solo "Desactivar" (`PUT /announcements/{id}` con `isActive=false`). El `DELETE` no se usa desde la app del Encargado.
- 6.6 queda **descartada**.
- 6.8: los roles de `AnnouncementsController` se fijan igual (el `DELETE` queda restringido a roles de gestión, aunque la app no lo use).

---

## 11. Riesgos

| Riesgo | Mitigación |
|---|---|
| Aprobar un pago por error desde el celular | Confirmación con monto y propietario visibles; trazabilidad existente |
| App desactualizada contra una API nueva | Control de versión (6.7) |
| `dashboard/summary` lento si se reutilizara | Endpoint nuevo acotado a un edificio (6.2) |
| Confusión de nombres de rol | Corregir etiquetas (hallazgo 10) |
| Cambios en `owner-payments` rompen la web | Parámetros nuevos opcionales y compatibles |
| APK compilado contra el túnel de desarrollo | Verificar `environment.prod` en cada release |

---

## 12. Estado de implementación

### Backend de la fase 1 — hecho (2026-10-01), pendiente de commit y despliegue

| Ítem | Archivo | Verificación |
|---|---|---|
| `GET /api/manager/summary?buildingId=` | `Condo.Api/Controllers/ManagerController.cs`, `Condo.Application/Models/ManagerDtos.cs` | Probado con datos de prueba (cobranza, morosidad, plan) y SQL traducido sin errores |
| `GET /api/owner-payments`: `buildingId`, `page`, `pageSize`, `status` con lista; total en `X-Total-Count` | `OwnerPaymentsController.cs`, `Program.cs` (CORS expone el encabezado) | SQL traducido; sin parámetros nuevos el comportamiento no cambia |
| `GET /api/expense-periods`: `buildingId`, `status`; corrección de alcance del Administrador acotado a un condominio | `ExpensePeriodsController.cs` | Compila |
| `GET /api/amenities/reservations`: `from`, `to` | `AmenitiesController.cs` | Compila |
| `GET /api/app/version` (anónimo) | `Condo.Api/Controllers/AppController.cs`, `appsettings.json` | Compila |
| Roles en `POST/PUT/DELETE/broadcast` de comunicados | `AnnouncementsController.cs` | Compila |
| `/api/app` permitido aun con plan bloqueado | `PlanRestrictionMiddleware.cs` | — |

**Control de versión en el VPS:** el `appsettings.json` del VPS no se pisa al desplegar, así que rigen los valores por defecto del código (`1` y `1`). Para cambiarlos sin tocar archivos, agregar al `override.conf` de systemd:

```
Environment="App__MinVersionCode=2"
Environment="App__LatestVersionCode=2"
```

Después `systemctl daemon-reload && systemctl restart condo-py-api`.

### Hallazgo durante la implementación

`MorosityController` **no excluye los pagos revertidos** (`IsReversed`) al calcular el saldo, a diferencia del dashboard y de los estados de cuenta. Un pago revertido sigue contando como pagado en el reporte de morosidad. El resumen nuevo sí los excluye, por lo que sus números pueden diferir del reporte web en edificios con pagos revertidos. Conviene corregir el reporte de morosidad.

### App de la fase 1 — hecho (2026-10-01), pendiente de commit y de compilar el APK

Repo `CondoPY-APP`. Compila en desarrollo y en producción. Probado en el navegador a ancho de teléfono contra una API simulada (no contra el backend real ni en un celular).

| Pantalla / pieza | Estado |
|---|---|
| Redirección por rol (`RoleRedirectGuard`), `ManagerGuard`, `AreaGuard`; el login pasa por `/` | Hecho |
| Shell `/manager` con pestañas y contadores; se oculta la barra con el plan bloqueado | Hecho |
| M1 selector de edificio (`app-building-switcher`), edificio recordado | Hecho |
| M2 Inicio (resumen, aviso de plan, tarjetas, cobranza, morosidad, acciones rápidas) | Hecho |
| M3/M4 Pagos: lista paginada, detalle, "Verificar y aprobar", "Solo verificar monto", Rechazar con motivo | Hecho |
| M5 Reclamos: lista por estado, detalle y cambio de estado | Hecho |
| M6 Reservas: lista, detalle, aprobar y rechazar | Hecho |
| M10 Notificaciones: se reutiliza la pantalla; el destino depende del rol (también al tocar un push) | Hecho |
| M11 Mi plan: estado, avisos y envío del comprobante con foto | Hecho |
| "Más": notificaciones, plan, cambiar contraseña, cerrar sesión, versión | Hecho |
| Interceptor con `plan_blocked` / `plan_read_only`; botones deshabilitados en solo lectura | Hecho |
| Control de versión mínima al abrir y al volver a primer plano | Hecho (no probado en dispositivo) |
| Etiquetas de rol corregidas (`Porter` ya no se muestra como "Encargado") | Hecho |
| M7 Morosos, M8 Comunicado rápido, M9 Gasto rápido | Fase 2 |

Para publicarlo: desplegar primero el backend (la app nueva usa `/api/manager/summary` y `/api/app/version`), subir el `versionCode` en `android/app/build.gradle`, compilar el APK de release con `environment.prod` (apunta a `https://api.tramiya.com.py/api`) y copiarlo a `Condo-PY-WEB/public/downloads/condopy.apk`.
