# Plan de alineación de los asientos contables con la legislación paraguaya

Fecha: 2026-10-19. Estado: **propuesta, nada implementado**. Alcance: pantalla *Asientos contables* (`/finance/accounting`), `AccountingEntryBuilder`, `AccountingEntriesService` y la exportación a Excel.

> **Aviso de verificación.** Lo que dice la Ley 1034/83 salió del texto publicado en Justia Paraguay (arts. 11, 74-85). La Ley 4924/2013 y la RG 90/21 de la DNIT salieron de una acordada de la Corte y de la página de la DNIT. **No pude verificar** el texto de la ley de propiedad horizontal, la resolución del Consejo de Contadores sobre NIIF para PYMES ni los artículos de la Ley 6380 sobre registros contables. Esos puntos están marcados «validar con el contador». Este documento no es dictamen legal.

---

## 1. Diagnóstico de lo que existe

### 1.1 Qué hace hoy
- El asiento **no se guarda**: se arma al consultar, a partir del libro de Finanzas (cobros, gastos, ingresos y notas de crédito del proveedor), con criterio de caja. Cuadra por construcción (`AccountingEntryBuilder.cs:133-165`).
- Un asiento por movimiento (origen + fecha + cuenta financiera). Debe/Haber con IVA crédito en las compras.
- Hay dos pestañas: asientos sugeridos y asignación de la cuenta del plan de cada cuenta financiera y del IVA crédito (`LedgerAccountRoles`).

### 1.2 Por qué «no ves el número de asiento»
La pantalla sí lo dibuja (`finance-accounting-page.component.ts:89`, «N° 1 · fecha» en el encabezado de cada tarjeta, y la primera columna del Excel). Pero hay dos problemas reales:
1. **El número no es un número de asiento.** Es `entries.Count + 1` (`AccountingEntryBuilder.cs:95`): el orden de aparición en esa consulta. Cambia al mover el filtro de fechas, al cargar un gasto con fecha anterior o al revertir un pago. El «asiento 7» de hoy puede ser el 8 mañana.
2. **Es poco visible** (texto pequeño dentro de la cabecera). Si en tu pantalla ni aparece, es probable que el web desplegado sea anterior al commit `a04fadc`; no lo pude comprobar desde acá.

### 1.3 Lo que no cumple el espíritu de la ley (hallazgos)
| # | Hallazgo | Evidencia |
|---|---|---|
| H1 | Numeración volátil, sin correlativo por edificio y ejercicio | `AccountingEntryBuilder.cs:95` |
| H2 | **Los asientos se reescriben solos**: editar un gasto o revertir un pago cambia asientos pasados sin dejar rastro ni contraasiento | el libro filtra `!IsReversed` (`FinanceLedgerService.cs:76,83`) |
| H3 | Sin asiento de apertura ni de transferencias entre cuentas propias → las sumas por cuenta no coinciden con el saldo real del banco | spec 12.6, «alcance que quedó afuera» |
| H4 | Sin asiento de cierre ni de resultado del ejercicio; sin balance de sumas y saldos | no existe |
| H5 | Solo criterio de caja: no hay expensas por cobrar ni proveedores por pagar | decisión de Tony 2026-10-09 |
| H6 | Sin quién/cuándo asentó, sin glosa normalizada ni respaldo (factura, timbrado, RUC) en el asiento | `AccountingEntryDto` |
| H7 | Rango máximo de 400 días y filtro por defecto «mes en curso»: no hay libro por ejercicio completo | `MaxRangeDays` |
| H8 | Glosa cortada a 300 caracteres | `AccountingEntryBuilder.cs:97` |
| H9 | Sin retenciones ni libro de ventas | spec 12.6 |

---

## 2. Marco legal paraguayo contrastado

