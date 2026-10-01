# Módulo "Finanzas del edificio" — Especificación y plan de trabajo

Estado: **fases 1, 2 y 3 implementadas (2026-10-01)**; fases 4 a 6 pendientes. Ver la sección 15 (estado de implementación y lo que sigue). Fecha del documento: 2026-10-01.
Pensado para trabajarse en una sesión nueva leyendo este documento. Repos: `Condo-PY-BACK` (API .NET), `Condo-PY-WEB` (panel Angular), `CondoPY-APP` (solo si más adelante se muestra algo al Encargado).

> **Instrucción para la sesión que implemente esto:** leer primero la sección 2 (lo que ya existe) y verificar en el código cada punto marcado **[verificar]** antes de diseñar sobre él. Las decisiones abiertas están en la sección 12: no asumir, preguntarlas una por una.

---

## 1. Qué es y qué no es

**Es:** un módulo que se **habilita por edificio** y le da a la administración una vista financiera clara: cuánto hay, cuánto entró, cuánto salió, cuánto se debe y cuánto se presupuestó. Los datos quedan ordenados y trazables para que un **contador externo** los tome sin retipear nada.

**No es:** contabilidad formal. No hay editor de asientos, balance general legal, libros rubricados ni cierre fiscal. Se llama **"Finanzas del edificio"** y debe decir en la pantalla que no reemplaza al contador. Esto evita que el cliente espere partida doble completa.

**Por qué conviene:**
- Mejora el servicio a los clientes (las comisiones piden presupuesto vs. real, flujo y fondo de reserva).
- Diferencia el producto: se puede incluir en un plan superior o cobrarse aparte.
- Casi todo el dato ya se captura hoy; gran parte del trabajo es ordenarlo y presentarlo.

---

## 2. Lo que ya existe (base sobre la que se construye)

Verificado por nombres de rutas y entidades vistos en el código; confirmar detalles antes de usar.

| Área | Qué hay | Dónde |
|---|---|---|
| Gastos del edificio | Alta/edición/baja, categoría, proveedor, monto, fecha, distribución, comprobante adjunto, marca `PaidByReserveFund` | `BuildingExpensesController`, entidad `BuildingExpense` |
| Gastos recurrentes | Plantillas de gasto repetido | `RecurringBuildingExpensesController` |
| Ingresos del edificio | Ingresos propios del edificio y su tratamiento (`IncomeTreatment`: acreditar a propietarios, etc.) | `BuildingIncomesController` |
| Períodos de expensas | Estados `Draft`, `Closed`, `Published`; vencimiento y fecha de mora | `ExpensePeriod` |
| Liquidación | Cálculo y distribución por unidad, aportes al fondo de reserva y extraordinario (`ReserveFundPercentage`, `ExtraordinaryPercentage`) | `ExpensePeriodsController`, `ExpenseSettlement` |
| Cargos y pagos | `ExpenseCharge` (cargos, con reversiones), `Payment` (con `IsReversed`), aplicación del pago al cargo más antiguo | `ExpenseChargesController`, `PaymentsController`, `OwnerCreditService` |
| Morosidad | Reporte por antigüedad (0-30, 31-60, 61-90, +90) | `MorosityController` |
| Reportes | Libro de movimientos, estado de resultados, comparativo entre edificios | `/api/reportes/*`, servicios `LibroMovimientosService`, `EstadoResultadosService`, `BuildingComparisonService` |
| Facturación | Facturas, series/timbrados, notas de crédito | `InvoicesController`, `InvoiceSeriesController`, `CreditNotesController` |
| Reservas de áreas | Reservas con precio y comprobante | `AmenitiesController` |
| Planes por edificio | `BuildingPlan` con vencimiento y restricciones | `BuildingPlansController`, `PlanAccessPolicy` |

**Reglas de negocio vigentes que el módulo debe respetar** (de la memoria del proyecto):
- Un comprobante es el total pendiente de una unidad en un período; sin pagos parciales ni saldo a favor (`OwnerCreditFeature.Enabled = false`).
- Pagos aplicados siempre del cargo más antiguo al más nuevo.
- Los usuarios finales solo ven períodos publicados.
- Los commits se hacen a nombre de Tony, sin `Co-Authored-By` y sin push.

**Hallazgos previos que afectan este módulo:**
- `MorosityController` **no excluye pagos revertidos** (`IsReversed`); el dashboard y los estados de cuenta sí. Hay una tarea abierta para corregirlo. Cualquier cifra nueva debe excluirlos.
- El `dashboard/summary` general es histórico y pesado: no reutilizarlo para el módulo.

---

## 3. Habilitación por edificio

