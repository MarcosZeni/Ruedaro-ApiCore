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
}
