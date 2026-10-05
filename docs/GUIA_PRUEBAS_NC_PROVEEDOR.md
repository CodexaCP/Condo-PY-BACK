# Guía de pruebas — Nota de crédito del proveedor (fases 1 a 4)

Para probar a mano en el web (y un poco en la app). Especificación y decisiones: `docs/ESPECIFICACION_NC_PROVEEDOR.md`.

## 0. Antes de empezar

1. **Migraciones aplicadas, en este orden** (los `.sql` están en `Condo.Infrastructure/Persistence/Migrations/`):
   1. `20261005114334_BuildingExpenseCreditNotes`
   2. `20261005115326_BuildingExpenseCreditNoteUniqueness`
   3. `20261005121441_BuildingExpenseCreditNoteAllocations`
   4. `20261005123940_OwnerCreditMovementOnHold`
2. Backend y web desplegados con esta versión. APK nuevo si vas a ver el aviso en el celular.
3. **Datos de prueba** (un edificio de prueba, no uno real):
   - 2 unidades **A** y **B** con coeficientes que sumen 1 (por ejemplo A = 0,6 y B = 0,4).
   - Cada unidad con **propietario principal** distinto: **Ana** (A) y **Beto** (B). Cada uno con su usuario en la app.
   - Un período en **borrador** con un gasto **₲ 1.000.000** (proveedor "Ferretería López", distribución por coeficiente).
   - Tener a mano un PDF o foto cualquiera para adjuntar como "documento del proveedor".
4. Dónde está el formulario: **Gastos y cargos › Gastos**, botón con ícono de menos ("Notas de crédito del proveedor") en la fila del gasto.

Marca ✅ / ❌ en cada caso. Si algo falla, anota el mensaje que sale.

---

## 1. Fase 1 — período en borrador

| # | Qué hacer | Resultado esperado |
|---|---|---|
| 1.1 | Abrir el formulario del gasto. | Resumen: facturado ₲ 1.000.000, NC aplicadas ₲ 0, monto que se reparte ₲ 1.000.000. Sin notas. |
| 1.2 | Registrar NC: número `001-001-0000123`, timbrado `12345678`, fecha de hoy, monto **200.000**, motivo, **con adjunto**. | Mensaje de éxito. El gasto queda en **₲ 800.000**; en la fila se ve `₲ 1.000.000 − NC ₲ 200.000`. La nota aparece en la lista con enlace al documento. |
| 1.3 | Intentar registrar **sin adjunto**. | El botón "Registrar" no se habilita. |
| 1.4 | Registrar la **misma NC otra vez** (mismo número). | Error: "Esta nota de crédito ya está registrada… No se puede registrar dos veces", con dónde está. |
| 1.5 | Registrar el mismo número escrito distinto: `0010010000123` o `001 001 0000123`. | Mismo error de duplicada. |
| 1.6 | Mismo número con **otro proveedor** (otro gasto con otro proveedor). | Se permite. |
| 1.7 | Mismo número y **otro timbrado** (`99999999`). | Se permite (otra nota). Con el timbrado vacío → duplicada. |
| 1.8 | Monto igual o mayor al gasto (≥ ₲ 800.000 pendiente). | Error: no puede igualar ni superar el monto pendiente. |
| 1.9 | Calcular la liquidación del período. | Los cargos de A y B suman **₲ 800.000** (A 480.000, B 320.000), no 1.000.000. |
| 1.10 | Si la liquidación ya estaba calculada al registrar la NC. | Se muestra el aviso "volvé a calcularla". |
| 1.11 | **Anular** la NC con motivo. | El gasto vuelve a ₲ 1.000.000. La nota queda "Anulada". |
| 1.12 | Volver a registrar la **misma NC** anulada. | Se permite. |
| 1.13 | Con una NC aplicada, **editar el monto** del gasto o intentar **eliminarlo**. | Error: tiene notas de crédito aplicadas; anulalas primero. Editar otro campo (descripción) sí se puede. |
| 1.14 | Clonar el período con un gasto que tiene NC. | El gasto clonado trae el monto **original** (₲ 1.000.000), no el neto. |
| 1.15 | Fecha de la NC en el futuro (+30 días). | Error de fecha no válida. |

## 2. Período cerrado

