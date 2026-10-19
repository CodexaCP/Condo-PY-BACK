# Libro Diario y asientos contables — Guía de pruebas

Para probar a mano lo hecho el 2026-10-19: **Libros contables** (Centro de configuración), **Libro diario** con número de asiento real (Fase 1) y
**correcciones con contraasiento** (Fase 2). Plan y decisiones en `PLAN_ALINEACION_ASIENTOS_CONTABLES_PY.md`. Marcá cada casilla `[ ]` a medida que lo verificás.

Lo que ya comprueban las pruebas automáticas (1268 en total, 0 errores, incluidas las de SQL Server real): numeración sin huecos, todo o nada, detección
de diferencias, contraasientos, bloqueo del cierre de mes y triggers de la base. Esta guía es para que veas **la pantalla** y confirmes que se entiende.

---

## ⚠️ Antes de empezar (leelo)

- **Lo asentado no se puede borrar ni editar, ni siquiera desde la base** (hay triggers). Probá en un **edificio de prueba**, no en uno real. Si después querés limpiar los datos de prueba, mirá la sección 12.
- El backend **aplica solo las migraciones al arrancar** (`MigrateAsync`): reinicialo y, si usás un VPS, corré antes los tres `.sql` en este orden:
  `20261019110927_AccountingBooks.sql` → `20261019120048_JournalEntries.sql` → `20261019125025_JournalReversals.sql`.
- Entrá como **SuperAdmin** o **Administrador de empresa** (los únicos que asientan). Más abajo hay un caso con **Operador**.
- El edificio necesita: Finanzas habilitado, configuración inicial completa, y en **Asientos contables → Cuentas del plan** todas las cuentas financieras y el IVA crédito asignados (si no, no se puede asentar: caso 6).
- Ese edificio tiene que tener movimientos: cobros de propietarios, gastos e ingresos propios con fecha **desde la fecha de arranque hasta ayer**.

---

## 1. Qué vas a ver nuevo

| Dónde | Qué cambió |
|---|---|
| **Asientos contables** | Pestaña nueva **«Libro diario»** (es la que abre primero) con el **N° real** de cada asiento: `2026-000001` |
| **Asientos contables → Asientos sugeridos** | Ahora dice **«Sugerido 1, 2, 3…»** (solo el orden de la lista) y marca **«Ya asentado»** lo que ya tiene número |
| **Centro de configuración** | Tarjeta nueva **«Libros contables»** (titular, contador, rúbrica) |
| **Plan de cuentas (edificios nuevos)** | Dos cuentas más: **4.3.09 Aportes extraordinarios** y **4.3.10 Ajustes a favor** |
| **Cierre de mes** | No deja cerrar un mes si hay asientos que cambiaron después de asentarse |

---

## 2. Libros contables

- [ ] Centro de configuración: aparece la tarjeta **«Libros contables»** (número 13), opcional, estado «Opcional» hasta que la configurés.
- [ ] Entrá: elegí **Consorcio**, dejá razón social/RUC/domicilio vacíos → abajo dice «Así saldría en el encabezado» con los datos de la ficha del edificio.
- [ ] Cambiá a **La empresa administradora** → la razón social pasa a ser el nombre de la empresa y el RUC queda «sin cargar». Cargá un RUC válido (por ejemplo `80012345-0`) y guardá → la tarjeta pasa a **Completa**.
- [ ] Probá un RUC inválido (`80012345-9`) → rechaza.
- [ ] Probá **folios usados mayores que autorizados** o una **fecha de rúbrica futura** → rechaza.
- [ ] **Ver historial de cambios**: queda la fila «Libros contables».
- [ ] Como **Operador** la ves pero sin poder editar; como **Encargado** no aparece.

---

## 3. Libro diario vacío y vista previa

- [ ] Asientos contables → **Libro diario**: banner naranja **«Todavía no se asentó nada»** y el texto «Lo asentado tiene número correlativo… no se puede editar ni borrar».
- [ ] Aparece el panel **«Asentar período»** con la fecha de **ayer** sugerida.
- [ ] Tocá **Ver qué se asentaría**: dice «Se asentarán **N asientos** del … al … (**2026-000001** a **2026-0000NN**), por Gs. …». **No asienta nada todavía**: el Libro diario sigue vacío.
- [ ] N coincide con la cantidad de la pestaña **Asientos sugeridos** para ese mismo rango.

