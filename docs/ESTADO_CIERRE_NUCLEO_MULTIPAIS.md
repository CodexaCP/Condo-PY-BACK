# Cierre del núcleo multipaís — estado y guía de pruebas

Fecha: 2026-11-02. Sin commit ni deploy. Marco: [ANALISIS_MULTIPAIS_NUCLEO.md](ANALISIS_MULTIPAIS_NUCLEO.md), [PLAN_MULTIPAIS_FISCAL.md](PLAN_MULTIPAIS_FISCAL.md) y las respuestas de la Fase 0 (`VALIDACIONES_MULTIPAIS_AR_VE_RESPONDIDO.md`).
Lo legal de Argentina sale de esas respuestas (Código Civil y Comercial arts. 2037-2072) y **no está validado por un contador ni un abogado**; Venezuela queda como borrador.

## 1. Qué quedó hecho en el BACK

| # | Pieza | Cómo funciona |
|---|---|---|
| 1 | País, moneda e impuesto en la empresa | `Company` guarda `CountryCode`, `CurrencyCode`, `LocaleCode`, `TaxName`, `TaxGeneralRate`, `TaxReducedRate`. Al crearla se completan desde el país (Paraguay si no se manda ninguno) y el SuperAdmin puede fijar el impuesto a mano (`TaxConfigured`). País y moneda **no cambian** si la empresa ya tiene cargos o gastos |
| 2 | Perfiles de país | `CountryProfiles` (PY, AR, VE-borrador): moneda, zona horaria, prefijo, impuesto, tipos de documento, jurisdicciones de Argentina (24), vocabulario y capacidades. `GET /api/locale/countries` |
| 3 | Moneda central | `Money.Format/Number/Round` con la moneda de la empresa (guaraní sin decimales, peso con 2). La fija un middleware por empresa del usuario y `MoneyScope` para el SuperAdmin y los procesos de fondo. Reemplaza los «Gs.» escritos a mano del núcleo: liquidación, recibos, estado de cuenta, cobranza, comparativo de edificios, avisos y mora. **Quedan en guaraníes** los PDF y Excel de Finanzas (libros, estado de resultados, exportaciones), la factura y la nota de crédito fiscales y el monto en letras |
| 4 | Sesión | `LoginResponse.Locale` y `GET /api/locale/me` |
| 5 | Documentos por país | DNI, CUIT, CUIL, cédula V/E, RIF validados por formato (sin dígito verificador hasta confirmarlo). RUC paraguayo con su dígito, igual que siempre. Identificador fiscal del edificio según el país de la empresa |
| 6 | Ficha del edificio | `Jurisdiction`, `AdministratorRegistryNumber`, `FiscalYearEndMonth`, `SettlementApprovalMode` |
| 7 | Mora mensual | `LateFeeFrequency.Monthly` (cada 30 días, interés simple) |
| 8 | Cadena de aprobación | `ManagerPresidentAdmin` (la de siempre) o `ManagerAdmin` (sin paso del presidente). El resumen trae `RequiresPresidentReview` |
| 9 | Votaciones | `MajorityRule` (`Simple` o `DoubleAbsolute`: más del 50 % de unidades **y** del coeficiente **totales**), `IsInformative` (por defecto verdadero fuera de Paraguay) |
| 10 | Liquidación | Título con el vocabulario del país y línea del administrador (nombre, registro, jurisdicción) fuera de Paraguay |
| 11 | Certificado de deudas y créditos | `GET /api/account-statements/units/{id}/debt-certificate` (JSON) y `/debt-certificate-pdf`; solo personal |
| 12 | Capacidades | `[RequiresCapability]` en facturas, series, notas de crédito, pagos online, conexiones de pago y libro de compras: Argentina y Venezuela devuelven 400 `capability_disabled` |
| 13 | Impuesto de la empresa en gastos | La tasa válida de un gasto y las columnas del libro de compras salen de las tasas de la empresa (Paraguay: 10, 5 y exento) |
| 14 | Tolerancia y redondeo por moneda | La tolerancia de un comprobante es media unidad mínima de la moneda; los aportes de la liquidación se redondean a la unidad de la moneda |

Migración: `20261102114757_MultiCountryCore` (solo columnas nuevas con valores de Paraguay; el `.sql` rellena IVA 10/5 en las empresas existentes).
Pruebas: 28 nuevas (`Condo.Tests/Localization/MultiCountryCoreTests.cs`); suite completa 1.469 superadas, 0 fallas, 93 omitidas (ya lo estaban).

