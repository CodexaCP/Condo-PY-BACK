# Guía de pruebas — Conciliación bancaria desde la app del Encargado

Qué hay de nuevo (2026-10-22):

- **Back:** interruptor por edificio `FinanceSettings.ManagerCanReconcile` (apagado por defecto). Con él encendido, el Encargado puede armar, cerrar y descartar conciliaciones; sin él solo las ve. Endpoint nuevo `PUT api/finance/reconciliation/settings`. El resumen (`GET api/finance/reconciliation`) ahora trae `managerCanReconcile` y `canConfigure`.
- **Web:** casilla «El Encargado de este edificio puede conciliar desde la app» en **Conciliación bancaria** (solo SuperAdmin y Administrador de empresa; queda en el historial de configuración).
- **App (Encargado):** tarjeta «Caja y bancos» en Inicio con semáforo, entrada en **Más → Caja y bancos · conciliación**, pantalla de cuentas, pantalla de tildar y cierre. Todo importe tipeado lleva **separador de miles** y una **confirmación adicional** con el número grande.

## 0. Antes de probar (una sola vez)

1. Correr en el VPS, **antes** de desplegar el código, `20261022105039_ManagerReconcile.sql` (va **después** de `SupplierCreditRecovery`; agrega una columna con valor 0, no toca datos). Se corre con `-I -b`.
2. Desplegar el back y la web. Generar un APK nuevo (la app cambió).
3. Tener un edificio con Finanzas configurado, **una cuenta bancaria activa** con movimientos (cobros, gastos o ingresos) y un usuario **Encargado** asignado a ese edificio. Tener a mano un extracto (o inventar el saldo).

## 1. Interruptor en la web

1. Entrar como **Administrador de empresa** → Finanzas → **Conciliación bancaria** → elegir el edificio.
2. Arriba de la tabla aparece la casilla **«El Encargado de este edificio puede conciliar desde la app»**, apagada.
3. Encenderla. Esperado: aviso verde «Encendido». Apagarla: «Apagado». Ambos cambios figuran en el **historial de configuración** del edificio (sección Conciliación).
4. Entrar como **Operador** o **Encargado** a la misma pantalla de la web: la casilla **no** aparece.

## 2. Encargado SIN el interruptor (solo consulta)

Con la casilla apagada, entrar a la app como Encargado:

1. **Inicio:** aparece la tarjeta **Caja y bancos** con un semáforo («Todavía no se concilió» en rojo si nunca se concilió).
2. Tocarla (o **Más → Caja y bancos · conciliación**). Esperado: las cuentas con su saldo y el semáforo, y un aviso azul «Podés consultar… Armar y cerrar conciliaciones lo hace el administrador».
3. **No** hay botón «Conciliar». La pestaña **Cerradas** lista el historial.
4. Si el edificio ya tiene una conciliación abierta, se ve «Continuar conciliación» pero adentro no se puede tildar ni cerrar (aviso «Solo podés consultar»).
5. Probar directo contra la API con el token del Encargado (opcional): `POST api/finance/reconciliation` responde **403** `reconciliation_forbidden`.

## 3. Encargado CON el interruptor: flujo completo

Encender la casilla (punto 1) y refrescar la app.

### 3.1 Empezar
1. En la cuenta, **Conciliar**. Se abre una hoja con fecha del extracto (hoy por defecto) y **Saldo del extracto**.
2. **Separador de miles:** escribir `12400000`. Esperado: se ve `12.400.000` mientras se escribe. Probar también pegar con puntos, escribir letras (se ignoran) y un saldo negativo (`-500000` → `-500.000`).
3. **Empezar** sin saldo: error «Escribí el saldo…». Con fecha futura: error.
4. Con saldo cargado, **Empezar**. Esperado: ventana **«Confirmá el saldo del extracto»** con `Gs. 12.400.000` en grande. **Corregir** vuelve a la hoja; **Sí, es correcto** crea la conciliación y abre la pantalla de tildar.

