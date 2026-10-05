# Nota de crédito de proveedor sobre un gasto del edificio — Especificación

Estado: **fases 1 y 2 implementadas (2026-10-05)**; fases 4 y 5 pendientes de "procesa" de Tony; la 3 en pausa. Ver la sección 15 (estado de implementación).
Repos: `Condo-PY-BACK` (API .NET), `Condo-PY-WEB` (panel), `CondoPY-APP` (solo aviso al propietario, ya cubierto por las notificaciones).

> **Para quien implemente:** leer la sección 3 (lo que ya existe) y verificar cada punto marcado **[verificar]** antes de tocar código. Los puntos de la sección 12 son decisiones abiertas: preguntarlas, no asumirlas. Commits solo a nombre de Tony, sin `Co-Authored-By` y sin push.

---

## 1. Problema

El proveedor de un gasto del edificio (ya registrado y prorrateado entre las unidades) emite una **nota de crédito** que baja el costo real. Hoy el sistema no tiene dónde registrarla: `BuildingExpense` solo guarda `SupplierName` y un archivo, y el monto debe ser mayor que cero (`BuildingExpensesController.cs:676`). Las notas de crédito que existen (`CreditNote`) son las que se emiten **al propietario** y exigen una factura (`InvoiceId`).

## 2. Decisiones cerradas (Tony, 2026-10-05)

1. **Nunca se modifica un comprobante ya emitido** a un propietario.
2. **Período en borrador:** la NC baja el gasto y se recalcula el prorrateo.
3. **Período cerrado:** se anula la liquidación (`void-settlement`, ya existe) y se recalcula como en borrador.
4. **Período publicado:** se prorratea la NC entre las unidades y la parte de cada una se suma a su **saldo a favor trazable**, junto a lo que ya tenga. **No se crea ni se exige un período nuevo.**
5. **Aportes de fondo de reserva y extraordinario:** se dejan como se cobraron (no se recalculan).
6. **Cómo se consume el saldo a favor: NO se cambia.** El saldo que genere la NC se acredita y se consume exactamente como hoy (automático, del lote más antiguo, en cualquier edificio de la empresa). Tony (2026-10-05): "solo acredita el saldo correspondiente a favor y que se consuma como es hoy, así evitas problemas". La idea anterior de restringir el crédito al edificio de origen **queda como pregunta pendiente** (sección 12, punto 8): no se implementa hasta que Tony la retome.
7. **Cambio de propietario de una unidad:** primero se deben liquidar las deudas de esa unidad, sí o sí; el saldo a favor de la unidad **pasa al nuevo propietario**.
8. **Unidad sin propietario:** significa que no se vendió; es del dueño del edificio. El Administrador de empresa le asigna como propietario a esa persona/empresa y se le calcula su parte como a cualquiera; nadie paga por ella o la paga el dueño del edificio.

## 3. Lo que ya existe (verificado en el código)

