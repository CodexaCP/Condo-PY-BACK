# Guía de pruebas — Cargo individual como crédito a recuperar

**Qué cambió.** Un gasto con distribución «Unidad individual» (el edificio le paga al proveedor, se lo cobra a una sola unidad) ya no es un egreso ni su cobro un ingreso. Es un crédito a recuperar en la cuenta **1.3.06 «Cargos particulares a recuperar»**:

| Momento | Antes | Ahora |
|---|---|---|
| Se paga al proveedor | Egreso en el rubro del gasto | Debe 1.3.06 / Haber banco (o caja) |
| La unidad paga el cargo | Ingreso en 4.1.04 | Debe banco / Haber 1.3.06 |
| Resultado, flujo, presupuesto | Subían los dos | No cambian |

4.1.04 «Cargos particulares a unidades» queda solo para los **ajustes** (compensaciones, créditos). El gasto de una sola unidad no cuenta más en el presupuesto contra real.

No hay migración SQL. Lo único que se despliega es el backend.

## Preparación
- Edificio con Finanzas activo, cuenta bancaria con plan de cuentas asignado (Configuración → Asientos contables) y plan genérico (existe 1.3.06).
- Una unidad cualquiera, por ejemplo la 5B.

## 1. Gasto individual pagado
1. En el período en borrador cargá un gasto de 500.000 con distribución **Unidad individual**, unidad 5B, un rubro de egresos cualquiera (por ejemplo 5.04.02) y sin vencimiento (o con vencimiento y registrá el pago).
2. Finanzas → Movimientos: sale 500.000 de la cuenta con el rubro **«Cargos particulares a recuperar»** y la glosa «… (cargo a la unidad 5B)».
- [ ] El flujo de caja y el tablero **no** muestran el egreso de 500.000 en el rubro 5.04.02.
- [ ] El saldo del banco baja 500.000.
- [ ] Presupuesto vs. real: 5.04.02 no suma esos 500.000.

## 2. Asiento
1. Finanzas → Asientos contables (o Libro diario → Vista previa), fecha del pago.
- [ ] Debe **1.3.06 Cargos particulares a recuperar** 500.000 / Haber el banco 500.000, sin renglón de IVA crédito aunque el gasto tenga IVA.
- [ ] Si el plan no tiene 1.3.06 (plan importado) el asiento sale incompleto con «Falta la cuenta del plan: Cargos particulares a recuperar». Se resuelve creando una cuenta de activo final llamada «… a recuperar».

## 3. Cobro de la unidad
1. Aprobá la liquidación, publicá y registrá el pago de 5B por el comprobante.
- [ ] En Movimientos entra a la cuenta una línea «Recupero de cargos individuales» con rubro «Cargos particulares a recuperar».
- [ ] Asiento: Debe banco / Haber **1.3.06**; en el mismo asiento puede haber líneas de 4.1.01 por la expensa ordinaria.
- [ ] El flujo y el estado de resultados **no** muestran 4.1.04 por ese cobro.
- [ ] El saldo de 1.3.06 en el Mayor llega a cero cuando se pagó el cargo completo.

## 4. Cargo impago
- [ ] Con el cargo sin pagar, el Mayor de 1.3.06 muestra 500.000 de saldo deudor (es lo que 5B le debe al edificio). El estado de resultados no muestra pérdida.

## 5. Lo que no debe cambiar
- [ ] Un gasto común (por coeficiente) sigue siendo egreso en su rubro y su cobro sigue siendo 4.1.01.
- [ ] Una mora o un ajuste cobrado sigue entrando en 4.2.01 / 4.1.04.
- [ ] El informe para asamblea muestra un aviso con lo adelantado y lo devuelto por cargos individuales, y los totales de ingresos y egresos no los incluyen.
- [ ] La conciliación bancaria sigue viendo los dos movimientos (son plata que entra y sale del banco).

## 6. Libro diario ya asentado
Los asientos de gastos individuales **ya asentados** antes de este cambio figuran como «Cambió»: el rubro pasó de gasto a 1.3.06. Al asentar, el sistema hace el contraasiento y el asiento nuevo (regla de rectificación del Libro diario). Es lo esperado.

## Pendientes conocidos (no se tocaron)
- Los reportes viejos de Estado de resultados, Libro de movimientos, Comparativo de edificios y el saldo de cierre del período siguen mostrando el gasto individual y su cobro en bruto.
- IVA: el gasto individual va completo a 1.3.06 en los asientos. El Libro de compras sigue declarando el crédito fiscal de la factura del proveedor. Cómo se refactura el IVA a la unidad se define con el contador.
- Si la liquidación se hace por devengado (decisión D4 pendiente), 1.3.06 pasaría a cargarse al emitir el cargo y no al pagar al proveedor.
