# Centro de configuración del edificio — Especificación y plan de trabajo

Fecha: 2026-10-09. Estado: **especificación, sin código**. Autor del pedido: Tony.

Objetivo: que un edificio pueda quedar **listo para operar de punta a punta** (cobrar, liquidar, facturar, pagar, conciliar, cerrar y rendir cuentas) desde **un solo lugar**, sin convertir el producto en un sistema contable. Principio rector: **la contabilidad sale sola de lo que ya se hace** (cobrar, pagar, liquidar). Si el administrador carga un dato dos veces, nos desviamos.

---

## 1. Diagnóstico (verificado contra el código el 2026-10-09)

### 1.1 Qué existe

| Tema | Dónde está | Notas |
|---|---|---|
| Ficha del edificio (5 pestañas: General, Legal, Facturación, Contabilidad y pagos, Configuración) | `building-create-page.component.ts` (WEB), `Building` (BACK) | Incluye RUC, régimen IVA, día de vencimiento, días de gracia, instrucciones de pago, mora, % reserva, % extraordinario, tratamiento de ingresos |
| Cuentas bancarias del edificio | `BuildingBankAccount` | |
| Finanzas del edificio (módulo con interruptor y plan) | `FinanceSettings`, `FinancialAccount`, `LedgerCategory`, `BudgetLine`; controladores `Finance*` | Solo SuperAdmin configura; Admin ve en solo lectura (decisión 2026-10-02) |
| Plan de cuentas genérico (197 nodos), importación Excel, copia entre edificios, funciones especiales | `FinanceChartTemplate`, `FinancePlanImport`, `FinancePlanCopier` | Hecho 2026-10-03 |
| Libro virtual, flujo de caja, presupuesto vs. real, fondo de reserva, exportación Excel y paquete del contador | `FinanceLedger`, `FinanceReportService`, `FinanceExportController` | |
| Mora automática | `LateFeeAccrualService` (cada 6 h) | Ver 1.3 |
| Morosidad y antigüedad de saldos | `MorosityController` | |
| Liquidación con circuito (presidente, aprobación, publicación) | `ExpenseSettlement`, `ExpensePeriodsController` | |
| Facturación (series, timbrado, notas de crédito, modo) | `Invoice*`, `CreditNote*`, `InvoiceSeriesController` | |
| NC de proveedor sobre gastos | `BuildingExpenseCreditNote*` | |
| Saldo a favor del propietario (código conservado, **desactivado** por regla vigente desde 2026-09-21: `OwnerCreditFeature.Enabled = false`) | `OwnerCredit*`, `CreditApplyMode` | No se reactiva en este plan |
| Plantillas de documentos propias | `Building.*TemplateUrl`, calibración | |

### 1.2 Qué falta

| Tema | Hoy |
|---|---|
| Cierre contable de período que bloquee | No existe. `ExpensePeriodStatus.Closed` es solo el estado de la liquidación |
| Auditoría del módulo (`FinanceAuditLog`) | No existe. Solo se guarda quién habilitó y quién completó la configuración |
| Proveedores (entidad) y cuentas por pagar | `BuildingExpense.SupplierName` es texto libre; sin estado de pago, vencimiento ni fecha de pago |
| IVA discriminado | `Invoice` y `BuildingExpense` no guardan base/IVA 10 %/5 %/exento |
| Conciliación bancaria | No existe (fase 6 de la especificación de Finanzas) |
| Asientos sugeridos (debe y haber) | No existe; el libro es de movimientos |
| Quién paga las expensas de una unidad alquilada | No existe. `Resident` tiene `LeaseUrl`, `LeaseEndDate` y relación `Tenant`, nada más |
| Informe de asamblea en un solo PDF | No existe; hay piezas sueltas |
| Indicador de «edificio listo para operar» | No existe |

### 1.3 Defectos detectados en lo existente (corregir en la fase 3)

1. **`Building.GraceDays` no la usa el backend.** Corrección de lo que decía esta especificación: el web sí la usa al crear un período a mano (propone la fecha de corte de mora = vencimiento + días de gracia), pero el backend no la aplicaba y clonar o crear en lote no la tomaban. La mora arranca en `ExpensePeriod.LateFeeDate ?? DueDate`.
2. **Los pagos revertidos seguían contando como pagados en cuatro lugares, no solo en la mora automática:** la mora automática (`LateFeeAccrualService`), el recargo manual (`ApplyLateFees`), el aviso de deuda que bloquea reservas de amenities (`UnitOverdueService`) y el reporte de Cobranza (`CollectionsController`). La morosidad (`MorosityController`) ya los excluía. Si se revertía un pago, la deuda seguía pareciendo saldada.
3. **La mora tiene un solo parámetro global** (tasa y frecuencia): sin tope, sin mínimo, sin elegir qué cargos la generan, sin exoneración por unidad.
4. **`Building.DefaultDueDay`** solo propone el día al crear un período; no hay forma de generar el período siguiente con esa regla automáticamente.

---

## 2. Qué es el Centro de configuración

Una pantalla por edificio con **secciones laterales** y un **indicador de estado** por sección (Completa / Incompleta / Opcional / No disponible por plan) y uno general («7 de 10 listas para operar»).

**Reglas de diseño**

1. **Envuelve, no reemplaza.** La ficha de 5 pestañas y la configuración de Finanzas siguen funcionando; el Centro las enlaza y suma lo que falta. Una sola fuente de verdad por dato.
2. **Políticas como datos, no como código.** Cada política es un campo o fila configurable por edificio; el motor es común.
3. **Nada se carga dos veces.** Si un dato vive en otra pantalla, el Centro lo muestra de solo lectura con enlace.
4. **Permisos por sección** (tabla 3.2). Lo que hoy es SuperAdmin sigue siéndolo.
5. **Lo que no aplica por plan o módulo apagado se muestra deshabilitado con el motivo**, no se oculta.
6. **«Copiar configuración de otro edificio»** (administradoras con varios edificios): copia por sección, con vista previa de lo que cambia; nunca toca movimientos ni datos transaccionales.

---

## 3. Secciones

### 3.1 Contenido de cada sección

