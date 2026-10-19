# Diseño — Operaciones guiadas de Finanzas

Estado: **implementado el 2026-10-19 (backend y web), sin commit ni deploy**. Las decisiones G1 a G5 se tomaron como se recomendaba; G6 (comisión/interés y ajuste de saldo) queda para después de la respuesta del contador. Guía de pruebas: `GUIA_PRUEBAS_OPERACIONES_FINANZAS.md`.
Relacionado: `PLAN_ALINEACION_ASIENTOS_CONTABLES_PY.md` (Fase 6) y `PREGUNTAS_PARA_EL_CONTADOR.md` (pregunta 19).

## 1. Qué es y por qué

Hoy Finanzas se arma solo con cuatro hechos: cobros de propietarios, gastos, ingresos propios del edificio y notas de crédito de proveedor. Todo lo demás que pasa con la plata **no tiene dónde cargarse**: pasar plata del banco a caja, depositar el efectivo cobrado, mover plata al fondo de reserva.

Una **operación guiada** es una pantalla donde la persona elige *qué pasó* («moví plata entre cuentas») y el sistema hace todo junto y de forma consistente:

1. Mueve los saldos de las cuentas financieras (Finanzas, Tablero, Flujo de caja).
2. Aparece en la conciliación bancaria como movimiento tildable.
3. Genera su asiento del Libro Diario, con número correlativo.
4. Queda registrada con quién, cuándo y por qué, y no se edita: se **anula con motivo** y el sistema hace el contraasiento.

Por qué no basta el asiento manual solo: un asiento manual cambia la contabilidad pero **no mueve el saldo de Finanzas ni la conciliación**, y quedan dos verdades distintas. Con la operación guiada hay una sola.

## 2. Catálogo de operaciones

| # | Operación | Qué hace | Etapa |
|---|-----------|----------|-------|
| 1 | **Transferencia entre cuentas propias** | Mueve plata de una cuenta financiera a otra (banco↔banco, banco↔caja, banco↔fondo de reserva). | **Primera** |
| 1a | Depósito de efectivo | Preajuste de la 1: origen Caja, destino un banco. | Primera (misma pantalla) |
| 1b | Retiro de efectivo | Preajuste de la 1: origen un banco, destino Caja. | Primera (misma pantalla) |
| 1c | Aporte al / retiro del fondo de reserva | Preajuste de la 1 con la cuenta del fondo. Pide respaldo (acta o resolución). | Primera (misma pantalla) |
| 2 | Comisión o interés bancario | Atajo que crea un gasto (comisión) o un ingreso (interés ganado) en la cuenta bancaria. | Segunda |
| 3 | Ajuste de saldo | Corrige una diferencia contra el extracto. Es peligroso: solo SuperAdmin, con motivo y adjunto. | Segunda, a discutir |
| 4 | Asiento manual de ajuste (genérico) | Para el contador, no mueve Finanzas. Es la Fase 6A del plan. | Aparte |

Las operaciones 2 y 3 se dejan para después porque dependen de decisiones que no están tomadas (si una comisión bancaria es un gasto que se reparte entre los propietarios o no; si un ajuste de saldo se permite).

## 3. Transferencia entre cuentas propias (diseño detallado)

### 3.1 Qué carga la persona

- **Desde** (cuenta financiera) y **Hacia** (cuenta financiera distinta).
- **Importe** mayor que cero.
- **Fecha** de la operación (por defecto hoy).
- **Referencia** (número de boleta de depósito, de transferencia, etc.), opcional.
- **Concepto / nota**, opcional. **Respaldo** (acta) obligatorio si participa el fondo de reserva.

### 3.2 Qué hace el sistema

Una sola operación, un solo registro `FinanceTransfer`, y en el libro **dos movimientos** que comparten el mismo origen:

| Movimiento | Cuenta | Sentido |
|---|---|---|
| 1 | Desde | Sale |
| 2 | Hacia | Entra |

El **saldo total del edificio no cambia**: lo que sale de una cuenta entra en la otra.

Asiento del Libro Diario (un asiento por transferencia):