| Norma | Qué exige (verificado salvo indicación) | Estado actual | Brecha |
|---|---|---|---|
| **Ley 1034/83, art. 11 c)** | Orden cronológico y regular de contabilidad | Orden por fecha, pero sin correlativo | H1 |
| **Art. 74 y 77** | Contabilidad obligatoria para el comerciante con capital sobre 1000 jornales mínimos; llevada por contador matriculado | Fuera del sistema | Validar si el consorcio o la empresa administradora está alcanzado |
| **Art. 75 y 80** | Libro **Diario** (y de Inventario): operaciones asentadas en el orden en que se hicieron | No hay Libro Diario; hay asientos sugeridos | H1, H2 |
| **Art. 78** | Libros presentados al Registro Público de Comercio antes de usarse, hojas numeradas y rubricadas, nota de folios | El sistema no foliada ni imprime hojas | Fase 3 y 8 |
| **Art. 79** | Sin interlineados, blancos, raspaduras ni enmiendas; **la rectificación es por contraasiento** | Hoy se reescribe en silencio | **H2, el más grave** |
| **Art. 81** | La Caja integra el Diario | Caja = cuenta financiera, ok | — |
| **Art. 82-84** | Inventario y balance al inicio y al cierre de cada ejercicio (máx. 1 año), balance general dentro de los 3 primeros meses | No existe | H3, H4 |
| **Art. 85** | Conservar libros y comprobantes **5 años** (libros desde el último asiento; comprobantes desde su fecha) | Hay borrado lógico; falta política | Fase 7 |
| **Ley 4924/2013** (modifica art. 76) | Habilita sistemas modernos (hojas sueltas, formularios continuos) con numeración correlativa verificada por el Registro | Permite libro digital impreso | Fase 3 y 8 |
| **RG DNIT 90/21** | Registro electrónico de comprobantes en Marangatu (libros IVA compras y ventas) | Libro de compras existe; sin ventas | H9, validar |
| **Ley 6380/19 (IRE/IVA)** | La Administración puede exigir libros o formas de contabilización según categoría (citado por RG 94/21). Sin texto verificado de registros mínimos | — | Validar con el contador |
| **Criterio DNIT 2008 (condominio)** | Un condominio inscripto en el RUC es empresa independiente con contabilidad separada y distinta de la de sus partes | El módulo es por edificio, ok | Validar si el consorcio está inscripto |
| **Propiedad horizontal / reglamento de copropiedad** | Rendición de cuentas a la asamblea | Existe informe de asamblea | **No verificado**, validar con escribano o abogado |
| **NIIF para PYMES (CCPP)** | Base de devengado. El CCPP fijó su uso desde 2012 | Solo caja | H5, validar si aplica a un consorcio |

**Lectura práctica:** un consorcio de propietarios sin fines de lucro probablemente no sea «comerciante» (art. 74), así que la obligación fuerte de Diario rubricado recae sobre la **empresa administradora** si lleva su propia contabilidad, o sobre el consorcio si está inscripto en el RUC. Como no es seguro, el plan **diseña el módulo para el estándar más estricto (arts. 78-85)**. Así sirve cualquiera sea la respuesta del contador.

---

## 3. Respuesta a «¿modifico solo por el admin o por asiento?»

**Ninguno de los dos.** El art. 79 prohíbe enmendar un asiento. La regla del sistema debe ser:

1. **El asiento asentado no se edita ni se borra, ni siquiera el SuperAdmin.**
2. Un error se corrige **en el origen** (gasto, pago, ingreso) y el sistema genera automáticamente un **contraasiento** (invierte el original) más el **asiento correcto nuevo**. Los tres quedan numerados y enlazados.
3. El admin solo configura las **cuentas del plan** (contrapartidas), que afecta a los asientos *futuros*.
4. Un **asiento manual de ajuste** (para hechos que no vienen de un movimiento) lo puede crear solo Administrador o SuperAdmin, con glosa obligatoria, y con la misma regla de inmutabilidad. Va en la fase 6, no antes.

Permisos propuestos: SuperAdmin y Administrador **asientan y generan ajustes**; Operador **ve y exporta**; Encargado **no ve** el módulo.

---

## 4. Decisiones (cerradas por Tony el 2026-10-19)

