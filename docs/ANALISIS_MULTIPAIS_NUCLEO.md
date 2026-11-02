# Análisis multipaís del núcleo de Expenses (sin Finanzas)

Estado: **análisis (2026-11-02), sin código**. Complementa a [PLAN_MULTIPAIS_FISCAL.md](PLAN_MULTIPAIS_FISCAL.md) (país, moneda, impuesto y fiscalidad) y se apoya en [VALIDACIONES_MULTIPAIS_AR_VE.md](VALIDACIONES_MULTIPAIS_AR_VE.md).
Lo que se dice de Argentina y Venezuela es una hipótesis a validar, no un hecho.

## 1. Qué se pide
Que lo **básico de Expenses sin el módulo Finanzas** (administrar edificio, unidades, propietarios, liquidar expensas, cobrar, avisar a los morosos, votar, reservar, reclamar) funcione en varios países **estandarizado**: un solo producto con las diferencias de cada país como configuración y no como código repetido.

## 2. Idea central: núcleo universal + perfil de país + perillas por edificio

| Capa | Qué es | Quién la define |
|---|---|---|
| **Núcleo universal** | Las entidades y los flujos que existen en cualquier condominio del mundo | El producto |
| **Perfil de país** | Datos y vocabulario del país: glosario, tipos de documento, estructura de dirección, moneda, zona horaria, formatos de cuenta bancaria, opciones permitidas | Tú (SuperAdmin), en una tabla |
| **Perillas del edificio** | Política concreta de ese edificio: mora, fondo de reserva, vencimiento, aprobación de la liquidación | La administradora, con valores iniciales que propone el perfil de país |

Esto no es nuevo en el sistema: el **Centro de configuración** ya hace la tercera capa para la mora y el fondo de reserva. Lo que falta es la segunda capa (perfil de país) y completar las perillas que hoy tienen Paraguay incrustado.

Una **cuarta pieza** es el tablero de **capacidades** del plan multipaís (`EmitsInvoices`, `HasPurchaseTaxBook`, etc.): apaga o enciende módulos enteros por país.

## 3. Qué hay en el núcleo hoy y cómo se comporta fuera de Paraguay

Leyenda: **U** = universal, sin cambios; **P** = parametrizable (ya hay perilla o basta con agregar una); **E** = específico de país (necesita variante o paquete).

| Módulo | Entidades / archivos | Clase | Qué hay que tocar |
|---|---|---|---|
| Edificios y condominios | `Building`, `Condominium` | **P** | Ver 4.1: campos registrales y fiscales con nombre paraguayo |
| Unidades y coeficiente | `Unit.Coefficient` | **U/P** | Un solo coeficiente por unidad. Validar si hace falta reparto por grupos (ver 4.3) |
| Propietarios y residentes | `Resident`, `UnitOwner`, `PersonRules` | **P** | Tipos de documento por país (hoy RUC y cédula paraguaya); ver 4.2 |
| Períodos | `ExpensePeriod` | **P** | Un vencimiento; falta el segundo vencimiento opcional (4.4) |
| Gastos del edificio | `BuildingExpense` (sin la parte de IVA y rubros) | **U** | Con IVA y rubro entra al plan fiscal |
| Liquidación | `ExpenseSettlement`, `SettlementPdfDocument` | **P/E** | Flujo de aprobación fijo (4.5); contenido legal y vocabulario por país (4.7) |
| Cargos | `ExpenseCharge`: Ordinario, Reserva, Extraordinario, Individual, Ajuste | **U** | Los cinco tipos sirven en cualquier país |
| Mora | `LateFeeAccrualRunner`, `Building.LateFee*` | **P** | Falta la frecuencia mensual y el interés compuesto (4.4) |
| Pagos y comprobante | `OwnerPayment`, `Payment`, `PaymentAllocation`, `ReceiptPdfDocument` | **U/P** | Transferencia + comprobante ya es neutro; imputación y recibo a validar (4.6) |
| Estado de cuenta y morosidad | `AccountStatement*`, `MorosityController`, `CollectionsController` | **U** | Solo moneda y vocabulario |
| Facturación al cobrar | `Invoice`, `InvoiceSeries`, `CreditNote` | **E** | Ver el plan fiscal; se enciende por capacidad |
| Cuenta bancaria del edificio | `BuildingBankAccount` | **P** | Moneda y formato del número por país |
| Votaciones | `Vote`, `VoteCast` (hoy quórum % y peso por unidad o coeficiente) | **P** | Mayorías por tipo de decisión (4.8) |
| Amenities y reservas | `Amenity`, `AmenityReservation` | **U** | Solo vocabulario |
| Reclamos, comunicados, notificaciones | `Claim`, `Announcement`, `Notification` | **U** | Solo idioma y tono |
| Planes y cobro del servicio | `Plan.Price`, `BuildingPlan*` | **P** | Moneda del plan |
| Pagos online | `PaymentIntent`, `PaymentConnection` | **E** | Solo Paraguay; apagado en los demás (decisión de Tony) |
| Marketplace, Publicidad | `Marketplace*`, `AdCampaign*` | **U/P** | Opcionales por edificio; moneda y cobro a revisar |