| # | Sección | Contenido | Estado actual | Trabajo |
|---|---|---|---|---|
| 1 | Identidad y fiscal | Ficha, RUC, razón social, régimen IVA, timbrados y series, modo de facturación | Existe | Solo enlazar y calcular estado |
| 2 | Cobro y vencimientos | Día de vencimiento, gracia, instrucciones de pago, cuentas bancarias, medios aceptados | Existe | Enlazar; **dar efecto a la gracia** (3.4); generación automática del período siguiente (opcional) |
| 3 | Política de mora | Tasa, frecuencia, gracia real, tope, mínimo, cargos que generan mora, exoneraciones por unidad, avisos | Parcial | Nuevo bloque (4.1) |
| 4 | Regla de pago | Informativo y fijo para todos los edificios: el comprobante (todo lo pendiente de una unidad en un período, con mora) se paga completo, del más antiguo al más nuevo; sin pagos parciales ni saldo a favor | Existe como regla | Solo mostrar la regla vigente; no hay nada que configurar |
| 5 | Fondos | % reserva, % extraordinario, tratamiento de ingresos, **política de uso del fondo** | Parcial | Agregar política de uso (4.2) |
| 6 | Plan de cuentas y cuentas financieras | Fecha de arranque, ejercicio, cuentas, plan, cuenta por defecto, funciones especiales | Existe (SuperAdmin) | Enlazar; agregar **mapeo de funciones contables** (4.5) |
| 7 | Impuestos | IVA por cuenta de gasto/ingreso (10 %, 5 %, exento) | Falta | Fase 4 |
| 8 | Proveedores | Alta con RUC, condiciones de pago, cuenta habitual | Falta | Fase 4 |
| 9 | Período y cierre | Quién cierra, bloqueo retroactivo, reapertura con motivo | Falta | Fase 2 |
| 10 | Presupuesto y alertas | Presupuesto anual, umbral del semáforo, avisos de desvío | Parcial | Umbral configurable (4.3) |
| 11 | Unidades y ocupación | Responsable de pago de la unidad (propietario o inquilino), contrato | Falta | Fase 8 |
| 12 | Documentos y comunicación | Plantillas PDF, avisos automáticos (previo al vencimiento, mora, pago recibido) | Parcial | Reglas de avisos (4.4) |

### 3.2 Permisos

| Sección | Ver | Editar |
|---|---|---|
| 1 Identidad y fiscal | SuperAdmin, Admin, Operador | SuperAdmin, Admin (los campos que hoy edita cada uno; sin cambios) |
| 2 Cobro y vencimientos | SuperAdmin, Admin, Operador | SuperAdmin, Admin |
| 3 Política de mora | SuperAdmin, Admin, Operador, Encargado | **SuperAdmin, Admin** |
| 4 Regla de pago (informativa) | SuperAdmin, Admin, Operador | Nadie (no es configurable) |
| 5 Fondos | SuperAdmin, Admin, Operador | SuperAdmin, Admin |
| 6 Plan de cuentas y cuentas | Los cuatro roles | **Solo SuperAdmin** (servicio; decisión vigente) |
| 7 Impuestos | Los cuatro roles | SuperAdmin, Admin |
| 8 Proveedores | Los cuatro roles | SuperAdmin, Admin, Operador, Encargado (decisión 7 de Finanzas) |
| 9 Período y cierre | Los cuatro roles | Cerrar y reabrir: SuperAdmin, Admin. Reabrir exige motivo |
| 10 Presupuesto y alertas | Los cuatro roles | SuperAdmin, Admin |
| 11 Unidades y ocupación | SuperAdmin, Admin, Operador | SuperAdmin, Admin, Operador |
| 12 Documentos y comunicación | SuperAdmin, Admin, Operador | SuperAdmin, Admin |

El servidor valida el permiso en **cada** endpoint y el acceso al edificio con `CanAccessBuildingAsync`; en endpoints por id, un edificio ajeno responde 404 (patrón vigente).

### 3.3 Cálculo del estado «listo para operar»

Endpoint de solo lectura que evalúa reglas simples por sección. Una sección está **Completa** cuando cumple su regla:

| Sección | Regla de «completa» |
|---|---|
| 1 | RUC, razón social, régimen IVA y al menos una serie vigente (si el modo de facturación lo exige) |
| 2 | Día de vencimiento definido y al menos una cuenta bancaria o instrucción de pago |
| 3 | Política de mora definida (o marcada explícitamente «sin mora») |
| 4 | Siempre completa (es una regla fija) |
| 5 | % reserva definido o marcado «no aplica»; tratamiento de ingresos elegido |
| 6 | `FinanceSettings.SetupCompleted` (o «No disponible por plan») |
| 7 | Cada cuenta final de egresos activa tiene tratamiento de IVA |
| 8 | Opcional |
| 9 | Ejercicio definido y rol de cierre confirmado |
| 10 | Opcional |
| 11 | Opcional |
| 12 | Opcional |

«Marcado explícitamente» evita confundir «no lo configuré» con «no aplica»: se guarda como valor, no como ausencia.

### 3.4 Gracia real (aclaración de regla)

Fecha desde la que corre la mora = `LateFeeDate` si existe; si no, `DueDate + GraceDays`. Al crear un período se propone `DueDate` desde `DefaultDueDay` y `LateFeeDate` desde `DueDate + GraceDays`; el valor del período manda sobre el del edificio. **Los períodos ya publicados no cambian** (la regla nueva rige solo desde la fecha de la migración para evitar recálculos históricos).

---

## 4. Modelo de datos nuevo

Todo opcional con valores por defecto que **reproducen el comportamiento actual** para no alterar edificios existentes.

### 4.1 Política de mora — columnas nuevas en `Buildings`

| Campo | Tipo | Defecto | Significado |
|---|---|---|---|
| `LateFeeCapPercentage` | decimal? | null | Tope acumulado de mora como % de la expensa original; null = sin tope (hoy) |
| `LateFeeMinAmount` | decimal? | null | Si la mora de un intervalo da menos que esto, se cobra este mínimo |
| `LateFeeAppliesToReserve` | bool | true | Si el aporte al fondo entra en la base de cálculo (hoy entra todo lo que no es mora) |
| `LateFeeAppliesToExtraordinary` | bool | true | Idem para extraordinarios |
| `LateFeeAppliesToIndividual` | bool | true | Idem para cargos individuales |
| `LateFeePolicyConfirmed` | bool | false | «Sin mora» explícito cuando la tasa está vacía |

La **exoneración por unidad** va en `Unit`: `LateFeeExempt` (bool, defecto false) y `LateFeeExemptReason` (texto), con auditoría de quién lo marcó.

### 4.2 Política de uso del fondo — `FinanceSettings`

`ReserveUsePolicy` (enum: `FreeUse`, `RequiresPresidentApproval`, `RequiresAssemblyApproval`), `ReserveUseThreshold` (decimal?, monto desde el cual rige). Informativo en la fase 1; en la fase 3 puede exigir un campo «Referencia de aprobación» al cargar un gasto pagado por el fondo.

