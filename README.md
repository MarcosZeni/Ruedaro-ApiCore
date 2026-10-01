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

## Endpoints

| Método y ruta | Sesión | Qué hace |
|---|---|---|
| `GET /health` | — | 200 si la base contesta, 503 si no |
| `POST /api/v1/auth/registro` | — | Crea la cuenta (comprador o revendedor) e inicia sesión |
| `POST /api/v1/auth/ingresar` | — | Inicia sesión |
| `POST /api/v1/auth/salir` | Sí | Cierra la sesión (borra la fila) |
| `GET /api/v1/auth/yo` | Sí | Datos del usuario |
| `PUT /api/v1/auth/yo` | Sí | Cambia nombre y WhatsApp |
| `GET /api/v1/cotizacion` | — | Dólar oficial del BCRA vigente (`valor`, `fecha`) |
| `GET /api/v1/publicaciones` | — | Catálogo público. Filtros: `vendedor` (`agencia`/`particular`), `tipo`, `marca`, `zona`, `hasta` (USD), `desde` (año), `limite` |
| `GET /api/v1/publicaciones/carrusel?vendedor=` | — | Hasta 10 al azar: destacados de esa sección y, si faltan, otros |
| `GET /api/v1/publicaciones/cantidades` | — | Avisos visibles por tipo |
| `GET /api/v1/publicaciones/{id}` | — | Un aviso visible |
| `GET/POST /api/v1/mis-pedidos` | Comprador | Sus pedidos / publicar uno |
| `GET /api/v1/mis-pedidos/{id}` | Comprador | Pedido con sus ofertas |
| `POST /api/v1/mis-pedidos/{id}/cerrar` | Comprador | Cerrar (`encontrado` o `ya_no_busca`) |
| `GET /api/v1/pedidos`, `/pedidos/{id}` | Revendedor | Pedidos activos (mismos filtros), con `yaOferte` |
| `POST /api/v1/pedidos/{id}/ofertas` | Revendedor | Ofertar (una vez por pedido, hasta 10 por pedido) |
| `GET /api/v1/mis-ofertas` | Revendedor | Sus ofertas con el pedido |
| `GET/POST /api/v1/mis-publicaciones` | Particular o agencia | Sus avisos con patente y cupo de destacados / publicar uno (409 `patente_existente`) |
| `POST /api/v1/mis-publicaciones/{id}/estado` | Particular o agencia | `activa`, `pausada` o `vendida` |
| `POST /api/v1/mis-publicaciones/{id}/destacar` | Particular o agencia | `{ destacada }`; 409 `sin_cupo` si no quedan lugares |
| `GET /api/v1/admin/resumen` | Admin | Números generales |
| `GET /api/v1/admin/usuarios?q=` | Admin | Usuarios, con búsqueda |
| `POST /api/v1/admin/usuarios/{id}/estado` | Admin | `activo` o `suspendido` |
| `POST /api/v1/admin/usuarios/{id}/plan` | Admin | `{ dias }`: plan de 10 destacados por esos días; 0 lo quita |
| `GET /api/v1/admin/{pedidos,ofertas,publicaciones}` | Admin | Todo el contenido |
| `POST /api/v1/admin/{pedidos,ofertas,publicaciones}/{id}/estado` | Admin | `visible` o `baja` |

Reglas que garantiza la API (no la pantalla): una oferta por revendedor y pedido (índice único), tope de 10 ofertas por pedido (transacción con `FOR UPDATE` sobre el pedido), WhatsApp obligatorio para ofertar y publicar, links solo `https://`. Suspender a un usuario corta sus sesiones y oculta todo lo suyo. Moderar es ocultar, nunca borrar. Cada monto guarda su equivalente en dólares con el dólar oficial del BCRA del momento (`CotizacionService`), así el filtro `hasta` compara pesos y dólares. La patente es única entre avisos vigentes (columna generada `patente_vigente` con índice único) y nunca sale en los endpoints públicos.

La sesión va en `Authorization: Bearer <token>`. La web guarda el token en una
cookie httpOnly y lo manda desde su servidor. Los errores vuelven como
`{ codigo, mensaje, campo }`, con el mensaje listo para mostrar.

**Administradores:** no se eligen al registrarse. Se asignan a mano en la base:

```sql
UPDATE usuarios SET rol = 'admin' WHERE email = 'tu@email.com';
```

**Geolocalización (opcional):** dejar `GeoLite2-Country.mmdb` de MaxMind al
lado del `.csproj` y reiniciar. Sin el archivo, los intentos se guardan con
país `ZZ`. No va al repo.

## Configuración

Orden de precedencia, de menor a mayor: `appsettings.json` <
`appsettings.Local.json` < variables de entorno < flags de línea de comandos.

| Clave | Para qué |
|---|---|
| `ConnectionStrings:Default` | Cadena de MySQL. En el servidor, por variable `ConnectionStrings__Default`. |
| `TrustedNetworks` | Redes CIDR desde las que se acepta `X-Forwarded-For` (la red de Docker de la web). Vacío = no se lee. |
| `RateLimitPerMinute` | Techo global de pedidos por minuto por IP. |
| `MaxRequestBodyBytes` | Tamaño máximo del cuerpo de un pedido (1 MB por defecto). |
| `Security:*` | Duración de la sesión, frenos de login, Argon2id y geolocalización. Detalle en `Config/ApiConfig.cs`. |

## Seguridad

- La API **no se expone a internet**: la llama solo el contenedor de Next.
- MySQL con dos usuarios: la API solo lee y escribe datos; el schema se cambia
  con otro usuario que solo entra desde localhost.
- SQL siempre con parámetros; nunca concatenar ni interpolar valores.
- Contraseñas con Argon2id (formato PHC, se re-hashean solas si suben los parámetros). Cupo de hashes simultáneos para acotar la memoria.
- Sesiones con token opaco de 32 bytes; la base guarda solo su SHA-256. Cerrar sesión o suspender al usuario la corta en el acto.
- Freno por IP (10 por minuto) y por email (10 contraseñas mal en 15 minutos), contados en `login_attempts`. Mismo mensaje y mismo tiempo de respuesta para "email inexistente" y "contraseña mal".
- MySQL en modo estricto (la API no arranca si no lo está).
- Secretos nunca en archivos versionados.

## Estructura

```
Config/      ApiConfig.cs          configuración tipada
Data/        Database.cs           conexiones a MySQL
             SchemaCheck.cs        verificación de la base al arrancar
             UserRepository.cs, SessionRepository.cs, LoginAttemptRepository.cs
             PedidoRepository.cs, OfertaRepository.cs, PublicacionRepository.cs, Lectura.cs
Endpoints/   HealthEndpoints.cs    /health
             AuthEndpoints.cs      /api/v1/auth
             PedidoEndpoints.cs, PublicacionEndpoints.cs, AdminEndpoints.cs
             ValidarContenido.cs   validación de pedidos, ofertas y avisos
             SesionActual.cs       .RequireSesion() y errores
Security/    PasswordHasher, HashGate, GeoIpResolver (de LoginServer),
             AttemptThrottle, SessionToken, Validacion, IpAddresses
Models/      DTOs (sealed record) y Catalogo.cs (tipos, marcas, zonas: igual que en la web)
sql/         migraciones, a mano y en orden
```
