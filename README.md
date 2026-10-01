# ApiCore

Backend de pedidos-autos: .NET 10, Minimal APIs, MySQL 8.

La documentación del proyecto (idea, MVP, stack y convenciones) está en el repo
`Web`, en `docs/`. La fuente de verdad técnica es `Web/docs/stack.md`.

## Correr en desarrollo (Windows)

Se prueba en Windows, sin Docker ni Linux. Docker es solo para el servidor.

**`iniciar-api.bat`** prende la base y levanta la API en `http://127.0.0.1:5080`
(probar `/health`). La base la prende **`iniciar-mysql.bat`**, que se puede usar solo.

La base es una instancia propia de MySQL 8, aparte de las del juego:

| | |
|---|---|
| Servicio | `MySQL8Autos`, arranque manual. El `.bat` lo instala la primera vez (pide UAC). |
| Puerto | `127.0.0.1:3308`, solo loopback |
| Configuración | `D:\MySQL\my-3308.ini` |
| Datos | `D:\MySQL\data3308` |
| Claves | `D:\MySQL\credenciales-3308.txt`, fuera del repo |

`appsettings.Local.json` (fuera de git) tiene la cadena de conexión con
`pedidos_api`. Si falta, copiar `appsettings.Local.example.json` y completar la
clave.

### Migraciones

Scripts de `sql/` en orden, a mano. `000` se corre una vez por servidor como
root: crea la base y los dos usuarios con claves generadas por MySQL, que se
muestran una sola vez. El resto, con `pedidos_migraciones`:

```bat
"C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe" -h 127.0.0.1 -P 3308 -u pedidos_migraciones -p pedidos_autos < sql\002_xxx.sql
```

Y sumar las tablas y columnas nuevas a `Data/SchemaCheck.cs`.

Al arrancar, la API revisa que la base tenga todas las tablas y columnas que
usa. Si falta una migración, lo dice y no arranca. Si MySQL todavía no
contesta, avisa y arranca igual; `/health` da 503 hasta que la base responda.

## Configuración

Orden de precedencia, de menor a mayor: `appsettings.json` <
`appsettings.Local.json` < variables de entorno < flags de línea de comandos.

| Clave | Para qué |
|---|---|
| `ConnectionStrings:Default` | Cadena de MySQL. En el servidor, por variable `ConnectionStrings__Default`. |
| `TrustedNetworks` | Redes CIDR desde las que se acepta `X-Forwarded-For` (la red de Docker de la web). Vacío = no se lee. |
| `RateLimitPerMinute` | Techo global de pedidos por minuto por IP. |
| `MaxRequestBodyBytes` | Tamaño máximo del cuerpo de un pedido (1 MB por defecto). |

## Seguridad

- La API **no se expone a internet**: la llama solo el contenedor de Next.
- MySQL con dos usuarios: la API solo lee y escribe datos; el schema se cambia
  con otro usuario que solo entra desde localhost.
- SQL siempre con parámetros; nunca concatenar ni interpolar valores.
- MySQL en modo estricto (la API no arranca si no lo está).
- Secretos nunca en archivos versionados.

## Estructura

```
Config/      ApiConfig.cs          configuración tipada
Data/        Database.cs           conexiones a MySQL
             SchemaCheck.cs        verificación de la base al arrancar
Endpoints/   HealthEndpoints.cs    /health
Models/      DTOs (sealed record)
sql/         migraciones, a mano y en orden
```