| # | Decisión | Resolución |
|---|---|---|
| D1 | ¿Quién lleva el libro oficial: el consorcio (RUC propio) o la empresa administradora? | **Configurable por edificio** (ver 4.1) |
| D2 | ¿Se asienta automático o con un botón «Asentar período»? | **Botón por período**, con marca de agua «asentado hasta». Si fuera automático, un error de tipeo ya genera contraasientos |
| D3 | Un movimiento con fecha anterior a lo asentado | **Contraasiento + asiento nuevo con fecha de hoy** y la fecha real en `OperationDate` y en la glosa. Mantiene el orden cronológico del art. 79 |
| D4 | ¿Pasar a devengado (expensas por cobrar, proveedores por pagar)? | **Sin definir.** Se arranca en caja (como hoy). El devengado queda como interruptor por edificio (Fase 5), apagado por defecto, y se enciende si el contador lo exige. Ver 4.2 |
| D5 | Formato del número | `AAAA-000001` por edificio y ejercicio fiscal, sin huecos (propuesta, sin objeción) |

### 4.1 D1 configurable: diseño
Sección nueva «Libros contables» en el Centro de configuración del edificio (la editan SuperAdmin y Administrador, con historial como el resto):
- **Titular del libro:** `Consorcio` o `Empresa administradora`.
- **Datos del titular** para el encabezado del Diario: razón social, RUC, domicilio. Por defecto salen de la ficha del edificio (consorcio) o de la empresa (administradora); se pueden pisar.
- **Contador:** nombre y matrícula (art. 77), opcionales.
- **Datos de rúbrica** (art. 78): número de libro, fecha de rúbrica, folios autorizados y folios usados. Se cargan a mano; la rúbrica es un trámite externo.
- **Mes de inicio del ejercicio:** ya existe (`FiscalYearStartMonth`).

Decisión de diseño que acompaña a D1: **el Diario es siempre uno por edificio**, aunque el titular sea la empresa administradora. Así la rendición de cada consorcio queda separada y la numeración no se mezcla. El titular solo cambia el encabezado y los datos del libro. Si el contador de una administradora pidiera un Diario consolidado de la empresa, sería una extensión posterior (numeración por empresa) y no cambia este modelo.

### 4.2 D4: por qué se puede postergar
Todo lo de las Fases 1 a 4 funciona igual en caja que en devengado: la numeración, la inmutabilidad, los libros y el cierre. Lo único que cambia es qué hecho genera asiento (la expensa liquidada y la factura del proveedor, además del cobro y el pago). Por eso `JournalEntries.Kind` y `LedgerAccountRoles` se diseñan ya con espacio para `Receivables` y `Payables`, pero no se construye nada de eso hasta que haya respuesta.
**Pregunta exacta para el contador:** «Un consorcio de propietarios sin fines de lucro, ¿debe llevar su contabilidad por devengado (expensas por cobrar y proveedores por pagar) o le alcanza el criterio de caja?»

---

## 5. Plan atómico

Cada paso es independiente, compila y se prueba solo. Las fases 1 a 3 cubren H1, H2 y la pregunta del número. Una migración SQL por fase, entregada como `cat <<'EOF'` + `sqlcmd`.

### Fase 0 — Validación (sin código; no bloquea las fases 1 a 4)
- **0.1** Enviar al contador la pregunta de 4.2 (D4) y «¿se aplica NIIF para PYMES a un consorcio?».
- **0.0 (nuevo, D1) — HECHO 2026-10-19 (sin commit ni deploy).** Sección «Libros contables» del Centro de configuración (titular, datos, contador, rúbrica) con su migración. Es lo primero de código, porque el encabezado del Diario (Fase 3) la necesita.
  - **Backend:** entidad `AccountingBookSettings` (una por edificio), `AccountingBooksService` (también expone `Resolve`/`HeaderAsync` para el encabezado del Diario de la Fase 3), `BuildingConfigBooksController` (`GET`/`PUT /api/building-config/{buildingId}/books`), sección `books` (orden 13; Documentos pasó a 14 y Pagos online a 15). La ven SuperAdmin, Administrador y Operador; la editan SuperAdmin y Administrador; el Encargado no la ve. Exige Finanzas habilitado y en el plan. No es obligatoria.
  - **Migración:** `20261019110927_AccountingBooks` y su `.sql` idempotente (una tabla, dos índices; no toca datos).
  - **Web:** `config-books-section.component.ts`, abierta desde la tarjeta «Libros contables» del Centro.
  - **Reglas:** el RUC del titular se valida (dígito verificador) solo si lo cambian; la fecha de rúbrica no puede ser futura ni anterior a 1983; folios usados ≤ autorizados; vacío = dato por defecto del titular (consorcio: ficha del edificio; administradora: nombre de la empresa, sin RUC por defecto porque la empresa no lo tiene cargado). Cada cambio queda en el historial.
  - **Pruebas:** 18 nuevas (`AccountingBooksTests`); suite completa con SQL Server real: 1178 pasan, 0 omitidas.
