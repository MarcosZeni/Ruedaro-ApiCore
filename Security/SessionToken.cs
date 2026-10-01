using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ApiCore.Security;

/// <summary>
/// Token de sesión: 32 bytes al azar, opaco.
///
/// <para><b>La base guarda solo el SHA-256.</b> Quien lea la tabla de sesiones
/// (un backup filtrado, una consulta de más) no puede usar nada de lo que ve.
/// Acá el hash rápido está bien, a diferencia de las contraseñas: contra 256 bits
/// de azar real no hay fuerza bruta posible, así que no hace falta Argon2.</para>
///
/// <para><b>Por qué opaco y no JWT.</b> Un JWT no se puede revocar antes de que
/// venza: "cerrar sesión" o suspender a alguien no lo invalida. Con un token
/// que se busca en la base, borrar la fila corta la sesión en el acto.</para>
/// </summary>
public static class SessionToken
{
    private const int TokenBytes = 32;

    /// <summary>Token nuevo, en base64url (va en una cookie y en un header).</summary>
    public static string Generate()
        => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>
    /// SHA-256 del token tal como llega. Un token mal formado igual se hashea y
    /// simplemente no aparece en la base: no hay un camino distinto (ni un error
    /// distinto) para "formato inválido" y "token inexistente".
    /// </summary>
    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