### 4.3 Alertas de presupuesto — `FinanceSettings`

`BudgetWarnPercent` (defecto 10) y `BudgetAlertEnabled`. Hoy el 10 % es fijo en código.

### 4.4 Avisos automáticos — tabla `BuildingNoticeRules`

| Campo | Significado |
|---|---|
| `BuildingId`, `CompanyId` | Alcance |
| `Kind` | `BeforeDue`, `OnDue`, `LateFeeApplied`, `PaymentReceived`, `PeriodPublished` |
| `OffsetDays` | Para `BeforeDue`: días antes |
| `Channel` | Reutiliza los canales existentes (push, notificación en pantalla, correo por Resend) |
| `IsActive` | |

Reutiliza `Notification` y `PushDispatcher`. Defecto: sin reglas = comportamiento actual.

### 4.5 Mapeo de funciones contables — sin tabla nueva en la primera versión

Las funciones especiales ya existen en `LedgerCategory.SystemKey` (cobranza y cuenta por defecto por categoría). Para asientos sugeridos faltan cuentas de **contrapartida** (caja, bancos, cuentas por cobrar, IVA débito/crédito, proveedores). Se agrega la tabla `LedgerAccountRoles` (`BuildingId`, `Role`, `LedgerCategoryId`) con roles: `Receivables`, `Cash`, `Bank`, `VatDebit`, `VatCredit`, `Payables`, `ReserveFund`. Se completa en la fase 6; antes no se necesita.

### 4.6 Período y cierre — `FinancePeriodClosures`

| Campo | Significado |
|---|---|
| `BuildingId`, `CompanyId` | Alcance |
| `Year`, `Month` | Mes calendario cerrado |
| `ClosedAtUtc`, `ClosedByUserId` | Quién y cuándo |
| `ReopenedAtUtc`, `ReopenedByUserId`, `ReopenReason` | Reapertura con motivo |

Única por (`BuildingId`, `Year`, `Month`) vigente. Un mes cerrado **bloquea** altas, ediciones y bajas de movimientos fechados en él: gastos, ingresos, pagos (alta y reversión), notas de crédito, presupuesto y recurrentes aplicados. La regla vive en **un solo servicio** (`FinancePeriodGuard`) que llaman todos los puntos de escritura; no se duplica en controladores. Respuesta de error uniforme `409 { error: "finance_period_closed", message }`.

No se puede cerrar un mes si hay un mes anterior abierto con movimientos (cierre secuencial), salvo el primero después de `FinanceStartDate`.

### 4.7 Auditoría — `FinanceAuditLog`

Patrón de `CreditNoteAuditLog`: `BuildingId`, `CompanyId`, `UserId`, `Section`, `Action`, `EntityType`, `EntityId`, `BeforeJson`, `AfterJson`, `CreatedAtUtc`. Registra **todo cambio de configuración** de las secciones 2 a 12, además de cierres y reaperturas. Visible en el Centro como «Historial de cambios» (solo lectura). **Debe existir antes de la fase 2** para que el cierre nazca auditado.

### 4.8 Proveedores — `Suppliers` y cambios en `BuildingExpense`

`Supplier`: `CompanyId`, `Name`, `Ruc`, `DefaultLedgerCategoryId?`, `PaymentTermDays?`, `IsActive`, `Phone`, `Email`. Único por (`CompanyId`, `Ruc`) cuando el RUC no es nulo.
`BuildingExpense`: agregar `SupplierId?` (FK sin cascada). `SupplierName` se conserva como texto de compatibilidad: si hay `SupplierId`, el nombre se copia al guardar; los gastos viejos no se tocan. Importación de gastos por Excel sigue aceptando nombre libre (si coincide por RUC o nombre exacto, vincula).

### 4.9 Cuentas por pagar — en `BuildingExpense`

`DueDate?` (vencimiento), `PaidAt?` (fecha de pago), `PaidFromAccountId?`, `PaymentStatus` (derivado: Pendiente / Pagada / Vencida). **Regla de compatibilidad:** los gastos sin estado explícito se tratan como pagados en `ExpenseDate` (comportamiento actual del libro). El libro (criterio percibido) pasa a usar `PaidAt` solo en gastos que tengan `DueDate` o `PaidAt` cargados; el resto sigue igual. Así no cambia ningún saldo existente.

### 4.10 IVA

`LedgerCategory.VatTreatment` (enum: `Vat10`, `Vat5`, `Exempt`, `NotApplicable`; null = no definido). `BuildingExpense.VatRate?` y `VatAmount?` (por defecto desde la cuenta; editable por gasto). `Invoice`: el detalle ya se guarda como `DetalleSnapshotJson`; se agrega un resumen de IVA por tasa al emitir (`Iva10Amount`, `Iva5Amount`, `ExemptAmount`) calculado desde el detalle. **Las facturas ya emitidas no se recalculan**; se rotulan «sin discriminar». El régimen del edificio (`VatRegime`) decide si se discrimina: Resimple/Exento no discrimina.

### 4.11 Ocupación — `Unit` y `UnitOwner`

`Unit.ExpensePayerType` (`Owner` por defecto, `Tenant`), `Unit.ExpensePayerResidentId?` (residente responsable cuando es inquilino). Solo cambia **a quién se muestra el estado de cuenta y se avisa**; el titular de la deuda sigue siendo la unidad y el propietario (no se traslada responsabilidad jurídica). Facturación: ya existe «cliente guardado al emitir» y ficha de facturación; se reutiliza.

---

## 5. API (propuesta)

Prefijo `/api/building-config/{buildingId}`. Todos validan acceso al edificio y rol; todos registran en `FinanceAuditLog`.

| Método | Ruta | Función |
|---|---|---|
| GET | `/overview` | Secciones con estado, porcentaje listo, motivos de lo incompleto, permisos del usuario (`canEdit` por sección) |
| GET/PUT | `/late-fee` | Política de mora |
| GET/PUT | `/fund-policy` | Política de uso del fondo |
| GET/PUT | `/budget-alerts` | Umbral de alertas |
| GET/PUT | `/notice-rules` | Reglas de avisos |
| GET/PUT | `/account-roles` | Contrapartidas contables (fase 6) |
| GET | `/closures` | Períodos cerrados |
| POST | `/closures/{year}/{month}/close` | Cerrar mes |
| POST | `/closures/{year}/{month}/reopen` | Reabrir (motivo obligatorio) |
| GET | `/audit` | Historial de cambios, paginado, filtrable por sección y usuario |
| POST | `/copy-from/{sourceBuildingId}` | Copiar configuración por sección (cuerpo: secciones elegidas; `preview=true` no guarda) |
| GET/POST/PUT | `/api/suppliers` | Proveedores (por empresa) |
| PUT | `/api/units/{id}/late-fee-exemption` | Exonerar o quitar exoneración |
| PUT | `/api/units/{id}/expense-payer` | Responsable de pago |