- **0.2** Confirmar con escribano la ley de propiedad horizontal y las obligaciones de rendición.
- **Aceptación:** respuestas escritas guardadas en `docs/`.

### Fase 1 — Asiento con número real y persistido (backend) — HECHA 2026-10-19 (sin commit ni deploy)

**Qué quedó (difiere en detalles del diseño de abajo):**
- **Tablas** (migración `20261019120048_JournalEntries` + `.sql` idempotente, va después de `AccountingBooks`): `JournalEntries`, `JournalEntryLines`, `JournalSequences` y la columna `FinanceSettings.JournalPostedThrough` (la marca «asentado hasta» vive ahí, no en el edificio). Los renglones **no** tienen llave foránea a la cuenta del plan (guardan copia de código y nombre), para que reemplazar el plan no toque lo asentado.
- **Servicio** `JournalPostingService`: `PreviewAsync`, `PostAsync`/`PostBuiltAsync`, `GetAsync`, `Allocate` (numeración pura), `HashOf`. Reusa el armado de asientos (`AccountingEntriesService.BuildAsync`, sin tope de 400 días).
- **API** `api/finance/journal`: `GET` (libro por ejercicio, filtros de número y fechas, páginas de 100), `GET preview?through=`, `POST post`. Ven los cuatro roles; asientan solo SuperAdmin y Administrador (403 `journal_forbidden`). Los sugeridos ahora traen `postedThrough` y `sourceAccountId`.
- **Reglas:** se asienta lo posterior a «asentado hasta» y hasta el día elegido (no futuro, no anterior al arranque); en orden cronológico; **todo o nada** en una sola transacción; un asiento incompleto o que no cuadra frena todo (409) y no consume números; el número es `AAAA-000001` por edificio y ejercicio fiscal y reinicia al cambiar de ejercicio. Dos personas a la vez: los índices únicos (número y movimiento de origen) dejan pasar a una sola y la otra recibe 409 sin guardar nada. Cada asentado queda en el historial (acción «Asentó»).
- **Web:** pestaña «Libro diario» (por defecto) con el **N° real** destacado (`2026-000001`), banner «Asentado hasta…», panel «Asentar período» con vista previa y confirmación, filtros por ejercicio, número y fechas, y paginación. Los sugeridos pasan a llamarse «Sugerido n» (es solo el orden de la lista) y marcan «Ya asentado» hasta la marca.
- **Pruebas:** 23 nuevas (`JournalPostingTests`, 3 de ellas de punta a punta con el libro real en SQL Server); suite completa con SQL Server: 1201 pasan, 0 omitidas.

**Límites que quedan para la Fase 2 (a propósito):**
- Un movimiento **fechado en un día ya asentado** (cargado o corregido después) **no se asienta** todavía y el Libro Diario **no se entera** si el origen cambió o se revirtió: eso lo resuelven la detección de diferencias y el contraasiento (2.1 a 2.4).
- La inmutabilidad es de aplicación (no hay endpoint que edite o borre); el bloqueo a nivel base (trigger o permisos) es el paso 2.4.
- No hay Libro Diario imprimible con folios ni Excel del libro asentado (Fase 3); el Excel actual sigue siendo el de los sugeridos.

