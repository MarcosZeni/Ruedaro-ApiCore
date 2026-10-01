using ApiCore.Config;
using ApiCore.Data;
using ApiCore.Models;
using ApiCore.Security;

namespace ApiCore.Endpoints;

/// <summary>
/// Registro, ingreso y sesión: <c>/api/v1/auth</c>.
///
/// <para>Orden en registro e ingreso, de lo más barato a lo más caro: validar
/// (nada), freno por IP y por email (una consulta), hash (19 MiB y ~40 ms, con
/// cupo). Así lo caro solo lo paga quien pasó todo lo barato.</para>
/// </summary>
public static class AuthEndpoints
{
    private static readonly string[] RolesPublicos = ["comprador", "revendedor"];

    /// <summary>Cada cuántos logins se barren sesiones vencidas e intentos viejos.</summary>
    private const int PurgarCada = 200;
    private static int _desdeUltimaPurga;

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth");

        auth.MapPost("/registro", Registro);
        auth.MapPost("/ingresar", Ingresar);
        auth.MapPost("/salir", Salir).RequireSesion();
        auth.MapGet("/yo", (HttpContext http) => Results.Ok(http.Usuario().ToDto())).RequireSesion();
        auth.MapPut("/yo", ActualizarPerfil).RequireSesion();