La ficha, Finanzas y Timbrados **conservan sus endpoints**; `overview` los lee, no los reemplaza.

---

## 6. Pantallas web (Condo-PY-WEB)

Revisado contra el repo `Condo-PY-WEB` (Angular, componentes standalone con plantilla en línea, PrimeNG, rutas planas con `loadComponent` en `src/app.routes.ts`, un servicio de API por módulo en `src/app/api`, tipos en `api/models.ts`).

### 6.1 Hallazgos del repo web que condicionan el diseño

1. **El selector de edificio de Finanzas no sirve para el Centro.** `FinanceBuildingPickerComponent` lista solo los edificios con el módulo Finanzas disponible; el Centro debe funcionar también para edificios **sin** Finanzas (sus secciones 1, 2, 3, 4, 5, 9, 11 y 12 no dependen del módulo). Se usa `BuildingsApiService.getAll()` (con caché de 30 s) y se recuerda la última elección en `localStorage` con una clave propia (`condopy.buildingconfig.buildingId`).
2. **La ficha no se puede embeber.** `building-create-page.component.ts` tiene 1315 líneas, un solo formulario y guarda con un `PUT /buildings/{id}` que manda **todo** el edificio. Editar un campo suelto desde el Centro duplicaría esa lógica. Decisión: **las secciones 1, 2, 4 y 5 se enlazan a la pestaña correspondiente de la ficha** y el Centro solo muestra un resumen de solo lectura y su estado. Esto cambia la frase «embebido o enlazado» del punto 3.1: es **enlazado**.
3. **La ficha no acepta una pestaña por URL.** Solo lee `:id` de la ruta. Hay que agregar `?tab=` (general, legal, billing, accounting, config) para el enlace profundo. Cambio mínimo y aislado.
4. **Hay editores reutilizables de Finanzas** (`finance-accounts-editor`, `finance-chart-editor`, `finance-plan-import`, `finance-plan-template`), pero hoy viven dentro de la pantalla de configuración de Finanzas y de su asistente. La sección 6 del Centro **enlaza** a `/finance/settings?buildingId=` en vez de reubicarlos.
5. **No hay guardas de rol por ruta**: solo `authGuard`; la visibilidad la decide el menú (`app.menu.ts`) y la seguridad real la pone el servidor. El Centro sigue ese patrón: entrada de menú por rol, y el servidor devuelve 403/`canEdit` por sección. No se introduce un guarda nuevo.
6. **Cambios sin guardar:** la pantalla de presupuesto ya maneja `dirty` con botones Guardar/Descartar. Las secciones editables nuevas siguen ese mismo patrón; cada sección guarda **por separado** (no hay un «guardar todo» del Centro).
7. **Estilos y componentes comunes:** `p-message` para avisos, `app-finance-state` para cargando/bloqueado/configuración incompleta, variables `--brand-*`. Se reutilizan; no se agrega una librería ni un tema.

### 6.2 Archivos del repo web

| Tipo | Archivo | Detalle |
|---|---|---|
| Ruta | `src/app.routes.ts` | `building-config` (más `?buildingId=` y `?section=`) |
| Menú | `layout/component/app.menu.ts` | «Gestión → Configuración del edificio» para `CompanyAdmin`, `CompanyOperator`, `BuildingManager`; en SuperAdmin, bajo «Edificios» |
| API | `api/building-config-api.service.ts` | Un método por endpoint de la sección 5 |
| Tipos | `api/models.ts` | `BuildingConfigOverview`, `ConfigSectionStatus`, `LateFeePolicy`, `FundPolicy`, `NoticeRule`, `PeriodClosure`, `ConfigAuditEntry`, `Supplier`, etc. |
| Pantalla | `pages/condo/building-config-page.component.ts` | Contenedor: selector de edificio, indicador general, menú lateral, `router-outlet` interno o secciones por `@switch` |
| Secciones nuevas | `pages/condo/building-config-*.component.ts` | `late-fee`, `fund-policy`, `budget-alerts`, `notice-rules`, `closures`, `audit`, `copy-from`; luego `suppliers` (fase 4) y `occupancy` (fase 8) |
| Ficha | `pages/condo/building-create-page.component.ts` | Solo agregar lectura de `?tab=` y, en fase 3, mostrar la gracia real |
| Unidades | `pages/condo/units-page.component.ts` | Fase 3: marca de exoneración de mora. Fase 8: responsable de pago |
| Gastos | `pages/condo/building-expenses-page.component.ts` | Fase 2: mensaje de mes cerrado. Fase 4: proveedor, vencimiento, pago e IVA |
| Tablero | `pages/condo/dashboard-page.component.ts` | Aviso «Configuración incompleta» con enlace al Centro |

### 6.3 Comportamiento de la pantalla

- Ruta `/building-config`: encabezado con selector de edificio (si hay más de uno) e indicador «N de M listas»; columna de secciones con estado (Completa, Incompleta, Opcional, No disponible por plan, con el motivo en texto); contenido a la derecha. En móvil, las secciones pasan a una lista desplegable.
- **Cada sección muestra el motivo de lo incompleto** (viene en `overview`), no solo un color.
- **Solo lectura clara:** si `canEdit` es falso, los campos se ven deshabilitados con una línea que dice quién puede editar (por ejemplo, «Lo configura el equipo de CondoPY»).
- **Errores del servidor** con `api-error.util.ts`; 409 `finance_period_closed` se traduce a un mensaje entendible en todas las pantallas que escriben (gastos, ingresos, pagos, NC, presupuesto), no solo en el Centro.
- **Copiar de otro edificio:** diálogo con selección de secciones y vista previa de diferencias antes de aplicar.
- **Historial de cambios:** tabla paginada con filtro por sección y usuario, mostrando antes/después en forma legible (no JSON crudo).
- **Cierre de período:** calendario de 12 meses del ejercicio con estado por mes; cerrar pide confirmación; reabrir exige motivo.

### 6.4 Impacto en la app móvil (`CondoPY-APP`, Ionic/Capacitor)