## 4. Asentar

- [ ] Tocá **Asentar N asientos** → pide confirmación («Lo asentado no se puede modificar») → aceptá.
- [ ] Aviso verde «Se asentaron N asientos» con el rango de números.
- [ ] Banner verde **«Asentado hasta dd/MM/yyyy»**.
- [ ] La lista muestra cada asiento con **N° 2026-000001, 2026-000002…** grande, fecha, glosa y renglones Debe/Haber. Los números son **consecutivos y siguen el orden de las fechas**.
- [ ] Tarjetas de totales: **Total debe = Total haber**.
- [ ] Un gasto con IVA muestra 3 renglones (gasto, IVA crédito fiscal y banco).
- [ ] Pestaña **Asientos sugeridos**: lo anterior a la fecha trae la marca verde **«Ya asentado»**.
- [ ] Centro de configuración → historial: fila **«Asentó»** con «se asentaron N asientos (… a …)».
- [ ] Volvé a elegir **la misma fecha** y tocá Ver qué se asentaría → dice **«No hay nada para asentar»**. Con una fecha **anterior** → «Ya está asentado hasta…». Con una fecha **futura** → rechaza.

## 5. Filtros y páginas

- [ ] Filtro **N° de asiento**: escribí `2` → solo ese asiento.
- [ ] Filtros **Desde / Hasta**: acotan por fecha.
- [ ] Con más de 50 asientos aparece la paginación (Anterior / Siguiente) y las sumas son de **todo el filtro**, no de la página.

## 6. Asiento incompleto no se asienta

- [ ] En **Cuentas del plan** quitá la cuenta del plan de un banco (o dejá sin cuenta un rubro) y volvé al Libro diario → **Ver qué se asentaría**.
- [ ] Aviso naranja «**N asientos están incompletos** y por eso no se puede asentar: falta la cuenta del plan de …» y **no hay botón de asentar**.
- [ ] Si falta una **cuenta financiera o el IVA**: link **«Asignar las cuentas financieras»**. Si falta un **rubro** (dice «(sin rubro)»): link **«Ir al plan de cuentas»**.
- [ ] Reasigná lo que faltaba y confirmá que ya se puede asentar. (Aportes extraordinarios sin cuenta: ver la guía del plan de cuentas, edificios viejos necesitan 4.3.09 y 4.3.10 a mano.)

---

## 7. Correcciones con contraasiento (lo más importante)

Con el Libro diario ya asentado hasta ayer, hacé **cambios en movimientos ya asentados** (en otra pantalla o pestaña):

- [ ] **Editar el importe** de un gasto con fecha ya asentada.
- [ ] **Revertir un pago** de un propietario ya asentado (o **borrar un ingreso** propio).
- [ ] **Cargar un gasto o ingreso nuevo con fecha de un día que ya está asentado**.

Volvé al **Libro diario**:

- [ ] Aparece el aviso **«3 asientos cambiaron desde que se asentaron»** con una tabla: **Cambió** (con importe antes y ahora), **Ya no existe**, **Sin asentar**, cada uno con su fecha de operación y el N° del asiento original.
- [ ] El panel «Asentar período» sugiere ahora **el mismo «asentado hasta»** (alcanza para asentar solo las correcciones).
- [ ] **Ver qué se asentaría**: dice «**Además se corregirán 3 asientos que cambiaron**: se crean **4** asientos (contraasiento y asiento nuevo) con fecha …. Los originales no se tocan.»
- [ ] **Asentar** → la lista muestra, **al final y con números consecutivos**:
  - Un **contraasiento** por cada Cambió / Ya no existe, con la etiqueta amarilla **«Contraasiento del N° 2026-0000XX»**, los **mismos renglones con Debe y Haber invertidos**, y «operación del dd/MM/yyyy».
  - Un **asiento nuevo** por cada Cambió / Sin asentar, con la glosa **«(corrige el asiento 2026-0000XX)»** o **«(asentado después)»**.