```
Debe   Cuenta del plan de «Hacia»     importe
  Haber  Cuenta del plan de «Desde»              importe
```

Las cuentas del plan salen del mismo vínculo que ya usan los cobros y gastos (rol `FinancialAccount` de cada cuenta financiera). Si una cuenta no está vinculada, la transferencia queda en «sin clasificar» y avisa, como el resto.

### 3.3 Reglas

1. Origen y destino distintos, ambas activas y del mismo edificio.
2. Fecha no anterior a la fecha de arranque de Finanzas, ni de un mes cerrado (Período y cierre), ni de un ejercicio cerrado en el Libro diario (se reabre primero). Lo mismo para anular.
3. Fecha no futura (el libro hoy ignora lo fechado a futuro).
4. Saldo (contra el saldo de hoy): si deja la cuenta de origen en negativo → **aviso que hay que confirmar** (casilla en la pantalla; el backend rechaza con 409 si no viene confirmada) para banco y caja; para el **fondo de reserva** → **bloqueo**, no se puede confirmar. (Decisión G3.)
5. Permisos: Administrador y SuperAdmin.
6. **No se edita.** Se **anula** con motivo: desaparece del libro y el Libro Diario genera el contraasiento con el mecanismo que ya existe («Ya no existe»). Si la persona se equivocó de importe o cuenta, anula y carga de nuevo.
7. Una transferencia anulada no se puede volver a activar; se carga una nueva.

### 3.4 Qué cambia en el resto del sistema (el punto delicado)

El libro de Finanzas hoy trata cada movimiento como ingreso o egreso con un rubro. Una transferencia **no es ni ingreso ni gasto**. Si se la suma como los demás, el estado de resultados, el flujo operativo y el presupuesto vs real quedan mal (aparecería un ingreso y un gasto inventados).

Solución: el movimiento de transferencia lleva una **marca de «transferencia interna»** (nuevo tipo de origen `LedgerSourceType.Transfer = 6`, sin rubro). Cada consumidor decide:

| Dónde | Trato |
|---|---|
| Saldo de cada cuenta (Tablero, Cuentas) | **Cuenta**: se suma/resta en cada cuenta. |
| Saldo total del edificio | No cambia (se compensa). |
| Conciliación bancaria | **Cuenta**: es un movimiento del banco, se tilda contra el extracto. |
| Fondo de reserva (pantalla) | **Cuenta**: aparece como aporte o retiro del fondo. |
| Flujo de caja | **No cuenta**: ni en las líneas ni en los totales (el saldo de cierre no cambia). Se ven en Movimientos y en la vista de cada cuenta. |
| Presupuesto vs real | **No cuenta** (no tiene rubro). |
| Informe para la asamblea | **No cuenta** como ingreso/egreso; se muestra el saldo por cuenta. |
| Libro Diario / Mayor / Sumas y saldos | **Cuenta**: es un asiento normal. |
| Libro de compras IVA | No cuenta. |

Esto es lo que más trabajo y más riesgo tiene: son 5 consumidores del libro (`FinanceReportService`, `BankReconciliationService`, `FinanceBudgetService`, `AssemblyReportService`, `AccountingEntriesService`) más el constructor de asientos. Se cubre con pruebas que verifican que **una transferencia no cambia ningún total de ingresos, egresos ni resultado**.

El asiento: hoy el constructor arma un asiento por «origen» y mira cada movimiento. La transferencia tiene dos movimientos con el mismo origen, así que hay que enseñarle a emitir un solo asiento con las dos patas.

### 3.5 Datos

Nueva tabla `FinanceTransfers` (una migración con su `.sql`):

- `Id`, `CompanyId`, `BuildingId`
- `FromAccountId`, `ToAccountId`, `Amount`, `TransferDate`
- `Reference`, `Notes`, `SupportReference` (acta)
- `CreatedByUserId`, `CreatedAtUtc`
- `Status` (Activa / Anulada), `VoidedAtUtc`, `VoidedByUserId`, `VoidReason`

Sin borrado físico. La anulación y el contraasiento dejan el rastro completo.

## 4. Pantalla