No contemplado en la primera versión del plan. Qué toca:
- **Fase 3:** los avisos de mora y de vencimiento llegan por `Notification` y push, que la app ya consume; solo hay que agregar los nuevos `NotificationType` y su icono/texto. Si la app muestra la mora, debe respetar el tope y la exoneración (los cargos ya vienen calculados, así que no cambia la lógica).
- **Fase 8:** el inquilino responsable necesita acceso a su estado de cuenta. Hoy el acceso depende del vínculo propietario–unidad y residente–usuario; hay que decidir si el inquilino entra con el usuario de residente existente (`Resident.ApplicationUserId`) y ver qué endpoints exigen ser propietario.
- Fases 1, 2, 4, 5, 6 y 7 no afectan a la app (son de administración).
- Como cada cambio de la app exige reconstruir el APK, conviene juntar los de las fases 3 y 8.

### 6.5 Orden de entrega del frente

| Fase | Trabajo en el repo web |
|---|---|
| 1 | Servicio de API, tipos, contenedor, selector, indicador, secciones enlazadas, historial, `?tab=` en la ficha, aviso en el Tablero, entrada de menú |
| 2 | Sección Período y cierre, manejo del 409 en todas las pantallas que escriben |
| 3 | Mora, avisos, umbral de alertas, exoneración en Unidades, gracia en la ficha |
| 4 | Proveedores, vencimiento y pago en Gastos, IVA, cuentas por pagar en el Tablero |
| 5 | Pantalla de conciliación en el menú de Finanzas |
| 6 y 7 | Asientos sugeridos en exportación; informe de asamblea (descarga de PDF) |
| 8 | Ocupación en Unidades; ajustes en la app móvil |

---

## 7. Plan de trabajo por fases

Cada fase se entrega compilando limpio, con su migración EF y su script `.sql` idempotente (entregado como `cat <<'EOF' | sqlcmd`), y con guía de pruebas para Tony en `docs/GUIA_PRUEBAS_*.md`.

### Fase 1 — Centro de configuración (sin esquema nuevo salvo auditoría)
- `FinanceAuditLog` (tabla) y su servicio; registrar cambios de las configuraciones existentes que el Centro edite.
- `GET /overview` con reglas de 3.3.
- Pantalla `/building-config` con las secciones 1, 2, 4, 5, 6 (enlazadas o embebidas) y el historial.
- Aviso en el Tablero.
- **Aceptación:** un edificio nuevo muestra el estado correcto; completar una sección actualiza el indicador; ninguna pantalla existente cambia de comportamiento; cada cambio queda en el historial con antes/después.

### Fase 2 — Cierre de período
- `FinancePeriodClosures`, `FinancePeriodGuard`, endpoints de cierre y reapertura, sección 9.
- Integrar el guard en: altas, ediciones y bajas de gastos, ingresos, recurrentes aplicados, pagos (alta y reversión), notas de crédito de proveedor, presupuesto.
- **Aceptación:** con el mes cerrado, todo intento de escritura fechado en él responde 409 `finance_period_closed`; reabrir exige motivo y queda auditado; no se puede cerrar saltando un mes con movimientos; ningún flujo de lectura ni la liquidación de otros meses se afecta; pruebas por cada punto de escritura.

### Fase 3 — Política de mora completa y corrección de defectos
- Columnas de 4.1; `Unit.LateFeeExempt`.
- **Corregir 1.3:** gracia efectiva (3.4), exclusión de pagos revertidos en `LateFeeAccrualService`, revisión de la morosidad.
- Tope, mínimo y bases elegibles en el cálculo; exoneración por unidad.
- Reglas de avisos (4.4): previo al vencimiento, mora aplicada, pago recibido.
- Sección 3, 10 (umbral) y 12.
- **Aceptación:** con valores por defecto el cálculo es idéntico al actual (prueba de regresión con datos reales de ejemplo); con gracia 5 la mora arranca 5 días después; un pago revertido reactiva la mora; tope y mínimo respetados; unidad exonerada no acumula; los avisos no se duplican (clave por unidad, período e intervalo).

### Fase 4 — Proveedores, cuentas por pagar e IVA
- `Suppliers`; `BuildingExpense.SupplierId`, `DueDate`, `PaidAt`, `PaidFromAccountId`; `LedgerCategory.VatTreatment`; IVA en gastos y resumen de IVA en facturas nuevas.
- Secciones 7 y 8; listado de cuentas por pagar con antigüedad; tablero suma cuentas por pagar y cobranza/morosidad.
- Libro: respetar `PaidAt` solo cuando está cargado (4.9).
- **Aceptación:** los saldos de edificios existentes no cambian; un gasto con vencimiento aparece en cuentas por pagar hasta cargar el pago; el libro mueve la plata en `PaidAt`; libro de compras y ventas por tasa cuadra con los comprobantes.

### Fase 5 — Conciliación bancaria manual
- Tabla persistente de movimientos del libro (`ReconciledAt`, `ReconciledByUserId`) y líneas de extracto cargadas a mano; marcado manual; diferencia y pendientes por cuenta.
- La conciliación respeta el cierre de período (no desmarca en meses cerrados).
- Importación del extracto por Excel/CSV queda para después de elegir los bancos principales (decisión 9 de Finanzas).
- **Aceptación:** diferencia = saldo del extracto − saldo del libro conciliado; una línea no se marca dos veces; todo marcado queda auditado.

### Fase 6 — Asientos sugeridos y paquete del contador completo
- `LedgerAccountRoles` (4.5), generación de asientos sugeridos (debe/haber) a partir de cobros, gastos, mora y NC, y su exportación; libro de IVA y retenciones simples en el paquete.
- **Aceptación:** cada asiento cuadra (debe = haber); la suma por cuenta coincide con el libro; sugeridos, no asentados: no alteran saldos.

### Fase 7 — Informe de asamblea
- PDF único (Estado de resultados, ejecución presupuestaria, morosidad por antigüedad, fondo de reserva, cuentas por pagar, notas del administrador), por rango de fechas.
- **Aceptación:** los números coinciden con las pantallas origen (mismo servicio `FinanceReportService`).

### Fase 8 — Ocupación y alquileres (mínimo)
- Campos de 4.11, sección 11, estado de cuenta y avisos al inquilino, sin módulo de contratos.
- **Aceptación:** el inquilino responsable ve su estado de cuenta; la deuda sigue en la unidad; la facturación usa el cliente de facturación configurado.

---

## 8. Reglas de compatibilidad (valen para todas las fases)

