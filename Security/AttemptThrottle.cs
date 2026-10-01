using System.Net;
using ApiCore.Config;
using ApiCore.Data;

namespace ApiCore.Security;

/// <summary>
/// Freno de login y registro que se apoya en <c>login_attempts</c> (adaptado de
/// Tidies24/LoginServer). Dos preguntas: ¿esta IP viene haciendo demasiado?,
/// ¿este email viene fallando demasiado?
///
/// <para><b>Completa al rate limit global en memoria, no lo reemplaza.</b> El de
/// memoria rechaza sin tocar la base (lo correcto bajo una avalancha) pero se
/// olvida de todo al reiniciar. Este se acuerda, porque la tabla no se
/// reinicia, y cuesta una consulta indexada por intento.</para>
///
/// <para><b>Va antes del hash.</b> Frenar acá cuesta una consulta; el Argon2id
/// cuesta 19 MiB y ~40 ms.</para>
/// </summary>
public sealed class AttemptThrottle(ApiConfig config, LoginAttemptRepository attempts)
{
    private const int VentanaIpSegundos = 60;

    /// <summary>Qué freno saltó y cuánto conviene esperar (para Retry-After).</summary>
    public readonly record struct Freno(string Motivo, int RetryAfterSegundos);

    /// <summary>
    /// Las dos preguntas, la de IP primero: si esa IP ya se pasó, la segunda
    /// consulta no cambia nada. <paramref name="email"/> en null (registro) solo
    /// mira la IP.
    /// </summary>
    public async Task<Freno?> RevisarAsync(IPAddress ip, string? email, CancellationToken ct)
    {
        var s = config.Security;

        if (s.MaxAttemptsPerIpPerMinute > 0
            && await attempts.IntentosDeIpAsync(ip, VentanaIpSegundos, ct) >= s.MaxAttemptsPerIpPerMinute)
            return new Freno("ip", VentanaIpSegundos);

        if (email is not null && s.MaxFailedAttemptsPerEmail > 0)
        {
            int minutos = Math.Max(1, s.EmailWindowMinutes);
            if (await attempts.FallosDeEmailAsync(email, minutos, ct) >= s.MaxFailedAttemptsPerEmail)
                return new Freno("email", minutos * 60);
        }

        return null;
    }
}