- Interruptor `FinanceModuleEnabled` en el edificio (campo nuevo en `Building`). Solo lo activa el **SuperAdmin** o el **Administrador de empresa**, según se decida (sección 12).
- Si está apagado, el módulo no aparece en el menú ni responde la API (403 con mensaje claro).
- Relación con planes: decidir si es parte de un plan (`Plan` con flag) o un complemento. **[decisión abierta]**
- Al activarlo por primera vez se abre un **asistente de configuración** (sección 4). Hasta completarlo, el módulo muestra solo el asistente.
- Fecha de arranque (`FinanceStartDate`): el módulo solo considera movimientos desde esa fecha, más los saldos iniciales. Evita reprocesar historia dudosa.

---

## 4. Configuración inicial (asistente por edificio)

Cada paso con valores por defecto sensatos; todo editable después hasta cerrar el primer período.

1. **Criterio de registro:** *devengado* (el hecho se registra al emitirse) o *percibido* (al cobrarse/pagarse). Un solo criterio por edificio, visible en todos los reportes. Recomendado: **percibido para caja y devengado para morosidad**, mostrando ambos explícitamente; ver sección 12.
2. **Cuentas financieras:** caja, uno o más bancos, fondo de reserva. Cada una con nombre, tipo, moneda (Gs.) y **saldo inicial a la fecha de arranque**.
3. **Plan de cuentas simple:** árbol de dos niveles (rubro → subrubro). Se ofrece una plantilla estándar de edificio que mapea las categorías de gasto existentes (`BuildingExpenseCategory`: servicios, limpieza, seguridad, mantenimiento, ascensor, seguro, sueldos, impuestos, administración, fondo de reserva, extraordinario, insumos, ANDE, ESSAP, internet/teléfono, otros). Cada rubro tiene un **código** editable (para exportar al contador).
4. **Impuestos:** si el edificio factura, tratamiento de IVA (10 %, 5 %, exento) por rubro.
5. **Proveedores:** alta rápida con RUC. Se pueden crear al cargar el primer gasto.
6. **Presupuesto anual:** monto mensual por rubro (puede copiarse del año anterior o del promedio real).
7. **Fondo de reserva:** porcentaje de aporte, saldo inicial y política de uso (qué requiere aprobación). Se parte de `ReserveFundPercentage` existente.
8. **Ejercicio y cierres:** mes de inicio del ejercicio y quién puede cerrar períodos.

---

## 5. Conceptos incluidos (sin contabilidad formal)

Cada uno con definición, de dónde sale el dato y qué se muestra.

### 5.1 Saldos por cuenta
Cuánto hay hoy en cada cuenta financiera. Saldo = saldo inicial + entradas − salidas desde la fecha de arranque. Entradas: pagos de propietarios aprobados, ingresos del edificio, aportes. Salidas: gastos pagados, transferencias al fondo.

### 5.2 Movimientos
Tabla única (extiende el libro de movimientos existente) con: fecha, cuenta, rubro, tercero, descripción, entrada/salida, saldo corrido, origen y enlace al comprobante. Filtrable y exportable.

### 5.3 Flujo de caja
Entradas y salidas del mes y del año, por rubro. **Proyección** del mes siguiente: ingresos esperados (cargos emitidos − morosidad histórica) y gastos recurrentes conocidos.

### 5.4 Presupuesto vs. real
Por rubro y período: presupuestado, ejecutado, diferencia y % de desvío, con semáforo (por ejemplo > 10 % rojo). Acumulado del ejercicio. Es el reporte que más usan las comisiones.

### 5.5 Cuentas por cobrar
Deuda de propietarios por antigüedad. Se reutiliza la lógica de morosidad **corrigiendo la exclusión de pagos revertidos**. Muestra también cobranza del período: emitido, cobrado, % de cobranza.

### 5.6 Cuentas por pagar
Facturas de proveedores pendientes: fecha de emisión, vencimiento, monto, estado (pendiente, pagada, vencida). Hoy un gasto se registra una vez; aquí se separa **registrar la obligación** de **registrar el pago** (campo nuevo de estado y fecha de pago en el gasto, o entidad `SupplierBill`; decidir en el diseño).

### 5.7 Fondo de reserva
Libro propio: aportes (por liquidación), usos (gastos con `PaidByReserveFund`), saldo y proyección. Historial por período. Gráfico de evolución.

### 5.8 Conciliación bancaria simple
Por cuenta bancaria: se cargan movimientos del extracto (importación CSV/Excel o manual) y se marcan contra movimientos del sistema. Muestra diferencia y pendientes de conciliar. No genera asientos.

### 5.9 Cierre de período financiero
Un período cerrado **bloquea** altas, ediciones y bajas de movimientos con fecha dentro de él, salvo reapertura por rol autorizado (con motivo registrado). Guarda quién cerró y cuándo.

### 5.10 Trazabilidad y auditoría
Cada movimiento enlaza a su origen (pago del propietario, factura, gasto, nota de crédito). Registro de quién creó, modificó o anuló, con valores antes/después (patrón de `CreditNoteAuditLog` ya existente).