Ubicación: Finanzas → nueva entrada **«Operaciones»** (junto a Movimientos y Conciliación). Una página con tarjetas y debajo el historial.

```
Operaciones

¿Qué querés hacer?
┌──────────────────────┐ ┌──────────────────────┐ ┌──────────────────────┐
│ Mover plata entre    │ │ Depositar efectivo   │ │ Aporte / retiro del  │
│ cuentas              │ │ en el banco          │ │ fondo de reserva     │
└──────────────────────┘ └──────────────────────┘ └──────────────────────┘

Historial
Fecha       Operación             Desde → Hacia           Importe     Asiento      Estado
18/10/2026  Depósito de efectivo  Caja → Banco Itaú    1.200.000   2026-000134  Activa      [Anular]
```

Formulario (ejemplo «Mover plata entre cuentas»):

1. Elegir **Desde** y **Hacia** (se muestra el saldo actual de cada una).
2. Importe, fecha, referencia, nota.
3. **Resumen antes de confirmar:**
   ```
   Banco Itaú    5.000.000  →  3.800.000
   Caja            200.000  →  1.400.000
   Total edificio no cambia: 5.200.000
   Asiento: Debe Caja / Haber Banco Itaú
   ```
4. Confirmar. Resultado: «Operación registrada. Asiento 2026-000134» (o «se asentará con el próximo asentado», según si el período ya está asentado).

El botón **Anular** pide motivo y muestra el aviso previo de impacto que ya existe en el Libro Diario («esto generará un contraasiento»).

## 5. Casos de ejemplo

1. **Depósito:** se cobraron 1.200.000 en efectivo y se depositan. Caja → Banco. Banco +1.200.000, Caja −1.200.000.
2. **Aporte al fondo:** pasar 3.000.000 del banco al fondo de reserva por resolución de asamblea. Banco → Fondo, con acta.
3. **Error de importe:** se cargó 1.200.000 y era 1.020.000. Se anula con motivo y se carga bien. En el Libro Diario quedan: asiento original, contraasiento y asiento nuevo (cada uno con su número).

## 6. Fases de construcción

| Fase | Qué | Notas |
|---|---|---|
| G1 | Entidad, migración + `.sql`, servicio de alta/anulación con las reglas de 3.3 | pruebas de reglas y de concurrencia |
| G2 | Integración con el libro (`Transfer = 6`), saldos, conciliación y fondo de reserva | pruebas: ningún total operativo cambia |
| G3 | Asiento en el Libro Diario (un asiento, dos patas), detección de anulación, aviso de impacto | reutiliza el contraasiento existente |
| G4 | Pantalla web «Operaciones» + historial + anulación | |
| G5 | Guía de pruebas manuales | |
| G6 | Operaciones 2 y 3 (comisión/interés, ajuste de saldo) | cuando se decidan |

La APP del propietario no se toca. Estimación: la mayor parte del esfuerzo está en G2 y G3.

## 7. Decisiones (G1 a G5 tomadas como se recomendaba; G6 queda pendiente)

| ID | Decisión | Recomendación |
|----|----------|---------------|
| G1 | Nombre y lugar del menú | «Operaciones» dentro de Finanzas |
| G2 | ¿El aporte o retiro del fondo de reserva exige respaldo (acta)? | Sí, obligatorio, con campo de texto y adjunto opcional |
| G3 | Saldo negativo en el origen: ¿aviso o bloqueo? | Aviso en banco y caja, bloqueo en el fondo de reserva |
| G4 | ¿Quién puede cargar y anular? | Administrador y SuperAdmin (no la empresa administradora sin rol) |
| G5 | ¿Se permiten transferencias entre bancos distintos del mismo edificio? | Sí |
| G6 | ¿Etapa 2 (comisión/interés y ajuste de saldo) ahora o después de la respuesta del contador? | Después |

## 8. Preguntas para el contador que esto agrega

(La 19 ya cubre «¿debe haber asiento y con qué cuentas?».)

