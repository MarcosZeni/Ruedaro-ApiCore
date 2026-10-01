using System.Net;

namespace ApiCore.Security;

public static class IpAddresses
{
    /// <summary>
    /// Una IPv4 que llega como IPv6 mapeada (::ffff:1.2.3.4) se guarda como
    /// IPv4. Si no, la misma persona contaría como dos IPs distintas según por
    /// dónde entró, y los frenos por IP se partirían a la mitad.
    /// </summary>
    public static IPAddress Normalize(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    /// <summary>
    /// IP real del cliente. Detrás de la web (y de nginx) solo es real si
    /// <c>TrustedNetworks</c> está configurado: sin eso, es la IP del
    /// contenedor de Next, igual para todos.
    /// </summary>
    public static IPAddress Client(HttpContext http) => Normalize(http.Connection.RemoteIpAddress ?? IPAddress.None);
}