- [ ] El asiento **original** quedó **igual** pero con la etiqueta roja **«Anulado por el N° …»**.
- [ ] El aviso de diferencias **desaparece** (todo corregido) y **Total debe = Total haber**.
- [ ] Probá corregir **dos veces el mismo gasto** (cambiar el importe, asentar, volver a cambiarlo, asentar): cada vez anula la versión vigente y crea la siguiente.
- [ ] **No genera correcciones**: renombrar o recodificar una cuenta del plan, ni reasignar la cuenta del plan de un banco. (Cambiar el **rubro** de un gasto **sí**.)

## 7b. Reglas del flujo real (agregadas el mismo día)

En tu sistema los gastos e ingresos solo se editan con el período en **borrador**; los pagos sí se pueden revertir con el período publicado.

- [ ] **Períodos en borrador no se asientan:** con un gasto o ingreso cargado en un período **en borrador**, «Ver qué se asentaría» dice **«No se puede asentar todavía: hay gastos o ingresos en períodos en borrador (…)»** y no hay botón de asentar. Publicá el período (o elegí una fecha anterior a esos movimientos) y ya se puede.
- [ ] **Los cobros de propietarios no dependen del período:** se asientan aunque el período siga en borrador.
- [ ] **Aviso antes de revertir un pago ya asentado:** abrí el pago → «Revertir pago». Arriba del motivo aparece un aviso amarillo **«Este pago ya está asentado en el Libro diario (asiento 2026-0000XX)… se va a generar un contraasiento»**. Si el pago no está asentado, no aparece nada. Confirmá la reversa igual.
- [ ] **Aviso antes de deshacer una publicación:** (SuperAdmin → Liquidaciones) «Deshacer publicación» de un período con movimientos asentados muestra el mismo aviso.
- [ ] **Aviso antes de deshacer el pago de un gasto a pagar:** la confirmación agrega «Este gasto ya está asentado en el Libro diario (…): el asiento no se modifica…».
- [ ] **Causa y motivo:** después de revertir el pago, en el Libro diario la tabla de diferencias dice **«Pago revertido»** con **«Motivo: …»** (el que escribiste). Al asentar, la glosa del contraasiento dice «(pago revertido. Motivo: …)».
- [ ] Un gasto editado dice **«Gasto modificado»**; un ingreso borrado, **«Ingreso eliminado»**; una NC anulada, **«Nota de crédito anulada»**.

## 7c. Libros oficiales (Fase 3): Libro Diario imprimible, Libro Mayor y sumas y saldos

Con el Libro diario ya asentado (al menos algunos asientos):

- [ ] **Imprimir:** en «Libro diario» aparece el bloque **«Imprimir el Libro Diario del ejercicio …»** con **Primer folio** (sugerido: el siguiente a los «folios usados» de Libros contables; 1 si no cargaste nada). Tocá **Libro Diario (PDF)** → baja `finanzas-libro-diario-<edificio>-2026.pdf`.
- [ ] En el PDF: cada hoja trae arriba **LIBRO DIARIO**, la razón social, RUC y domicilio del titular, el ejercicio, «Libro N° …» y la rúbrica, el contador, y un **FOLIO** grande que **empieza en el primer folio que pusiste y sube de a uno**.
- [ ] Los asientos van en orden de número, con la glosa en dos líneas como máximo y las cuentas con Debe y Haber; los renglones de Haber van sangrados. **No hay renglones en blanco**.
- [ ] Al pie de cada hoja: **«Suma de esta hoja»**, **«Suma anterior»** (desde la segunda) y **«Suma y sigue»**; la última dice **«Total del ejercicio»** y coincide con el total de las tarjetas del Libro diario.
- [ ] Un asiento **no se corta** entre dos hojas (todos sus renglones quedan juntos).
- [ ] Cambiá el **Primer folio** a otro número y volvé a descargar: los folios cambian.
- [ ] **Excel** del Libro Diario: mismas hojas y folios, con las filas de suma por folio.
- [ ] Imprimilo de verdad una vez y mirá que entre bien en la hoja (A4 vertical) y que se lea.
- [ ] **Libro mayor:** lista por cuenta con **Saldo inicial, Debe, Haber y Saldo final**. «Ver N movimientos» abre el detalle con **fecha, N° de asiento, glosa y saldo acumulado**. El Excel trae todas las cuentas con su detalle.
- [ ] Un asiento corregido con contraasiento: en el Mayor los dos movimientos aparecen y el saldo de la cuenta queda **como si el original no existiera**.
- [ ] **Sumas y saldos:** columnas Saldo inicial, Sumas del período y Saldo final, cada una en **Deudor / Acreedor**, con totales. Arriba dice **«El balance cuadra…»**. Si alguna vez dijera «NO cuadra», avisá: indica un asiento descuadrado.
- [ ] Cambiá de **ejercicio** en el selector (si hay más de uno asentado): las tres pestañas siguen al ejercicio elegido.
- [ ] Recordá lo que **todavía no está**: sin asiento de apertura, el saldo del banco acá **no coincide con el saldo real**; sin asiento de cierre, las cuentas de resultado arrastran su saldo al ejercicio siguiente (la pantalla lo dice).

