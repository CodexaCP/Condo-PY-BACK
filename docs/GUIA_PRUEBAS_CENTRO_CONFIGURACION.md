# Guía de pruebas — Centro de configuración del edificio

Pantallas nuevas del web (rama `feature/centro-configuracion` en BACK y WEB). Cada punto dice qué hacer y qué tiene que pasar.

## 0. Preparación

1. En BACK, aplicar las migraciones a la base local:
   ```bash
   dotnet ef database update --project Condo.Infrastructure --startup-project Condo.Api
   ```
   (agregan 6 tablas/columnas nuevas, sin tocar datos existentes).
2. Tener un edificio con **Finanzas habilitado y la configuración inicial completa** (fecha de arranque, una cuenta bancaria, plan de cuentas) y otro **sin Finanzas** para ver los estados «No disponible».
3. Entrar con el rol que indica cada prueba. Quién puede qué:

| Pantalla | SuperAdmin | Administrador | Operador | Encargado |
|---|---|---|---|---|
| Centro (ver) | sí | sí | sí | sí (menos secciones) |
| Editar mora, fondos, avisos, IVA, cierre, asignación de cuentas | sí | sí | no | no |
| Plan de cuentas (en Finanzas → Configuración) | sí | no | no | no |
| Proveedores (ver y editar) | sí | sí | sí | sí |
| Registrar pago de factura | sí | sí | sí | sí |
| Armar, cerrar y reabrir conciliación | sí | sí | no (solo ve) | no (solo ve) |
| Historial de cambios | sí | sí | no | no |
| Informe de asamblea | sí | sí | sí | sí |

## 1. Centro de configuración (`/building-config`, menú «Centro de configuración»)

- Elegí un edificio: aparece el indicador **«Listo para operar»** o **«Faltan pasos»** con la barra de progreso y una tarjeta por sección.
- Cada tarjeta muestra estado (Completa / Incompleta / Opcional / No disponible), motivos y un resumen. Los botones llevan a la ficha del edificio en la pestaña correcta (`?tab=`), a Finanzas o abren el editor dentro del Centro (`?section=`).
- Con el edificio **sin Finanzas**, las secciones que dependen de él figuran «No disponible» y sin botón.
- Al volver de una sección (← Volver al Centro) el resumen se recarga: lo que guardaste cambia de estado.
- Como **Encargado** solo aparecen 8 secciones; como Administrador/Operador, 13.

## 2. Política de mora (sección «Política de mora»)

1. Cargá tasa 2 %, «Se suma cada: Semana», gracia 5 días, tope 20 %, mínimo 10.000, desmarcá «Cargos individuales» → Guardar y confirmar. Mensaje «Política de mora guardada» y la sección pasa a Completa.
2. Dejá la tasa vacía y guardá: queda confirmada como «no cobra mora» (frecuencia deshabilitada).
3. Tasa 5 sin frecuencia → aviso «Elegí cada cuánto se suma».
4. Como Operador: los campos están deshabilitados y no hay botón Guardar.
5. Cambiá la tasa a un valor distinto: los propietarios del edificio reciben el aviso de cambio de mora (ya existía) y queda una fila en el historial.
6. **Exoneración por unidad**: Unidades → Editar una unidad → sección «Mora» (solo SuperAdmin y Administrador): marcá «Exonerar», sin motivo da error; con motivo guarda y en el listado la unidad muestra la etiqueta «Sin mora». El contador «Unidades exoneradas» de la política sube.

## 3. Fondos

- Porcentajes 0–100; uso del fondo «Requiere aprobación de la asamblea» muestra «Aprobación desde (Gs.)». Guardar confirma la política (con ambos porcentajes vacíos significa «sin aportes a fondos»).

## 4. Avisos automáticos (sección «Documentos y comunicación»)

