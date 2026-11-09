# Guía de pruebas: QR de verificación en la liquidación (opción A)

Fecha: 2026-11-09 · Diseño: `docs/PROPUESTA_QR_LIQUIDACION.md`

## Qué se hizo
- El PDF de la liquidación lleva un QR (y la dirección corta debajo) que abre `https://expenses.com.py/v/<código>`.
- El código es aleatorio de 128 bits y se crea **al descargar el PDF**, también para las liquidaciones que ya existían. No hay nada que cargar a mano.
- La página pública (`/v/<código>`, ES/PT/EN, sin login) dice si el documento es **auténtico y vigente**, está **en borrador**, fue **reemplazado**, **anulado** o ya **no existe**. Solo muestra edificio, administración, período, total general y fecha de emisión.
- Mientras la liquidación no cambie, el mismo papel y el mismo QR valen siempre. Si se recalcula o se publica, el QR del papel anterior pasa a decir «reemplazado».

## Antes de probar (VPS)
1. Correr la migración `Condo.Infrastructure/Persistence/Migrations/20261109141541_DocumentVerification.sql` (tabla nueva, no toca datos).
2. Desplegar el backend y el web.

## Casos de prueba
| # | Qué hacer | Resultado esperado |
|---|-----------|--------------------|
| 1 | Descargar el PDF de una liquidación **ya existente y publicada** (modelo estándar) | Al pie, a la derecha, el QR con «Verificá este documento» y la dirección corta |
| 2 | Escanear ese QR con el celular | Página verde «Documento auténtico y vigente» con edificio, período, total y fecha |
| 3 | Comparar el total de la página con el «total general» del papel | Coinciden |
| 4 | Descargar el mismo PDF otra vez | Mismo código (la dirección impresa no cambia) |
| 5 | Descargar el PDF de una liquidación **en borrador** | La página dice «Borrador: todavía no está publicado» (amarillo) |
| 6 | Publicar ese período y volver a descargar | QR nuevo. Escanear el del borrador ahora dice «Este documento fue reemplazado» |
| 7 | Cambiar un gasto, recalcular y descargar | Igual: el papel viejo dice «reemplazado» y el nuevo «vigente» |
| 8 | Abrir `/v/AAAAAAAAAAAAAAAAAAAAAA` o `/v/cualquiercosa` | «No encontramos este documento» (rojo), mismo mensaje para cualquier código |
| 9 | Cambiar el idioma en la página | Se traduce a PT y EN |
| 10 | Edificio con **papel propio**: descargar la liquidación | **Sin QR** (arranca oculto para no pisar el diseño del papel) |
| 11 | Calibración del papel → sección «QR» → destildar «No dibujar», moverlo y ajustar el ancho (lado) | El PDF de calibración muestra el QR en esa posición; luego las liquidaciones lo traen ahí |
| 12 | Desde la app del propietario, descargar la liquidación publicada | También lleva QR (usa el mismo endpoint) |

## Cosas a tener presentes
- El QR de las liquidaciones **ya impresas o colgadas** no existe: solo aparece al volver a descargar el PDF.
- No hay botón para anular un código a mano todavía (la columna existe en la tabla); si el período se elimina, la página dice «ya no está disponible».
- El límite de consultas públicas es por ruta (300 por minuto en total), porque detrás del proxy todos comparten IP.
- Pendiente (no hecho): QR en el certificado de deudas y el recibo, y el interruptor por edificio del documento de diseño.
