# Preguntas para el contador — Condo-PY, módulo de asientos contables

Condo-PY administra consorcios de propietarios (edificios). Para cada edificio genera **asientos de partida doble** a partir de lo que pasa en el sistema (cobros de expensas, gastos, ingresos, notas de crédito) y los guarda en un **Libro Diario**: con número correlativo por ejercicio (por ejemplo `2026-000001`), sin posibilidad de modificarlos (un error se corrige con un contraasiento) y con impresión en hojas con folio para rubricar. También genera Libro Mayor, balance de sumas y saldos, balance general y estado de resultados, y el cierre del ejercicio.

Las respuestas definen cómo termina de funcionar. Para cada pregunta basta una línea; las marcadas ⭐ son las que más cambian el diseño.

## A. Alcance y criterio

1. ⭐ **¿Caja o devengado?** Hoy los asientos son por **caja**: se asienta cuando entra o sale plata. ¿Un consorcio sin fines de lucro debe llevar la contabilidad por **devengado** (expensas por cobrar, proveedores por pagar), o le alcanza el criterio de caja?
2. ⭐ **¿Quién lleva el libro oficial?** ¿El consorcio (con RUC propio, como empresa independiente) o la empresa administradora? ¿El consorcio es sujeto obligado a llevar Libro Diario e Inventario y Balances (Ley 1034/83, art. 74)? El sistema lleva **un Diario por edificio** y permite configurar el titular.
3. ¿Qué **normas contables** aplican a un consorcio (NIIF para PYMES, norma para entidades sin fines de lucro u otra)? ¿Hay un plan de cuentas obligatorio o sugerido?
4. ¿Qué **estados** debe presentar a la asamblea? Hoy: balance general, estado de resultados, flujo de caja, presupuesto vs real y fondo de reserva.

## B. Libros y formalidades

5. **Rúbrica.** El sistema imprime el Libro Diario en **hojas sueltas con folio correlativo** (Ley 4924/2013). ¿Cómo se rubrican esas hojas en el Registro Público de Comercio, y cada cuánto (por ejercicio, por lote de folios)? ¿Alcanza con el Libro Diario o también se exige el Mayor?
6. **Numeración.** El número de asiento es `AAAA-000001`, correlativo por edificio y ejercicio, sin huecos. ¿Es aceptable, o debe ser un solo correlativo continuo entre ejercicios?
7. **Ejercicio fiscal.** ¿Enero–diciembre o puede ser otro mes de inicio? ¿Conviene definirlo igual para todos los edificios?
8. **Correcciones.** Un error en algo ya asentado se corrige con un **contraasiento** (que invierte el asiento) y un asiento nuevo. Esos dos asientos llevan **la fecha del día en que se asienta**, y la fecha real de la operación queda en la glosa. ¿Es correcto, o deben llevar la fecha original mientras el período esté abierto?
9. **Apertura y cierre.** Los saldos iniciales se asientan contra **3.2.01 Resultados acumulados**. Al cerrar el ejercicio, los ingresos y gastos se saldan contra **3.2.02 Resultado del período** y ese resultado pasa a 3.2.01. ¿Es el tratamiento correcto? ¿Hace falta además un asiento de cierre y apertura de las cuentas de balance en cada ejercicio?
10. **Conservación.** La ley pide conservar libros y comprobantes 5 años (art. 85). ¿Es suficiente el respaldo digital del sistema, o se exige conservar también el papel?

## C. Plan de cuentas

11. ⭐ **Fondo de reserva y aportes extraordinarios.** Hoy el fondo de reserva figura en la clase 3 (patrimonio / fondos). ¿Es pasivo o patrimonio? Y los aportes extraordinarios de los propietarios, ¿son ingreso, pasivo o fondo?
12. **Cuentas que faltan.** Para el devengado harían falta «Expensas por cobrar», «Proveedores por pagar», «Créditos a favor con proveedores» y «Saldos a favor de propietarios». ¿Qué nombre y código sugiere para cada una y en qué clase?
13. **Saldos iniciales.** Al arrancar el sistema se asienta el saldo de banco, caja y fondo contra Resultados acumulados. ¿Hay otros saldos de arranque que deban cargarse (deudas con proveedores, expensas adeudadas, anticipos)?

## D. Casos concretos

14. ⭐ **Nota de crédito del proveedor.** Registra la baja del gasto y del IVA crédito (con la tasa de la factura original). Con criterio de caja, ¿se asienta **cuando se emite** o **recién cuando el proveedor devuelve la plata o la compensa** con un pago futuro? Hoy se asienta como entrada al banco el día de la nota, aunque el dinero no haya vuelto.
15. ⭐ **NC del proveedor repartida a los propietarios.** Cuando la factura ya se había repartido entre las unidades, la NC se acredita como **saldo a favor** de cada propietario. ¿Son dos asientos (la NC al proveedor y después el saldo a los propietarios)? ¿Cómo se contabiliza ese saldo a favor?
16. ⭐ **NC emitida al propietario** (sobre una factura del consorcio). Reduce el ingreso por expensas y, si el propietario ya había pagado, queda como saldo a favor. ¿Asiento y cuenta? Si el consorcio factura con IVA, ¿reduce el IVA débito? ¿Corresponde un libro de ventas?
17. **Expensas e intereses.** Con devengado, ¿el ingreso por expensas se reconoce al **liquidar** o al **cobrar**? ¿Y los intereses por mora?
18. **IVA y retenciones.** ¿El consorcio es contribuyente de IVA? ¿El IVA crédito de las compras se computa? ¿Hay retenciones de IVA o renta que deba registrar? Hoy se arma el libro de compras y no hay libro de ventas ni retenciones.
19. **Transferencias entre cuentas propias** (banco, caja, fondo de reserva). Hoy el sistema no las registra. ¿Debe haber asiento? ¿Qué cuentas se usan?
20. **Pagos de propietarios.** Un pago se asienta cuando está **aprobado**. Si se revierte después, se corrige con contraasiento. ¿Cómo debe tratarse un **pago adelantado** o un **pago de más** (saldo a favor)?

## Lo que necesito de vuelta
Una respuesta corta por número, y si aplica, un ejemplo del asiento (cuentas, Debe y Haber). Con eso se decide si se construye el **devengado** (expensas por cobrar, proveedores por pagar, notas de crédito) o se mantiene la caja.