**Conclusión:** casi todo el núcleo es **universal o parametrizable**. Lo específico de país se concentra en tres lugares: la **ficha del edificio**, la **liquidación** (contenido legal) y la **facturación**. Por eso conviene sacar el núcleo primero (ver sección 6).

## 4. Qué está escrito «a la paraguaya» y cómo estandarizarlo

### 4.1 Ficha del edificio (`Building.cs`)
Campos con nombre o significado paraguayo:

| Hoy | Problema | Estandarización propuesta |
|---|---|---|
| `Department` | «Departamento» es la división paraguaya | `Region1` con etiqueta del país («Provincia», «Estado») y, si hace falta, `Region2` («Partido», «Municipio») |
| `FincaNumber`, `PadronNumber`, `CadastralAccount`, `LegalEntityNumber` | Identificadores del registro paraguayo | Lista de **identificadores registrales** con etiqueta por país (pares clave / valor), con los de Paraguay como valor por defecto |
| `Ruc`, `TaxpayerType`, `VatRegime` (General, **Resimple**, Exempt) | Resimple es régimen paraguayo; «ante la SET» | Identificador fiscal genérico y condición fiscal tomada de una lista del país (ver plan fiscal) |
| `InvoicingMode` (Preimpresa / Autoimpresa / Electrónica) | Modos de Paraguay | Forma de numeración fiscal por país |
| `LateFeeMinAmount`, `ReserveUseThreshold` | Comentarios en «guaraníes»; el valor en sí es un importe de la moneda de la empresa | Mismo campo, con la moneda de la empresa |
| `ContactPhonePrefix`, `WhatsAppPhone` | Funcionan con prefijo; el valor por defecto es +595 | Valor por defecto tomado del país |
| `TimeZoneId` | Ya existe | Valor por defecto tomado del país (por ejemplo `America/Argentina/Buenos_Aires` o `America/Caracas`) |
| `DefaultDueDay`, `GraceDays` | Ya son perillas | Sin cambio; el perfil propone un valor inicial |

### 4.2 Personas (`PersonRules`, `Resident`, `UnitOwner`)
- `DocumentType` es texto libre en la base, pero las reglas de validación solo conocen `RUC` y `CedulaParaguaya`. Se pasa a una **tabla de tipos de documento por país** con nombre, formato y validador (DNI, CUIT, CUIL, pasaporte; cédula V/E, RIF, pasaporte).
- Los mensajes de validación están en voseo («usá el formato…»): van al diccionario de textos del país.
- Nacionalidad, inquilino y contrato de alquiler son universales.

### 4.3 Reparto de gastos
Según lo revisado, el reparto de gastos comunes se hace con **el coeficiente de cada unidad**, y existe el **cargo individual** a una unidad. No hay reparto por grupos (ascensor, torre, cocheras, locales).
Es la **primera pregunta de diseño** (A5 del listado): si en Argentina o Venezuela es habitual repartir algunos gastos con **otro criterio**, hace falta un concepto de **grupo de unidades** con su propio coeficiente por gasto. Si la respuesta es que sí, es el cambio más grande del núcleo y conviene decidirlo antes de lanzar.

