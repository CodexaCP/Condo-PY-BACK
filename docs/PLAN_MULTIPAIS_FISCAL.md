# Plan de expansión multipaís con fiscalidad completa

Estado: **análisis y propuesta (2026-11-02), sin código**. Decisiones de Tony: alcance «fiscal completo» (no solo moneda e IVA);
**los dos países, Argentina y Venezuela**; y **sin inversión en pago electrónico por ahora** (ver sección 4.7).
Lo que dice de leyes de Argentina y Venezuela sale de conocimiento general y **no está validado**: es el objetivo de la Fase 0.

## 1. Idea central: país en la empresa y «paquete fiscal» por país

La empresa (`Company`) guarda el país. Del país salen la moneda, los impuestos y todo lo fiscal, empaquetado en un **paquete fiscal por país** (`ICountryFiscalPack`).
Hoy Paraguay está escrito a mano en unos 70 archivos; el primer paso es mover eso a un `ParaguayFiscalPack` sin cambiar nada de lo que ve un cliente actual.

Un paquete tiene dos partes:

| Parte | Qué es | Dónde vive |
|---|---|---|
| **Datos** | Tasas de impuesto, nombres («IVA», «RUC»), moneda y decimales, tipos de documento, etiquetas, textos legales | Tablas (editables por SuperAdmin) |
| **Reglas** | Validar RUC/CUIT/RIF, numerar comprobantes, armar libros, hablar con el fisco | Código, una clase por país |

## 2. Qué cambia de país a país (inventario de lo que hoy es solo Paraguay)

| Área | Hoy (Paraguay) | Lo que varía |
|---|---|---|
| Identificación fiscal | RUC con dígito verificador (`BuildingProfile.IsValidRuc`), cédula (`PersonRules`) | Argentina CUIT/CUIL/DNI; Venezuela RIF/cédula. Otro algoritmo y formato cada uno |
| Impuesto | IVA 10/5/0 fijo en `VatMath`, `VatTreatment`, `FinanceVatService`, plan de cuentas | Tasas, nombre, si el precio incluye o no el impuesto |
| Comprobante de venta | Timbrado + establecimiento + punto de expedición + rango (`InvoiceSeries`), papel preimpreso calibrable | Argentina: factura electrónica con CAE por servicio web. Venezuela: imprenta autorizada y número de control. Cada una con su modelo de datos |
| Nota de crédito | Con timbrado propio (`CreditNote.FiscalTimbrado`) | Igual que el comprobante |
| Libros fiscales | Libro de compras IVA (`FinanceVatService`) | Argentina: Libro IVA Compras/Ventas, régimen de información. Venezuela: libros de compras y ventas |
| Libros contables | Diario y Mayor con numeración y «art. 85» (`JournalBooks`, `AccountingBookSettings`) | Requisitos de rúbrica, conservación y formato propios de cada país |
| Plan de cuentas | Plantilla de 197 nodos con cuentas DNIT (`FinanceChartTemplate`) | Una plantilla por país (retenciones, impuestos a pagar, aportes) |
| Retenciones | Cuentas 1.4.09 y 2.3.02 (retenciones IVA/IRE) | Regímenes distintos; en Argentina hay retenciones y percepciones por jurisdicción |
| Moneda | `₲`/`Gs.` en unos 30 formateadores BACK, 41 componentes WEB, 24 pantallas APP | Símbolo, decimales, separadores, monto en letras |
| Pagos online | Bancard (solo PY) | Otra pasarela por país |
| Normas del condominio | Intereses por mora, fondo de reserva, quórum de votaciones | Las fija cada ley de propiedad horizontal |
| Zona horaria y teléfono | `Building.TimeZoneId` ya existe; prefijo +595 en varios sitios | Prefijo y formato por país |

## 3. Punto delicado: ¿el edificio emite factura?

El módulo de facturación de hoy asume que el edificio factura a los propietarios con timbrado. **No en todos los países es así.**
Por mi conocimiento (a validar), en Argentina las expensas se comunican con la *liquidación de expensas* del administrador y el consorcio normalmente no factura al propietario; en Venezuela se emite el *recibo de condominio*.

Por eso el paquete declara **capacidades** que encienden o apagan módulos:

