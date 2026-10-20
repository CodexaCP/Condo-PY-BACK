# Guía: configuración de pagos online (Bancard) — despliegue y pruebas

**Alcance de esta etapa:** solo la **configuración**. La empresa carga su conexión con el comercio de Bancard (Tpago o vPOS), la asigna a edificios y la deja activada. **No se cobra nada todavía**: no existe el botón "Pagar ahora", ni el callback, ni la liquidación del pago online. Los propietarios siguen pagando como hasta ahora. Eso viene en las etapas siguientes (ver `PROPUESTA_PAGOS_ONLINE.md`, sección 5.2).

Dónde está: **Centro de configuración → sección "Pagos online"** (opcional, la ven los cuatro roles administrativos; la editan el SuperAdmin y el Administrador de toda la empresa; el Operador y el Encargado solo ven el estado).

## 1. Despliegue (en este orden)

### 1.1 Migración SQL (primero)

Archivo: `Condo.Infrastructure/Persistence/Migrations/20261012185825_PaymentConnections.sql`. Se pega en el VPS como las anteriores:

```bash
cat > /tmp/PaymentConnections.sql << 'EOF'
-- (pegar el contenido del archivo .sql)
EOF
/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'ServerSA*2026' -C -d CondoPY -I -b -i /tmp/PaymentConnections.sql
```

Debe terminar mostrando `PaymentConnections OK` con tres números (los ids de las tablas). Solo agrega 3 tablas e índices; no toca datos.

### 1.2 Clave maestra del servidor (antes de usar la pantalla)

Las claves privadas de Bancard se guardan **cifradas** (AES-GCM) con una clave maestra que **no está en la base ni en el repositorio**: vive en una variable de entorno del servicio. Sin ella la pantalla funciona pero **no deja guardar claves privadas** (avisa con un cartel).

Generar una clave (32 bytes en base64) y dársela al servicio:

```bash
openssl rand -base64 32
```

```bash
systemctl edit condo-py-api
```

En el editor escribir (con la clave generada) y guardar:

```ini
[Service]
Environment="Payments__MasterKey=PEGAR_AQUI_LA_CLAVE"
Environment="Payments__MasterKeyId=k1"
Environment="Payments__CallbackBaseUrl=https://api.expenses.com.py"
```

Luego reiniciar con el despliegue normal del backend (o `systemctl restart condo-py-api`).

**Muy importante sobre esta clave:**

- **Guardala en un lugar seguro aparte** (gestor de contraseñas). Si se pierde, las claves de Bancard ya cargadas **no se pueden recuperar** y hay que volver a cargarlas.
- **No la incluyas en el mismo respaldo que la base de datos**: si viajan juntas, el cifrado no sirve de nada.
- No la pegues en `appsettings.json` ni en el repositorio.
- Para **rotarla** más adelante: poner la nueva en `Payments__MasterKey` con otro `Payments__MasterKeyId` (por ejemplo `k2`) y dejar la anterior en `Payments__PreviousKeys__k1` para que se sigan leyendo las claves ya cifradas.
- `Payments__CallbackBaseUrl` es la dirección pública de la API: se usa para armar la dirección de callback que se registra en Bancard. Si no se define, se usa el host con el que se llamó a la API.

### 1.3 Backend y web

Despliegue normal del backend (`/opt/condo-py-src` → publish → rsync) y del web. El orden recomendado es: SQL → clave maestra → backend → web.

## 2. Qué hace cada cosa en pantalla

- **Nueva conexión** (solo quien edita): elegir proveedor y modalidad (Bancard · Tpago o vPOS 2.0), nombre, ambiente (**Sandbox** o **Producción**) y cargar los datos del comercio y las claves.
  - Tpago pide: código de comercio, código de sucursal, clave pública y clave privada.
  - vPOS pide: clave pública y clave privada.
  - Sandbox y producción tienen claves distintas: se cargan en **conexiones separadas**.
- **La clave privada es de solo escritura**: después de guardarla solo se ve `••••1234` y la fecha de la última carga. Para cambiarla se escribe una nueva; si se deja vacía, se conserva la actual.
- **Estados**: *Incompleta* (faltan datos) → *Pendiente de validación* (completa, sin habilitar) → *Activa* → *Desactivada*. Cambiar las credenciales o el ambiente de una conexión activa la devuelve a *Pendiente de validación* (se vuelve a activar a propósito).
- **Edificios**: se elige en cuáles de la empresa se usa la conexión. Un edificio puede tener varias conexiones y una conexión puede servir a varios edificios. Para **activar** hace falta que estén todos los datos obligatorios y al menos un edificio.
- **Dirección de callback**: es la dirección que después se registra en el portal de Bancard. **No la registres todavía**: hoy no recibe avisos.
- **Historial**: cada alta, cambio, reemplazo de clave, asignación, activación y eliminación queda registrado con quién y cuándo. El historial nunca contiene una clave.
- **Eliminar**: solo una conexión que no esté activa (primero se desactiva). Libera el nombre.

## 3. Qué probar

1. **Sin clave maestra**: antes de configurarla, abrir la sección: debe mostrar el cartel de "Falta la clave maestra". Crear una conexión *con* clave privada debe rechazarse con ese mismo motivo; sin clave privada se guarda como *Incompleta*.
2. **Alta**: con la clave maestra puesta, crear "Bancard Tpago (sandbox)" con todos los datos. Debe quedar *Pendiente de validación*, con la clave privada mostrada como `••••` y sus últimos 4 caracteres.
3. **La clave no se filtra**: en F12 → Network, mirar las respuestas de `payment-connections`: en ninguna aparece la clave completa. En el historial tampoco.
4. **Reemplazo**: editar dejando la clave vacía (se conserva) y luego escribiendo una nueva (cambia el final y queda en el historial como *SecretsReplaced*).
5. **Edificios y activación**: intentar activar sin edificios (debe explicar el motivo); asignar uno y activar. Quitarle todos los edificios a una conexión activa: vuelve a *Pendiente de validación*.
6. **Ambiente**: cambiar de Sandbox a Producción en una conexión activa: vuelve a *Pendiente de validación*. Al activar en Producción pide una confirmación extra.
7. **Nombre repetido**: crear dos conexiones con el mismo nombre en la empresa: la segunda se rechaza.
8. **Permisos**: como Operador o Encargado, la sección se ve en solo lectura (nombre, estado y sus edificios), **sin** datos del comercio, claves ni dirección de callback, y sin botones. Un Encargado solo ve las conexiones de sus edificios.
9. **Otra empresa**: un Administrador de otra empresa no ve estas conexiones.
10. **Resumen del Centro**: en la tarjeta "Pagos online" debe figurar *Completa* cuando hay una conexión activa para ese edificio y *Opcional* si no.
11. **Clave maestra distinta** (prueba técnica, opcional): cambiar `Payments__MasterKey` y reiniciar: intentar activar una conexión ya cargada debe fallar con un mensaje claro en lugar de dejar pasar claves ilegibles. Volver a poner la clave original.

## 4. Lo que todavía no hace (a propósito)

- Cobrar, redirigir al propietario o recibir avisos de Bancard.
- "Probar conexión": depende de lo que permita la API de Bancard; hay que confirmarlo con ellos (preguntas 3 y 5.5 de la propuesta).
- Decisiones abiertas antes de programar el cobro (sección 5.9 de la propuesta): a nombre de quién está el comercio, qué hacer si el importe cambia entre el cobro y la confirmación (recargo de mora), redondeo, comisiones y que sandbox nunca liquide deuda real.
