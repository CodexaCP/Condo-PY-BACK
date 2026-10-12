# Guía de pruebas: carga masiva de propietarios y de residentes

Son dos cargas **independientes**: cada una tiene su plantilla, su vista previa y su confirmación. Ninguna exige la otra.

- **Propietarios**: pantalla *Propietarios* → botón **Importar desde Excel**.
- **Residentes**: pantalla *Residentes* → botón **Importar desde Excel**.

Quién puede: el **SuperAdmin** (elige la empresa) y el **Administrador de toda la empresa**. Un administrador acotado a un condominio, un Encargado o un Operador no ven el botón y el servidor les responde 403.

No hay migración SQL. Solo hace falta desplegar el backend y el web.

## Cómo funciona (los dos casos)

1. **Descargar plantilla**: viene con una hoja de datos, una hoja *Edificios* (de ahí sale la lista desplegable) y una hoja *Unidades* con los códigos exactos para consultar.
2. **Elegir el archivo completado**: se valida solo, sin guardar, y muestra la vista previa fila por fila.
3. **Importar**: se habilita solo si **todas** las filas están bien. Si hay un error no se crea nada (todo o nada).

Reglas comunes:

- **Una fila por persona y unidad.** Si una persona tiene 3 unidades, se repiten sus datos en 3 filas: se crea **una sola** persona y se le asignan las 3 unidades.
- **Edificio y Unidad son opcionales, pero van juntos.** Sin ellos solo se da de alta a la persona.
- **Si la persona ya existe en la empresa** (mismo documento), no se duplica: solo se le asigna la unidad. Si ya estaba asignada a esa unidad, la fila dice *Ya estaba asignada* y no se toca. Por eso se puede volver a subir el mismo archivo sin riesgo.
- Los documentos se comparan sin puntos ni espacios (`1.234.567` = `1234567`).
- Máximo 2000 filas y 2 MB por archivo.
- Si hay errores, el botón **Descargar los errores (CSV)** baja las filas rechazadas con su motivo.

## Propietarios

Columnas: Nombre\*, Apellido\*, Tipo de documento, Número de documento\*, Correo\*, Teléfono, Usuario, Edificio, Unidad, % titularidad, Principal.

- **Contraseña inicial**: `Temporal*<año en curso>` (ej. `Temporal*2026`, con el año a hora de Paraguay). Es la misma para todas las cuentas nuevas y deben cambiarla al ingresar. La vista previa y el resultado la muestran.
- **Usuario**: si se deja vacío se arma con lo que está antes de la `@` del correo; si ya existe se le agrega un número (`ana`, `ana2`…).
- **Reglas de la unidad**: máximo 2 propietarios; los porcentajes de titularidad no pueden sumar más de 100; un solo propietario principal. *Principal* vacío = Sí si la unidad todavía no tiene principal.
- Al asignar un principal se hace el mismo traspaso de saldo a favor retenido que al asignarlo a mano.
- **No** crea fichas de residente: eso es la otra carga.

## Residentes

Columnas: Nombre completo\*, Tipo de documento, Número de documento\*, Correo\*, Teléfono\*, Relación, Edificio, Unidad, Desde, Principal, Vence el contrato.

- Mismas reglas del alta manual: correo y teléfono obligatorios.
- **Desde** vacío = hoy. **Principal** vacío = Sí si la unidad todavía no tiene residente principal vigente.
- **Vence el contrato** solo con la relación *Inquilino*.
- Si el correo coincide con una cuenta de la empresa, la ficha queda vinculada a ella (igual que en el alta manual).

## Qué probar

1. **Camino feliz (propietarios)**: bajar la plantilla, completar 5 propietarios (uno con 2 unidades, dos copropietarios de la misma unidad con 60 % y 40 %, uno sin unidad). Validar, confirmar, y comprobar en *Propietarios* y en *Asignaciones*. Ingresar con uno de ellos con `Temporal*<año>` y verificar que pide cambiar la contraseña.
2. **Repetir el mismo archivo**: debe decir 0 personas nuevas y todas *Ya estaba asignada*.
3. **Errores**: poner un correo mal escrito, una unidad que no existe, un tercer propietario en una unidad, porcentajes que suman 120, un RUC con dígito verificador incorrecto. Cada fila debe explicar el motivo, el botón *Importar* debe quedar bloqueado y no debe crearse nada. Probar **Descargar los errores (CSV)**.
4. **Correo ya usado por personal**: usar el correo de un Administrador o un Encargado: debe rechazarse.
5. **Residentes**: el mismo recorrido con la plantilla de residentes, incluyendo un inquilino con vencimiento de contrato y un correo que coincida con un propietario ya cargado.
6. **Independencia**: cargar residentes sin haber cargado propietarios (y al revés): ambas deben funcionar.
7. **Permisos**: como Encargado no debe verse el botón. Como SuperAdmin debe pedir la empresa antes de habilitar la plantilla.
8. **Plantilla equivocada**: subir la plantilla de residentes en la pantalla de propietarios: debe rechazarla con un mensaje claro.
