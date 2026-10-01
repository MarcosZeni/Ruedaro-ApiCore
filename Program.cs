using System.Threading.RateLimiting;
using ApiCore.Config;
using ApiCore.Data;
using ApiCore.Endpoints;
using ApiCore.Security;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// Orden de precedencia, de menor a mayor:
//
//   appsettings.json  <  appsettings.Local.json  <  variables de entorno  <  --flags
//
// En configuración gana el último que se registra. CreateBuilder ya dejó las
// variables de entorno y los flags arriba de todo, así que agregar un archivo
// DESPUÉS los tapa: un appsettings.Local.json olvidado en el servidor haría que
// ConnectionStrings__Default se ignore en silencio. Por eso el archivo local se
// mete primero y las fuentes de arriba se vuelven a registrar.
//
// ⚠ En Linux el nombre distingue mayúsculas: `appsettings.local.json` en
// minúscula es otro archivo y se ignora sin decir nada.
// -----------------------------------------------------------------------------
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

// ASPNETCORE_URLS necesita su propio registro con el prefijo: Kestrel lee la
// clave "Urls" pelada, y sin quitar el prefijo el "Urls" de appsettings.json le
// gana. El síntoma: pedís 0.0.0.0 en el contenedor y arranca en 127.0.0.1.
builder.Configuration.AddEnvironmentVariables("ASPNETCORE_");
builder.Configuration.AddCommandLine(args);

var config = builder.Configuration.Get<ApiConfig>() ?? new ApiConfig();

// En el servidor la cadena va por variable de entorno (ConnectionStrings__Default),
// así la contraseña de MySQL no queda en un archivo.
string connectionString = builder.Configuration.GetConnectionString("Default") ?? "";

if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Falta la cadena de conexión: poné ConnectionStrings:Default en appsettings.Local.json " +
        "o la variable de entorno ConnectionStrings__Default.");

var database = new Database(connectionString);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<SchemaCheck>();
builder.Services.AddSingleton(config.Security);
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<HashGate>();
builder.Services.AddSingleton<GeoIpResolver>();
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<SessionRepository>();
builder.Services.AddSingleton<LoginAttemptRepository>();
builder.Services.AddSingleton<AttemptThrottle>();

// Errores: ProblemDetails genérico, sin stack trace ni mensaje interno hacia afuera.
builder.Services.AddProblemDetails();

builder.WebHost.ConfigureKestrel(options =>
{
    // No anunciar qué servidor es: no frena a nadie, pero tampoco hay por qué
    // regalar el dato.
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = config.MaxRequestBodyBytes;
});

// -----------------------------------------------------------------------------
// IP real del cliente
//
// La API no está expuesta a internet: la llama el contenedor de Next, y
// RemoteIpAddress es la IP de ESE contenedor, igual para todos los usuarios. La
// web reenvía la IP del usuario en X-Forwarded-For, y acá se acepta solo si el
// pedido viene de una red declarada en TrustedNetworks. Sin esto, el rate limit
// cuenta a todo el mundo como uno solo.
//
// ForwardLimit = 1 (el default): se toma solo el último valor del header, que es
// el que escribió la web. Lo que venga antes lo pudo haber inventado cualquiera.
// -----------------------------------------------------------------------------
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();

    foreach (string red in config.TrustedNetworks)
    {
        // Una red mal escrita se rechaza al arrancar: ignorarla en silencio
        // dejaría el rate limit contando a todos como uno sin que nadie se entere.
        if (!System.Net.IPNetwork.TryParse(red, out var network))
            throw new InvalidOperationException(
                $"TrustedNetworks: '{red}' no es una red válida. Formato CIDR, por ejemplo 172.16.0.0/12.");

        options.KnownIPNetworks.Add(network);
    }
});

// -----------------------------------------------------------------------------
// Rate limit global por IP
//
// Techo grueso contra scripts, en memoria. Los límites finos (login, publicar
// pedidos, ofertar) se agregan en cada endpoint y cuentan en la base, para que
// sobrevivan a un reinicio.
// -----------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.RateLimitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

// El orden importa: primero se corrige la IP, después se cuenta por IP.
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseRateLimiter();

// Cabeceras de seguridad en todas las respuestas. Es una API JSON, así que la
// política es la más cerrada: nada se ejecuta, nada se embebe, nada se cachea.
// Un endpoint que necesite cache (un listado público) lo pisa en su respuesta.
// HSTS lo pone nginx, que es quien termina el TLS.
app.Use(async (http, next) =>
{
    var headers = http.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    headers["Referrer-Policy"] = "no-referrer";
    headers.CacheControl = "no-store";
    await next();
});

app.MapHealth();
app.MapAuth();

var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Arranque");

var schema = await app.Services.GetRequiredService<SchemaCheck>().RunAsync(CancellationToken.None);

if (!schema.Alcanzable)
{
    // Puede ser que MySQL todavía esté levantando: se sigue y /health lo refleja.
    startupLog.LogWarning(
        "No se pudo conectar a MySQL para verificar el schema ({Error}). La API arranca igual; " +
        "/health va a dar 503 hasta que la base conteste.", schema.ErrorConexion);
}
else if (!schema.Ok)
{
    startupLog.LogCritical(
        "La base no está lista:\n  - {Problemas}\nCorré los scripts pendientes de sql/ en orden.",
        string.Join("\n  - ", schema.Problemas));
    return 1;
}

// El techo de memoria de los hashes, a la vista: es un número que conviene
// conocer antes de que lo descubra el servidor.
var gate = app.Services.GetRequiredService<HashGate>();
startupLog.LogInformation(
    "Argon2id: hasta {Slots} hashes a la vez, techo de ~{Mb} MB.", gate.MaxConcurrent, gate.MemoryCeilingMb(config.Security));

// Geolocalización: la base no viene en el repo y sin ella todo funciona igual,
// así que se dice cuál de los dos casos es.
var geo = app.Services.GetRequiredService<GeoIpResolver>();
if (geo.Activo)
    startupLog.LogInformation("Geolocalización activa desde {Ruta}", geo.Ruta);
else
    startupLog.LogInformation(
        "Sin geolocalización: no está {Ruta}. Los intentos se guardan con país ZZ. " +
        "Es GeoLite2-Country de MaxMind (~9 MB, cuenta gratis); se deja ahí y se reinicia.", geo.Ruta);

if (config.TrustedNetworks.Length == 0)
    startupLog.LogInformation(
        "TrustedNetworks vacío: no se lee X-Forwarded-For y cada pedido cuenta con la IP de la conexión.");

await app.RunAsync();
return 0;
