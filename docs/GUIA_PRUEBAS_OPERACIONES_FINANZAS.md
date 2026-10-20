# Guía de pruebas — Operaciones de Finanzas (transferencias entre cuentas)

Menú: **Finanzas (módulo) → Operaciones**. Hace falta un edificio con Finanzas configurado y **al menos dos cuentas activas** (por ejemplo un banco y la caja; para probar el fondo, una cuenta de tipo Fondo de reserva).

## 0. Antes de probar (una sola vez)

1. Aplicar las migraciones `.sql` en este orden: `AccountingBooks`, `JournalEntries`, `JournalReversals`, `JournalAccountTypes` y la nueva **`20261019182431_FinanceTransfers.sql`** (crea la tabla y sus dos triggers). Se corre con `-I -b`.
2. En **Asientos contables → Cuentas del plan**, comprobar que cada cuenta financiera (banco, caja, fondo) tiene su cuenta del plan. Si no, la operación se registra igual pero su asiento queda incompleto y la pantalla lo avisa.
3. Anotar los saldos de cada cuenta y el **total del edificio** (arriba de la pantalla Operaciones).

## 1. Depósito de efectivo

1. Tarjeta **«Depositar efectivo en el banco»**: ya elige Caja → Banco. Importe 150.000, fecha de hoy, referencia «Boleta 123». **Revisar**.
2. El resumen muestra los saldos de hoy y después, que el **total no cambia**, y «Debe Banco · Haber Caja». **Confirmar y registrar**.
3. Esperado: aviso verde «Operación registrada»; el banco subió 150.000, la caja bajó 150.000, el total es el mismo; aparece en el historial como **Vigente**, «Sin asentar».

## 2. Lo importante: no es ingreso ni gasto

Con la operación ya cargada, mirar y comprobar que **no cambió ningún número** respecto de antes de cargarla:

| Pantalla | Qué mirar | Esperado |
|---|---|---|
| Tablero | Ingresos y egresos del mes y del ejercicio, gráfico de 12 meses | iguales |
| Flujo de caja | Total de ingresos y de egresos, saldo de cierre | iguales (no hay línea nueva) |
| Presupuesto vs. real | Real de cada rubro | iguales |
| Informe de asamblea | Total ingresos, total egresos, resultado | iguales; hay un aviso que menciona las transferencias |
| Movimientos (todas las cuentas) | Totales de entradas y salidas | iguales; la operación aparece **dos veces** (sale de la caja, entra al banco) con la etiqueta «Transferencia» |
| Movimientos, filtrando una cuenta | Totales | esa cuenta sí cuenta la operación |
| Movimientos, filtrando por Ingresos o por Egresos | — | la transferencia no aparece |

## 3. Conciliación bancaria

En **Conciliación bancaria** el saldo del libro del banco incluye el depósito, y al armar una conciliación el depósito aparece como movimiento para tildar contra el extracto.

## 4. Fondo de reserva

1. **«Aporte o retiro del fondo de reserva»**: Banco → Fondo. Sin completar el respaldo, **Revisar** pide el número de acta. Con «Acta 12/2026» pasa.
2. En **Fondo de reserva** el aporte figura como «Aportes» y el saldo del fondo subió.
3. **Invertir sentido** y retirar más de lo que tiene el fondo: la pantalla lo **rechaza** (el fondo no puede quedar en negativo; no se puede confirmar).

## 5. Saldo negativo en banco o caja

Mover de la caja más de lo que tiene. En el resumen sale un aviso en amarillo y hay que tildar «Entiendo que … queda con saldo negativo» para poder confirmar.

## 6. Libro diario

1. **Asientos contables → Libro diario → Asentar** hasta hoy. La operación sale como **un solo asiento** con dos renglones (Debe el destino, Haber el origen), con su número `AAAA-000001`. En el historial de Operaciones aparece ese número.
2. Volver a Operaciones y **Anular** esa operación: antes de pedir el motivo, la pantalla avisa que la operación ya está asentada y que se va a generar un contraasiento. Escribir el motivo y anular.
3. Esperado: los saldos vuelven a lo de antes; el historial la muestra como **Anulada** con quién, cuándo y el motivo, y la leyenda «Falta asentar el Libro diario para generar el contraasiento».
4. En **Libro diario → Cambios desde que se asentó** aparece «Transferencia anulada» con el motivo. Al asentar nace el contraasiento y la leyenda desaparece.