**Diseño original (referencia):**
- **1.1 Tablas.** `JournalEntries` (`Id, CompanyId, BuildingId, FiscalYear, Number, Date, OperationDate, Description, ThirdParty, Reference, SourceType, SourceId, Kind {Auto, Opening, Adjustment, Reversal, Closing}, ReversesEntryId?, PostedAtUtc, PostedByUserId, TotalDebit, TotalCredit, ContentHash`), `JournalEntryLines` (`EntryId, LineNo, LedgerCategoryId?, Code, Name, Debit, Credit`, con **copia** del código y nombre de la cuenta al asentar) y `JournalSequences` (`BuildingId, FiscalYear, LastNumber`).
  - Índice único `(BuildingId, FiscalYear, Number)`.
  - Sin `IsDeleted` en estas tablas: no se borran.
- **1.2 Numeración sin huecos.** Dentro de la misma transacción que inserta el asiento: `UPDATE JournalSequences ... WITH (UPDLOCK, HOLDLOCK)` y lectura del nuevo valor. Si el asiento falla, el número no se consume.
- **1.3 Servicio `JournalPostingService.PostAsync(building, to)`.** Reusa `AccountingEntryBuilder.Build`, ordena por fecha y asienta todo lo que esté después de la marca de agua hasta `to`. Es **idempotente**: índice único filtrado `(BuildingId, SourceType, SourceId, Date, FinancialAccountId, Revision)`.
- **1.4 Marca de agua.** `Building.JournalPostedThrough` (fecha). Solo avanza.
- **1.5 Endpoints.** `POST /api/finance/accounting/post {through}` (SuperAdmin y Administrador), `GET /api/finance/accounting/journal?fiscalYear&from&to&number` (lee lo asentado).
- **1.6 No asentar incompletos.** Si hay asientos con cuenta del plan faltante, `post` falla con el listado (reusa `Pending`). Nunca se asienta un asiento incompleto.
- **Aceptación:** se asienta dos veces el mismo rango y no se duplica nada; los números son 1..N sin huecos aunque falle una transacción a mitad de camino; con dos usuarios asentando a la vez no se repiten números. Pruebas con SQL Server real (`CONDO_TEST_SQLSERVER`).

### Fase 2 — Inmutabilidad y contraasiento (backend)
- **2.1 Detección de diferencias.** Al consultar, comparar `ContentHash` de lo asentado contra lo que hoy produce el libro para ese origen. Salida: lista «Pendientes de reasentar» (cambió, se anuló o apareció con fecha vieja).
- **2.2 Reasentar.** Para cada diferencia: contraasiento (`Kind=Reversal`, líneas invertidas, `ReversesEntryId`) más asiento nuevo, ambos con la fecha de hoy y `OperationDate` = fecha real (D3). Nunca se toca el original.
- **2.3 Gancho al cierre de mes.** Los meses cerrados ya bloquean altas, ediciones y bajas (`FinancePeriodClosure`). Se agrega: no se puede cerrar un mes con diferencias pendientes de reasentar.
- **2.4 Bloqueo a nivel base.** Trigger o permiso que impida `UPDATE` y `DELETE` sobre `JournalEntries` y `JournalEntryLines`.
- **Aceptación:** revertir un pago ya asentado produce contraasiento + asiento nuevo; el original no cambia; el saldo del banco vuelve a coincidir con el libro.

### Fase 3 — Libros oficiales y pantalla (backend + web)
- **3.1 UI.** Columna **N°** grande y primera en la tabla; filtro por número, por estado (Sugerido, Asentado, Contraasiento), por origen. Botón **Asentar hasta…** con vista previa («se asentarán 42 asientos, 3 incompletos»). Enlace de cada asiento a su gasto, pago o ingreso.
- **3.2 Libro Diario.** PDF y Excel por ejercicio: encabezado (razón social, RUC, ejercicio), **folio** correlativo por página, fecha, N°, glosa, cuentas Debe/Haber, totales por página y acumulados, sin renglones en blanco. Respeta el formato de hojas sueltas de la Ley 4924/2013.
- **3.3 Libro Mayor** por cuenta: saldo inicial, movimientos con N° de asiento, saldo acumulado.
- **3.4 Balance de sumas y saldos.** Cuenta por cuenta: saldo inicial, debe, haber, saldo final. Debe cuadrar; si no, se muestra el desvío.
- **3.5 Ampliar el rango** al ejercicio completo (quitar el tope de 400 días solo en estos reportes).
- **3.6 Glosa sin corte** (campo `nvarchar(max)` o 1000) y con datos del respaldo.
- **Aceptación:** el Diario del ejercicio tiene números consecutivos, el total Debe = total Haber, y los saldos del Mayor coinciden con el libro de Finanzas.

