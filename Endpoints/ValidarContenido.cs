using System.Text.RegularExpressions;
using ApiCore.Models;

namespace ApiCore.Endpoints;

/// <summary>
/// Validación de pedidos, ofertas y publicaciones. Cada método devuelve el
/// pedido normalizado (textos recortados) o el primer error, con el campo a
/// marcar. El primero y no todos: el formulario muestra un mensaje por vez,
/// arriba, y así se lee sin esfuerzo.
/// </summary>
internal static class ValidarContenido
{
    /// <summary>Máximo razonable de precio o presupuesto: evita números absurdos que rompan el orden y los filtros.</summary>
    private const long MontoMaximo = 1_000_000_000_000;

    public static (PedidoRequest? Ok, IResult? Error) Pedido(PedidoRequest r)
    {
        if (!Catalogo.TipoValido(r.Tipo)) return (null, E("tipo", "Elegí el tipo de vehículo."));
        if (!Catalogo.MarcaValida(r.Tipo!, r.Marca)) return (null, E("marca", "Elegí una marca de la lista."));
        if (Texto(r.Modelo, 1, 80) is not { } modelo) return (null, E("modelo", "Escribí el modelo (hasta 80 caracteres)."));
        if (!Catalogo.AnioValido(r.AnioDesde)) return (null, E("anioDesde", "Elegí desde qué año."));
        if (!Catalogo.AnioValido(r.AnioHasta)) return (null, E("anioHasta", "Elegí hasta qué año."));
        if (r.AnioDesde > r.AnioHasta) return (null, E("anioHasta", "El año \"hasta\" no puede ser anterior al \"desde\"."));
        if (!MontoValido(r.Presupuesto)) return (null, E("presupuesto", "Escribí cuánto querés gastar, solo números."));
        if (!Catalogo.MonedaValida(r.Moneda)) return (null, E("moneda", "Elegí dólares o pesos."));
        if (!Catalogo.ZonaValida(r.Zona)) return (null, E("zona", "Elegí tu zona."));
        if (!Opcional(r.Comentarios, 500, out string? comentarios)) return (null, E("comentarios", "Los comentarios pueden tener hasta 500 caracteres."));

        return (r with { Modelo = modelo, Comentarios = comentarios }, null);
    }

    /// <summary>La oferta usa el tipo del pedido: la marca se valida contra ese tipo.</summary>
    public static (OfertaRequest? Ok, IResult? Error) Oferta(OfertaRequest r, string tipoPedido)
    {
        if (!Catalogo.MarcaValida(tipoPedido, r.Marca)) return (null, E("marca", "Elegí una marca de la lista."));
        if (Texto(r.Modelo, 1, 80) is not { } modelo) return (null, E("modelo", "Escribí el modelo."));
        if (Texto(r.Version, 1, 80) is not { } version) return (null, E("version", "Escribí la versión (motor y equipamiento)."));
        if (!Catalogo.AnioValido(r.Anio)) return (null, E("anio", "Elegí el año."));
        if (r.Uso is not (>= 0 and <= 5_000_000)) return (null, E("uso", tipoPedido == "lancha" ? "Escribí las horas de motor, solo números." : "Escribí los kilómetros, solo números."));
        if (!MontoValido(r.Precio)) return (null, E("precio", "Escribí el precio, solo números."));
        if (!Catalogo.MonedaValida(r.Moneda)) return (null, E("moneda", "Elegí dólares o pesos."));
        if (!Opcional(r.Link, 500, out string? link)) return (null, E("link", "El link es demasiado largo."));
        if (link is not null && !LinkValido(link)) return (null, E("link", "El link tiene que empezar con https://"));
        if (!Opcional(r.Comentario, 500, out string? comentario)) return (null, E("comentario", "El mensaje puede tener hasta 500 caracteres."));

        return (r with { Modelo = modelo, Version = version, Link = link, Comentario = comentario }, null);
    }