| Qué | Dónde |
|---|---|
| Ciclo del período: Borrador → (aprueba el Encargado: nacen los cargos, queda **Cerrado**, gastos bloqueados) → aprueba el presidente → publica el Administrador | `ExpensePeriodsController.cs` (`ApproveSettlement` ~564, `Publish` 882) |
| Gastos solo editables en borrador | `BuildingExpensesController.cs` (líneas 223, 563, 601) |
| Rechazar o anular la liquidación borra los cargos y vuelve a borrador | `president-reject-settlement` 802, `reject-settlement` 1068, `void-settlement` 1278 (solo período Cerrado y liquidación Aprobada) |
| Deshacer una publicación: solo SuperAdmin y solo sin pagos ni mora | `unpublish-settlement` 960 |
| Cada cargo guarda de qué gasto viene y a qué unidad (`SourceBuildingExpenseId`, `UnitId`) | `ExpenseCharge.cs`, `ExpenseSettlementDistributionService.cs` |
| Reparto por gasto: por coeficiente, fijo por unidad, a una unidad, grupo manual, no distribuido; gastos pagados por el fondo de reserva **no** se reparten | `ExpenseSettlementDistributionService.cs` líneas 30-95 |
| Aportes de reserva/extraordinario = porcentaje de los gastos comunes | `SettlementContributions.Compute` (`Condo.Application/Services`) |
| Saldo a favor: `OwnerCredit` (total por propietario y empresa) y `OwnerCreditMovement` (lotes: `Generated`/`Applied`, `RemainingAmount`, referencia) | `OwnerCredit.cs`, `OwnerCreditMovement.cs`, `OwnerCreditService.cs` |
| La NC al propietario ya crea un lote de saldo a favor con referencia (`AddCreditNoteExcessLotAsync`) | `OwnerCreditService.cs:240` |
| El saldo a favor se consume solo, completo, del lote más antiguo, al aprobar el próximo pago; el pago acepta "comprobante menos saldo" | `OwnerPaymentsController.cs` `SettlePaymentAsync` 1073 y `CoverWithCredit` |
| `OwnerCreditFeature.Enabled = false` apaga solo la aplicación manual; el saldo por NC y su consumo automático **siguen activos** | `ComprobanteService.cs:17` |
| El saldo a favor **no tiene edificio ni unidad**: es por propietario y empresa; hoy un crédito paga comprobantes de cualquier edificio de la empresa | `OwnerCreditService.LoadLinkedUnitIdsAsync` 16 |
| Dar de baja a un propietario **no valida deudas** ni mueve su saldo | `UnitOwnersController.Delete` 147 |
| Alta/baja del propietario principal ya dispara la "nota de cambio" del Marketplace | `UnitOwnersController` (`marketplaceHandover.OnPrimaryAssignedAsync` / `OnPrimaryRemovedAsync`) |
| Si una NC al propietario no encuentra propietario, no acredita a nadie y no avisa | `CreditNotesController.cs:320-327` (a no repetir) |
| Los reportes leen `BuildingExpenses` bruto | `LibroMovimientosService.cs:42`, `FinanceLedgerService.cs:122,203`, `EstadoResultadosService.cs:42` |
| Conciliación del período: gastos + aportes − ingresos = cargos | `ExpensePeriodsController.cs` `GetReconciliation` ~456 |

## 4. Modelo de datos (nuevo)

### 4.1 `BuildingExpenseCreditNote` (NC de proveedor)

Hereda `CompanyScopedEntity`. Campos:

- `BuildingId`, `BuildingExpenseId`, `ExpensePeriodId` (el del gasto original).
- `Numero` (string, número del documento del proveedor), `Timbrado` (string?, opcional), `IssueDate` (DateOnly, fecha de la NC), `Amount` (decimal > 0), `Reason` (string).
- `DocumentUrl` (string?, archivo subido, ruta relativa `/uploads/...`).
- `Mode`: `Netted` (se descontó del gasto en un período no publicado) o `Credited` (período publicado: se repartió como saldo a favor).
- `Status`: `Applied` o `Voided`; `VoidReason`, `VoidedAtUtc`, `VoidedByUserId`.
- `CreatedByUserId`, `CreatedAtUtc` (los hereda).

Regla: la suma de NC `Applied` de un gasto **no puede superar** `BuildingExpense.Amount`.

### 4.2 `BuildingExpenseCreditNoteAllocation` (reparto por unidad, solo `Credited`)

`CreditNoteId`, `UnitId`, `OwnerId` (propietario principal al momento), `Amount`, `OwnerCreditMovementId` (el lote creado).

### 4.3 Cambios en `OwnerCreditMovement`

Agregar, todos opcionales para no romper lo existente:

- `BuildingId` (Guid?): edificio de origen, **solo para trazabilidad**. NO restringe el consumo (decisión 6): el lote se consume como cualquier otro.
- `UnitId` (Guid?): unidad de origen (para transferirlo al cambiar de propietario).
- `SupplierCreditNoteId` (Guid?): la NC de proveedor que lo originó.
- `OnHold` (bool, por defecto false): lote retenido, **no se consume** (ver sección 8).

Migración EF + script SQL manual en `docs/sql/` (el VPS aplica las migraciones a mano, ver memoria `vps_deploy`).

### 4.4 Nuevo tipo de notificación

`NotificationType.SupplierCreditApplied = 38` ("Saldo a favor por ajuste de un gasto"). Al agregarlo hay que sumarlo en los mapas de ícono/color de web y app (`notification-visuals.ts` en ambos) y en el tipo `NotificationType` de `core/models.ts` de la app. Entidad de la notificación: `Unit` o `OwnerPayment`/pantalla de saldo **[decidir destino]**.

## 5. Comportamiento según el estado del período del gasto

### 5.1 Borrador (`Draft`)

1. Crear NC con `Mode = Netted`.
2. La base de reparto del gasto pasa a ser `Amount − Σ NC Netted Applied`. Cambia `ExpenseSettlementDistributionService` (donde usa `expense.Amount`, líneas 60-95) y el cálculo de `commonExpenses` (línea 56-62), para que los aportes de reserva/extraordinario **sigan calculándose sobre lo que corresponda en un período aún sin cobrar** (en borrador se recalcula todo con el neto; la decisión 5 aplica solo a períodos ya publicados).
3. Si la liquidación ya estaba calculada, hay que **recalcularla** (`calculate-settlement`); el sistema debe avisar que quedó desactualizada.
4. `GetReconciliation` usa el mismo neto.

### 5.2 Cerrado (`Closed`)

No se permite crear la NC. Mensaje: "El período está cerrado. Anulá la liquidación (Gastos y cargos › Liquidación › Anular) y registrá la NC en borrador." No hay que automatizar la anulación: ya la hace el Encargado o el Administrador (`void-settlement`), con su trazabilidad.

### 5.3 Publicado (`Published`)

Crear NC con `Mode = Credited` y repartir:

1. **Validar** que todas las unidades afectadas tengan propietario principal (sección 9). Si falta alguna, responder 400 con la lista de unidades y no crear nada.
2. Tomar los cargos del gasto: `ExpenseCharge` con `SourceBuildingExpenseId = gasto`, `!IsDeleted`, `!IsReversal`, de ese período. Sumarlos por unidad.
3. Crédito de cada unidad = `round(cargoUnidad × NC / Amount del gasto, 0)` (guaraníes sin decimales **[verificar]** cómo redondea hoy el reparto: usa 2 decimales con `MidpointRounding.AwayFromZero`; mantener esa convención y la moneda). El resto de redondeo se asigna a la unidad de mayor cargo para que **Σ créditos = NC exactamente**.
4. Se reparte sobre lo **realmente cobrado**, no sobre los coeficientes de hoy: así no depende de que no hayan cambiado.
5. Por cada unidad con crédito > 0: crear un lote `OwnerCreditMovement` (`Kind = Generated`, `Amount = RemainingAmount = crédito`, `BuildingId`, `UnitId`, `SupplierCreditNoteId`, `SourceReference` = "NC proveedor {Numero} · {SupplierName}"), sumar a `OwnerCredit.Amount` del propietario principal y guardar la fila de `Allocation`.
6. Todo en **una transacción**. Si el gasto no generó cargos (no distribuido, o pagado por el fondo de reserva) no hay reparto: ver 5.4.
7. No se tocan comprobantes, cargos, mora ni facturas existentes.
8. Notificar a cada propietario acreditado (`SupplierCreditApplied`), con monto y motivo.

### 5.4 Casos del gasto