### Fase 4 — Apertura, transferencias y cierre (backend)
- **4.1 Asiento de apertura** por edificio a la fecha de arranque: Debe cuentas financieras con su `OpeningBalance`, Haber patrimonio / saldo inicial. Primer asiento del ejercicio (`Kind=Opening`).
- **4.2 Transferencias entre cuentas propias** (banco ↔ caja ↔ fondo): asiento propio. Hoy fuera de alcance.
- **4.3 Asiento de cierre** del ejercicio: salda ingresos y egresos contra resultado (`Kind=Closing`), y apertura del ejercicio siguiente con los saldos arrastrados.
- **4.4 Inventario y balance** (arts. 82-83): estado de situación y estado de resultados desde el Mayor, con aviso si faltan más de 3 meses desde el cierre.
- **Aceptación:** el saldo de cada cuenta financiera en el Mayor es igual al saldo real del libro de Finanzas, en cualquier fecha.

### Fase 5 — Devengado (solo si D4 lo exige)
- **5.1** Expensa liquidada: Debe expensas por cobrar, Haber ingreso. El cobro pasa a ser Debe banco, Haber expensas por cobrar.
- **5.2** Factura de proveedor: Debe gasto (+ IVA crédito), Haber proveedores por pagar. El pago cancela proveedores.
- **5.3** Roles nuevos en `LedgerAccountRoles`: `Receivables`, `Payables`. Hoy no existen.
- **Aceptación:** el estado de situación muestra cuentas por cobrar iguales a la mora del sistema.

### Fase 6 — Asiento manual de ajuste
- **6.1** Pantalla y endpoint solo para Administrador y SuperAdmin; glosa obligatoria; debe = haber; cuentas activas del plan; mes no cerrado.
- **6.2** Misma regla: no se edita, se contraasienta.
- **6.3** Auditoría: quién, cuándo y motivo, visible en el Centro de configuración.

### Fase 7 — Respaldo, retención y auditoría (art. 85)
- **7.1** Cada asiento guarda número y timbrado de la factura, RUC del proveedor y enlace al comprobante adjunto.
- **7.2** Política de **5 años**: prohibir la purga física de gastos, pagos, comprobantes y asientos antes de ese plazo; documentarlo en el procedimiento de respaldos del VPS.
- **7.3** Registro de auditoría del módulo (quién asentó, reasentó, exportó el Diario).

### Fase 8 — Rúbrica y cumplimiento externo
- **8.1** Procedimiento para presentar las hojas impresas del Diario al Registro Público de Comercio (art. 78) y obtener la rúbrica; el sistema solo genera las hojas con folio. **La rúbrica no puede ser digital desde el sistema.**
- **8.2** Libro de ventas y retenciones de IVA/renta (H9) si el contador lo pide.
- **8.3** Revisión final con el contador contra este documento.

---

## 6. Orden recomendado y esfuerzo

| Orden | Fase | Esfuerzo | Por qué primero |
|---|---|---|---|
| 1 | 0 | Bajo | Evita construir sobre una suposición legal falsa |
| 2 | 1 | Alto | Resuelve el número de asiento (H1) y es la base de todo |
| 3 | 2 | Alto | Resuelve el hallazgo más grave (H2, art. 79) |
| 4 | 3 | Medio | Lo que el contador y la DNIT piden ver |
| 5 | 4 | Medio | Sin apertura, los libros no cuadran con el banco |
| 6 | 7 | Bajo | Barato y exigido por el art. 85 |
| 7 | 6, 5, 8 | Medio | Según la respuesta del contador |

## 7. Qué no se toca
- Los flujos de pagos, liquidación y gastos no cambian; el asentado **lee** el libro, no lo modifica.
- Los asientos sugeridos actuales se conservan como vista previa («lo que se asentaría»).
- No hay cambios en la app móvil.
