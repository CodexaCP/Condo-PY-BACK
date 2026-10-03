# Plan de cuentas de Finanzas del edificio — Guía de pruebas

Para probar a mano el **plan de cuentas genérico**, la **importación del plan del cliente** y los **varios niveles**. Complementa a
`ESPECIFICACION_MODULO_FINANZAS.md` (sección 15.10). Marcá cada casilla `[ ]` a medida que lo verificás.

Lo que ya está comprobado por pruebas automáticas (689 en total, 0 errores; las de plantilla, validador, servicio, controlador, copiador, libro y
Excel están en `Condo.Tests/Finance`): las reglas de abajo. Esta guía es para que veas **la pantalla** y confirmes que se entiende y se siente bien.

---

## 1. Qué cambió (en simple)

| Antes | Ahora |
|---|---|
| Plantilla corta: ~28 subrubros en 2 niveles | **Tu plan genérico completo**: 5 clases (Activo, Pasivo, Patrimonio / Fondos, Ingresos, Egresos), **30 grupos** y **162 cuentas finales** (197 nodos), hasta 6 niveles |
| Todo el plan nacía activo | Solo **54 cuentas recomendadas** nacen activas (81 nodos activos con sus grupos y clases). El resto está inactivo y lo activás con el tilde **Usar** |
| Solo gastos e ingresos | Además hay **Activo, Pasivo y Patrimonio**, pero son **de referencia**: no reciben gastos ni ingresos, se exportan al contador |
| Plan fijo por edificio | El SuperAdmin puede **reemplazar el plan por el del cliente (Excel)**, volver a aplicar el genérico, copiar de otro edificio o editar a mano |
| «Plantilla» = no se puede borrar | Ahora lo que no se borra ni se mueve son las cuentas con **función especial** (cobranza de expensas y «cuenta por defecto» de cada categoría) |

**Para qué sirve cada cuenta de egresos/ingresos:** al cargar un gasto o ingreso se elige una cuenta activa. Esa cuenta decide **dos cosas**: en qué línea del plan/flujo/presupuesto cuenta, y
**en qué categoría de la liquidación** cuenta (por ejemplo «Electricidad» → ANDE). La liquidación no cambia: sigue agrupando por esas categorías.

**Funciones especiales** (campo «Función en el sistema», opcional, **una cuenta por función**):
- *Cobranza de…* (5 funciones): la cuenta que recibe lo que **pagan los propietarios** (ordinarias, extraordinarias, cargos individuales y ajustes, mora, aportes al fondo). No recibe gastos/ingresos cargados a mano.
- *Cuenta por defecto de…* (una por categoría): recibe los gastos/ingresos que se cargan **sin elegir cuenta**.
- Nada es obligatorio: si falta, los reportes muestran la línea igual (ver caso 7).

---

## 2. Antes de probar

### 2.1 Despliegue (no hay migración de base de datos)
El tipo de cuenta se guarda como texto y no se agregaron columnas: **no hay script SQL**.
1. Hacer `git push` de BACK y WEB desde tu máquina y desplegar como siempre (backend en el VPS con los comandos de `vps-deploy`; web con tu publicación habitual).
2. Los edificios que **ya tenían plan** siguen con el plan viejo funcionando. Para pasarlos al nuevo: *Finanzas → Configuración → Plan de cuentas → Plan genérico → Reemplazar* (caso 3). Los edificios que se habiliten de ahora en adelante nacen con el plan genérico nuevo.

### 2.2 Qué necesitás
- Un **edificio de prueba** con Finanzas habilitado y configuración inicial completa (fecha de arranque + una cuenta bancaria). Si usás uno con movimientos, mejor: se ve qué pasa con ellos.
- Usuarios: **SuperAdmin** (hace todo) y un **Administrador de empresa** (debe ver todo en solo lectura).
- Los dos Excel de ejemplo de esta carpeta: `docs/ejemplos/plan-cuentas-cliente-ejemplo.xlsx` y `docs/ejemplos/plan-cuentas-cliente-con-errores.xlsx`.
- Dónde está: menú **Finanzas → Configuración**, paso **Plan de cuentas**. (Habilitar el módulo: **Finanzas por edificio**.)

---

## 3. Plan genérico en un edificio nuevo

**Habilitar el módulo en un edificio que nunca lo tuvo** (SuperAdmin → Finanzas por edificio → Habilitar). Después abrir *Configuración → Plan de cuentas*.