| Tipo de gasto | Efecto de la NC |
|---|---|
| Por coeficiente / fijo por unidad / grupo manual | Reparto de 5.3 sobre los cargos que generó |
| A una sola unidad (`IndividualUnit`) | Todo el crédito a esa unidad |
| No distribuido | Solo se registra la NC (y el movimiento en el libro); sin saldo a favor |
| Pagado por el fondo de reserva (`PaidByReserveFund`) | Solo se registra; el efecto es **devolución al fondo de reserva** en el libro. Sin saldo a favor |

Los aportes de reserva y extraordinario del período publicado **no se tocan** (decisión 5).

## 6. Consumo del saldo a favor — **NO SE CAMBIA (decisión de Tony, 2026-10-05)**

> Esta sección describe la restricción por edificio que se había propuesto. **No se implementa**: el saldo a favor generado por la NC se consume exactamente como se consume hoy. Queda como pregunta pendiente (sección 12, punto 8). Se conserva el texto por si Tony decide retomarla.

Hoy `SettlePaymentAsync` calcula `availableCredit = OwnerCredit.Amount` (total por propietario) y consume lotes del más antiguo al más nuevo sin mirar el edificio. Con la decisión 6:

1. El crédito disponible para cubrir un comprobante de un edificio B = lotes con `Remaining > 0`, `!OnHold` y (`BuildingId` nulo **o** `BuildingId = B`).
2. Al cubrir varios comprobantes de un mismo pago, cada comprobante solo puede consumir lotes elegibles para su edificio, del más antiguo al más nuevo.
3. `CoverWithCredit`, `ConsumeLots`, `EnsureLotsAsync` y la validación "el pago debe ser exacto" deben usar este crédito **por edificio**, no el total. El mensaje de descuadre (`ComprobanteService.MismatchMessage`) debe mostrar el saldo aplicable.
4. `OwnerCredit.Amount` sigue siendo el total del propietario (lo ven las pantallas); agregar en las pantallas el desglose por edificio cuando haya lotes con edificio.
5. Regla vigente que no cambia: sigue valiendo "un comprobante es un pago completo"; el crédito completa el pago, nunca lo parte.
6. **[verificar]** todos los lugares que leen `OwnerCredit.Amount` (`OwnerPaymentsController` 1040 y 1180, `ExpensePeriodsController` 2262 `ApplyOwnerCreditsAsync`, `OwnerCreditService.ApplyCreditAsync`, pantallas web/app del propietario) y adaptarlos.

## 7. Anular una NC de proveedor

- `Netted`: se marca `Voided` y el gasto vuelve a su base; avisar que hay que recalcular la liquidación.
- `Credited`: solo si **todos** sus lotes siguen sin consumir (`RemainingAmount = Amount`). Se quitan los lotes, se resta de `OwnerCredit.Amount` y se marca `Voided`. Si algún lote ya se consumió, **no se puede anular** (400 con el detalle).
- Motivo obligatorio. Permiso igual que crear.

## 8. Cambio de propietario de una unidad (decisión 7)

Aplica a `UnitOwnersController` (alta de un principal nuevo y baja del principal actual) y a cualquier otro flujo que reasigne el propietario principal **[verificar]** (importaciones, edición de la unidad).

1. **Exigir liquidar las deudas antes**: no se puede dar de baja o reemplazar al propietario principal si la unidad tiene deuda pendiente (cargos con saldo > 0, incluida mora). Respuesta 400 con el total y los períodos adeudados. Usar el mismo cálculo de pendiente que `OwnerCreditService.LoadPendingChargesAsync` / `Comprobante`.
2. Al dar de baja al principal: los lotes con `UnitId = esa unidad` y `Remaining > 0` pasan a `OnHold = true` (no se consumen) y se **restan del `OwnerCredit.Amount`** del propietario saliente.
3. Al asignar el nuevo principal (`OnPrimaryAssigned`): esos lotes cambian `OwnerId` al nuevo, `OnHold = false`, y se **suman** a su `OwnerCredit.Amount`. Registrar un movimiento de traspaso en el historial para la trazabilidad.
4. Si el principal se reemplaza en un solo paso (hay otro principal inmediato), el traspaso es directo.
5. Lotes **sin `UnitId`** (excedentes de pagos o de NC al propietario, anteriores a este cambio) **se quedan con el propietario saliente**. **[confirmar con Tony]**
6. Mostrar al Administrador cuánto saldo se traspasa antes de confirmar.