- `EmitsInvoices` (facturas y notas de crédito del edificio),
- `HasPurchaseTaxBook` (libro de compras),
- `RequiresElectronicInvoicing`,
- `HasLegalAccountingBooks`,
- `SupportsMultiCurrency` (Venezuela usa dólar de hecho).

«Fiscal completo» significa entonces: lo que **ese país exige**, no copiar lo de Paraguay.

## 4. Diseño técnico

### 4.1 Empresa
Campos nuevos en `Company`: `CountryCode`, `CurrencyCode`, `CurrencyDecimals`, `TaxName`, `DefaultLocale`. Las tasas viven en una tabla `CompanyTaxRate` (nombre, porcentaje, es general/reducida/exento).
Migración: todas las empresas actuales quedan `PY` / `PYG` / 0 decimales / IVA 10-5-0. La moneda queda fija al haber movimientos.

### 4.2 Servicios
- `ICompanyLocale`: símbolo, decimales, cultura y monto en letras. Reemplaza los formateadores sueltos.
- `ICountryFiscalPack` (resolución por `CountryCode`): validador de documento fiscal, estrategia de numeración, generador de libros, plantilla de plan de cuentas, textos legales, cliente del fisco.
- `VatMath` pasa a `TaxMath` y recibe tasas y decimales de la empresa.
- `VatTreatment` deja de llamarse `Vat10`/`Vat5`: pasa a «tasa general / reducida / exento / no corresponde».

### 4.3 Comprobantes
Hoy `InvoiceSeries` mezcla datos genéricos con datos de timbrado. Se separa:

- Entidad genérica `FiscalDocumentSeries` con el tipo de numeración (`Timbrado`, `ElectronicCae`, `ControlNumber`).
- Datos específicos de cada país en una tabla o JSON validado por el paquete.
- La parte de papel preimpreso (calibración, `HideFrame`, `HalfPage`) queda dentro de la estrategia de Paraguay.

### 4.4 Cliente y edificio
Tipo de documento del propietario según el país (RUC/CUIT/RIF/cédula/DNI); el campo `Ruc` del edificio pasa a «identificador fiscal». La condición frente al impuesto (Responsable Inscripto, Monotributo, etc.) es un dato de país.

### 4.5 WEB y APP
La sesión devuelve la configuración de la empresa (moneda, decimales, nombre del impuesto, etiquetas fiscales, capacidades). Un formateador central y un diccionario de etiquetas reemplazan los `es-PY` repartidos, y los menús se esconden según las capacidades.

### 4.6 Vistas que mezclan empresas
El panel del SuperAdmin y la actividad de plataforma suman importes de todos los edificios: con monedas distintas se agrupan por moneda y no se suman entre sí.

### 4.7 Pagos: igual que hoy en todos los países (decisión de Tony)
No se invierte en cobro online fuera de Paraguay. El pago se maneja como hoy: el propietario transfiere y sube el comprobante, y el personal lo confirma.
Esa parte ya es neutra (`PaymentMethod.BankTransfer`, `ComprobanteUrl`, `ExternalReference`). Lo que sí hay que ajustar:

- **Cuenta bancaria del edificio** (`BuildingBankAccount`): hoy es genérica (banco, número, alias, titular). Se le agrega la **moneda de la cuenta** y un validador por país del formato del número (por ejemplo CBU/CVU y alias en Argentina; cuenta de 20 dígitos y pago móvil en Venezuela; a validar en la Fase 0).
- **Lista de bancos y tipos de cuenta por país** (dato en tabla).
- **Pagos online** (Bancard, configuración de «Pagos online» y botón «Pagar online» de la APP): se apagan con la capacidad `SupportsOnlinePayments`, que solo tiene Paraguay. En los demás países no aparecen.
- **Venezuela:** si el propietario paga en una moneda distinta a la del cargo, hay que registrar la moneda pagada y el tipo de cambio del día. Es la única parte de pagos que no es solo etiquetas; se define en la Fase 0.
- Cuando más adelante se quiera cobro online en otro país, se agrega como un proveedor más sobre lo ya existente, sin rehacer nada.

## 5. Plan por fases