- [ ] Aparece el árbol con 5 clases arriba: **1 ACTIVO, 2 PASIVO, 3 PATRIMONIO / FONDOS, 4 INGRESOS, 5 EGRESOS**, con sus grupos y cuentas (por ejemplo 5 → 5.02 Servicios públicos → 5.02.01 Electricidad (ANDE)).
- [ ] Arriba a la derecha de la lista dice **«81 de 197 activas»**.
- [ ] Las cuentas **recomendadas** están con el tilde *Usar* (por ejemplo 5.02.01, 5.04.01, 4.1.01, 4.3.01); las no recomendadas (por ejemplo 5.03.07 Portones automáticos, 1.2.01 Plazo fijo) están **atenuadas** y sin tilde.
- [ ] 5.02.01 tiene una etiqueta azul **«Cuenta por defecto de gastos: ANDE»** y debajo dice «En la liquidación: ANDE».
- [ ] 4.1.01 «Expensas ordinarias» tiene la etiqueta **«Cobranza de expensas ordinarias»**. Lo mismo 4.1.02 (extraordinarias), 4.1.03 (aportes al fondo), 4.1.04 (cargos particulares) y 4.2.01 (mora).
- [ ] Las cuentas de la clase 1, 2 y 3 dicen «De referencia: no recibe gastos ni ingresos».
- [ ] **No** aparece el aviso amarillo «Faltan cuentas de cobranza» (las 5 están asignadas).
- [ ] Los botones **Expandir todo / Colapsar todo** abren y cierran el árbol; la flecha de cada grupo lo abre y cierra.
- [ ] La **búsqueda** funciona: escribir «ande» deja solo 5 → 5.02 → 5.02.01 (con sus grupos); escribir «5.04» muestra ese grupo y sus cuentas. Borrar el texto vuelve al árbol.
- [ ] El tilde **Solo activas** oculta las cuentas y los grupos inactivos.

## 4. Elegir qué cuentas usa el edificio

- [ ] Tildar *Usar* en **5.03.07 Portones automáticos**: se activa, y su grupo **5.03** ya estaba activo. El contador sube a **82 de 197**.
- [ ] Destildar *Usar* en esa misma cuenta: vuelve a 81.
- [ ] Destildar el **grupo 5.02 «Servicios públicos»**: **todas** sus cuentas quedan inactivas (5.02.01 a 5.02.05). El contador baja.
- [ ] Tildar *Usar* solo en **5.02.03 Gas**: se activa la cuenta **y también su grupo 5.02 y la clase 5** (una cuenta activa necesita sus grupos activos). 5.02.01 sigue inactiva.
- [ ] Al cargar un gasto en un período en borrador (pantalla de gastos del edificio), el selector de rubro **solo ofrece cuentas activas de egresos**, ordenadas por código y agrupadas por su grupo. No aparecen grupos ni cuentas de activo/pasivo/patrimonio ni las de cobranza.
- [ ] En *Ingresos*, el selector ofrece 4.2.02, 4.2.03, 4.3.x activas… y **no** 4.1.01 a 4.1.04 ni 4.2.01.

## 5. Editar una cuenta (varios niveles, función, categoría)

1. **Subcuenta en un grupo:** en el grupo 5.04 apretar **+** → nueva cuenta «Tablero eléctrico», código `5.04.12`. Debe heredar el tipo *Egresos*; elegir categoría de liquidación «Mantenimiento».
   - [ ] Se crea y aparece en 5.04, con «En la liquidación: Mantenimiento».
2. **Un nivel más:** en esa cuenta nueva apretar **+** → «Medidor trifásico», código `5.04.12.1`.
   - [ ] Se crea. Ahora `5.04.12` es un **grupo** (sin categoría propia) y la cuenta final es `5.04.12.1`.
3. **No se puede convertir en grupo una cuenta con movimientos / función:** intentar agregar una subcuenta a **5.02.01** (tiene función).
   - [ ] Error: «El grupo elegido es una cuenta con función especial: no puede tener subcuentas.»
   - Cargar un gasto en 5.04.01 y luego intentar agregarle una subcuenta → [ ] Error: «…ya tiene gastos o ingresos cargados: no puede pasar a ser un grupo.»
4. **Límite de niveles:** crear una cadena hasta el nivel 6 y probar el 7.
   - [ ] Error: «El plan admite hasta 6 niveles.»
5. **Código repetido:** crear otra cuenta con código `5.04.12`.
   - [ ] Error: «Ya existe una cuenta con ese código en este edificio.»