## 9. Unidades sin propietario (decisión 8)

- No se agrega un concepto nuevo: una unidad no vendida se asigna al **dueño del edificio** (persona o empresa, como usuario Owner) como propietario principal. Así recibe su cálculo y su crédito como cualquiera.
- Al crear una NC `Credited`, si alguna unidad afectada **no tiene propietario principal**, se rechaza con la lista ("Asigná un propietario principal a las unidades A-3, B-1 antes de aplicar la NC"). Nada se pierde en silencio.
- Pantalla de unidades: facilitar ver y filtrar "unidades sin propietario" **[opcional, fase 4]**.

## 10. Reportes y contabilidad

- **Periodo en borrador (`Netted`, fase 1):** el gasto se ve **neto en su propio mes** (el periodo todavía no se cobró ni se publicó). Se logró sin tocar los reportes: `BuildingExpense.Amount` pasa a ser el neto y `OriginalAmount` guarda lo facturado; libro de movimientos, estado de resultados, presupuesto, flujo, conciliación y liquidación leen `Amount`. El Excel y los reportes de un periodo con NC muestran el gasto por el neto.
- **Periodo publicado (`Credited`, fase 2):** se aplica lo siguiente. Las NC aparecen en el **libro de movimientos / estado de resultados / flujo** como **movimiento negativo con la fecha de la NC y el rubro del gasto original**; el gasto original queda bruto en su mes. No se reescriben meses cerrados.
- Gasto pagado por el fondo de reserva: el movimiento negativo se refleja como ingreso al fondo (`FinanceLedgerService` / reserva) **[verificar]**.
- La liquidación publicada (PDF) **no cambia**. Ofrecer un anexo/listado de NC de proveedor aplicadas al período.
- Excel del contador: incluir las NC como filas (ver `LibroMovimientosService`).

## 11. API y pantallas

### Backend (`Condo.Api`)

- `POST /api/building-expenses/{expenseId}/credit-notes` — crea la NC (según el estado del período, 5.1 / 5.2 / 5.3). Body: `numero`, `timbrado?`, `issueDate`, `amount`, `reason`, `documentUrl?`.
- `GET /api/building-expenses/{expenseId}/credit-notes` — lista con estado, modo y reparto.
- `POST /api/building-expenses/{expenseId}/credit-notes/preview` — **simulación sin guardar** del reparto por unidad (obligatoria antes de confirmar en períodos publicados).
- `POST /api/building-expense-credit-notes/{id}/void` — anular (sección 7).
- Permisos: los mismos que gestionan liquidaciones: BuildingManager, CompanyAdmin, SuperAdmin **[confirmar]**. El `CompanyOperator` solo puede en borrador **[confirmar]**. Respeta `PlanGateService` (solo lectura tras el vencimiento del plan).
- Auditoría: registrar quién y cuándo (patrón de `CreditNoteAuditLog` o `MarketplaceEvent`) **[decidir]**.

### Web (`Condo-PY-WEB`), pantalla `period-ledger` (gastos)

- Botón "Registrar NC de proveedor" en cada fila de gasto.
- Diálogo: número, fecha, monto (tope = gasto − NC previas), motivo, adjunto (usar `UploadsApiService` y mostrarlo con `resolveUploadUrl`).
- En período publicado: mostrar la **tabla de reparto por unidad** (preview) y confirmar.
- En período cerrado: mensaje con el paso de anular la liquidación.
- Lista de NC del gasto con estado y botón anular.

### App (`CondoPY-APP`)

