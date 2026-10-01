using ApiCore.Data;
using ApiCore.Models;
using ApiCore.Security;

namespace ApiCore.Endpoints;

/// <summary>
/// Sesión del pedido. La web manda el token en <c>Authorization: Bearer ...</c>
/// (lo tiene en una cookie httpOnly que el navegador no deja leer a ningún
/// script). Los endpoints que necesitan usuario llevan <c>.RequireSesion()</c>.
/// </summary>
public static class SesionActual
{
    private const string ClaveUsuario = "sesion.usuario";
    private const string ClaveHash = "sesion.hash";

    public static RouteHandlerBuilder RequireSesion(this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            string? token = BearerToken(http);
            if (token is null)
                return Errores.NoAutorizado("sin_sesion", "Tenés que ingresar para hacer esto.");

            byte[] hash = SessionToken.Hash(token);
            var sesiones = http.RequestServices.GetRequiredService<SessionRepository>();
            var usuario = await sesiones.GetUsuarioAsync(hash, http.RequestAborted);

            // Vencida, cerrada o de un usuario suspendido: para la web es lo mismo,
            // borra la cookie y manda a ingresar.
            if (usuario is null)
                return Errores.NoAutorizado("sesion_invalida", "Tu sesión venció. Volvé a ingresar.");

            http.Items[ClaveUsuario] = usuario;
            http.Items[ClaveHash] = hash;
            return await next(ctx);
        });

    /// <summary>Usuario de la sesión. Solo en endpoints con <c>.RequireSesion()</c>.</summary>
    public static UsuarioRow Usuario(this HttpContext http) => (UsuarioRow)http.Items[ClaveUsuario]!;

    public static byte[] TokenHash(this HttpContext http) => (byte[])http.Items[ClaveHash]!;

    private static string? BearerToken(HttpContext http)
    {
        string? header = http.Request.Headers.Authorization;
        const string prefijo = "Bearer ";
        if (header is null || !header.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)) return null;

        string token = header[prefijo.Length..].Trim();
        // Un token real tiene 43 caracteres; algo enorme no se hashea ni se busca.
        return token.Length is > 0 and <= 128 ? token : null;
    }
}

/// <summary>Respuestas de error con forma única: código estable + mensaje para mostrar.</summary>
public static class Errores
{
    public static IResult Json(int status, string codigo, string mensaje, string? campo = null)
        => Results.Json(new ErrorResponse(codigo, mensaje, campo), statusCode: status);

    public static IResult Validacion(string campo, string mensaje)
        => Json(StatusCodes.Status400BadRequest, "validacion", mensaje, campo);

    public static IResult NoAutorizado(string codigo, string mensaje)
        => Json(StatusCodes.Status401Unauthorized, codigo, mensaje);
}