| Fase | Contenido | Se puede liberar sola |
|---|---|---|
| **0 — Validación (sin código)** | Para cada país: reunión con contador, abogado y administrador local, lista exacta de obligaciones del consorcio/condominio, ejemplos reales de comprobantes y libros. Definir qué capacidades tiene. **Lista de preguntas en [VALIDACIONES_MULTIPAIS_AR_VE.md](VALIDACIONES_MULTIPAIS_AR_VE.md); análisis del núcleo sin Finanzas en [ANALISIS_MULTIPAIS_NUCLEO.md](ANALISIS_MULTIPAIS_NUCLEO.md)** | Sí |
| **1 — Base** | Campos en `Company`, tabla de tasas, alta con país, sesión con configuración a WEB y APP | Sí |
| **2 — Moneda** | `ICompanyLocale` y reemplazo de formateadores en BACK, WEB y APP; `PaymentIntent.Currency` de la empresa | Sí |
| **3 — Impuesto genérico** | `TaxMath`, tratamientos de impuesto neutros, libro de compras con tasas de la empresa | Sí |
| **4 — Paquete de Paraguay** | Mover validaciones, timbrado, libros y plan de cuentas al `ParaguayFiscalPack`. **Cero cambios visibles**, con pruebas de regresión sobre PDFs y asientos actuales | Sí |
| **5 — Paquetes de Argentina y Venezuela** | Por cada país: validador de documento, plantilla de plan de cuentas, comprobante y libros que exija, textos legales, cuentas bancarias (4.7). Se hacen sobre la misma base de las fases 1 a 4; se entregan uno tras otro | No, dependen de F0 |
| **6 — Fisco electrónico** | Integración con el servicio del fisco (CAE en Argentina u otro). Es la pieza más grande; se decide por país según lo que F0 diga que es obligatorio | No |
| **7 — Pasarela de pago** | **Aplazada por decisión de Tony.** Hoy: transferencia y comprobante en todos los países | — |
| **8 — Reglas del condominio** | Mora, fondo de reserva, quórum según la ley local | Opcional |

La Fase 4 es la que da seguridad a todo lo demás: sin ella, tocar Paraguay para abrir otro país puede romper a los clientes actuales.

## 6. Riesgos
1. **Cumplimiento legal ajeno a tu conocimiento:** sin contador local el paquete puede salir mal. Por eso F0 va primero y no tiene código.
2. **Factura electrónica:** en Argentina exige certificados digitales y homologación con el fisco; es un proyecto en sí.
3. **Dos países a la vez:** Tony decidió tener ambos. Es viable porque comparten la base (fases 1 a 4); lo costoso es cada paquete (F5), que conviene entregar de a uno para poder probar con un contador de cada país.
4. **Paraguay también cambia:** la factura electrónica (SIFEN) se implanta por etapas; conviene que el paquete de Paraguay deje lugar a ella.
5. **Moneda dual en Venezuela:** requiere tipo de cambio por fecha y definir en qué moneda se llevan los libros; el impuesto sobre pagos en divisas hay que validarlo.
6. **Inflación fuerte** (Argentina, Venezuela): el ajuste por inflación en reportes es un tema aparte.
7. **Tus planes:** `Plan.Price` está en un solo valor; hay que decidir la moneda en que cobras a cada empresa.
8. **Idioma ≠ país:** el idioma del usuario (ES/PT/EN) sigue siendo independiente del país de la empresa.

## 7. Qué no se toca
Los datos y documentos ya emitidos de las empresas de Paraguay, las migraciones aplicadas y la numeración fiscal vigente.

## 8. Decisiones abiertas de Tony
Cerradas el 2026-11-02: países = Argentina y Venezuela; pago electrónico fuera de alcance (transferencia y comprobante).

Abiertas:
1. ¿Cuál de los dos países se entrega primero?
2. ¿Hay contador o cliente piloto en cada país para la Fase 0?
3. ¿En qué moneda se cobran los planes de cada empresa?
4. ¿La Fase 4 (ordenar Paraguay) se hace primero y sola, antes de sumar un país?
5. ¿Los edificios de Venezuela llevan la contabilidad en una o en dos monedas (bolívar y dólar)?