---

## 6. Qué le interesa a un contador

Esto es lo que vuelve el módulo útil para quien lleva los libros fuera del sistema.

1. **Exportaciones** (Excel y CSV), con códigos de cuenta:
   - Libro diario simplificado.
   - Mayor por cuenta.
   - Balance de sumas y saldos.
   - Detalle de IVA débito y crédito por período.
   - Listado de comprobantes con RUC, número de factura y timbrado.
2. **Asientos sugeridos:** cada hecho genera un asiento propuesto (debe/haber) según el mapeo de cuentas, mostrado solo en la exportación. No editable en pantalla.
3. **Mapeo de cuentas:** cada rubro del edificio apunta a una cuenta del plan del contador (importable por Excel).
4. **Saldos de apertura documentados:** pantalla con los saldos iniciales y su respaldo.
5. **Auditoría accesible:** exportar el registro de cambios del período.
6. **Rol de solo lectura "Contador":** usuario externo que ve y exporta, sin modificar. **[decisión abierta]**
7. **Cierres con constancia:** PDF de cierre con saldos y firmas, similar a la liquidación.

---

## 7. Modelo de datos (propuesta, a validar contra el esquema real)

Entidades nuevas (todas con `CompanyId`, `BuildingId`, `IsDeleted`, fechas, según `BaseEntity` / `CompanyScopedEntity`):

| Entidad | Campos principales |
|---|---|
| `FinanceSettings` | `BuildingId`, `Enabled`, `StartDate`, `AccountingBasis` (`Accrual`/`Cash`), `FiscalYearStartMonth`, `SetupCompleted` |
| `FinancialAccount` | `Name`, `Type` (`Cash`/`Bank`/`ReserveFund`), `OpeningBalance`, `IsActive` |
| `LedgerCategory` | `Code`, `Name`, `Type` (`Income`/`Expense`/`Fund`), `ParentId`, `ExternalCode` (mapeo del contador) |
| `Supplier` | `Name`, `Ruc`, `Phone`, `Email` |
| `SupplierBill` (o campos en `BuildingExpense`) | `SupplierId`, `IssueDate`, `DueDate`, `Amount`, `Status`, `PaidAt`, `AccountId` |
| `FinanceMovement` | `Date`, `AccountId`, `CategoryId`, `Amount` (signado), `SourceType`, `SourceId`, `Description`, `ReconciledAt` |
| `BudgetLine` | `FiscalYear`, `Month`, `CategoryId`, `Amount` |
| `BankStatementLine` | `AccountId`, `Date`, `Description`, `Amount`, `MatchedMovementId` |
| `FinancePeriodClose` | `Year`, `Month`, `ClosedAt`, `ClosedByUserId`, `ReopenedAt`, `ReopenReason` |
| `FinanceAuditLog` | Mismo patrón que `CreditNoteAuditLog` |

Decisión de diseño clave: **`FinanceMovement` como libro derivado.** Se genera a partir de eventos existentes (pago aprobado, gasto pagado, ingreso, aporte al fondo) mediante un servicio, en lugar de reescribir los flujos actuales. Los flujos de pagos y liquidación no se tocan; el módulo **observa** y registra. Esto reduce el riesgo de romper lo que ya funciona. Hay que definir cómo se rellena el histórico al activar el módulo (desde `StartDate`) y cómo se mantiene consistente ante anulaciones y reversiones.

Cambios en entidades existentes: `Building.FinanceModuleEnabled`, `BuildingExpense` (estado de pago, fecha de pago, cuenta de pago, rubro del plan de cuentas), `Plan` (si el módulo va por plan).

**Migraciones:** el VPS aplica migraciones a mano con `sqlcmd` (ver `vps_deploy.md` en la memoria): preparar el script SQL junto a cada migración EF, con `-I` y `-b`, y verificar que la columna o tabla exista tras aplicarla.

---

## 8. API (propuesta)

Prefijo `/api/finance`, todo exige `buildingId` y módulo habilitado. Roles según sección 9.

| Método y ruta | Uso |
|---|---|
| `GET/PUT /finance/settings` | Estado y configuración del edificio |
| `POST /finance/settings/enable` | Activa y crea los valores por defecto |
| `GET/POST/PUT/DELETE /finance/accounts` | Cuentas financieras |
| `GET/POST/PUT/DELETE /finance/categories` | Plan de cuentas; `POST /finance/categories/import` mapeo del contador |
| `GET/POST/PUT/DELETE /finance/suppliers` | Proveedores |
| `GET/POST/PUT /finance/bills`, `POST /finance/bills/{id}/pay` | Cuentas por pagar |
| `GET /finance/movements` | Libro de movimientos con filtros y paginación |
| `GET /finance/dashboard?buildingId&year&month` | Tablero (saldos, flujo, presupuesto vs. real, fondo, cobranza) |
| `GET/PUT /finance/budget?year` | Presupuesto |
| `GET /finance/reports/{cash-flow,budget-vs-actual,receivables,payables,reserve-fund}` | Reportes |
| `GET /finance/export/{journal,ledger,trial-balance,vat,vouchers}?format=xlsx|csv` | Exportaciones para el contador |
| `POST /finance/reconciliation/import`, `PUT /finance/reconciliation/match` | Conciliación |
| `POST /finance/periods/{year}/{month}/close`, `…/reopen` | Cierre y reapertura |
| `GET /finance/audit` | Registro de auditoría |