Solo el aviso `SupplierCreditApplied` (banner y push ya funcionan) y que el propietario vea su saldo a favor y de qué edificio es **[verificar]** si hoy lo ve.

## 12. Decisiones abiertas

8. **(PENDIENTE — Tony la dejó como pregunta)** ¿El saldo a favor de una NC de proveedor solo debe usarse en el edificio de origen, o en cualquier edificio de la empresa como hoy? Por ahora se consume **como hoy** (cualquier edificio de la empresa). Si algún día se decide restringirlo, ver la sección 6.

1. **Fiscal**: la factura al propietario sigue por el monto original y el ajuste llega como crédito. **Confirmar con el contador** si corresponde NC fiscal al propietario (el sistema ya sabe emitirla con `CreditNote`) o basta el registro interno.
2. **Mora ya cobrada**: por defecto **no se recalcula**. Confirmar.
3. **Lotes sin unidad** al cambiar de propietario (8.5): se quedan con el saliente. Confirmar.
4. ~~**Permisos** (11)~~ **Cerrada (Tony, 2026-10-05):** puede registrarla cualquiera con acceso al edificio (igual que los gastos), a cambio de validar que una misma NC no se registre dos veces (ver sección 15).
5. **Destino al tocar la notificación** `SupplierCreditApplied` (qué pantalla abre).
6. **Auditoría**: tabla propia o evento existente.
7. Qué es "deuda a liquidar" al cambiar de propietario: ¿solo comprobantes publicados vencidos o todo lo pendiente (incluido lo no vencido)? Por defecto: **todo lo pendiente de la unidad**.

## 13. Fases sugeridas

1. **Datos y borrador** — ✅ HECHA (2026-10-05): entidades, migración + script SQL, NC `Netted`, base de reparto neta, conciliación y bloqueo en cerrado. Tests.
2. **Publicado** — ✅ HECHA (2026-10-05): preview + reparto + lotes de saldo a favor (con `BuildingId`/`UnitId` solo de trazabilidad) + notificación + anular. El consumo del saldo NO cambia.
3. ~~**Crédito por edificio**~~ — **en pausa**: pregunta pendiente (sección 12, punto 8). No hacer sin que Tony lo confirme.
4. **Cambio de propietario**: validación de deuda, retención y traspaso de lotes.
5. **Reportes y pantallas**: libro/Excel (solo lo de `Credited`), anexo de liquidación, UI web, aviso en app.

Cada fase compila limpio y se commitea por separado como Tony. Respetar la regla de pagos del más antiguo al más nuevo y "comprobante completo, sin pagos parciales" (memoria `feedback_commits_and_payment_rule`).

## 14. Pruebas mínimas (Condo.Tests)

- NC en borrador: la base baja, el reparto suma exacto y los aportes se recalculan.
- NC en cerrado: rechazada con mensaje.
- NC publicada: Σ créditos = NC (con redondeo), una unidad sin propietario rechaza todo, NC mayor al gasto rechazada, dos NC que suman más que el gasto rechazada.
- Gasto del fondo de reserva / no distribuido / a una unidad.
- Consumo: lote de edificio A no cubre comprobante del edificio B; lote sin edificio sí; pago "comprobante menos saldo" por edificio.
- Anular: sin consumo sí; con consumo no.
- Cambio de propietario: bloqueado con deuda; sin deuda traspasa lotes con unidad y deja los demás.
- Aislamiento por empresa y edificio (ver `Condo.Tests/Marketplace/MarketplaceIsolationMatrixTests.cs` como modelo).

## 15. Estado de implementación

### Fase 1 — hecha (2026-10-05, solo backend; compila y 31 pruebas nuevas pasan, suite completa 722/722)