6. **Mover un grupo dentro de su propio hijo:** editar `5.04.12` y elegir como grupo a `5.04.12.1`.
   - [ ] En la lista de grupos **no** aparece `5.04.12.1` ni lo que cuelga de ella; si se fuerza por la API, devuelve «No se puede mover un grupo dentro de uno de sus propios subgrupos.»
7. **Eliminar:** eliminar `5.04.12.1` (sin movimientos): [ ] se elimina. El botón de eliminar está **desactivado** en grupos con subcuentas, en cuentas con función y en cuentas con movimientos (el hint explica por qué).

## 6. Funciones especiales

1. **Pasar «Cuenta por defecto de gastos: ANDE» a otra cuenta:** editar **5.04.01 Electricidad**, en *Función en el sistema* elegir «Cuenta por defecto de gastos: ANDE», guardar.
   - [ ] 5.04.01 ahora tiene la etiqueta y su categoría de liquidación pasa a **ANDE** (el selector de categoría está bloqueado: «Lo fija la función elegida»).
   - [ ] **5.02.01 perdió la función** pero **conserva «En la liquidación: ANDE»** (no cambia la categoría de lo ya cargado ahí).
   - **Dejalo como estaba:** editar **5.02.01** y volver a asignarle «Cuenta por defecto de gastos: ANDE». Ahora la que pierde la función es 5.04.01, y **conserva la categoría ANDE** (así está pensado: quitar una función no cambia la categoría). Para devolverla a «Mantenimiento» editá 5.04.01 y elegí esa categoría (ahora el selector está habilitado).
2. **Cobranza no se puede quitar:** editar 4.1.01 → el selector de función está **bloqueado** con la nota «Las cuentas de cobranza no se pueden dejar sin función: para cambiarla, asignala a otra cuenta.».
3. **Mover una cobranza a otra cuenta:** crear una cuenta de ingresos nueva en el grupo 4.1 (por ejemplo `4.1.05` «Cuotas sociales») y asignarle «Cobranza de expensas ordinarias».
   - [ ] La función pasa a `4.1.05` y 4.1.01 queda como cuenta de ingresos común (ya sin etiqueta, categoría «Otro»). Para dejar todo como estaba: editar 4.1.01, asignarle de nuevo «Cobranza de expensas ordinarias» y eliminar 4.1.05.
4. **Tipo incorrecto:** la lista de funciones de una cuenta de **Egresos** solo ofrece «Cuenta por defecto de gastos: …»; la de **Ingresos** ofrece «Cobranza de…» y «Cuenta por defecto de ingresos: …». No hay forma de mezclarlas desde la pantalla.
5. **Cuenta con movimientos:** cargar un gasto en 5.04.01 (categoría Mantenimiento) y luego intentar asignarle la función «…ANDE» → [ ] Error: «solo se le puede asignar una función de su misma categoría de liquidación». (Con «Cuenta por defecto de gastos: Mantenimiento» sí se puede.)

## 7. Lo cargado sin elegir cuenta no se pierde

Con el plan genérico: cargar un gasto de **categoría ANDE sin elegir rubro** y otro de **Mantenimiento eligiendo 5.04.01**, ambos con fecha entre la fecha de arranque y hoy.
- [ ] En *Flujo de caja* (egresos) el gasto sin rubro aparece en **5.02.01 Electricidad (ANDE)**; el otro, en **5.04.01**.
- [ ] En *Movimientos*, cada uno muestra su cuenta; filtrar por la **clase 5 EGRESOS** muestra **los dos** (el filtro incluye todo lo que cuelga, a cualquier nivel); filtrar por el grupo **5.02** muestra solo el de ANDE.

Con un **plan sin esas funciones** (por ejemplo después de importar el plan de ejemplo, caso 9):
- [ ] El gasto sin rubro de categoría ANDE cae en la **primera cuenta final activa de esa categoría** (en el ejemplo, `5.1.1 Energía eléctrica ANDE`). No queda una línea suelta.
- [ ] Si el plan **no tiene ninguna cuenta** de una categoría (por ejemplo si desactivás todas las de ESSAP), esos gastos aparecen en el flujo como **«ESSAP (sin rubro)»** y en *Presupuesto vs. real* en un renglón **«Sin cuenta en el plan»** (en rojo si no tenía presupuesto): **los totales no quedan por debajo de lo gastado**.
- [ ] Si falta una cuenta de cobranza, el flujo muestra **«Cobro de expensas ordinarias»** (sin código) y aparece el aviso amarillo **«Faltan cuentas de cobranza»** en el editor.