### 4.4 Períodos y mora
- **Segundo vencimiento** (con recargo): hoy hay un `DueDate` y un `LateFeeDate` por periodo. Si el país usa dos vencimientos, se agrega un segundo vencimiento opcional con su recargo.
- **Frecuencia mensual**: `LateFeeFrequency` tiene Diaria, Semanal y Quincenal; falta **Mensual**, que probablemente sea la más usada en los dos países.
- **Interés compuesto, tope legal y actualización por inflación**: hoy la mora es simple por intervalo con tope opcional. El perfil de país dice qué modalidades se ofrecen.
- La regla de mora ya vive en el Centro de configuración, de modo que agregar opciones no cambia a los edificios existentes.

### 4.5 Flujo de aprobación de la liquidación
Hoy: **Borrador → Calculada → Aprobada (BuildingManager) → Revisión del presidente → Publicada (CompanyAdmin)**. Es una cadena fija, pensada para Paraguay.
En otros países puede haber menos pasos (el administrador liquida y publica) o más (consejo, junta). Se propone una **cadena de aprobación configurable por edificio**, con un mínimo obligatorio y valores iniciales por país. Los edificios actuales conservan su cadena.

### 4.6 Pagos
- El flujo **transferencia + comprobante + confirmación del personal** es neutro y se mantiene igual en todos los países (decisión de Tony).
- A validar: **orden de imputación** (hoy el cargo más antiguo primero; puede ser que la ley local mande cancelar primero los intereses), pagos parciales, saldo a favor y **recibo**.
- **Moneda del pago** distinta a la del cargo y tipo de cambio (Venezuela): hoy el pago y el cargo comparten moneda. Es el único cambio estructural de pagos; hay que registrar moneda pagada, tipo de cambio y equivalente.
- Los **pagos online** quedan como capacidad que solo tiene Paraguay.

### 4.7 Documentos (PDF y Excel)
Los documentos del núcleo (liquidación, recibo, estado de cuenta, morosidad, cobranza, informe de asamblea) escriben «Gs.», «Liquidacion de Expensas», «Monto (Gs.)» y etiquetas en español paraguayo. Para estandarizar:

1. Un **modelo de datos común** por documento (la liquidación es la misma información en cualquier país).
2. Un **diccionario de textos por país** (títulos, columnas, notas al pie, leyendas legales).
3. La **moneda y el formato** salen del formateador central (ver plan fiscal, 4.2).
4. Los bloques con contenido **exigido por ley** (datos del administrador, número de registro, avisos) son **secciones opcionales** que el perfil de país enciende.
5. El modelo propio del edificio (plantilla subida y calibrada) sigue funcionando igual.

### 4.8 Votaciones y asambleas
`Vote` guarda un `QuorumPercentage` por votación y un tipo de peso (por unidad o por coeficiente). Para varios países, un **catálogo de tipos de decisión** con mayoría y quórum sugeridos por país, que la administradora puede ajustar, evita tener que acordarse de la regla en cada votación. A validar: poderes de representación, voto a distancia, restricción a morosos.

### 4.9 Idioma y tono
- Hoy hay ES / PT / EN en la interfaz. Se agrega la **variante de país** (es-AR, es-VE, es-PY): mismas claves de texto, con voseo en Paraguay y Argentina y tuteo en Venezuela, y las palabras propias del país (expensas, cuota de condominio, alícuota, etc.).
- El glosario del país reemplaza las palabras que hoy están escritas en el código y en las pantallas.

### 4.10 Roles
Los roles (SuperAdmin, CompanyAdmin, CompanyOperator, BuildingManager, Owner, Resident, Porter) son universales. Solo cambia el **nombre que se muestra** (administradora, consorcista, condómino).

## 5. Cómo estandarizar (sin romper Paraguay)