Reglas transversales: excluir pagos revertidos; respetar el alcance por edificio (`CanAccessBuildingAsync`); no devolver datos de edificios con el módulo apagado; paginar listados; cifras con agregados en base de datos, no cargando tablas completas.

---

## 9. Permisos

| Acción | SuperAdmin | Administrador de empresa | Operador | Encargado | Contador (nuevo, opcional) |
|---|---|---|---|---|---|
| Habilitar módulo | Sí | Sí **[decidir]** | No | No | No |
| Configurar (cuentas, plan, presupuesto) | Sí | Sí | No | No | No |
| Cargar movimientos, proveedores, facturas | Sí | Sí | Sí **[decidir]** | Sí **[decidir]** | No |
| Ver tablero y reportes | Sí | Sí | Sí | Sí | Sí |
| Exportar | Sí | Sí | No | No | Sí |
| Cerrar y reabrir períodos | Sí | Sí | No | No | No |

Coherencia con la regla de liquidación ya vigente: el Operador calcula, el Encargado aprueba, el Administrador publica. Aplicar el mismo criterio de separación de funciones al cierre financiero **[decidir]**.

Interacción con la restricción por plan vencido: la lectura queda permitida en solo lectura; las escrituras se bloquean igual que el resto del sistema.

---

## 10. Pantallas web (Condo-PY-WEB)

1. **Finanzas → Tablero:** tarjetas (saldo total, caja, bancos, fondo de reserva), flujo del mes, presupuesto vs. real con semáforo, cobranza y morosidad, cuentas por pagar vencidas.
2. **Movimientos:** tabla filtrable con enlace al origen.
3. **Presupuesto:** grilla rubro × mes editable, con copiar del año anterior.
4. **Cuentas por pagar y proveedores.**
5. **Fondo de reserva:** libro y gráfico.
6. **Conciliación bancaria.**
7. **Cierres de período.**
8. **Para el contador:** exportaciones, mapeo de cuentas, saldos de apertura, auditoría.
9. **Configuración (asistente):** los 8 pasos de la sección 4.

Cada pantalla muestra el criterio de registro del edificio y un aviso de que no reemplaza al contador. La app móvil del Encargado queda fuera de esta etapa; se evalúa después mostrar saldos y alertas.

---

## 11. Plan de trabajo por fases

| Fase | Contenido | Resultado visible |
|---|---|---|
| **0. Decisiones** | Responder la sección 12 | Alcance cerrado |
| **1. Base** | `FinanceSettings`, interruptor por edificio, asistente, cuentas y plan de cuentas, saldos iniciales. Migración SQL manual | Se puede habilitar y configurar un edificio |
| **2. Libro y tablero** | `FinanceMovement` derivado de pagos y gastos, saldos por cuenta, flujo de caja, tablero | Saldos y flujo reales |
| **3. Presupuesto y fondo** | `BudgetLine`, presupuesto vs. real, libro del fondo de reserva | Reporte que piden las comisiones |
| **4. Pagar y cobrar** | Proveedores y cuentas por pagar; cuentas por cobrar con la corrección de pagos revertidos | Obligaciones pendientes |
| **5. Contador** | Exportaciones, mapeo de cuentas, asientos sugeridos, auditoría, rol Contador | Entrega al contable |
| **6. Control** | Conciliación bancaria y cierre de período | Cierre mensual con constancia |

Cada fase se cierra con: compilación limpia, prueba con datos de ejemplo, y commit separado. Probar contra el criterio ya fijado, sin cambiar los flujos de pagos ni liquidación.

---

## 12. Decisiones tomadas (2026-10-01)

Las 11 decisiones abiertas quedaron cerradas. **Ya no hay que volver a preguntarlas.** Donde el resto del documento diga "[decidir]" o "[decisión abierta]", rige esta tabla.