## 8. Plan genérico: agregar lo que falta / reemplazar

Botón **Plan genérico**.

**A. Agregar lo que falta** (modo por defecto):
- [ ] En un edificio con el plan completo: resultado «0 cuentas creadas».
- [ ] Eliminar a mano una cuenta sin movimientos (por ejemplo 5.03.07), volver a *Agregar lo que falta*: «1 cuentas creadas» y la cuenta reaparece (inactiva), **sin tocar** nombres que hayas cambiado.

**B. Reemplazar todo el plan:**
- [ ] Al elegir *Reemplazar* se muestra el recuadro amarillo con **cuántas cuentas se reemplazan** y, si hay datos que dependen del plan, cuántos **gastos / ingresos / plantillas recurrentes quedan sin rubro** y cuántos **renglones de presupuesto se borran**.
- [ ] Si hay algo que perder, el botón **Reemplazar el plan** está **desactivado** hasta tildar «Entiendo que se pierde esto…».
- [ ] Probar con un edificio que tenga gastos con rubro y presupuesto cargado: tras reemplazar, los gastos **siguen existiendo** (en *Gastos del período* se ven igual, ahora con rubro vacío), el presupuesto quedó vacío, y el resultado verde de arriba dice los números exactos.
- [ ] La liquidación del período, los pagos y los saldos **no cambian**.
- [ ] Después de reemplazar: **81 de 197 activas** otra vez, plan nuevo y limpio.

## 9. Importar el plan del cliente (Excel)

Botón **Importar plan del cliente**. Hay un enlace **Descargar plantilla de ejemplo (.xlsx)**: debe bajar un Excel con una hoja *Plan de cuentas* (con unas filas de ejemplo), una hoja *Instrucciones* y una hoja *Categorías*.

### 9.1 Archivo sin errores: `plan-cuentas-cliente-ejemplo.xlsx`
Elegir el archivo y **Leer archivo**. Debe mostrar:
- [ ] **33 filas · 19 cuentas finales · 14 grupos**, sin errores. Hay avisos (naranja) — son normales, se explican abajo.
- [ ] Las filas se ven indentadas por nivel (1.1.1.01 está en el nivel 4).
- [ ] Clases: **1 ACTIVO** (tipo Activo, venía en el archivo), **2 PASIVO** (aviso «Tipo deducido del primer número del código… Verificalo»), **3 PATRIMONIO NETO** (Patrimonio), **4 INGRESOS** (deducido, con aviso), **5 GASTOS** (Egresos). Las clases tienen un **selector de tipo editable**; el resto de las cuentas muestran el tipo de su clase.
- [ ] **4.1.1 Expensas ordinarias** y **4.1.2 Expensas extraordinarias** muestran «Cobranza de expensas ordinarias / de aportes extraordinarios» (función **sugerida por el nombre**, con aviso) y un enlace **quitar**.
- [ ] **4.2.1 Alquiler del salón de eventos**: «Alquiler de área común» (venía en el archivo, **sin** la marca «sugerida»). **4.2.2 Intereses ganados**: «Interés» (sugerida).
- [ ] Gastos con categoría **sugerida por el nombre**: 5.1.1 → ANDE, 5.1.2 → ESSAP, 5.1.3 → Internet y telefonía, 5.2.1 → Salarios, 5.2.2 → Salarios, 5.3.1 → Ascensor, 5.3.2 → Mantenimiento, 5.3.3 → Limpieza, 5.4.1 → Administración, 5.4.2 → Seguro.
- [ ] **5.4.3 «Cosas varias del consorcio»**: categoría **Otro** con el aviso «Sin coincidencia por el nombre…» y **sin tilde en Usar** (la fila traía Activo = No).
- [ ] Cambiar la categoría de una cuenta con el selector (por ejemplo 5.3.2 → «Seguro») borra la marca «sugerida».
- [ ] El tilde **Ver solo filas con errores o avisos** deja solo esas filas.
- [ ] Cambiar el tipo de la clase 5 a «Ingresos» cambia el tipo de **todas** sus cuentas (y reinicia sus categorías); volver a «Egresos» (revisá que las categorías queden razonables; si no, volvé a elegir el archivo).
- [ ] Elegir **Reemplazar el plan del edificio**: muestra cuánto se pierde; pide tildar la confirmación si el edificio tiene gastos/presupuesto. El botón **Importar plan** se habilita.
- [ ] Importar: aparece el resultado verde «Plan aplicado: 33 cuentas creadas…». El árbol ahora es **el del cliente**, con sus códigos (`1.1.1.01`, `5.1.1`…), sus códigos del contador (por ejemplo `5110101` en 5.1.1) y las categorías que dejaste.
- [ ] En *Gastos*, el selector de rubro ofrece las cuentas del cliente (solo las activas de egresos).
- [ ] *Exportar a Excel* del plan: columna **Grupo** con la ruta completa («5 · GASTOS › 5.1 · Servicios»), **Clase**, **Función**, **Código del contador** y **Nivel**.

