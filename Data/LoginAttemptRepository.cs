using System.Net;
using ApiCore.Security;
using MySqlConnector;

namespace ApiCore.Data;

/// <summary>Valores de login_attempts.resultado.</summary>
public static class Resultado
{
    public const string Ok = "ok";
    public const string ClaveIncorrecta = "clave_incorrecta";
    public const string EmailInexistente = "email_inexistente";
    public const string Suspendido = "suspendido";
    public const string Frenado = "frenado";
    public const string Registro = "registro";
}

/// <summary>
/// Historial de intentos de login y registro (adaptado de Tidies24/LoginServer).
///
/// <para><b>Registrar no puede romper un login.</b> Es un historial, no parte
/// de la decisión de dejar entrar: si el INSERT falla se pierde la fila y el
/// usuario entra igual. Y la escritura usa su propio plazo en vez del del
/// pedido: un script que corta la conexión no tiene que poder borrar su
/// rastro.</para>
/// </summary>
public sealed class LoginAttemptRepository(Database db, GeoIpResolver geo, ILogger<LoginAttemptRepository> log)
{
    private static readonly TimeSpan Plazo = TimeSpan.FromSeconds(5);

    public async Task RecordAsync(string resultado, IPAddress ip, string? email, ulong? usuarioId)
    {
        try
        {
            using var cts = new CancellationTokenSource(Plazo);
            await using var conn = await db.OpenAsync(cts.Token);
            await using var cmd = new MySqlCommand(
                """
                INSERT INTO login_attempts (fecha, ip, email, usuario_id, resultado, pais)
                VALUES (UTC_TIMESTAMP(3), @ip, @email, @uid, @resultado, @pais)
                """, conn);
            cmd.Parameters.AddWithValue("@ip", IpAddresses.Normalize(ip).GetAddressBytes());
            cmd.Parameters.AddWithValue("@email", email is null ? DBNull.Value : email.Length > 254 ? email[..254] : email);
            cmd.Parameters.AddWithValue("@uid", usuarioId.HasValue ? usuarioId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@resultado", resultado);
            cmd.Parameters.AddWithValue("@pais", geo.Pais(ip));
            await cmd.ExecuteNonQueryAsync(cts.Token);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pudo registrar el intento de {Email} desde {Ip}", email, ip);
        }
    }

    /// <summary>
    /// Intentos de esta IP en los últimos <paramref name="segundos"/>. Los
    /// frenados no cuentan: si contaran, cada rechazo alimentaría su propio
    /// bloqueo y la ventana no se vaciaría nunca mientras el cliente insista.
    /// </summary>
    public async Task<int> IntentosDeIpAsync(IPAddress ip, int segundos, CancellationToken ct)
    {
        try
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new MySqlCommand(
                """
                SELECT COUNT(*) FROM login_attempts
                WHERE ip = @ip
                  AND fecha > DATE_SUB(UTC_TIMESTAMP(3), INTERVAL @seg SECOND)
                  AND resultado <> 'frenado'
                """, conn);
            cmd.Parameters.AddWithValue("@ip", IpAddresses.Normalize(ip).GetAddressBytes());
            cmd.Parameters.AddWithValue("@seg", segundos);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            // Se deja pasar: con la base caída el login falla igual más adelante,
            // con un error que dice lo que realmente pasa.
            log.LogWarning(ex, "No se pudo contar los intentos de {Ip}", ip);
            return 0;
        }
    }

    /// <summary>
    /// Contraseñas equivocadas contra este email en los últimos
    /// <paramref name="minutos"/>, contando desde el último acierto. Solo
    /// fallos: contar todo permitiría dejar afuera a otro tirándole logins.
    /// </summary>
    public async Task<int> FallosDeEmailAsync(string email, int minutos, CancellationToken ct)
    {
        try
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new MySqlCommand(
                """
                SELECT COUNT(*) FROM login_attempts
                WHERE email = @email
                  AND resultado = 'clave_incorrecta'
                  AND fecha > IFNULL(
                        (SELECT MAX(fecha) FROM login_attempts
                          WHERE email = @email AND resultado = 'ok'
                            AND fecha > DATE_SUB(UTC_TIMESTAMP(3), INTERVAL @min MINUTE)),
                        DATE_SUB(UTC_TIMESTAMP(3), INTERVAL @min MINUTE))
                """, conn);
            cmd.Parameters.AddWithValue("@email", email);
            cmd.Parameters.AddWithValue("@min", minutos);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pudo contar los fallos de {Email}", email);
            return 0;
        }
    }

    public async Task<int> PurgeOlderThanAsync(int dias, CancellationToken ct)
    {
        if (dias <= 0) return 0;
        try
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new MySqlCommand(
                "DELETE FROM login_attempts WHERE fecha < DATE_SUB(UTC_TIMESTAMP(3), INTERVAL @dias DAY)", conn);
            cmd.Parameters.AddWithValue("@dias", dias);
            return await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pudo purgar login_attempts");
            return 0;
        }
    }
}