| # | Tema | Decisión | Consecuencia en el diseño |
|---|---|---|---|
| 1 | Criterio de registro | **Percibido para caja y devengado para morosidad** | `AccountingBasis` deja de ser un interruptor único: la caja, los saldos y el flujo se calculan por lo cobrado y pagado; las cuentas por cobrar y la morosidad, por lo emitido. Ambos criterios rotulados en cada reporte |
| 2 | Comercial | **Incluido en un plan superior** | Indicador en `Plan` (por ejemplo `IncludesFinanceModule`). Si el plan vigente del edificio no lo incluye, no se puede habilitar. Si el plan baja o vence en bloqueo, rigen las reglas de plan existentes |
| 3 | Quién habilita | **Solo SuperAdmin** | El interruptor del edificio lo opera solo SuperAdmin. El Administrador de empresa configura una vez habilitado, pero no lo enciende ni apaga |
| 4 | Plan de cuentas | **Plantilla estándar con códigos editables** | Sin importación del plan del contador en la primera versión (queda para más adelante). El campo `ExternalCode` se mantiene para el mapeo manual |
| 5 | Cuentas por pagar | **Sí, en la fase 4** | Se separa registrar la factura del proveedor de registrar su pago |
| 6 | Rol Contador | **No se crea** | Se entrega la exportación por Excel/CSV; sin rol nuevo. Se retira "Contador" de la tabla de permisos |
| 7 | Quién carga movimientos | **Administrador, Operador y Encargado** | Los tres pueden cargar movimientos, proveedores y facturas, como ya ocurre con los gastos |
| 8 | Cierre de período | **Administrador cierra; reabrir pide motivo** | Cierra el Administrador de empresa (y SuperAdmin). Reabrir exige motivo y queda en la auditoría. Sin la separación de funciones de la liquidación |
| 9 | Conciliación bancaria | **Marcado manual primero, importación después** | La fase 6 empieza sin importar extractos. Importación en una etapa posterior, cuando se elijan los bancos principales |
| 10 | Histórico | **Se parte de saldos iniciales, sin historia** | No se rellena el libro con datos anteriores. Cada cuenta tiene saldo inicial a `FinanceStartDate`; solo cuenta lo posterior. Se elimina el problema de reconstruir historia |
| 11 | App del Encargado | **No por ahora, solo web** | Fuera de alcance de este plan |

### Ajustes que estas decisiones obligan a hacer en el resto del documento
- **Sección 3 (habilitación):** el interruptor lo opera solo SuperAdmin, y requiere que el plan lo incluya.
- **Sección 5.8 (conciliación):** solo marcado manual en la primera versión; las líneas de extracto (`BankStatementLine`) se cargan a mano o quedan para la etapa posterior.
- **Sección 7 (modelo):** agregar el indicador de finanzas en `Plan`; `FinanceSettings.AccountingBasis` se reemplaza por la regla fija "caja percibida, deuda devengada".
- **Sección 9 (permisos):** quitar la columna "Contador"; "Cargar movimientos" y "Proveedores y facturas" para Administrador, Operador y Encargado; "Cerrar y reabrir" solo Administrador y SuperAdmin (con motivo al reabrir); "Habilitar módulo" solo SuperAdmin.
- **Sección 6 (contador):** sin rol, la exportación pasa a ser la entrega principal; el punto 6 queda sin efecto.
- **Sección 11 (fases):** la fase 6 queda como "conciliación manual y cierre"; sin importación.

### Lo que estaba abierto (no son decisiones de producto)
Los puntos marcados **[verificar]** se comprobaron contra el esquema real el 2026-10-01; el resultado está en la sección 15.1:
- campos exactos de `BuildingExpense`, `Payment`, `BuildingIncome` y `ExpenseSettlement`;
- cómo se registran hoy los aportes y usos del fondo de reserva;
- que la corrección de pagos revertidos en el reporte de morosidad ya esté aplicada (**no lo está**: sigue abierta, ver 15.1).

## 13. Riesgos y cómo mitigarlos

| Riesgo | Mitigación |
|---|---|
| Expectativa de contabilidad formal | Nombre y avisos claros; sin editor de asientos; asientos solo sugeridos y exportables |
| Cifras que no cierran entre reportes | Un solo criterio por edificio, visible; libro de movimientos como fuente única |
| Romper flujos de pagos o liquidación | El libro observa y no reescribe; pruebas con datos de ejemplo antes de activar |
| Pagos revertidos mal contados | Excluirlos en todo; corregir `MorosityController` antes o en la fase 4 |
| Historia inconsistente al activar | Fecha de arranque y saldos iniciales documentados |
| Rendimiento (tablas grandes) | Agregados en base de datos, paginación, índices por `BuildingId` y fecha |
| Migraciones manuales en el VPS | Script SQL por migración, `sqlcmd -I -b`, verificar la columna después |
| Fuga entre edificios o empresas | Validar `CanAccessBuildingAsync` en cada endpoint; revisar la auditoría de aislamiento pendiente |

---

## 14. Criterios de aceptación generales