## 2. Cambios visibles para Paraguay (leer antes de desplegar)
- Los importes que antes salían con la cultura del servidor (`{x:N0}`) ahora salen siempre con puntos de miles: «Gs. 1.234.567». Es lo que ya hacían la liquidación y el recibo.
- El correo de morosidad conserva «₲»; las demás monedas usan su símbolo.
- Todo lo demás (liquidación sin título nuevo, series, facturas, pagos online, libro de compras) queda igual.

## 3. Lo que NO se hizo y por qué

| Pieza | Motivo |
|---|---|
| Imputación de pagos configurable | Al leer el motor: un **comprobante es un pago completo** (capital y mora del período juntos) y **no se aceptan pagos parciales**. «Intereses primero» no aplica a ese diseño, y permitir pagos parciales o que el deudor elija qué pagar es un cambio del motor que depende de un dictamen. Queda como está (más antiguo primero) |
| Segundo vencimiento, interés compuesto, bloqueo por mora | Fuera de alcance por las respuestas de la Fase 0 |
| Aviso automático a los 60 días del cierre del ejercicio | Está el campo `FiscalYearEndMonth`; el aviso no |
| Privacidad (Ley 25.326): política, consentimiento y derechos del titular | Es redacción legal y un proceso de atención; no se construyó |
| Pago en otra moneda con tipo de cambio | Solo aplica a Venezuela |
| Reparto de gastos por grupos de unidades | Las respuestas piden no asumirlo |
| Todo lo fiscal de Argentina y de Venezuela | Es la fase siguiente |
| Formato de fechas | Siguen en `es-PY` en el WEB y la APP (ver el estado del WEB y la APP más abajo) |

## 4. Despliegue (cuando Tony lo decida)
1. Correr el `.sql` **antes** de subir el código (va después de `JournalSupportDocs`):
   `/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/MultiCountryCore.sql`
2. Subir el BACK y, después, el WEB y la APP.
3. Probar con una empresa de Paraguay (que no cambie nada) y luego con una de Argentina.

## 5. Guía de pruebas (Tony)
1. **Paraguay igual que antes:** abrir una empresa y un edificio existentes; estado de cuenta, liquidación en PDF, recibo y morosidad se ven como siempre.
2. **Alta de empresa argentina:** como SuperAdmin crear una empresa con país Argentina → moneda ARS, IVA 21 %. Crear un edificio con jurisdicción «Ciudad Autónoma de Buenos Aires», CUIT y un administrador.
3. **Moneda:** en esa empresa los PDF muestran «$ 1.234,50» (dos decimales).
4. **Capacidades:** en la empresa argentina no deben aparecer Facturación, Notas de crédito ni Pagos online; llamar a esas pantallas devuelve «no habilitado».
5. **Liquidación sin presidente:** poner la cadena «Encargado → Administrador», aprobar con el encargado y publicar con el administrador sin paso del presidente.
6. **Mora mensual:** configurar mora mensual y verificar los intervalos de 30 días.
7. **Votación:** crear una con mayoría doble en un edificio de 4 unidades de 25 %: con 3 votos una opción gana; con 2 no. Debe decir «consulta informativa».
8. **Certificado de deudas:** en un estado de cuenta, descargar el certificado de una unidad con deuda y de otra sin deuda.
9. **Documentos de personas:** cargar un propietario con DNI y otro con CUIT; rechazar «123».
10. **Cambio de moneda:** intentar cambiar el país de una empresa que ya tiene gastos → error.

## 6. WEB y APP (hechos por separado, sin commit; compilan)
**WEB** (`Condo-PY-WEB`, ~45 archivos): servicio central de localización (`core/locale/`), importes con `formatMoney` en las pantallas del núcleo, alta y edición de empresa con país / moneda / impuesto, ficha del edificio con etiquetas y jurisdicción del país, tipos de documento por país en propietarios y residentes, liquidación sin paso del presidente, votaciones con mayoría doble e insignia informativa, botón de certificado de deudas, menú por capacidades y glosario en menú y títulos principales.
**APP** (`CondoPY-APP`): formateador central, importes tipeados con los separadores de la moneda, «Pagar online» oculto sin la capacidad, mora «mensual» y pestaña «Expensas» con el glosario.

No cubierto: el **SuperAdmin ve Paraguay** (no tiene empresa; solo la ficha del edificio toma el país de la empresa elegida); Marketplace y las pantallas de Finanzas conservan «Gs.»; fechas en `es-PY`; sin pantallas de votaciones / documentos de personas / presidente en la APP; falta un texto de ayuda cuando un edificio no tiene presidente. El `ng build` del WEB muestra avisos de presupuesto de estilos (dashboard, votaciones) que conviene revisar.