- **Datos**: `BuildingExpenseCreditNote` (+ enums `BuildingExpenseCreditNoteMode` y `...Status`), `BuildingExpense.OriginalAmount`. Migración `20261005114334_BuildingExpenseCreditNotes` con su script manual `.sql` (idempotente, con verificación) en `Condo.Infrastructure/Persistence/Migrations/`. **Correrlo en el VPS antes de desplegar el código.**
- **Cómo queda el gasto**: `Amount` es el neto (lo que se reparte); `OriginalAmount` es lo facturado por el proveedor (null si no tiene NC). `BuildingExpenseDto` suma `OriginalAmount` y `CreditedAmount`.
- **API** (`BuildingExpenseCreditNotesController`):
  - `GET /api/building-expenses/{expenseId}/credit-notes`
  - `POST /api/building-expenses/{expenseId}/credit-notes` — número, timbrado?, fecha, monto, motivo, archivo? (solo `/uploads/...`). Devuelve la NC, el gasto actualizado y `settlementNeedsRecalculation` (true si el periodo ya tenía una liquidación **Calculada**: hay que volver a calcularla).
  - `POST /api/building-expenses/credit-notes/{id}/void` — motivo obligatorio.
- **Reglas**: solo con el periodo en **Borrador**; **Cerrado** → 400 con el paso de anular la liquidación; **Publicado** → 400 "todavía no está disponible" (fase 2). La NC debe ser menor que el monto pendiente del gasto (si el proveedor anuló todo, se elimina el gasto). Sin repetir número por proveedor y edificio. Anular solo en borrador.
- **Gasto con NC aplicadas**: no se cambia su monto ni su periodo ni se elimina hasta anular las NC (sí se editan los demás campos). Clonar un periodo copia el monto **original** (la NC fue de un solo periodo).
- **Permisos**: igual que los gastos (acceso al edificio); cualquiera con acceso puede registrarla. Decidido por Tony.
- **Una misma NC no se registra dos veces** (Tony, 2026-10-05): se compara proveedor + número (+ timbrado si ambos lo traen) con llaves normalizadas (mayúsculas, sin acentos, espacios, puntos ni guiones: "001-001-0000123" = "0010010000123"; "Ferretería López S.A." = "FERRETERIA LOPEZ SA"). El control mira **toda la empresa** (otro gasto u otro edificio) y el mensaje dice dónde ya está registrada. Si dos personas la guardan a la vez, un **índice único filtrado** de la base (`CompanyId, SupplierKey, TimbradoKey, NumeroKey` solo entre aplicadas) rechaza la segunda (409). Una NC **anulada** se puede volver a registrar. Distinto timbrado = otra nota (la numeración se reinicia); sin timbrado en alguna de las dos no se puede distinguir y cuenta como repetida. Segunda migración: `20261005115326_BuildingExpenseCreditNoteUniqueness` (+ su .sql), va DESPUÉS de la primera.
- **Adjunto obligatorio**: el documento que envió el proveedor (PDF, imagen JPG/PNG/WEBP o XML, hasta 10 MB, subido con `/api/uploads`) es requisito para registrar la NC, en la API y en el formulario.
- **Formulario web (hecho)**: en Gastos y cargos › Gastos, cada gasto tiene el botón "Notas de crédito del proveedor" (ícono de menos). Abre una ventana con el resumen (facturado / NC aplicadas / monto que se reparte), las notas registradas (con enlace al documento y botón Anular con motivo) y el formulario: número, timbrado, fecha de emisión, monto, motivo y adjunto. Con el período cerrado o publicado la ventana solo muestra la lista y el aviso correspondiente. La fila del gasto muestra "facturado − NC" cuando tiene notas. Componente: `building-expense-credit-notes-dialog.component.ts` en el WEB.
- **No hecho en fase 1 (a propósito)**: notificaciones, reparto del periodo publicado, cambio de propietario.
- **Riesgo conocido**: dos NC simultáneas sobre el mismo gasto podrían pisarse (no hay control de concurrencia en `BuildingExpense`); es una operación manual poco frecuente.

### Fase 2 — hecha (2026-10-05, backend + formulario web; suite completa 756/756)