- Con el módulo apagado, ningún endpoint ni pantalla del módulo responde ni aparece.
- Con el módulo encendido y configurado, el saldo de una cuenta coincide con saldo inicial + entradas − salidas, verificado con datos de ejemplo.
- Presupuesto vs. real cuadra con los gastos cargados del período.
- Un pago revertido no suma como cobrado ni como pagado en ningún reporte.
- Un período cerrado rechaza altas, ediciones y bajas con fecha dentro de él.
- El Excel de exportación contiene códigos de cuenta y se abre sin errores en Excel.
- Un usuario de otro edificio o empresa no ve nada del módulo.
- Con el plan en solo lectura, se puede consultar y exportar pero no cargar movimientos.

---

## 15. Estado de implementación (actualizado 2026-10-01)

### 15.1 Verificación contra el esquema real (los puntos [verificar])

| Entidad | Lo que realmente tiene | Consecuencia para el módulo |
|---|---|---|
| `BuildingExpense` | `Category` (16 valores), `SupplierName` (texto libre, sin entidad proveedor), `ExpenseDate`, `Amount`, `DistributionType`, `PaidByReserveFund`, comprobante. **Sin** estado de pago, fecha de pago, cuenta ni rubro. Solo se edita con el período en borrador | Hasta la fase 4 un gasto se cuenta como pagado en `ExpenseDate` (igual que el libro actual). El rubro se resuelve por `LedgerCategory.SystemKey` (`Expense.<Categoría>`), sin tocar la entidad |
| `Payment` | Sin `BuildingId` (se llega por `Unit` o `ExpensePeriod`), `PaymentDate`, `Amount`, `Method`, `Reference`, `IsReversed`/`ReversedAt`. Revertir marca el pago y da de baja sus `PaymentAllocation`. Sin cuenta destino | Los cobros se asignan a cuentas por defecto según `Method` (decisión de la fase 2). La parte que fue a fondo de reserva sale de `PaymentAllocation` → `ExpenseCharge.ChargeType` |
| `BuildingIncome` | Por período, 7 categorías, sin cuenta. `AccumulatedBalance` es el arrastre del rollover (no es plata nueva; el libro y el estado de resultados ya lo excluyen). `OperationalFund` es informativo (la liquidación lo excluye) | La plantilla no tiene rubro para `AccumulatedBalance`. Decidir en la fase 2 si `OperationalFund` es dinero real |
| `ExpenseSettlement` | Foto del período (totales y circuito de aprobación); no mueve plata. `ReserveFundAmount` es el **saldo** del fondo al cierre, no el aporte | Sirve para cotejar, no como fuente del libro |
| Fondo de reserva | No hay libro ni saldo guardado. **Aportes:** cargos `ChargeType.ReserveFund` (por `Building.ReserveFundPercentage` y por gastos de categoría `ReserveFund`) e ingresos con `IncomeTreatment.ToReserveFund`. **Usos:** gastos con `PaidByReserveFund`. El saldo se calcula por período y se arrastra con un rollover manual | El saldo del fondo del módulo (desde su saldo inicial) y el de la liquidación pueden diferir: mostrar ambos y no reutilizar el rollover |
| Morosidad | **Sin corregir:** `MorosityController` no filtra `IsReversed` y suma `Payment.Amount` | Sigue pendiente (fase 4, o antes si se decide) |

Un gasto de categoría `ReserveFund` es un aporte al fondo (se cobra a las unidades como cargo `ReserveFund`), no un egreso real: en la fase 2 hay que tratarlo como transferencia hacia la cuenta del fondo.

### 15.2 Ajustes aplicados a la especificación
- `FinanceSettings`: sin `Enabled` (el interruptor es solo `Building.FinanceModuleEnabled`) y sin `AccountingBasis` (regla fija: caja percibida, deuda devengada). `StartDate` se llama `FinanceStartDate`. Se agregaron `FiscalYearStartMonth`, `SetupCompleted*` y `EnabledAtUtc`/`EnabledByUserId`.
- `LedgerCategory`: se agregó `SystemKey` (clave estable de los rubros de la plantilla, por ejemplo `Expense.Ande`, `Income.CommonAreaRental`, `Collection.ReserveFund`, `Fund.Usage`).
- `FinancialAccount`: sin `Currency` (solo guaraníes en la primera versión).
- La habilitación exige que el plan vigente (el no archivado) incluya el módulo (`Plan.IncludesFinanceModule`).
- Si el edificio pasa a un plan sin el indicador, el módulo queda apagado (403 `finance_plan_not_included`) pero conserva datos e interruptor; vuelve al subir de plan.
- El indicador del plan se edita solo en planes sin edificios asignados (regla de inmutabilidad vigente); en planes asignados se usa Clonar, que copia el indicador.