        return app;
    }

    private static async Task<IResult> Registro(
        RegistroRequest req, HttpContext http, ApiConfig config, AttemptThrottle throttle, HashGate gate,
        PasswordHasher hasher, UserRepository users, SessionRepository sesiones, LoginAttemptRepository attempts,
        CancellationToken ct)
    {
        string? nombre = Validacion.Nombre(req.Nombre);
        if (nombre is null) return Errores.Validacion("nombre", "Escribí tu nombre (entre 2 y 80 caracteres).");

        string? email = Validacion.Email(req.Email);
        if (email is null) return Errores.Validacion("email", "Revisá el email: no parece válido.");

        string rol = RolesPublicos.FirstOrDefault(r => r == req.Rol) ?? "";
        if (rol.Length == 0) return Errores.Validacion("rol", "Elegí si buscás o vendés.");

        string? telefono = null;
        if (!string.IsNullOrWhiteSpace(req.Telefono))
        {
            telefono = Validacion.Telefono(req.Telefono);
            if (telefono is null)
                return Errores.Validacion("telefono", "Revisá el WhatsApp: código de área y número, sin 0 ni 15. Ej: 11 2233 4455.");
        }
        else if (rol == "revendedor")
        {
            return Errores.Validacion("telefono", "Si vendés, necesitamos tu WhatsApp: es como te contactan los compradores.");
        }

        string password = req.Password ?? "";
        if (password.Length < Validacion.PasswordMin || password.Length > Validacion.PasswordMax)
            return Errores.Validacion("password", $"La contraseña tiene que tener entre {Validacion.PasswordMin} y {Validacion.PasswordMax} caracteres.");

        var ip = IpAddresses.Client(http);
        if (await throttle.RevisarAsync(ip, null, ct) is { } freno)
            return Frenado(http, freno);

        var (turno, hash) = await gate.TryRunAsync(() => hasher.Hash(password), ct);
        if (!turno) return Ocupado(http);

        ulong? id = await users.InsertAsync(nombre, email, telefono, rol, hash, ct);
        if (id is null)
            return Errores.Json(StatusCodes.Status409Conflict, "email_existente",
                "Ya hay una cuenta con ese email. Probá ingresar.", "email");

        await attempts.RecordAsync(Resultado.Registro, ip, email, id);

        var usuario = await users.GetByIdAsync(id.Value, ct);
        return Results.Json(await CrearSesionAsync(usuario!, ip, config, sesiones, ct), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> Ingresar(
        IngresarRequest req, HttpContext http, ApiConfig config, AttemptThrottle throttle, HashGate gate,
        PasswordHasher hasher, UserRepository users, SessionRepository sesiones, LoginAttemptRepository attempts,
        CancellationToken ct)
    {
        string password = req.Password ?? "";
        string? email = Validacion.Email(req.Email);
        if (email is null || password.Length == 0)
            return Errores.Validacion(email is null ? "email" : "password", "Completá tu email y tu contraseña.");

        // Una contraseña más larga que el máximo no puede ser correcta: no se
        // gasta un hash en ella, pero se contesta igual que cualquier fallo.
        if (password.Length > Validacion.PasswordMax)
            return CredencialesInvalidas();

        var ip = IpAddresses.Client(http);
        if (await throttle.RevisarAsync(ip, email, ct) is { } freno)
        {
            await attempts.RecordAsync(Resultado.Frenado, ip, email, null);
            return Frenado(http, freno);
        }

        var usuario = await users.GetByEmailAsync(email, ct);

        // Si el email no existe se gasta el mismo hash igual: si "no existe"
        // contestara al instante y "contraseña mal" tardara 40 ms, midiendo el
        // tiempo se sabría qué emails tienen cuenta.
        var (turno, resultado) = await gate.TryRunAsync(() =>
        {
            if (usuario is null)
            {
                hasher.BurnTime(password);
                return (Ok: false, NeedsRehash: false);
            }
            return hasher.Verify(password, usuario.PasswordHash);
        }, ct);
        if (!turno) return Ocupado(http);

        if (usuario is null)
        {
            await attempts.RecordAsync(Resultado.EmailInexistente, ip, email, null);
            return CredencialesInvalidas();
        }

        if (!resultado.Ok)
        {
            await attempts.RecordAsync(Resultado.ClaveIncorrecta, ip, email, usuario.Id);
            return CredencialesInvalidas();
        }

        // Se dice recién después de verificar la contraseña: a quien no la sabe,
        // no le contamos que la cuenta existe y está suspendida.
        if (usuario.Estado != "activo")
        {
            await attempts.RecordAsync(Resultado.Suspendido, ip, email, usuario.Id);
            return Errores.Json(StatusCodes.Status403Forbidden, "cuenta_suspendida",
                "Tu cuenta está suspendida. Si creés que es un error, escribinos.");
        }

        if (resultado.NeedsRehash)
        {
            var (ok, nuevo) = await gate.TryRunAsync(() => hasher.Hash(password), ct);
            if (ok) await users.UpdatePasswordHashAsync(usuario.Id, nuevo, ct);
        }

        await attempts.RecordAsync(Resultado.Ok, ip, email, usuario.Id);
        await PurgarDeVezEnCuandoAsync(config, sesiones, attempts);

        return Results.Ok(await CrearSesionAsync(usuario, ip, config, sesiones, ct));
    }

    private static async Task<IResult> Salir(HttpContext http, SessionRepository sesiones, CancellationToken ct)
    {
        await sesiones.DeleteAsync(http.TokenHash(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ActualizarPerfil(
        PerfilRequest req, HttpContext http, UserRepository users, CancellationToken ct)
    {
        var yo = http.Usuario();

        string? nombre = Validacion.Nombre(req.Nombre);
        if (nombre is null) return Errores.Validacion("nombre", "Escribí tu nombre (entre 2 y 80 caracteres).");

        string? telefono = null;
        if (!string.IsNullOrWhiteSpace(req.Telefono))
        {
            telefono = Validacion.Telefono(req.Telefono);
            if (telefono is null)
                return Errores.Validacion("telefono", "Revisá el WhatsApp: código de área y número, sin 0 ni 15. Ej: 11 2233 4455.");
        }
        else if (yo.Rol == "revendedor")
        {
            return Errores.Validacion("telefono", "Como vendedor necesitás un WhatsApp: es como te contactan.");
        }

        await users.UpdatePerfilAsync(yo.Id, nombre, telefono, ct);
        return Results.Ok((yo with { Nombre = nombre, Telefono = telefono }).ToDto());
    }

    // ---- Auxiliares -----------------------------------------------------------

    private static async Task<SesionResponse> CrearSesionAsync(
        UsuarioRow usuario, System.Net.IPAddress ip, ApiConfig config, SessionRepository sesiones, CancellationToken ct)
    {
        string token = SessionToken.Generate();
        DateTime expira = await sesiones.InsertAsync(
            SessionToken.Hash(token), usuario.Id, ip, Math.Max(1, config.Security.SessionDays), ct);
        return new SesionResponse(token, expira, usuario.ToDto());
    }

    /// <summary>Mismo mensaje para "no existe" y "contraseña mal": no se revela qué emails tienen cuenta.</summary>
    private static IResult CredencialesInvalidas()
        => Errores.Json(StatusCodes.Status401Unauthorized, "credenciales_invalidas", "El email o la contraseña no coinciden.");

    private static IResult Frenado(HttpContext http, AttemptThrottle.Freno freno)
    {
        http.Response.Headers.RetryAfter = freno.RetryAfterSegundos.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int minutos = Math.Max(1, (int)Math.Ceiling(freno.RetryAfterSegundos / 60.0));
        return Errores.Json(StatusCodes.Status429TooManyRequests, "demasiados_intentos",
            $"Hubo demasiados intentos. Esperá {minutos} {(minutos == 1 ? "minuto" : "minutos")} y probá de nuevo.");
    }

    private static IResult Ocupado(HttpContext http)
    {
        http.Response.Headers.RetryAfter = "5";
        return Errores.Json(StatusCodes.Status503ServiceUnavailable, "ocupado",
            "Hay mucha gente entrando ahora. Probá de nuevo en unos segundos.");
    }

    /// <summary>
    /// Barrido de sesiones vencidas e intentos viejos cada tantos logins. Con su
    /// propio plazo y sin el token del pedido: no le agrega espera al usuario
    /// si la base está lenta, y si falla no pasa nada.
    /// </summary>
    private static async Task PurgarDeVezEnCuandoAsync(ApiConfig config, SessionRepository sesiones, LoginAttemptRepository attempts)
    {
        if (Interlocked.Increment(ref _desdeUltimaPurga) < PurgarCada) return;
        Interlocked.Exchange(ref _desdeUltimaPurga, 0);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await sesiones.PurgeExpiredAsync(cts.Token);
            await attempts.PurgeOlderThanAsync(config.Security.LoginAttemptsRetentionDays, cts.Token);
        }
        catch (Exception e) when (e is MySqlConnector.MySqlException or OperationCanceledException)
        {
            // Se intenta de nuevo dentro de otros PurgarCada logins.
        }
    }
}
