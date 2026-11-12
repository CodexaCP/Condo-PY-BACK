# Guía de pruebas: comandos de voz

Funciona para SuperAdmin, CompanyAdmin, CompanyOperator y BuildingManager. El intérprete son reglas propias del backend (sin IA): `GET api/voice-commands/interpret`.

## Dónde está el micrófono
- **WEB**: barra superior, ícono de micrófono (antes de las notificaciones). Abre un panel con selector de edificio, campo de texto y botón «Dictar». Para dictar hace falta **Chrome o Edge**; en otros navegadores queda solo el campo de texto.
- **APP (Encargado)**: inicio del Encargado, ícono de micrófono en la barra superior (página `/manager/voice`). El edificio es el elegido en el selector de edificios de la pantalla. En Android hay que **aceptar el permiso de micrófono** la primera vez.

## Frases de ejemplo
1. Período vacío: «crear período vacío», «crea un período nuevo sin gastos».
2. Período igual al anterior: «crear período igual al anterior», «copiá el período pasado».
3. Morosos por antigüedad: «morosos con 3 meses de atraso», «deudores de 15 días», «morosos con 2 semanas», «morosos con un año». Sin antigüedad («morosos») lista todos.

## Qué debe pasar
- Crear o clonar período: siempre muestra un resumen y pide **Confirmar / Cancelar**. Nada se crea hasta confirmar. El período propuesto es el mes siguiente al último del edificio (diciembre pasa a enero del año siguiente), con las mismas fechas que daría «clonar».
- Morosos: no pide confirmación; muestra una fila por unidad con propietario/responsable, «vencido desde», días de atraso, cantidad de períodos y deuda total. «Con 3 meses» es «al menos 3 meses» por calendario (el 12/11/2026 corta en 12/08/2026).
- Sin edificio elegido, morosos lista los de todos los edificios accesibles.

## Casos de error
- Crear período sin edificio elegido: «Elegí un edificio antes de crear el período.»
- Edificio sin períodos: avisa que todavía no tiene y que se cree el primero desde Períodos de expensas.
- «crear período nuevo» (sin decir vacío o igual): pregunta cuál de las dos opciones.
- Frase no entendida: sugiere los tres comandos.
- Navegador o teléfono sin reconocimiento de voz, o permiso denegado: se escribe el comando en el campo de texto.
- Si el mes ya está cerrado o el período ya existe, lo informa el endpoint real al confirmar.