- ¿Un retiro del fondo de reserva hacia la cuenta operativa es un préstamo que debe reintegrarse, o un uso definitivo del fondo? ¿Cambia el asiento?
- ¿El fondo de reserva, para el asiento, es una cuenta de patrimonio/fondo o un pasivo? (Es la pregunta 11; afecta el tipo de cuenta en las transferencias al fondo.)

## 9. Cómo se evita que una transferencia se cuente como ingreso o gasto (implementado)

Es el punto delicado del diseño. Hay **cinco capas**, para que un olvido futuro no lo rompa:

1. **Marca en el origen.** Los dos movimientos de una transferencia llevan `LedgerSourceType.Transfer` y el rubro especial `Transfer` (que no es una cuenta del plan). `LedgerBucket.IsTransfer` y `LedgerRow.IsTransfer` lo dicen.
2. **Seguro por defecto.** `GetBucketsAsync` y `GetRowsAsync` **no devuelven transferencias** salvo que el consumidor las pida (`includeTransfers: true`). Un reporte nuevo que no piense en esto queda correcto sin hacer nada.
3. **Doble filtro en los cálculos.** Aunque a un cálculo le lleguen transferencias, `Flow`, `RubroAmounts`, `CashFlow`, los ingresos reales del presupuesto y las líneas del informe de la asamblea las descartan por su cuenta.
4. **Lista cerrada de consumidores.** Un test lee el código y exige que solo estos archivos pidan transferencias: `AccountingEntriesService`, `AssemblyReportService` (saldos por cuenta), `BankReconciliationService`, `FinanceLedgerController` (tablero), `FinanceReportService` y `FinanceTransferService`. Si alguien agrega un consumidor nuevo, el test falla y obliga a decidir a propósito.
5. **Test de invariante.** Carga ingresos y gastos, saca una fotografía de **todo lo operativo** (tablero del mes y del ejercicio, serie de 12 meses, flujo de caja, presupuesto vs real, informe de la asamblea, movimientos del edificio), carga tres transferencias (incluidas dos del fondo de reserva) y exige que la fotografía sea **idéntica**. Se comprobó que el test falla si se quita el filtro.

**Qué sí ve las transferencias:** el saldo de cada cuenta, la conciliación bancaria (el depósito figura en el extracto), la pantalla del fondo de reserva (como aporte o retiro) y el Libro diario (un asiento, Debe el destino y Haber el origen, sin renglón de rubro ni IVA).

## 10. Otras protecciones

- **Todo o nada y sin carreras.** El alta y la anulación revisan el saldo y guardan en una transacción serializable: dos retiros simultáneos del fondo que juntos lo dejarían en negativo no pasan los dos (el test lo comprueba con dos conexiones).
- **Base de datos.** Dos triggers (`TR_FinanceTransfers_NoDelete`, `TR_FinanceTransfers_Protected`): no se borra una operación, no se le cambia importe, cuentas, fecha ni edificio, y una anulada no se reactiva. Va en la migración (y declarado en el modelo para que EF actualice bien al anular).
- **Anular** exige motivo (5 a 500 caracteres) y se frena si: el mes o el ejercicio están cerrados; la operación ya figura en una conciliación bancaria (hay que desmarcarla antes); o anularla dejaría el fondo de reserva en negativo.
- **Libro diario.** Anular una operación ya asentada aparece en «Cambios desde que se asentó» como «Transferencia anulada» con el motivo, y al asentar sale el contraasiento. La pantalla avisa antes de anular que eso va a pasar.
- **Historial de cambios** (Centro de configuración, sección Asientos contables): quién la cargó o anuló, cuándo y de qué monto.
- **Permisos:** la ven los cuatro roles administrativos; la cargan y anulan solo SuperAdmin y Administrador de empresa. Un edificio ajeno o sin Finanzas responde como el resto del módulo.

## 11. Límites conocidos

- El aviso de saldo negativo se calcula contra el **saldo de hoy**, no contra el saldo a la fecha de una operación atrasada.
- No hay adjuntos: el respaldo del fondo es un texto (número de acta o resolución).
- Las transferencias no se ven todavía en el informe para la asamblea como renglón propio: solo en los saldos por cuenta, con un aviso con el monto.
- La APP del propietario no se tocó.