1. Defaults que reproducen el comportamiento actual; nadie nota la migración.
2. No se reprocesa historia: reglas nuevas rigen hacia adelante.
3. Los datos viejos sin el campo nuevo se muestran como «sin definir», no como error.
4. Ninguna fase obliga a completar el Centro para seguir operando lo que hoy funciona; el estado «incompleto» informa, no bloquea (salvo cierre de período, que bloquea solo meses cerrados).
5. El interruptor del módulo Finanzas y las reglas de plan (solo lectura/bloqueo por plan vencido) se heredan de `PlanRestrictionMiddleware`; el Centro vive bajo rutas que ya filtra.
6. Aislamiento: cada consulta filtra por `CompanyId` y valida acceso al edificio. Debe revisarse contra los hallazgos abiertos de la auditoría de aislamiento (2026-09-29) antes de publicar.

---

## 9. Riesgos y mitigación

| Riesgo | Mitigación |
|---|---|
| El guard de cierre se olvida en algún punto de escritura | Un solo servicio; pruebas que recorren todos los controladores de escritura con el mes cerrado; lista de verificación en la guía de pruebas |
| Cambiar la mora altera deuda de propietarios reales | Defaults idénticos, prueba de regresión, regla nueva solo hacia adelante, mostrar vista previa del efecto antes de guardar |
| El libro cambia saldos al introducir `PaidAt` | Solo aplica a gastos con `DueDate`/`PaidAt` cargados (4.9) |
| IVA mal discriminado en facturas | Solo facturas nuevas; régimen del edificio decide; las viejas rotuladas «sin discriminar» |
| Centro se vuelve un cajón de sastre | Regla 3 de la sección 2: no duplica, enlaza; cada campo nuevo exige justificar qué proceso lo consume |
| Proveedores duplicados | Único por RUC y empresa; vinculación por RUC al importar |

---

## 10. Decisiones y supuestos

Estas cuatro decisiones quedaron abiertas en la conversación. La especificación **asume la opción recomendada**; cambiarla es barato antes de la fase 1.

| # | Tema | Supuesto adoptado | Alternativa |
|---|---|---|---|
| 1 | Centro frente a la ficha | El Centro **envuelve** la ficha y la configuración de Finanzas | Reemplazarlas |
| 2 | Quién edita mora, cierre y proveedores | **SuperAdmin y Administrador de empresa** (plan de cuentas sigue solo SuperAdmin) | Todo solo SuperAdmin |
| 3 | Perfil de cliente | Se asume **administradoras con varios edificios**: incluye «copiar de otro edificio» | Omitirlo si son comisiones de un solo edificio |
| 4 | Pagos online | **Fuera de este plan**; sigue en `PROPUESTA_PAGOS_ONLINE.md`. Cuando se decida, entra por las secciones 2 y 5 y la fase 5 | Incluirlo antes de la conciliación |

Pendiente de Tony además: confirmar si el cierre de período puede ser **opcional por edificio** (apagable) o es siempre obligatorio cuando el módulo de Finanzas está encendido. Supuesto adoptado: **opcional**, apagado por defecto.

---

## 11. Orden recomendado y esfuerzo relativo

| Fase | Valor percibido | Riesgo | Esfuerzo |
|---|---|---|---|
| 1 Centro y auditoría | Alto | Bajo | Medio |
| 2 Cierre de período | Alto (control) | Medio | Medio |
| 3 Mora completa y correcciones | Alto (plata real) | **Alto** (toca deuda) | Medio |
| 4 Proveedores, CxP e IVA | Alto (contador) | Medio | Alto |
| 5 Conciliación | Alto | Medio | Medio |
| 6 Asientos y paquete contador | Medio | Medio | Medio |
| 7 Informe de asamblea | Alto (visible) | Bajo | Bajo |
| 8 Ocupación | Medio | Bajo | Bajo |

Si hay que priorizar por dinero: **3 antes que 2**, porque corrige un defecto que hoy puede dejar de cobrar mora. La fase 1 va primero igual: crea la auditoría que las demás necesitan.

---

## 12. Estado de implementación

Decisiones de Tony (2026-10-09) que rigen la ejecución: trabajo en la rama `feature/centro-configuracion`; **un commit por fase** al terminar compilando limpio y con las pruebas en verde, sin push salvo indicación; el cierre de período es **opcional por edificio y apagado por defecto**; mora, cierre y proveedores los editan **SuperAdmin y Administrador de empresa**; el frente web se hace **al final de todo**.

### 12.1 Fase 1 — Centro de configuración y auditoría (backend, hecha 2026-10-09)

**Datos:** tabla `FinanceAuditLogs` (entidad `FinanceAuditLog`; migración EF `20261009163132_ConfigCenterAuditLog` y su script `.sql` idempotente). Solo agrega una tabla y tres índices; no toca datos existentes. En vez de `BeforeJson`/`AfterJson` separados guarda `ChangesJson`, una lista de `{ field, label, before, after }` (más legible para el historial); `Summary` lleva la frase para mostrar.

**Historial:** `ConfigAuditWriter` solo agrega la fila al contexto: queda en el mismo `SaveChanges` que el cambio (todo o nada). Se engancha en:
- Ficha del edificio (`PUT /api/buildings/{id}`): una entrada por **sección** que cambió (identidad, cobro, mora, fondos), comparando una foto (`BuildingConfigSnapshot`) antes y después; si no cambió nada, no escribe. Incluye las cuentas bancarias del edificio. Las plantillas de documentos aún no se registran (llegan con la sección 12).
- Finanzas: habilitar/apagar el módulo, fecha de arranque y mes del ejercicio, cuenta por defecto, completar la configuración inicial, cuentas financieras (alta, edición con cambios, baja), plan de cuentas (alta, edición con cambios, baja, activar/desactivar en bloque, copiar de otro edificio, plan genérico y Excel del cliente; el **reemplazo** del plan lleva su entrada dentro de la misma transacción) y presupuesto (guardar, copiar, completar con el promedio).
- Timbrados (alta y desactivación), sección de identidad.

**API** (`/api/building-config/{buildingId}`):
- `GET overview`: secciones visibles para el rol, con estado, motivos, resumen, `required`, `canEdit`, y a dónde se edita (`linkKind` = `building` o `finance`, `linkTab`). Devuelve `requiredCount`, `readyCount`, `readyToOperate`, `financeAvailable`, `canViewAudit`. Roles: los cuatro administrativos; otro rol responde 403 `building_config_forbidden`; un edificio ajeno o inexistente, 404.
- `GET audit`: historial paginado (más reciente primero), filtros `section`, `userId`, `from`, `to`; muestra el nombre de quien lo hizo. Solo **SuperAdmin y Administrador de empresa** (el historial contiene correos y nombres; la especificación original no lo restringía de forma explícita).