### 5.1 Principios
1. **Cambios solo aditivos.** Cada campo nuevo tiene un valor por defecto que reproduce a Paraguay; ninguna migración altera a los edificios actuales.
2. **La política va en datos, no en `if (país)`.** Si hace falta una pregunta tipo «¿qué país es?» en medio de un flujo, la perilla está mal puesta.
3. **Un solo glosario** y un solo formateador de moneda para todo el sistema.
4. **El perfil de país es una tabla editable** que tú mantienes, versionada, con valores iniciales de Paraguay, Argentina y Venezuela.
5. **Pruebas de regresión de Paraguay primero** (liquidación, mora y recibo con resultados idénticos antes y después).

### 5.2 Piezas nuevas
- `CountryProfile` (tabla): código de país, nombre, moneda y decimales, zona horaria, prefijo telefónico, etiquetas de región, tipos de documento permitidos, lista de bancos, opciones permitidas de mora y vencimiento, capacidades.
- `Glossary` por país e idioma: clave → texto.
- Perillas nuevas en el edificio: frecuencia mensual, segundo vencimiento, cadena de aprobación, criterio de imputación (si se valida).
- Tabla de tipos de documento con validadores.
- Moneda y tipo de cambio en el pago (solo si Venezuela lo confirma).
- Opcional según A5: grupos de unidades y reparto por grupo.

### 5.3 Orden recomendado del trabajo en el núcleo
1. País y moneda en la empresa y formateador central (compartido con el plan fiscal).
2. Glosario y variantes de idioma por país.
3. Tipos de documento por país y validadores.
4. Ficha del edificio: región, identificadores registrales e identificador fiscal genéricos.
5. Perillas de mora (mensual y compuesto) y segundo vencimiento.
6. Documentos del núcleo con diccionario y secciones opcionales.
7. Cadena de aprobación configurable.
8. Pago en otra moneda y tipo de cambio (si se confirma).
9. Grupos de unidades y reparto por grupo (si se confirma).

## 6. Recomendación: lanzar primero el núcleo, después lo fiscal
Un edificio de Argentina o Venezuela puede **administrarse completo con el núcleo** (liquidar, cobrar con transferencia y comprobante, mora, estado de cuenta, asambleas, reservas, reclamos) **sin Finanzas y sin facturación electrónica**. Eso permite:

- **Entrar al mercado antes**, con riesgo legal bajo (no se emite ningún comprobante fiscal desde el sistema).
- **Validar con administradoras reales** si el núcleo calza antes de invertir en lo fiscal.
- Ir sumando Finanzas y facturación por país cuando las validaciones lo respalden.

El alcance «fiscal completo» sigue siendo la meta; esto solo propone el **orden de entrega**: núcleo estandarizado → paquete fiscal de cada país.

## 7. Riesgos propios del núcleo
1. **Reparto por grupos** (A5): si es necesario, es el cambio más grande y toca la liquidación, los cargos y los reportes.
2. **Imputación de pagos**: si la ley manda otro orden, cambia el motor de pagos y hay que cuidar los edificios actuales.
3. **Venezuela con dos monedas**: afecta cargos, pagos, estado de cuenta, mora y reportes a la vez.
4. **Liquidación con contenido legal**: si cada país exige un formato, el PDF necesita una variante por país con revisión de un administrador local.
5. **Vocabulario**: con glosario faltante o mal puesto, el producto «se siente paraguayo» aunque funcione. Se prueba con un administrador de cada país.
6. **Mensajes de cobranza**: las reglas sobre cobranza por WhatsApp o correo pueden diferir.

## 8. Decisiones abiertas
1. ¿Se acepta el orden «núcleo primero, fiscal después» (sección 6)?
2. ¿Hay que soportar reparto de gastos por grupos de unidades? (depende de A5)
3. ¿Qué país se valida y entrega primero?
4. ¿Quién mantiene el glosario por país? (traducción y revisión local)
5. ¿La cadena de aprobación de la liquidación se hace configurable ahora o se deja la de Paraguay hasta saber qué pide cada país?
