# Guía de pruebas — Votaciones, Fase 1 (integridad)

Fecha: 2026-11-11 · Rama: `feature/pagos-online` (BACK y WEB) · Migración: `20261111145617_VotingIntegrity` (+ `.sql`).
Plan de origen: rediseño del sistema de votación (Fase 1 de 3). La Fase 2 (cierre automático, avisos de cierre, acta PDF) y la Fase 3 (voto del propietario desde la APP) no están incluidas.

## Qué cambió

| Tema | Antes | Ahora |
|---|---|---|
| Quién gestiona | Cualquier usuario con acceso al edificio | Crear, editar, eliminar borrador, abrir, cerrar y anular: **Encargado, Administrador de empresa, SuperAdmin**. Cargar y quitar votos presenciales: además el **Operador**. Propietarios, inquilinos y porteros no acceden (403/404). |
| Votación ajena | 403 (revelaba que existía) | 404: no se revela que existe ni su estado. |
| Historial | Al cambiar o quitar un voto se borraba sin rastro | Cada voto, cambio y quita queda en `VoteCastEvents` (quién, cuándo, opción anterior y nueva, canal, titular de la unidad). No se puede modificar ni borrar. |
| Resultado al cerrar | Se recalculaba siempre con las unidades actuales | Se **congela** al cerrar (JSON + huella SHA-256 + totales). Cambiar coeficientes o unidades después no altera el resultado. |
| Anular | No existía | Una votación **abierta** se puede anular con motivo obligatorio; conserva votos e historial, queda sin opción aprobada. |
| Quórum | Siempre por cantidad de unidades | Según el peso: por unidades o **por coeficiente**. |
| Regla Simple | Sin ganadora explícita | Gana la opción con más peso; **empate = sin ganadora**; se aprueba solo si hay quórum. |
| Mayoría absoluta | Comparaba porcentajes redondeados | Compara valores exactos. |
| Concurrencia | Error 500 genérico | Voto y cierre simultáneos: 409 con mensaje claro y la pantalla se recarga. |
| Informativa | Por defecto solo fuera de Paraguay | **Por defecto en todos los países** (también PY). Las votaciones existentes no se tocaron. |
| Validaciones | Opciones vacías se guardaban | Opciones únicas (sin repetir, sin distinguir mayúsculas), entre 2 y 10, hasta 200 caracteres; título ≤ 200; descripción ≤ 1000. |

## Antes de probar
1. Aplicar `Condo.Infrastructure/Persistence/Migrations/20261111145617_VotingIntegrity.sql` en la base (o arrancar la API en local, que migra sola).
2. Tener un edificio con al menos 4 unidades activas con coeficiente, un propietario principal en alguna, y usuarios Encargado y Operador con acceso al edificio.

## Casos

### A. Permisos
1. Como **Operador**: en Votaciones no aparece «Nueva votación». En una votación abierta ve los botones de opción por unidad pero no «Cerrar» ni «Anular».
2. Como **Encargado**: crea, edita, abre, cierra y anula.
3. Como **Encargado de otro edificio**: la votación no aparece en la lista.

### B. Crear y validar
1. Crear con opciones «Sí» y «sí» → error de opciones repetidas.
2. Crear con 2 opciones válidas y una vacía → se guarda sin la vacía.
3. La casilla «Consulta informativa» viene marcada (también en Paraguay).

### C. Votar, cambiar, quitar e historial
1. Abrir la votación. En una unidad votar «A favor», luego «En contra», luego quitar el voto (×).
2. Aparece el botón de historial (reloj) con **3**; al abrirlo: «Votó: A favor», «Cambió: A favor → En contra», «Quitó el voto (En contra)», con el nombre de quien lo hizo, «Personal» y el titular de la unidad.
3. Volver a tocar la opción que ya tiene la unidad no agrega movimientos.

### D. Quórum y resultado
1. Votación **por coeficiente** con quórum 50 %: que vote solo una unidad con coeficiente mayor al 50 % → «Alcanzado» aunque haya votado 1 sola unidad.
2. Regla **Simple** con 2 unidades votando opciones distintas → «Empate en el primer puesto: no hay opción ganadora».
3. Regla **Doble mayoría absoluta**: el texto indica si alguna opción supera el 50 % de unidades y de coeficiente.

### E. Cierre congelado
1. Cerrar (pide confirmación). Se ve «Cerrada … por <nombre>» y «Resultado congelado al cerrar» con la huella.
2. Cambiar el coeficiente de una unidad o agregar una unidad nueva al edificio.
3. Volver a abrir el detalle: totales, porcentajes y unidades **no cambian**.
4. Una votación cerrada **antes** de esta versión muestra el aviso «cerrada antes del congelamiento… se calculan con las unidades actuales».

### F. Anular
1. En una votación abierta, «Anular» → pide motivo; sin motivo el botón queda deshabilitado.
2. Con motivo: estado «Anulada», se ve el motivo, no hay opción aprobada y ya no se pueden cargar votos.
3. Un borrador no se anula (se elimina); una cerrada no se anula.

### G. Concurrencia (dos navegadores)
1. Con la misma votación abierta en dos sesiones, cerrar en una y, sin recargar, votar en la otra → mensaje «La votación no está abierta» o «La votación cambió mientras guardabas…», y la pantalla se actualiza sola.

## Pruebas automáticas
- `dotnet test Condo.Tests/Condo.Tests.csproj --filter "FullyQualifiedName~Condo.Tests.Votes"` (21 en SQLite).
- Concurrencia con SQL Server real: definir `CONDO_TEST_SQLSERVER` (ver `docs/DESPLIEGUE_MARKETPLACE.md`) y correr el mismo filtro (23 en total).

## Despliegue (no ejecutado)
Orden: primero el `.sql` en el VPS con `sqlcmd -I -b`, después el backend, después el web. Sin la migración, el backend nuevo falla al leer `Votes`.