**Secciones incluidas en la fase 1:** 1 Identidad y fiscal, 2 Cobro y vencimientos, 3 Política de mora (resumen; se edita en la ficha), 4 Regla de pago (informativa), 5 Fondos (resumen), 6 Plan de cuentas y cuentas financieras, 10 Presupuesto. Las demás se suman en su fase.

**Reglas de estado tal como quedaron** (ajustes respecto de la sección 3.3):
- Identidad: RUC, razón social, régimen de IVA y **al menos un timbrado vigente** (activo y dentro de su fecha de vigencia). Se exige siempre, no solo según el modo de facturación: hoy la emisión de facturas siempre necesita un timbrado vigente, sea cual sea el modo.
- Cobro: día de vencimiento y (una cuenta bancaria activa o instrucciones de pago).
- Mora y Fondos: **no son obligatorias en esta fase** (`required = false`): configuradas se ven completas, y sin configurar quedan «Opcional». Pasan a obligatorias en la fase 3, cuando exista la confirmación explícita de «sin mora» / «no aplica»; antes, un edificio sin mora no tiene forma de decirlo y quedaría incompleto para siempre.
- Plan de cuentas: «No disponible» si el módulo Finanzas no está habilitado o el plan no lo incluye (no cuenta para el indicador); completo cuando se completó la configuración inicial.
- Regla de pago: siempre completa; muestra la regla vigente (comprobante completo, del más antiguo al más nuevo, sin parciales ni saldo a favor).
- Indicador: cuentan solo las secciones con `required = true` y disponibles.

**Pruebas:** 26 pruebas nuevas (`ConfigCenterPhase1Tests`): estados y motivos, timbrado vencido o inactivo, roles y `canEdit`, 403 y 404, historial de cuentas, fecha de arranque, plan genérico, alta de cuenta del plan, paginación y filtros, nombre de usuario, aislamiento entre edificios, permisos del historial, y el guardado real de la ficha (una entrada por sección, sin entrada si no hay cambios). La prueba del guardado de la ficha detectó que una cuenta bancaria nueva se contaba dos veces en la foto «después» (EF la engancha a la colección del edificio al agregarla); quedó corregido. Suite completa: 827 pasan, 15 omitidas (las que exigen SQL Server).

**Pendiente de esta fase para el final:** todo el frente web (secciones 6.1 a 6.5), incluido el `?tab=` de la ficha.

### 12.2 Fase 2 — Cierre de período (backend, hecha 2026-10-09)

Decisiones de Tony para esta fase: el cierre **depende del módulo Finanzas** (sin Finanzas disponible la sección figura «No disponible»); **solo se cierran meses ya terminados y en orden** (desde la fecha de arranque; reabrir puede hacerse con cualquier mes, con motivo); un mes cerrado bloquea **gastos e ingresos, pagos de propietarios, notas de crédito de proveedor y presupuesto**, evaluado por la fecha de cada movimiento.

**Datos:** `FinanceSettings.PeriodClosingEnabled` (interruptor por edificio, **apagado por defecto**: los edificios existentes no cambian) y tabla `FinancePeriodClosures` (migración `20261009164951_PeriodClosing` y su `.sql` idempotente). Reabrir no borra: la fila queda con quien reabrió, cuándo y por qué, y volver a cerrar crea otra fila. Un índice único filtrado garantiza un solo cierre vigente por edificio y mes.

**Guarda única:** `FinancePeriodGuard` es el único lugar donde se decide si un mes está cerrado. Rige solo si el interruptor del edificio está encendido, el módulo Finanzas está disponible y el mes tiene un cierre vigente; sin interruptor, el costo es una consulta. Respuesta uniforme `409 { error: "finance_period_closed", message }`; el mensaje nombra el mes y dice dónde reabrirlo.

**Dónde se aplica** (con prueba en cada punto):
- Gastos del edificio: alta, edición (se miran la fecha actual **y** la nueva), baja e importación de Excel (la fecha de los gastos nuevos y, con «reemplazar», la de los que se eliminan).
- Ingresos: alta, edición y baja. **No se bloquean** el arrastre de saldo (`AccumulatedBalance`) ni el fondo operativo (`OperationalFund`): el libro los excluye, no son plata.
- Gastos recurrentes al aplicarlos a un período, y clonado de período (los gastos e ingresos copiados llevan la fecha de inicio del período nuevo).
- Notas de crédito de proveedor: alta y anulación, según la fecha del gasto.
- Pagos: registrar (se rechaza **antes** de crear el pago, sin dejar un registro rechazado), aprobar un pago de la app (queda «En revisión»), revertir un pago registrado por el sistema y el camino antiguo de reversa. Cada pago cuenta en el edificio de su período y en el mes de su **fecha de pago**. Un pago que el propietario envía desde la app no se bloquea al enviarlo (todavía no toca el libro); sí al aprobarlo.
- Presupuesto: guardar celdas (se rechaza si cambia el importe de un mes cerrado; una celda sin cambio de importe se acepta) y, en «copiar del ejercicio anterior» y «completar con el promedio», los meses cerrados **se omiten**.
- Lo que cambiaría los números de los meses cerrados sin ser un movimiento con fecha: con meses cerrados **no se cambia** la fecha de arranque ni el mes de inicio del ejercicio, el saldo inicial o el tipo de una cuenta financiera, no se elimina una cuenta y no se **reemplaza** el plan de cuentas (agregar lo que falta sí se puede).
- No se bloquean (no alimentan el libro): cargos de la liquidación, mora automática, facturas, comprobantes adjuntos de un gasto, ni el saldo a favor (desactivado) — este último crea pagos con la fecha de hoy, que nunca cae en un mes cerrado.

**API** (`/api/building-config/{buildingId}/closing`): `GET` (interruptor, meses desde el arranque hasta el mes en curso con su estado, quién cerró y cuándo, si se puede cerrar o reabrir y por qué no, y el historial de cierres), `PUT` (encender o apagar), `POST {año}/{mes}/close` y `POST {año}/{mes}/reopen` (motivo obligatorio, máximo 500 caracteres). Ver: los cuatro roles administrativos; encender, apagar, cerrar y reabrir: **SuperAdmin y Administrador de empresa**. Requiere Finanzas disponible (403 con el código del módulo) y fecha de arranque (409 `finance_setup_incomplete`). Errores propios: `closing_disabled`, `previous_month_open`, `month_already_closed` y `closing_has_closed_months` (no se apaga el cierre mientras haya meses cerrados: se reabren primero). Todo queda en el historial del Centro (sección `closing`).