| # | Qué hacer | Resultado esperado |
|---|---|---|
| 2.1 | Aprobar la liquidación (el período queda **Cerrado**) y abrir el formulario. | No hay formulario; aviso: período cerrado, anulá la liquidación primero. |
| 2.2 | Anular la liquidación (vuelve a borrador) y registrar la NC. | Funciona como en la fase 1. |

## 3. Fase 2 — período publicado (saldo a favor)

Preparar: gasto de ₲ 1.000.000 repartido (A = 600.000, B = 400.000), liquidación aprobada por el Encargado y el presidente y **publicada**.

| # | Qué hacer | Resultado esperado |
|---|---|---|
| 3.1 | Abrir el formulario del gasto. | Aviso azul: período publicado, no se tocan comprobantes, se acredita saldo a favor. |
| 3.2 | Completar la NC de **200.000** y pulsar **Ver reparto por unidad**. | Tabla: A cobrado 600.000 → saldo **120.000** (Ana); B cobrado 400.000 → saldo **80.000** (Beto). Total acreditado 200.000. No se guardó nada todavía. |
| 3.3 | Cambiar el monto. | La vista previa desaparece y hay que volver a verla. |
| 3.4 | **Confirmar y acreditar saldo a favor**. | Éxito: "Se acreditaron ₲ 200.000…". La nota queda con "Saldo a favor" y el detalle por unidad. El gasto sigue en ₲ 1.000.000. |
| 3.5 | Ver el saldo a favor de Ana y de Beto (pantalla de pagos / saldo del propietario). | Ana +120.000, Beto +80.000. En el historial del saldo: "nota de crédito … de Ferretería López … unidad A". |
| 3.6 | En la app de Ana. | Llega aviso: "Saldo a favor por ajuste de un gasto" (banner con la app abierta, push con la app cerrada). Al tocarlo abre el estado de cuenta. |
| 3.7 | Registrar una **segunda NC** de 100.000. | Ana +60.000 más (total 180.000), Beto +40.000 más (total 120.000): misma proporción 60/40. |
| 3.8 | Intentar acreditar más de lo que queda del gasto. | Error: supera lo que todavía se puede acreditar. Se puede acreditar el gasto **completo**, no más. |
| 3.9 | Quitar el propietario principal de **B** (sin deuda) y repetir una NC. | Error: "Asigná un propietario principal a las unidades B…". No se guarda nada (ni siquiera a A). |
| 3.10 | Gasto **pagado por el fondo de reserva** (periodo publicado) y una NC. | Se registra, mensaje de que no genera saldo a favor. Sin lotes nuevos. |
| 3.11 | **Consumo del saldo**: hacer que Ana envíe un pago de su próximo comprobante usando su saldo a favor. | El saldo se aplica **igual que siempre** (automático, completa el pago). Sin cambios de comportamiento. |
| 3.12 | **Anular** una NC publicada cuyo saldo **no se usó**. | Ana y Beto vuelven al saldo anterior; aviso "Se anuló un ajuste de un gasto". |
| 3.13 | Anular una NC cuyo saldo **ya se usó** en un pago. | Error: "ya se usó…". No cambia nada. |
| 3.14 | Intentar anular una NC que se registró en **borrador** y el período ya se publicó. | Error: ya se tuvo en cuenta en la liquidación. |
| 3.15 | Repetir 3.2–3.4 con una NC con un monto que no divide exacto (ej. ₲ 100.001 entre 3 unidades iguales). | La suma de las partes es **exactamente** el monto de la NC (el centavo va a una unidad). |

## 4. Fase 4 — cambio de propietario

Preparar: unidad **A** con Ana como propietaria principal y saldo a favor por NC (de la sección 3). Pantalla: **Asignaciones** (propietarios de la unidad).

