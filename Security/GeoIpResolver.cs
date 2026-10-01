using System.Net;
using System.Net.Sockets;
using ApiCore.Config;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;

namespace ApiCore.Security;

/// <summary>
/// De qué país vino una IP, resuelto <b>local</b> contra la base de MaxMind.
///
/// <para><b>Offline a propósito.</b> Un servicio HTTP tipo <c>ip-api.com</c>
/// metería una llamada de red bloqueante adentro del camino del login — la
/// operación que estamos protegiendo— y el día que ese servicio se pone lento se
/// cae el login entero. Contra el archivo <c>.mmdb</c> la respuesta son
/// microsegundos y no depende de nadie.</para>
///
/// <para><b>Sin la base, esto no falla: devuelve null.</b> El archivo pesa 9 MB y
/// hay que bajarlo con una cuenta gratis de MaxMind, así que no está en el repo.
/// El servicio arranca igual y los intentos quedan con <c>pais</c> en NULL, que es
/// exactamente lo que pasaba antes de que esto existiera.</para>
///
/// <para><b>Por qué el país se congela al insertar y no se calcula al consultar.</b>
/// La base de MaxMind se actualiza: un rango que hoy es AR el año que viene puede
/// figurar en otro país. Si el país se resolviera al leer el historial, actualizar
/// el <c>.mmdb</c> reescribiría el pasado — y el historial existe justamente para
/// que el pasado no cambie.</para>
/// </summary>
public sealed class GeoIpResolver : IDisposable
{
    private readonly DatabaseReader? _reader;
    private readonly ILogger<GeoIpResolver> _log;

    /// <summary>Ruta que se intentó abrir, para poder decirla en el arranque.</summary>
    public string Ruta { get; }

    /// <summary>Hay base cargada. En false, <see cref="Pais"/> siempre da null.</summary>
    public bool Activo => _reader is not null;

    public GeoIpResolver(ApiConfig config, ILogger<GeoIpResolver> log)
    {
        _log = log;
        Ruta = ResolverRuta(config.Security.GeoIpDatabasePath);

        if (string.IsNullOrWhiteSpace(Ruta) || !File.Exists(Ruta))
            return;

        try
        {
            // El reader por defecto mapea el archivo en memoria y es seguro de usar
            // desde varios hilos a la vez, que es lo que hace falta: esto se llama
            // desde cada request.
            _reader = new DatabaseReader(Ruta);
        }
        catch (Exception ex)
        {
            // Un .mmdb corrupto o de un formato que esta versión no entiende no
            // puede impedir que la API levante: el país es un dato del historial,
            // no parte de dejar entrar a nadie.
            log.LogWarning(ex, "No se pudo abrir la base de geolocalización {Ruta}", Ruta);
        }
    }

    /// <summary>
    /// Lo que se escribe cuando no se pudo saber el país.
    ///
    /// <para><c>ZZ</c> no es un país: es el código que ISO 3166-1 deja reservado
    /// para uso propio y que todo el mundo usa para "desconocido". Se guarda eso
    /// y no NULL para que la columna signifique una sola cosa: <b>NULL es una fila
    /// escrita antes de que existiera la geolocalización</b>, <c>ZZ</c> es una
    /// donde se preguntó y no se supo. Con NULL para las dos, un "¿de dónde entró
    /// esta cuenta?" mezcla las que no se pudieron resolver con las de otra
    /// época.</para>
    /// </summary>
    public const string Desconocido = "ZZ";

    /// <summary>
    /// Código ISO de dos letras, o <see cref="Desconocido"/>.
    ///
    /// <para><b>Esto no tira nunca.</b> No es amabilidad: el país es un dato del
    /// historial y el que llama está en el medio de un login. Un <c>.mmdb</c>
    /// truncado, una versión de formato que esta librería no entienda o un reader
    /// ya liberado no pueden costar ni una fila del registro, y mucho menos un
    /// login. Por eso el catch de abajo agarra todo y no una lista de tipos: la
    /// lista se queda corta justo con la excepción que no vimos venir —
    /// <c>InvalidDatabaseException</c>, por ejemplo, no hereda de
    /// <c>GeoIP2Exception</c>.</para>
    /// </summary>
    public string Pais(IPAddress ip)
    {
        if (_reader is null) return Desconocido;

        // Las privadas se descartan antes de preguntar. MaxMind contesta con una
        // excepción para estos rangos, y armar una excepción por cada login desde
        // localhost —el escenario de desarrollo entero— cuesta mucho más que este
        // par de comparaciones.
        if (EsPrivada(ip)) return Desconocido;

        try
        {
            return _reader.Country(ip).Country.IsoCode ?? Desconocido;
        }
        catch (AddressNotFoundException)
        {
            // La IP no está en la base. Normal y frecuente: no se loguea, o un
            // barrido de rangos raros llena el log él solo.
            return Desconocido;
        }
        catch (Exception ex)
        {
            // Cualquier otra cosa se avisa UNA vez. Si la base quedó corrupta, el
            // problema es el mismo en todos los pedidos y un warning por login
            // taparía el resto del log justo cuando hace falta leerlo.
            if (Interlocked.Exchange(ref _avisado, 1) == 0)
                _log.LogWarning(ex,
                    "La geolocalización dejó de funcionar contra {Ruta}. Los intentos se " +
                    "siguen registrando con el país en '{Desconocido}'; este aviso no se repite.",
                    Ruta, Desconocido);

            return Desconocido;
        }
    }

    /// <summary>Ya se avisó de una falla inesperada del lector. 0/1 en vez de bool
    /// para poder ganarle la carrera a los otros hilos con un Interlocked.</summary>
    private int _avisado;

    /// <summary>
    /// Rangos que nunca van a estar en la base: loopback, link-local y las tres
    /// redes privadas de la RFC 1918, más sus equivalentes IPv6.
    /// </summary>
    private static bool EsPrivada(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            return b[0] switch
            {
                10 => true,
                127 => true,
                169 when b[1] == 254 => true,          // link-local
                172 when b[1] >= 16 && b[1] <= 31 => true,
                192 when b[1] == 168 => true,
                0 => true,                              // "esta red"
                _ => false,
            };
        }

        // IPv6: únicas locales (fc00::/7) y link-local (fe80::/10).
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            byte primero = ip.GetAddressBytes()[0];
            return (primero & 0xFE) == 0xFC;
        }

        return false;
    }

    /// <summary>Una ruta relativa se busca al lado del ejecutable, no en el
    /// directorio de trabajo: un servicio de systemd arranca con el que le toque
    /// y ahí el archivo no está.</summary>
    private static string ResolverRuta(string configurada)
    {
        if (string.IsNullOrWhiteSpace(configurada)) return "";

        return Path.IsPathRooted(configurada)
            ? configurada
            : Path.Combine(AppContext.BaseDirectory, configurada);
    }

    public void Dispose() => _reader?.Dispose();
}