### 15.3 Fase 1 — qué se hizo
- **Migración EF** `20261001163613_FinanceModuleBase` y su script SQL idempotente `Condo.Infrastructure/Persistence/Migrations/20261001163613_FinanceModuleBase.sql` (se corre con `sqlcmd -I -b` y verifica al final columnas, tablas e índices antes del COMMIT).
- **Datos:** `Plans.IncludesFinanceModule`, `Buildings.FinanceModuleEnabled`, tablas `FinanceSettings`, `FinancialAccounts` y `LedgerCategories` (índices únicos filtrados: una configuración por edificio; nombre de cuenta y código de rubro únicos por edificio; una sola caja y un solo fondo de reserva por edificio).
- **API** (`/api/finance`): `GET /buildings` (edificios con el módulo disponible), `GET /admin/buildings` y `POST /settings/enable|disable` (solo SuperAdmin), `GET|PUT /settings`, `POST /settings/complete`, y CRUD de `/accounts` y `/categories`. Con el módulo apagado o con un plan sin el indicador responde 403 con `{ error: "finance_module_disabled" | "finance_plan_not_included", message }`.
- **Permisos:** lectura para SuperAdmin, Administrador de empresa, Operador y Encargado con acceso al edificio; escritura solo SuperAdmin y Administrador de empresa. Habilitar y apagar, solo SuperAdmin. Se valida `CanAccessBuildingAsync` en cada endpoint; en los endpoints por id, un edificio ajeno responde 404. La restricción por plan vencido (solo lectura y bloqueo) se hereda de `PlanRestrictionMiddleware` porque filtra por ruta.
- **Plantilla del plan de cuentas:** 38 rubros en dos niveles (fondos 3.x, ingresos 4.x, gastos 5.x), con una clave por cada categoría actual de gasto e ingreso (excepto `AccumulatedBalance`). Los códigos son editables; los rubros de la plantilla se renombran, recodifican y desactivan, pero no se mueven ni eliminan.
- **Web:** casilla en Planes, página «Finanzas por edificio» (SuperAdmin), grupo de menú «Finanzas del edificio» (solo si el módulo está disponible en algún edificio del usuario) y pantalla de configuración con asistente de 4 pasos (fecha de arranque, cuentas con saldo inicial, plan de cuentas, revisión) y pestañas una vez completa.
- **Pruebas:** 136 comprobaciones con datos de ejemplo y base en memoria (módulo apagado, habilitación, aislamiento entre empresas, roles, validaciones, bajada de plan, plan vencido en solo lectura y bloqueo, plantilla); traducción a SQL Server revisada con `ToQueryString`; recorrido completo de las pantallas contra el backend real en memoria.

### 15.4 Fuera de la fase 1 (queda para las fases siguientes)
- IVA por rubro y política de uso del fondo de reserva (pasos 4 y 7 del asistente de la sección 4). El presupuesto se hizo en la fase 3 (15.6).
- Auditoría del módulo (`FinanceAuditLog`): hoy solo se guarda quién habilitó y quién completó la configuración. Conviene sumarla antes de que existan movimientos.
- Cuando existan movimientos: bloquear el cambio de la fecha de arranque y de los saldos iniciales, y permitir solo desactivar (no eliminar) cuentas y rubros usados.

### 15.5 Fase 2 — libro y tablero (implementada 2026-10-01)

**Decisión de diseño: libro virtual.** En vez de una tabla `FinanceMovement` que haya que mantener al día, los movimientos se **arman al consultar** a partir de los cobros, gastos e ingresos existentes (`FinanceLedgerService`). Así no se tocan los flujos de pagos ni de liquidación, y anulaciones, reversiones y bajas se reflejan solas. Hay dos caminos con las mismas reglas: agregados por mes (una consulta agrupada en la base, para saldos, flujo y tablero) y movimientos línea por línea de un rango acotado a 400 días. Una prueba verifica que ambos coinciden. La tabla persistente (con `ReconciledAt`) se agregará en la fase 6, cuando la conciliación la necesite.

**Cómo se resolvieron los cuatro puntos de 15.1:**
1. *Gasto sin estado de pago:* se cuenta como pagado en su `ExpenseDate`, hasta que la fase 4 separe obligación y pago.
2. *Cobros sin cuenta:* el efectivo entra a la caja; los demás medios, a la **cuenta por defecto** del edificio (`FinanceSettings.DefaultAccountId`; si no se elige, el único banco activo; si hay varios y no se eligió, queda «sin cuenta asignada» con aviso en el tablero). Lo imputado a cargos de fondo de reserva entra a la cuenta del fondo.
3. *Gasto de categoría Fondo de reserva:* no es un egreso (es el aporte que se cobra a las unidades): queda afuera del libro; el dinero se ve del lado de los cobros.
4. *Saldo del fondo:* el del módulo parte del saldo inicial de su cuenta y suma lo cobrado desde el arranque; puede diferir del «saldo acumulado» de la liquidación, y la pantalla lo aclara.