### 3.2 Tildar
1. Arriba, siempre a la vista: **Extracto**, **Conciliado** y **Diferencia** (naranja mientras no cuadra, verde cuando es cero).
2. Pestaña **Por tildar (N)**: los movimientos del libro hasta la fecha del extracto, agrupados por día; entradas en verde (`+`), salidas en rojo (`−`).
3. Tocar varias filas: se marcan y, abajo, aparece **«Tildar N · + importe»**. Arriba dice «Si confirmás lo elegido, la diferencia queda en…». Esperado: ese número es el que queda después de confirmar.
4. **Tildar N**. Esperado: aviso «N movimiento(s) tildado(s)», pasan a la pestaña **Tildados**, **Conciliado** sube y **Diferencia** baja.
5. Filtros: **Entradas / Salidas**, buscador por detalle o nombre, y **«Elegir todos (N)»** (hasta 500 por vez).
6. Pestaña **Tildados**: tocar uno y **Quitar el tilde a N** (pide confirmación) lo devuelve a pendientes.
7. Tirar hacia abajo para refrescar: la pantalla recarga y se pierde la selección sin confirmar.

### 3.3 Cerrar
1. Mientras la diferencia no es cero, el botón **Cerrar conciliación** está deshabilitado y dice «Cuando la diferencia sea cero…».
2. Tildar hasta que **Diferencia = Gs. 0** (fila verde «Cuadra»). El botón se habilita.
3. **Cerrar conciliación**. Esperado: ventana de confirmación con el saldo del extracto en grande («Sí, cerrar»). Al aceptar: aviso «Conciliación cerrada» y vuelve a Cuentas, con el semáforo **verde** («Conciliada hoy»).
4. En **Cerradas** aparece con «Cerró <nombre> · fecha». Al abrirla es solo consulta (aviso «Conciliación cerrada»).
5. En la **web**, la misma conciliación figura cerrada por el Encargado (control cruzado: lo ve el administrador).

### 3.4 Corregir o descartar
1. En una conciliación abierta, menú **⋮ → Corregir fecha o saldo del extracto**: separador de miles en el saldo y, al **Guardar**, la confirmación con el número grande. Los tildados se mantienen y la diferencia se recalcula.
2. **⋮ → Descartar esta conciliación**: pide confirmación, borra la conciliación y sus tildes, y no toca el libro. Vuelve a Cuentas.

## 4. Confirmación y miles en los demás importes tipeados

| Pantalla | Campo | Esperado |
|---|---|---|
| Pagos → detalle de un pago **Por revisar** | Monto recibido (Gs.) | Se escribe con puntos de miles. **Verificar y aprobar** y **Solo verificar monto** abren la confirmación con el monto en grande |
| Más → Mi plan → Enviar comprobante | Monto pagado (Gs.) | Se escribe con puntos de miles. **Enviar comprobante** abre «Confirmá el monto pagado» y recién ahí envía |
| Propietario → Enviar pago | Monto declarado | Ya tenía separador de miles (sin cambios) |

## 5. Casos de borde

| Caso | Esperado |
|---|---|
| Edificio sin Finanzas, o con la configuración incompleta | La tarjeta de Inicio **no** aparece; la pantalla Caja y bancos muestra el motivo que da el back |
| Edificio sin cuentas para conciliar | «Este edificio no tiene cuentas para conciliar» |
| Dos personas concilian la misma cuenta a la vez | Mensaje «Otra persona hizo un cambio al mismo tiempo… actualizá» y la pantalla se recarga |
| Tildar un movimiento de un **mes cerrado** y luego quitarlo | 409 con el mensaje del cierre de período |
| Plan vencido en **solo lectura** | Aviso rojo; no se puede armar, tildar ni cerrar |
| Un movimiento tildado cambia de importe o se borra en el libro | Aviso naranja en la conciliación y chip «Cambió de importe» / «Ya no figura»; no se puede cerrar hasta quitar el tilde de los que ya no figuran |
| Más de 1.000 pendientes | Aviso «Hay más movimientos de los que se listan»: se tildan los de arriba y se cargan los siguientes |
| Apagar la casilla con una conciliación abierta | El Encargado deja de poder tildar o cerrar (403); la ve en solo consulta |

## 6. Semáforo (cómo se calcula)

Por cuenta activa, según la última conciliación **cerrada**: **verde** hasta 31 días, **amarillo** de 32 a 60 o con una conciliación en curso, **rojo** más de 60 días o si nunca se concilió. La tarjeta de Inicio muestra el peor semáforo de las cuentas del edificio.

## 7. Lo que no se hizo (a propósito)

- **Reabrir** una conciliación cerrada y **exportar a Excel** siguen solo en la web.
- La web mantiene su campo numérico de saldo (sin separador de miles ni confirmación); el cambio pedido fue para la app.
- El precio por hora del Marketplace del propietario conserva su campo numérico actual.
- No se leen extractos automáticamente (PDF o foto): el saldo se escribe a mano, por eso la doble confirmación.
