using System.Net.Mail;

namespace ApiCore.Security;

/// <summary>
/// Normalización y validación de lo que escribe el usuario. La web valida lo
/// mismo antes de mandar, pero esa es comodidad para el usuario: la validación
/// que vale es esta, porque la API no confía en quien la llama.
/// </summary>
public static class Validacion
{
    public const int PasswordMin = 8;

    /// <summary>
    /// Tope para la contraseña. No es por la base (se guarda el hash, de largo
    /// fijo): es para que nadie mande un texto de un megabyte y nos haga gastar
    /// Argon2 en eso.
    /// </summary>
    public const int PasswordMax = 128;

    /// <summary>
    /// Email en minúscula y sin espacios alrededor, o null si no es válido.
    /// Se guarda así para que "Juan@x.com" y "juan@x.com" sean la misma cuenta
    /// (la columna además es case-insensitive, por las dudas).
    /// </summary>
    public static string? Email(string? valor)
    {
        string email = (valor ?? "").Trim().ToLowerInvariant();
        if (email.Length is 0 or > 254) return null;

        // MailAddress acepta formas raras ("Juan <juan@x.com>"): exigimos que la
        // dirección parseada sea exactamente lo que se escribió.
        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email) return null;

        // Sin punto en el dominio no llega a ningún lado ("juan@gmail").
        return parsed.Host.Contains('.') ? email : null;
    }

    /// <summary>Nombre recortado, o null si queda vacío, es muy largo o trae caracteres de control.</summary>
    public static string? Nombre(string? valor)
    {
        string nombre = (valor ?? "").Trim();
        if (nombre.Length is < 2 or > 80) return null;
        return nombre.Any(char.IsControl) ? null : nombre;
    }

    /// <summary>
    /// WhatsApp argentino en E.164 sin el "+" (5491122334455), o null si no se
    /// puede interpretar. Acepta como lo escribe la gente: "11 2233-4455",
    /// "+54 9 11 2233 4455", "011 2233 4455".
    ///
    /// <para>No se intenta adivinar el "15" de los celulares viejos: con él, el
    /// número no tiene 10 dígitos y se rechaza con un mensaje que dice cómo
    /// escribirlo. Adivinar mal manda los mensajes a otra persona.</para>
    /// </summary>
    public static string? Telefono(string? valor)
    {
        string d = new((valor ?? "").Where(char.IsAsciiDigit).ToArray());

        if (d.StartsWith('0')) d = d[1..];                       // 011... → 11...
        if (d.Length == 10) return "549" + d;                     // 1122334455
        if (d.Length == 12 && d.StartsWith("54")) return "549" + d[2..]; // 541122334455
        if (d.Length == 13 && d.StartsWith("549")) return d;      // 5491122334455
        return null;
    }
}