## 8. Cierre de mes

- [ ] Con **cierre de período encendido** y un asiento cambiado sin corregir: **Cerrar mes** → rechaza con «Hay N asientos del Libro Diario que cambiaron… Corregilos antes de cerrar el mes».
- [ ] Después de asentar las correcciones, el mismo mes **se puede cerrar**.

## 9. Permisos

- [ ] **Operador** y **Encargado**: ven el Libro diario pero **no** el panel «Asentar período».
- [ ] Un edificio de **otra empresa** no se ve ni se puede asentar.
- [ ] Edificio **sin Finanzas** habilitado: el Libro diario no abre.

## 10. Dos personas a la vez (opcional)

- [ ] Abrí el Libro diario en **dos pestañas**, en las dos «Ver qué se asentaría» y asentá primero en una y después en la otra → la segunda dice **«Otra persona asentó al mismo tiempo. No se guardó nada…»** o **«No hay nada para asentar»**. No quedan números repetidos ni saltados.

---

## 11. Plan genérico para edificios nuevos

- [ ] Creá un edificio nuevo con Finanzas y plan genérico: en el grupo **4.3 Otros ingresos** están **4.3.09 Aportes extraordinarios** y **4.3.10 Ajustes a favor**, **activas**.
- [ ] Contador de la lista: **«83 de 199 activas»**.

## 12. Limpiar datos de prueba (solo en una base de prueba)

Lo asentado está protegido por triggers. Para limpiar un edificio **de prueba** hay que desactivarlos a propósito, borrar y volver a activarlos:

```sql
DISABLE TRIGGER TR_JournalEntryLines_Immutable ON JournalEntryLines;
DISABLE TRIGGER TR_JournalEntries_Immutable ON JournalEntries;
-- borrar los asientos de prueba (primero renglones, después los contraasientos, después los originales) y reiniciar «asentado hasta»:
--   DELETE FROM JournalEntryLines WHERE EntryId IN (SELECT Id FROM JournalEntries WHERE BuildingId = '<id>');
--   UPDATE JournalEntries SET ReversesEntryId = NULL WHERE BuildingId = '<id>';
--   DELETE FROM JournalEntries WHERE BuildingId = '<id>';
--   DELETE FROM JournalSequences WHERE BuildingId = '<id>';
--   UPDATE FinanceSettings SET JournalPostedThrough = NULL WHERE BuildingId = '<id>';
ENABLE TRIGGER TR_JournalEntries_Immutable ON JournalEntries;
ENABLE TRIGGER TR_JournalEntryLines_Immutable ON JournalEntryLines;
```

(Hacelo **solo** en una base de pruebas y nunca en producción: en un libro real eso sería alterar la contabilidad.)

- [ ] Verificación del bloqueo: `UPDATE JournalEntries SET Description = 'x'` sin desactivar los triggers → error **«El Libro Diario no se puede modificar ni borrar: un error se corrige con un contraasiento.»**

---

## Si algo no coincide

Anotá: qué pantalla, qué hiciste, qué esperabas y qué viste (una captura ayuda). Lo que no cubre todavía (viene en las próximas fases): **Libro Diario imprimible con folios, Mayor y balance de sumas y saldos** (Fase 3) y **asiento de apertura, de transferencias y de cierre del ejercicio** (Fase 4). Por eso, **el banco en el Mayor todavía no coincide con el saldo real**: falta el asiento de apertura.