### 9.2 Archivo con errores: `plan-cuentas-cliente-con-errores.xlsx`
- [ ] Leer archivo: aparece el cartel rojo «El archivo tiene errores: corregilos en el Excel y volvé a cargarlo». **No** se muestra la elección de modo y el botón **Importar plan** está desactivado.
- [ ] Errores por fila (los números de fila son los del Excel): **5** «El código 5.1.1 está repetido (ya figura en la fila 4)»; **6** «Falta el nombre»; **7** «El grupo «ZZZ» no existe en el archivo»; **8** «Tipo desconocido: «Cosa»…»; **9** «No se pudo deducir el tipo…»; **10** «Esta cuenta de ingresos o egresos no tiene grupo…».
- [ ] **Elegir otro archivo** vuelve al paso 1 sin haber guardado nada (el plan del edificio sigue igual).

### 9.3 Otros chequeos de la importación
- [ ] Un archivo que **no es .xlsx** (por ejemplo un .csv renombrado o un PDF) → error «Solo se permiten archivos Excel (.xlsx)» / «no es un Excel válido».
- [ ] Un Excel **vacío** o sin las columnas Código y Nombre → error claro («No se encontraron las columnas obligatorias…»).
- [ ] Modo **Agregar y actualizar por código**: importar el mismo archivo sobre un plan que ya tiene algunas cuentas con esos códigos → no borra nada; actualiza nombre, código del contador, estado y categoría de las que coinciden (la categoría **no** cambia si la cuenta ya tiene gastos cargados) y suma las nuevas.
- [ ] Un usuario **que no es SuperAdmin** no ve los botones Plan genérico / Importar / Copiar / Nueva cuenta, ni los tildes *Usar* (ve «Activa/Inactiva»).

## 10. Copiar de otro edificio
- [ ] Copiar el plan de un edificio con plan del cliente a uno vacío o con el genérico: se crean/actualizan las cuentas en **todos los niveles**, con sus funciones, códigos del contador y estado. Copiar dos veces seguidas: la segunda dice **0 creadas, 0 actualizadas**.
- [ ] Si el código ya lo usa otra cuenta de otro tipo en el destino, esa cuenta se informa como «sin copiar» (con el motivo) y las demás siguen.

## 11. Reportes con el plan nuevo
Con movimientos en varias cuentas:
- [ ] **Presupuesto**: la grilla lista **solo cuentas finales de ingresos y egresos activas** (o con presupuesto cargado), agrupadas por su grupo. Cargar un importe en una cuenta y guardar funciona igual que antes.
- [ ] **Presupuesto vs. real**: el real de cada cuenta coincide con lo cargado en ella; los gastos sin rubro cuentan en la cuenta por defecto de su categoría (caso 7).
- [ ] **Flujo de caja**: cada cuenta con movimientos tiene su línea, con el código y el grupo.
- [ ] **Movimientos**: filtrar por una **clase**, un **grupo** o una **cuenta** incluye todo lo que cuelga de ella.
- [ ] **Paquete para el contador** (Excel): la hoja *Plan de cuentas* trae las 197 cuentas (o las del cliente) con su ruta, clase, función, estado y nivel; el resto de las hojas usa los códigos del plan.

## 12. Permisos (con el Administrador de empresa)
- [ ] Ve el árbol completo y puede **buscar, expandir y exportar**, pero **no** ve botones de edición ni los tildes *Usar*.
- [ ] Si intenta cualquier cambio del plan (por ejemplo con una herramienta de API), recibe 403 «La configuración financiera del edificio… la realiza CondoPY».
- [ ] Un usuario de otro edificio/empresa **no** ve el plan de este edificio.

---

### Si algo no coincide
Anotá: **caso, qué viste y qué esperabas** (y, si es un error de pantalla, el texto exacto). Los textos entre «comillas» de esta guía son los reales del sistema.