- Cinco avisos con interruptor. «Aviso antes del vencimiento» pide días de anticipación (1–30). Por defecto están activos solo «pago recibido» y «período publicado» (se ve «por defecto»).
- Guardar y comprobar que, con «Aviso el día del vencimiento» activo, quien no pagó recibe la notificación ese día (aparece con el ícono nuevo de calendario).

## 5. Impuestos (IVA de las compras)

- Con régimen **General** (se cambia en ficha → Facturación) todas las cuentas de egresos deben clasificarse: contador «x de y».
- «Mostrar solo las sin definir», elegir una tasa y «Aplicar a las visibles»; Guardar N cambios. Las filas modificadas se resaltan hasta guardar.
- Con régimen Resimple/Exento el aviso dice que no es obligatorio.

## 6. Proveedores (sección «Proveedores»)

1. Nuevo proveedor: nombre y RUC `80012345-6` → aparece en la lista. RUC inválido o repetido → mensaje del servidor. Nombre repetido → conflicto.
2. Plazo de pago 30 días, correo inválido → error.
3. Editar y desactivar: queda «Desactivado» y atenuado. Un proveedor con gastos no se elimina (no hay botón de borrar).
4. Búsqueda por nombre o RUC (espera medio segundo) y filtro de estado.

## 7. Gastos con factura (Gastos y cargos)

1. Nuevo gasto: el selector «Proveedor» lista los de la empresa. Al elegir uno se toma su nombre y, si el edificio tiene Finanzas y el proveedor tiene plazo, **propone el vencimiento** = fecha + plazo.
2. Cargá N° de factura, timbrado, vencimiento e IVA (10 / 5 / Exento / Según el rubro). Guardar: en la lista aparecen las etiquetas «A pagar · vence …», «Factura …», «IVA 10 % · Gs …».
3. Editar un gasto ya **pagado**: el pago se conserva (no se pierde al guardar).
4. Sin vencimiento: el gasto cuenta en la caja en su fecha, como siempre.

## 8. Cuentas por pagar (Finanzas → Cuentas por pagar)

- Resumen: A pagar / Vencido / Vence en 7 días y antigüedad de lo vencido.
- «Registrar pago»: fecha (no futura ni anterior a la factura) y cuenta (por defecto la del edificio) → Confirmar. La factura pasa a «Pagada» y **entra a la caja en la fecha del pago**: comprobarlo en Movimientos y Saldos.
- «Deshacer pago» vuelve a «A pagar» y sale de la caja.
- Filtros por estado y por vencimiento; paginación a partir de 25 filas.

## 9. Libro de compras (Finanzas → Libro de compras)

- Resumen por tasa (10 %, 5 %, Exento) y comprobantes del rango (por defecto el mes en curso). Una nota de crédito de proveedor figura en negativo con etiqueta «NC».
- Si hay gastos sin tasa aparece el aviso con enlace a Impuestos.
- «Exportar a Excel» descarga `finanzas-libro-de-compras-…xlsx`.

## 10. Cierre de período (sección «Período y cierre»)

1. «Activar cierre»: confirma y queda «Cierre activo». Los meses terminados muestran «Cerrar mes» solo en orden (el más antiguo abierto primero); el mes en curso no se puede cerrar.
2. Cerrar un mes. Después, en ese mes: crear/editar/borrar un gasto, ingreso, pago o nota de crédito del proveedor, o cambiar el presupuesto → **error «mes cerrado»** con el mensaje del servidor (409).
3. Reabrir con motivo (obligatorio) → el mes vuelve a «Abierto» y el historial de cierres muestra quién, cuándo y por qué.
4. «Apagar cierre»: nada queda protegido hasta volver a activarlo.

## 11. Conciliación bancaria (Finanzas → Conciliación bancaria)