    public static (PublicacionRequest? Ok, IResult? Error) Publicacion(PublicacionRequest r)
    {
        if (!Catalogo.TipoValido(r.Tipo)) return (null, E("tipo", "Elegí el tipo de vehículo."));
        if (!Catalogo.MarcaValida(r.Tipo!, r.Marca)) return (null, E("marca", "Elegí una marca de la lista."));
        if (Texto(r.Modelo, 1, 80) is not { } modelo) return (null, E("modelo", "Escribí el modelo."));
        if (Texto(r.Version, 1, 80) is not { } version) return (null, E("version", "Escribí la versión (motor y equipamiento)."));
        if (!Catalogo.AnioValido(r.Anio)) return (null, E("anio", "Elegí el año."));
        if (r.Uso is not (>= 0 and <= 5_000_000)) return (null, E("uso", r.Tipo == "lancha" ? "Escribí las horas de motor, solo números." : "Escribí los kilómetros, solo números."));
        if (!MontoValido(r.Precio)) return (null, E("precio", "Escribí el precio, solo números."));
        if (!Catalogo.MonedaValida(r.Moneda)) return (null, E("moneda", "Elegí dólares o pesos."));
        if (!Catalogo.ZonaValida(r.Zona)) return (null, E("zona", "Elegí la zona."));
        if (Patente(r.Tipo!, r.Patente) is not { } patente)
            return (null, E("patente", r.Tipo == "lancha"
                ? "Escribí la matrícula, solo letras y números."
                : r.Tipo == "moto"
                    ? "Revisá la patente: como 123ABC o A123BCD."
                    : "Revisá la patente: como ABC123 o AB123CD."));
        if (!Opcional(r.Descripcion, 1000, out string? descripcion)) return (null, E("descripcion", "La descripción puede tener hasta 1000 caracteres."));

        return (r with { Modelo = modelo, Version = version, Patente = patente, Descripcion = descripcion }, null);
    }

    /// <summary>
    /// Patente normalizada (mayúsculas, sin espacios, guiones ni puntos) si tiene
    /// un formato argentino válido para el tipo; null si no. Normalizar es lo que
    /// hace que "ab 123 cd" y "AB-123-CD" sean la misma y choquen en el índice.
    /// </summary>
    public static string? Patente(string tipo, string? valor)
    {
        string p = new((valor ?? "").ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
        bool ok = tipo switch
        {
            // Viejo: ABC123. Mercosur: AB123CD.
            "auto" or "camion" => Regex.IsMatch(p, "^([A-Z]{3}[0-9]{3}|[A-Z]{2}[0-9]{3}[A-Z]{2})$"),
            // Viejo: 123ABC. Mercosur: A123BCD.
            "moto" => Regex.IsMatch(p, "^([0-9]{3}[A-Z]{3}|[A-Z][0-9]{3}[A-Z]{3})$"),
            // Matrícula náutica: formatos variados, solo se acota el largo.
            "lancha" => p.Length is >= 3 and <= 12,
            _ => false,
        };
        return ok ? p : null;
    }

    // ---- Auxiliares -----------------------------------------------------------

    private static IResult E(string campo, string mensaje) => Errores.Validacion(campo, mensaje);

    private static string? Texto(string? valor, int min, int max)
    {
        string t = (valor ?? "").Trim();
        return t.Length >= min && t.Length <= max && !t.Any(char.IsControl) ? t : null;
    }

    /// <summary>Texto opcional: vacío pasa a null; con contenido, hasta <paramref name="max"/>. Se permiten saltos de línea.</summary>
    private static bool Opcional(string? valor, int max, out string? limpio)
    {
        string t = (valor ?? "").Trim();
        limpio = t.Length == 0 ? null : t;
        return t.Length <= max && !t.Any(c => char.IsControl(c) && c is not '\n' and not '\r');
    }

    private static bool MontoValido(long? monto) => monto is > 0 and <= MontoMaximo;

    /// <summary>Solo https: un "javascript:" o "data:" en un link que se muestra con un botón es un XSS esperando.</summary>
    private static bool LinkValido(string link)
        => Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host);
}