**Resumen del Centro:** nueva sección 9 «Período y cierre» (opcional; completa cuando el cierre está encendido; «No disponible» sin Finanzas).

**Pruebas:** 46 pruebas nuevas (`ConfigCenterPhase2Tests`): pantalla del cierre (estados, orden, mes en curso, mes anterior al arranque, doble cierre, apagar y reabrir, motivo, rehacer un cierre, reabrir un mes intermedio, permisos y dependencia de Finanzas), la guarda (cierre apagado, módulo apagado, mes reabierto, aislamiento entre edificios) y cada punto protegido con su caso negativo y su caso permitido. Suite completa: 873 pasan, 15 omitidas.

**No cubierto por pruebas automáticas:** la importación de gastos por Excel (la guarda está, pero no se armó un archivo de plantilla firmado) y el camino feliz del clonado de período (la base de pruebas SQLite no soporta la suma de decimales que usa el cálculo del saldo de arrastre).

### 12.3 Fase 3 — Política de mora, fondos, avisos y umbral del presupuesto (backend, hecha 2026-10-09)

Decisiones de Tony para esta fase: la **gracia se rellena al crear, solo en períodos nuevos** (nada se recalcula hacia atrás); se corrigen **los cuatro lugares** donde un pago revertido seguía contando como pagado; los **avisos son por pantalla y push**, con reglas por edificio (sin correo); del presupuesto, **solo el umbral del semáforo** configurable (sin avisos de desvío).

**Datos** (migración `20261009171429_LateFeePolicyAndNotices` y su `.sql` idempotente; todos los valores por defecto reproducen el comportamiento actual): `Buildings` (`LateFeeCapPercentage`, `LateFeeMinAmount`, `LateFeeAppliesToReserve/Extraordinary/Individual`, `LateFeePolicyConfirmed`, `ReserveUsePolicy`, `ReserveUseThreshold`, `FundPolicyConfirmed`), `Units` (`LateFeeExempt` con motivo, quién y cuándo), `FinanceSettings.BudgetWarnPercent` (10) y tabla `BuildingNoticeRules`. Las columnas con valor por defecto no usan el centinela de EF (`ValueGeneratedNever`): guardar `false` o `0` se guarda de verdad.

**Mora automática** (`LateFeeAccrualRunner`, separado del servicio programado para poder probarlo con una fecha dada):
- Excluye pagos revertidos (la mora sigue corriendo si se revierte el pago).
- Base de cálculo: la expensa ordinaria y los ajustes siempre entran; reserva, extraordinario e individual, según el edificio.
- **Mora mínima** por intervalo y **tope** acumulado por unidad y período (% de la base; cuenta la mora automática **y la manual**). Al llegar al tope se cobra solo lo que falta y se deja de acumular; la nota del recargo lo dice.
- **Unidades exoneradas** no acumulan mora (la que ya tenían se conserva).
- Idempotente: correr dos veces no duplica.

**Gracia:** `ExpensePeriodsController.ResolveLateFeeDate` completa la fecha de corte (vencimiento + días de gracia) al **crear, crear en lote y clonar** un período sin fecha de corte. Editar un período no la vuelve a aplicar y los períodos existentes no cambian.

**Correcciones de pagos revertidos:** mora automática, recargo manual, aviso de deuda para bloquear reservas y reporte de Cobranza ya no cuentan los pagos revertidos. El recargo manual dejó de sumar montos en la base (se suman en memoria, como la mora automática).

**Avisos automáticos** (`BuildingNoticeRule`, una fila por tipo y edificio; **sin fila rige el valor por defecto: nada cambia hasta que se configuren**): *antes del vencimiento* (1 a 30 días) y *el día del vencimiento* (apagados por defecto), *mora aplicada* (apagado), *pago recibido* y *período publicado* (los avisos que ya existían, encendidos por defecto; la regla permite apagarlos). `PaymentReminderRunner` (servicio horario, desde las 8 de la mañana de Paraguay) avisa a los propietarios actuales y residentes con usuario de las unidades que **todavía deben** el período (un pago revertido no cuenta como pago), **una sola vez por persona, tipo y período**. El aviso de mora se manda la primera vez que se aplica mora a la unidad en el período. Tipos de notificación nuevos: `PaymentDueSoon`, `PaymentDueToday`, `LateFeeApplied` (texto en la base: no hace falta migrar).

**API** (`/api/building-config/{buildingId}`, `BuildingConfigPoliciesController`): `GET/PUT late-fee` (tasa, frecuencia, gracia, tope, mínimo, cargos incluidos; **guardar la política la deja confirmada**, y con la tasa vacía queda como decisión explícita de «sin mora»; un cambio de tasa avisa como siempre, y el push sale después de guardar), `GET/PUT fund-policy` (tratamiento de ingresos, aportes, política de uso del fondo informativa y su monto; guardar también la confirma), `GET/PUT budget-alerts` (umbral 1 a 100, requiere Finanzas y configuración inicial) y `GET/PUT notice-rules`; más `PUT /api/units/{id}/late-fee-exemption` (motivo obligatorio, hasta 300). Editan **SuperAdmin y Administrador de empresa**; el Encargado ve mora y presupuesto pero no fondos ni avisos; cada cambio queda en el historial del Centro. La ficha del edificio sigue pudiendo editar tasa, frecuencia, gracia y aportes (mismos campos); el historial registra las dos vías.

**Resumen del Centro:** *Política de mora* y *Fondos* pasan a ser **obligatorias** (completas con la política definida o confirmada); nueva sección 12 *Documentos y comunicación* (modelos de documentos y avisos encendidos); la sección de presupuesto muestra el umbral. Los edificios sin mora ni fondos configurados aparecen «Incompletos» hasta que alguien los confirme: es el cambio de criterio previsto, pero baja el indicador «listo para operar» de los edificios existentes.

**Alcance que quedó afuera (a propósito):** la política de uso del fondo de reserva es **informativa** (no obliga a cargar una referencia de aprobación al pagar un gasto con el fondo); no hay avisos por correo; los avisos de desvío del presupuesto no se hicieron. Para el web y la app: los tipos de notificación nuevos necesitan icono y texto en `notification-visuals.ts` (web y APP).

**Pruebas:** 75 pruebas nuevas (`ConfigCenterPhase3Tests`) más las de la fase 1 ajustadas al nuevo criterio: mora (base, mínimo, tope, exoneración, idempotencia, pagos revertidos, gracia), avisos (una sola vez, solo a quien debe, horario, edificios, tipos), políticas y permisos, exoneración, umbral del semáforo y las correcciones de pagos revertidos. Suite completa: 948 pasan, 15 omitidas.
