namespace ApiCore.Config;

/// <summary>
/// Configuración de la API, leída de appsettings.json, appsettings.Local.json y
/// variables de entorno (en ese orden, gana la última).
///
/// <para>Los defaults son los seguros: sin configurar nada, la API no confía en
/// ningún proxy y acepta cuerpos chicos. Abrir algo tiene que ser una decisión
/// escrita en la configuración, no un olvido.</para>
/// </summary>
public sealed class ApiConfig
{
    /// <summary>
    /// Redes (CIDR) desde las que se acepta <c>X-Forwarded-For</c>.
    ///
    /// <para>La API no se expone a internet: la llama el contenedor de Next desde
    /// sus Server Components y Server Actions, y es él quien reenvía la IP real
    /// del usuario. Acá va la red de Docker donde vive ese contenedor. Se declara
    /// la red y no la IP porque la de un contenedor cambia en cada recreación.</para>
    ///
    /// <para>Vacío significa que no se lee el header. Aceptarlo de cualquiera es
    /// peor que no leerlo: cualquiera se inventa la IP y esquiva el rate limit.</para>
    /// </summary>
    public string[] TrustedNetworks { get; init; } = [];

    /// <summary>
    /// Pedidos por minuto que acepta cada IP en toda la API. Es un techo grueso
    /// contra scripts; los límites finos (login, publicar pedidos, ofertar) van en
    /// cada endpoint.
    /// </summary>
    public int RateLimitPerMinute { get; init; } = 300;

    /// <summary>
    /// Tamaño máximo del cuerpo de un pedido HTTP. Kestrel acepta 30 MB por
    /// defecto, que es mucho para una API que recibe JSON: un cuerpo gigante es
    /// memoria y CPU gratis para el que ataca. Si más adelante un endpoint recibe
    /// fotos, se sube solo en ese endpoint.
    /// </summary>
    public long MaxRequestBodyBytes { get; init; } = 1_048_576;

    public SecurityConfig Security { get; init; } = new();
}

/// <summary>Contraseñas, sesiones y frenos de login.</summary>
public sealed class SecurityConfig
{
    // --- Sesiones ---------------------------------------------------------

    /// <summary>Días que dura una sesión desde que se inicia.</summary>
    public int SessionDays { get; init; } = 30;

    // --- Frenos de login y registro -----------------------------------------

    /// <summary>
    /// Intentos (login y registro) por IP por minuto, contados en la tabla
    /// login_attempts: sobrevive a un reinicio, a diferencia del rate limit
    /// global en memoria. 0 = apagado.
    /// </summary>
    public int MaxAttemptsPerIpPerMinute { get; init; } = 10;

    /// <summary>
    /// Contraseñas equivocadas seguidas contra UN MISMO email antes de frenar,
    /// dentro de EmailWindowMinutes. Es lo único que ve a una botnet repartida
    /// (mil IPs, un intento cada una, todas contra la misma cuenta). Cuenta solo
    /// fallos y un acierto reinicia la cuenta: si contara todo, cualquiera
    /// dejaría afuera a otro tirándole logins a su nombre. 0 = apagado.
    /// </summary>
    public int MaxFailedAttemptsPerEmail { get; init; } = 10;

    public int EmailWindowMinutes { get; init; } = 15;

    /// <summary>Días que se guarda el historial de intentos.</summary>
    public int LoginAttemptsRetentionDays { get; init; } = 90;

    /// <summary>
    /// Base offline de MaxMind para guardar de qué país vino cada intento.
    /// Relativa = al lado del ejecutable. Sin el archivo, el país queda en ZZ.
    /// </summary>
    public string GeoIpDatabasePath { get; init; } = "GeoLite2-Country.mmdb";

    // --- Argon2id -----------------------------------------------------------

    /// <summary>
    /// Hashes que corren a la vez. Cada uno reserva Argon2MemoryKib mientras
    /// calcula, así que el techo de memoria es el producto de los dos (10 ×
    /// 19 MiB ≈ 190 MB), lleguen 10 logins o 10.000. 0 = cantidad de núcleos.
    /// </summary>
    public int MaxConcurrentHashes { get; init; }

    /// <summary>Espera máxima por un turno de hash antes de contestar 503.</summary>
    public int HashQueueTimeoutSeconds { get; init; } = 3;

    /// <summary>Parámetros mínimos recomendados por OWASP para Argon2id.</summary>
    public int Argon2MemoryKib { get; init; } = 19456;
    public int Argon2Iterations { get; init; } = 2;
    public int Argon2Parallelism { get; init; } = 1;
}