1. Una cuenta bancaria → «Nueva conciliación»: fecha de corte y saldo del extracto → se abre la pantalla de trabajo.
2. Tildar movimientos pendientes que están en el extracto → «Marcar N como conciliados». La **Diferencia** baja; en cero se pone verde y se habilita «Cerrar conciliación».
3. Un depósito que el banco todavía no acreditó queda pendiente (se ve en «Entradas sin marcar»).
4. Cerrar → queda de solo lectura y aparece en «Conciliaciones cerradas». La siguiente conciliación de la cuenta arranca desde lo ya conciliado (el corte tiene que ser posterior).
5. Reabrir la última cerrada con motivo. Reabrir una anterior da error.
6. Cambiar el importe de un gasto ya conciliado: en la conciliación abierta aparece «Cambió de importe» y la diferencia lo refleja; borrarlo marca «Ya no figura en el libro» y **impide cerrar** hasta desmarcarlo.
7. Con el mes cerrado: marcar se puede; desmarcar o descartar da error de mes cerrado.
8. «Descartar» deshace las marcas y permite empezar otra. Como Operador/Encargado: solo se ve, sin botones.

## 12. Asientos contables (Finanzas → Asientos contables)

1. Pestaña **Cuentas del plan**: cada cuenta financiera y el IVA crédito sin asignar muestran «Usar 1.1.03 …» (sugerida). «Usar todas las sugeridas» y Guardar. Se aceptan cuentas inactivas (el IVA crédito fiscal viene sin activar). Un punto naranja en la pestaña avisa si faltan.
2. Pestaña **Asientos sugeridos**: un asiento por movimiento (cobro: banco al debe / ingreso al haber; gasto con IVA: rubro + IVA crédito al debe, banco al haber; NC de proveedor al revés). Debe y Haber cuadran siempre.
3. Sin asignar las cuentas, los asientos salen «Incompleto» con aviso y las cuentas faltantes en rojo.
4. Las «Sumas por cuenta» del banco coinciden con el movimiento del libro en el rango.
5. «Exportar a Excel» trae las hojas Asientos y Sumas por cuenta. El **paquete del contador** (Finanzas → Tablero → Exportar) ahora incluye además Asientos, Sumas por cuenta y Libro de compras.

## 13. Informe de asamblea (Finanzas → Informe de asamblea)

1. «Ver informe» sin fechas: ejercicio en curso hasta hoy. Comprobar que **saldos, ingresos y egresos** coinciden con Saldos y Movimientos del mismo rango.
2. Notas del administrador (hasta 4.000 caracteres) → aparecen al final del informe y del PDF.
3. «Descargar PDF»: debe traer resumen, saldos, estado de resultados, ejecución presupuestaria (si hay presupuesto), fondo (si hay), morosidad y cuentas por pagar.
4. La morosidad **no muestra ninguna unidad ni propietario**; es la situación a hoy.
5. Hasta futura, desde posterior a hasta, o más de 400 días → mensaje de error.

## 14. Umbral del semáforo del presupuesto

- Presupuesto y Presupuesto vs. real muestran «Umbral del semáforo». Cambiarlo (1–100, Administrador/SuperAdmin) y guardar: el semáforo del presupuesto vs. real se recalcula con el nuevo límite.

## 15. Historial de cambios (Centro → «Ver historial de cambios», solo SuperAdmin y Administrador)

- Debe registrar cada cambio de las pruebas anteriores (mora, fondos, avisos, IVA, cierre, conciliación, asientos, ficha) con usuario, rol, fecha y el detalle «antes / después». Filtros por sección y por fechas; paginado de 20.

## 16. Casos transversales

- **Edificio ajeno**: abrir `/building-config?buildingId=<id de otra empresa>` no muestra datos (404).
- **Finanzas apagado** en un edificio: Cuentas por pagar, Conciliación, Asientos, Libro de compras e Informe muestran el mensaje de «módulo no disponible».
- **Configuración inicial incompleta** (sin fecha de arranque): las pantallas de Finanzas muestran el aviso con el enlace a Configuración.

## 17. Lo que NO está (para no buscarlo)

Retenciones de IVA/renta, contabilidad devengada (los asientos son por caja), importar el extracto bancario, pagos online, ocupación y alquileres (fase 8) y los ajustes de la aplicación móvil (se hacen después).