- **Período publicado**: `POST /api/building-expenses/{id}/credit-notes` crea la NC con `Mode = Credited`. No cambia `Amount` del gasto (ya está repartido y cobrado) ni toca comprobantes, cargos, mora ni facturas.
- **Reparto**: se prorratea sobre lo realmente cobrado de ese gasto a cada unidad (`ExpenseCharge.SourceBuildingExpenseId`, sin reversos), con tope en lo que le queda por acreditar a cada unidad. La suma de los créditos es exactamente el monto de la NC (el centavo de redondeo va a la unidad con más margen). Dos NC del mismo gasto se reparten con la misma proporción.
- **Tope**: lo ya acreditado por NC de período publicado + la nueva ≤ monto del gasto (se puede acreditar el gasto completo, no más).
- **Saldo a favor**: un lote por unidad (`OwnerCreditMovement`, `Kind = Generated`, con `SupplierCreditNoteId`, `BuildingId` y `UnitId` solo de trazabilidad) y suma al `OwnerCredit` del **propietario principal**. **Se consume exactamente como hoy** (decisión de Tony): el flujo de pagos no se tocó. Un propietario con varias unidades en la misma NC recibe un solo saldo (y un solo aviso con la suma).
- **Unidad sin propietario principal**: se rechaza toda la NC con la lista de unidades ("asignalas al dueño del edificio si no se vendieron"); no se guarda nada.
- **Gastos que no se reparten** (pagado por el fondo de reserva, no distribuido): la NC se registra sin saldo a favor (el efecto en el libro queda para la fase 5).
- **Vista previa**: `POST /api/building-expenses/{id}/credit-notes/preview` { amount } — en borrador dice cómo queda el gasto; en publicado devuelve el reparto por unidad (unidad, propietario, cobrado, saldo a favor), las unidades sin propietario y un mensaje. No guarda nada.
- **Aviso al propietario**: nuevo `NotificationType.SupplierCreditApplied = 38` (bandeja + push). Entidad `ExpensePeriod`: en la app abre el estado de cuenta del período. También se avisa al anular. **Falta** sumarlo a `notification-visuals.ts` y al tipo `NotificationType` de `core/models.ts` en la APP (hoy se ve con el ícono genérico): se hace en la rama de la app donde viven las notificaciones (feature/marketplace); el web ya lo tiene.
- **Anular una NC de período publicado**: solo si todos sus lotes siguen sin consumir y el saldo del propietario alcanza; quita el saldo (lote en 0 y resta del `OwnerCredit`) y avisa. Si ya se usó parte, 400 con la unidad ("un saldo ya aplicado a un pago no se revierte desde acá"). Una NC de borrador ya publicada no se anula (ya se tuvo en cuenta en la liquidación).
- **Migración**: `20261005121441_BuildingExpenseCreditNoteAllocations` (+ .sql): tabla `BuildingExpenseCreditNoteAllocations` y columnas `BuildingId`, `UnitId`, `SupplierCreditNoteId` en `OwnerCreditMovements`. Va DESPUÉS de las dos anteriores.
- **Web**: con el período publicado, la ventana de notas de crédito muestra el formulario con un paso de **"Ver reparto por unidad"** (tabla con propietario, cobrado y saldo a favor; las unidades sin propietario salen en rojo y bloquean la confirmación) y luego **"Confirmar y acreditar saldo a favor"**. La lista de notas muestra a quién se acreditó cada parte.
- **No cambia** (a propósito): el consumo del saldo a favor; los aportes de reserva y extraordinario del período publicado (se dejan como se cobraron); la mora ya cobrada.
- **Pendiente para la fase 5**: libro de movimientos / estado de resultados con las NC de período publicado (movimiento negativo con la fecha de la NC) y devolución al fondo de reserva.
- **Riesgo conocido**: dos NC simultáneas sobre el mismo gasto podrían superar el tope a la vez (sin control de concurrencia en el gasto).