| # | Qué hacer | Resultado esperado |
|---|---|---|
| 4.1 | Con **deuda pendiente** en A (un comprobante publicado sin pagar), pulsar quitar a Ana. | **No deja.** Mensaje con el monto y los períodos adeudados: "Liquidá la deuda antes de cambiar de propietario". |
| 4.2 | Pagar la deuda (aprobar el pago) y volver a quitar a Ana. | Pregunta de confirmación: el saldo a favor de la unidad (₲ X) queda **retenido** hasta asignar al nuevo propietario principal. |
| 4.3 | Cancelar la pregunta. | No se quita a Ana. |
| 4.4 | Confirmar. | Ana sale de la unidad. Mensaje: el saldo quedó retenido. **El saldo a favor de Ana baja** en el monto de la unidad. |
| 4.5 | Con A **sin propietario principal**, ver el saldo de Ana. | No incluye lo retenido. No se puede usar en ningún pago. |
| 4.6 | Intentar otra NC de periodo publicado sobre ese gasto. | Error: unidad A sin propietario principal. |
| 4.7 | Asignar a **Carlos** como propietario principal de A. | Mensaje: "Se le traspasaron ₲ X de saldo a favor que tenía la unidad". Carlos tiene ese saldo; Ana no. |
| 4.8 | App de Carlos. | Aviso "Saldo a favor de la unidad A" (se aplica en su próximo pago). |
| 4.9 | Carlos paga su próximo comprobante. | Usa el saldo traspasado como cualquier otro saldo. |
| 4.10 | Quitar a un propietario **no principal** (copropietario). | Se quita sin pedir nada; no toca deuda ni saldo. |
| 4.11 | Unidad con **dos principales**; quitar a uno. | No exige deuda; el saldo de la unidad pasa **directo** al principal que queda (mensaje y aviso). |
| 4.12 | Unidad con saldo retenido y se **anula la NC** que lo generó. | Se anula bien (el saldo retenido pasa a 0) sin tocar el saldo de nadie. |
| 4.13 | Saldos **sin unidad de origen** (excedentes de pagos o NC al propietario). | Se quedan con Ana (no se retienen ni se traspasan). |
| 4.14 | Marketplace: quitar al propietario principal con reservas abiertas. | Sigue generando la nota de cambio de propietario y suspendiendo publicaciones, como antes. |

## 5. Casos transversales

| # | Qué hacer | Resultado esperado |
|---|---|---|
| 5.1 | Usuario **sin acceso** al edificio intenta ver o registrar la NC. | Prohibido. |
| 5.2 | NC con número de 60 caracteres, motivo vacío, monto 0 o negativo. | Errores de validación; no se guarda nada. |
| 5.3 | Archivo no permitido (por ejemplo .exe) o de más de 10 MB. | Error de archivo; no se adjunta. |
| 5.4 | Texto largo: proveedor de 200 caracteres y descripción de gasto de 500. Registrar una NC en período publicado. | Se guarda sin error (los textos del saldo y del aviso se recortan). |
| 5.5 | Dos personas registran la **misma NC a la vez**. | Una la guarda; la otra recibe el aviso de duplicada. |
| 5.6 | Plan del edificio vencido (solo lectura). | Debería comportarse igual que los gastos (la restricción del plan es general y no la toqué); confirmar que no deja registrar ni anular. |
| 5.7 | Los **reportes** de un período en borrador con NC. | Ven el gasto neto. (Las NC de períodos publicados todavía **no** salen en libro y estado de resultados: fase 5.) |

---

## 6. Pruebas automáticas

```bash
dotnet test Condo.Tests/Condo.Tests.csproj
```

- Fases 1 y 2 tienen pruebas (`Condo.Tests/Finance/BuildingExpenseCreditNotes*Tests.cs`).
- **La fase 4 no tiene pruebas automáticas** (las dejaste para el final). Casos sugeridos para escribirlas:
  1. Quitar al único principal con deuda pendiente en un período publicado → 400 con monto y períodos; no se borra el vínculo.
  2. Quitar al único principal sin deuda → vínculo borrado; los lotes de la unidad quedan `OnHold`, el `OwnerCredit` baja en su suma y `EnsureLotsAsync` no los devuelve.
  3. Asignar un nuevo principal tras la baja → los lotes cambian de `OwnerId`, `OnHold = false`, el `OwnerCredit` del nuevo sube y la respuesta trae `TransferredCredit`.
  4. Quitar a uno de dos principales → sin chequeo de deuda y los lotes pasan directo al que queda.
  5. Quitar a un no principal → no toca nada.
  6. Lotes sin `UnitId` → no se retienen ni se traspasan.
  7. Vista previa (`removal-preview`): `CanRemove`, `PendingDebt`, `DebtPeriods`, `CreditToHold`/`CreditToTransfer`.
  8. Anular una NC cuyo lote está retenido, o cuyo propietario cambió → resta del saldo de quien lo tiene hoy (o nada si está retenido).
  9. Textos largos: referencia (100) y descripción (500) del lote y cuerpo (1000) del aviso se recortan sin romper el guardado.
  10. Aislamiento: usuario sin acceso al edificio → `Forbid` en la baja y en la vista previa.