**Otras reglas del libro:** criterio percibido, desde la fecha de arranque y **hasta hoy** (lo fechado a futuro no entra); excluye pagos revertidos y borrados; el arrastre de saldo (`AccumulatedBalance`) y el fondo operativo (`OperationalFund`, informativo en la liquidación) no son ingresos; los ingresos propios del edificio entran a la cuenta del fondo si el edificio los manda al fondo (`ToReserveFund`) y, si no, a la cuenta por defecto; lo cobrado sin imputar a ningún cargo cae en expensas ordinarias, así que el total cobrado siempre coincide con la suma de los pagos; sin cuenta del fondo, sus movimientos pasan a la cuenta por defecto.

**API** (`/api/finance`, lectura para los cuatro roles administrativos; exige la configuración completa, si no responde 409 `finance_setup_incomplete`): `GET balances`, `GET movements` (filtros por fecha, cuenta, rubro y sentido, paginación y saldo corrido), `GET cash-flow` (ejercicio por rubro y mes), `GET dashboard`, y `PUT settings/default-account` (SuperAdmin y Administrador de empresa).

**Web:** Tablero (tarjetas, cuentas, flujo del mes y del ejercicio, gráfico de 12 meses), Movimientos, Flujo de caja y selector de cuenta por defecto en Configuración → Cuentas. Selector de edificio compartido: aparece solo con más de un edificio y recuerda la última elección.

**Migración:** `20261001173551_FinanceLedgerDefaultAccount` (columna `FinanceSettings.DefaultAccountId`) con su script SQL.

### 15.6 Fase 3 — presupuesto y fondo de reserva (implementada 2026-10-01)

- **Presupuesto** (`BudgetLine`, migración `20261001174227_FinanceBudget` con su script SQL): importe mensual por subrubro de ingresos y de gastos (los fondos y los rubros principales no se presupuestan). Se identifica por mes calendario (`Year`/`Month`) en lugar de `FiscalYear`/`Month`; el ejercicio (desde `FiscalYearStartMonth`) solo agrupa 12 meses seguidos. Edita el SuperAdmin o el Administrador de empresa; los demás roles consultan. Atajos: copiar del ejercicio anterior y completar con el promedio real de los últimos 3, 6 o 12 meses completos (solo celdas vacías salvo que se pida reemplazar).
- **Presupuesto vs. real** (mes y acumulado del ejercicio): lo real de los **gastos** es lo cargado como gasto del edificio en el mes (por fecha, incluido lo pagado por el fondo y el aporte al fondo cargado como gasto, para que cuadre con los gastos cargados); lo real de los **ingresos** es lo cobrado. Semáforo: verde dentro de lo presupuestado, amarillo hasta 10 % de desvío, rojo más allá (en gastos preocupa pasarse; en ingresos, quedar por debajo; un gasto sin presupuesto es rojo). Se rotula el criterio de cada tipo.
- **Fondo de reserva:** libro mes a mes (aportes, usos, saldo de apertura y cierre) con movimientos y saldo corrido (`GET reserve-fund`). Requiere una cuenta de tipo Fondo de reserva. El tablero suma el resumen del presupuesto del mes y del fondo.
- **Guardas del plan de cuentas:** un rubro con presupuesto cargado no se elimina, no cambia de tipo ni recibe subrubros.
- **Limitaciones conocidas:** los rubros propios (creados a mano) se pueden presupuestar, pero su «real» queda en cero porque todavía no se les puede asignar categorías de gastos o ingresos; no se incluyó la **proyección del mes siguiente** del flujo de caja (sección 5.3); el tablero todavía no muestra cobranza/morosidad ni cuentas por pagar (fase 4).
- **Pruebas:** 257 comprobaciones con datos de ejemplo y base en memoria (libro, saldos, filtros, semáforo, presupuesto, copiar y promedio, fondo, permisos y aislamiento); consultas agrupadas revisadas contra la traducción a SQL Server; pantallas recorridas contra el backend real en memoria.

### 15.7 Qué sigue
1. **Fase 4 — cuentas por pagar y por cobrar:** proveedores con RUC, facturas de proveedor con vencimiento y pago (separar obligación de pago), cuentas por cobrar con la corrección de pagos revertidos en `MorosityController`, y cobranza/morosidad en el tablero.
2. **Fase 5 — para el contador:** exportaciones con códigos de cuenta, asientos sugeridos, IVA por rubro, auditoría del módulo (`FinanceAuditLog`; conviene adelantarla) y mapeo de rubros propios a categorías.
3. **Fase 6 — control:** conciliación bancaria manual (con la tabla persistente de movimientos) y cierre de período con constancia.
4. Pendientes menores: proyección del mes siguiente, bloquear el cambio de la fecha de arranque y de los saldos iniciales una vez que existan movimientos, e importación del plan de cuentas del contador por Excel (si el contador lo pide).
