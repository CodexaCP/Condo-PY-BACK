# Guía de pruebas: QR de verificación en la liquidación (opción A)

Fecha: 2026-11-09 · Diseño: `docs/PROPUESTA_QR_LIQUIDACION.md`

## Qué se hizo
- El PDF de la liquidación lleva un QR (y la dirección corta debajo) que abre `https://expenses.com.py/v/<código>`.
- El código es aleatorio de 128 bits y se crea **al descargar el PDF**, también para las liquidaciones que ya existían. No hay nada que cargar a mano.
- La página pública (`/v/<código>`, ES/PT/EN, sin login) dice si el documento es **auténtico y vigente**, está **en borrador**, fue **reemplazado**, **anulado** o ya **no existe**. Solo muestra edificio, administración, período, total general y fecha de emisión.
- Mientras la liquidación no cambie, el mismo papel y el mismo QR valen siempre. Si se recalcula o se publica, el QR del papel anterior pasa a decir «reemplazado».

## Antes de probar (VPS)
1. Correr, en este orden, las dos migraciones de `Condo.Infrastructure/Persistence/Migrations/`:
   - `20261109141541_DocumentVerification.sql` (tabla nueva)
   - `20261109150521_BuildingQrShowBankAccounts.sql` (una columna nueva en Edificios, apagada por defecto)
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

## Botones de la página del QR (solo en un documento vigente)
La página que abre el QR trae, debajo de los datos, estos botones. Se muestran solo si el documento es «auténtico y vigente»; en borrador, reemplazado o anulado no aparece ninguno.

| Botón | Qué hace | Cuándo aparece |
|-------|----------|----------------|
| 🔐 Ver mi estado de cuenta | Despliega: «Abrí la app Expenses e ingresá con tu usuario» (más descarga de la app) y, para la administración, «Ingresar en la web» | Siempre |
| 💬 Hablar con el encargado del edificio | Abre WhatsApp con el mensaje armado («Hola, tengo una consulta sobre la liquidación de expensas de Octubre 2026 del edificio Torre Sol») al **encargado del edificio**: el usuario con rol Encargado de edificio y acceso activo a ese edificio (usa su WhatsApp o, si no, su prefijo + teléfono). Si hay varios, el primero asignado que tenga teléfono | Si hay un encargado con teléfono. Si no, el botón dice «Hablar con la administración» y usa el WhatsApp del edificio o, por último, el teléfono del administrador |
| 🏦 Datos para transferir | Banco, tipo, N.º de cuenta, titular, documento y alias, con botón Copiar | Solo si el edificio activó el interruptor **y** tiene cuentas activas |
| 📱 Descargar la app | Baja el APK | Si está configurado `App:ApkUrl` (ya lo está) |
| 📅 Agregar el vencimiento a mi calendario | Baja un archivo `.ics` de todo el día en la fecha de vencimiento, con aviso a las 9:00 del día anterior | Siempre que el período tenga vencimiento |

El interruptor de las cuentas está en **Edificios → editar → Contabilidad y pagos → «Cuentas bancarias de cobro»**: «Mostrar estas cuentas como Datos para transferir…». Apagado por defecto, lo cambia quien edita los datos financieros (no el encargado).

| # | Qué hacer | Resultado esperado |
|---|-----------|--------------------|
| 13 | Escanear el QR de una liquidación publicada de un edificio con WhatsApp cargado | Aparece «Hablar con la administración» y abre WhatsApp con el mensaje |
| 13b | Edificio con un encargado asignado (Usuarios → Encargado de edificio, con teléfono) | El botón dice «Hablar con el encargado del edificio» y abre WhatsApp con el número del encargado |
| 14 | Mismo edificio, interruptor de cuentas **apagado** | No aparece «Datos para transferir» |
| 15 | Encender el interruptor, guardar, recargar la página del QR | Aparece «Datos para transferir» con las cuentas activas y se pueden copiar |
| 16 | Tocar «Agregar el vencimiento a mi calendario» | Baja `vencimiento-AAAA-MM-DD.ics`; al abrirlo crea un evento de todo el día |
| 17 | Tocar «Ver mi estado de cuenta» | Se despliega el texto de la app; «Ingresar en la web» pide login y, al entrar, vuelve a Estado de cuenta |
| 18 | Probar el QR de un borrador o de un papel reemplazado | Sin botones |

## Cosas a tener presentes
- **Propietarios y la web:** los propietarios no usan la web (la web les niega el acceso), usan la app. Por eso «Ver mi estado de cuenta» les indica abrir la app. Abrir la app directamente desde el QR necesita enlaces profundos en la APP y recompilar el APK: no está hecho.
- El login de la web ahora vuelve a donde ibas (`?returnUrl=`): sirve para este botón y también cuando se vence la sesión. Solo acepta rutas internas.
- El QR de las liquidaciones **ya impresas o colgadas** no existe: solo aparece al volver a descargar el PDF.
- No hay botón para anular un código a mano todavía (la columna existe en la tabla); si el período se elimina, la página dice «ya no está disponible».
- El límite de consultas públicas es por ruta (300 por minuto en total), porque detrás del proxy todos comparten IP.
- Pendiente (no hecho): QR en el certificado de deudas y el recibo, un interruptor por edificio para apagar el QR completo (hoy solo está el de las cuentas) y abrir la app directo desde el QR.