## 7. Cosas que tienen que fallar

| Intento | Esperado |
|---|---|
| Misma cuenta de origen y destino | no deja elegirla / error |
| Importe 0 o negativo | error |
| Fecha futura o anterior al arranque de Finanzas | error |
| Fecha en un **mes cerrado** (Período y cierre) | error «El mes … está cerrado» |
| Fecha en un **ejercicio cerrado** | error: reabrir primero el ejercicio |
| Anular una operación ya **tildada en una conciliación** | error: desmarcarla antes |
| Anular un aporte al fondo si el fondo quedaría en negativo | error |
| Anular dos veces | error «ya está anulada» |
| Entrar como operador o gerente de edificio | ven la pantalla, **sin** tarjetas ni botón Anular |

## 8. Base de datos (opcional, para quien tenga acceso)

Con los triggers instalados, estas sentencias deben fallar con «Una operacion de Finanzas no se puede …»: `DELETE FROM FinanceTransfers`, `UPDATE FinanceTransfers SET Amount = 1`, `UPDATE FinanceTransfers SET Status = 1` (reactivar una anulada).

## Lo que NO hace todavía

- Comisiones e intereses bancarios y ajuste de saldo (etapa 2, después de que responda el contador).
- Adjuntar el acta como archivo (hoy es un texto).
- Aviso de saldo negativo contra el saldo **a la fecha** de una operación atrasada (usa el saldo de hoy).

## 9. Conciliar el fondo de reserva (o la caja): casilla «Se concilia con el extracto»

Antes solo se conciliaban las cuentas de tipo Banco. Ahora cada cuenta tiene una casilla, en **Finanzas → Configuración → Cuentas → Editar**:

1. Editar la cuenta de **Fondo de reserva** y tildar **«Se concilia con el extracto»**. Guardar. En la lista aparece «Fondo de reserva · se concilia».
2. Ir a **Conciliación bancaria**: la cuenta del fondo ahora figura (con «(fondo de reserva)» al lado del nombre) y se puede empezar una conciliación igual que con un banco.
3. Cargar un aporte al fondo desde **Operaciones** (Banco → Fondo, con acta): al armar la conciliación del fondo, el aporte aparece como movimiento para tildar contra el extracto.
4. Los bancos vienen tildados por defecto. Se puede destildar uno (por ejemplo, una cuenta que no tiene extracto): sale de la lista de conciliación. **No se puede destildar una cuenta que tiene una conciliación abierta**: primero se cierra o se descarta.
5. Una cuenta que ya tenía conciliaciones no cambia nada: los bancos existentes siguen conciliándose (la migración no toca datos).

Sigue siendo una sola cuenta de fondo de reserva y una sola caja por edificio. La casilla solo decide si se concilia; el comportamiento de cada tipo (a dónde van los aportes del fondo, la pantalla del fondo) no cambia.

## 10. Conciliación: listas largas, «esta conciliación» y Excel

En **Conciliación bancaria → Ver** (una conciliación):

1. **Conciliados en esta conciliación:** solo muestra lo que se tildó **en esa conciliación**, no todo lo de la cuenta. Si la cuenta tiene movimientos conciliados en otras conciliaciones, un aviso dice cuántos son y que **cuentan en el saldo conciliado** aunque no se listen.
2. Casilla **«Mostrar también los conciliados en otras conciliaciones de esta cuenta»**: los suma a la lista (y al Excel).
3. **Pendientes y Conciliados:** cada lista tiene **buscador** (por detalle, referencia, tipo, importe o fecha) y **paginación** con selector de 25, 50, 100 o 250 por página. «Seleccionar todos» actúa sobre todo lo que coincide con la búsqueda, no solo la página.
4. **Exportar a Excel** (arriba a la derecha, también en las cerradas y para los cuatro roles): archivo con hojas **Resumen** (saldos, diferencia, cuántos movimientos), **Conciliados** (de esa conciliación), **Pendientes** (**todos**, sin el tope de 1000 de la pantalla) y, si tildaste la casilla del punto 2, **Otras conciliaciones**.
5. Marcar o desmarcar movimientos con la casilla del punto 2 activada sigue funcionando: la lista se vuelve a cargar completa.
