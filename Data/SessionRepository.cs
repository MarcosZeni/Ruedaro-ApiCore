using System.Net;
using ApiCore.Security;
using MySqlConnector;

namespace ApiCore.Data;

public sealed class SessionRepository(Database db, UserRepository users)
{
    /// <summary>Guarda una sesión nueva y devuelve cuándo vence (UTC).</summary>
    public async Task<DateTime> InsertAsync(byte[] tokenHash, ulong usuarioId, IPAddress ip, int dias, CancellationToken ct)
    {
        DateTime ahora = DateTime.UtcNow;
        DateTime expira = ahora.AddDays(dias);

        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            "INSERT INTO sesiones (token_hash, usuario_id, ip, creado, expira) VALUES (@hash, @uid, @ip, @creado, @expira)",
            conn);
        cmd.Parameters.AddWithValue("@hash", tokenHash);
        cmd.Parameters.AddWithValue("@uid", usuarioId);
        cmd.Parameters.AddWithValue("@ip", IpAddresses.Normalize(ip).GetAddressBytes());
        cmd.Parameters.AddWithValue("@creado", ahora);
        cmd.Parameters.AddWithValue("@expira", expira);
        await cmd.ExecuteNonQueryAsync(ct);

        return expira;
    }

    /// <summary>
    /// Usuario dueño de una sesión vigente, o null. "Vigente" incluye que el
    /// usuario esté activo: suspender a alguien corta sus sesiones en el
    /// siguiente pedido, sin tener que ir a borrarlas.
    /// </summary>
    public async Task<UsuarioRow?> GetUsuarioAsync(byte[] tokenHash, CancellationToken ct)
    {
        ulong? usuarioId;
        await using (var conn = await db.OpenAsync(ct))
        await using (var cmd = new MySqlCommand(
            "SELECT usuario_id FROM sesiones WHERE token_hash = @hash AND expira > UTC_TIMESTAMP(3)", conn))
        {
            cmd.Parameters.AddWithValue("@hash", tokenHash);
            usuarioId = await cmd.ExecuteScalarAsync(ct) is { } v ? Convert.ToUInt64(v, System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        if (usuarioId is null) return null;
        var usuario = await users.GetByIdAsync(usuarioId.Value, ct);
        return usuario is { Estado: "activo" } ? usuario : null;
    }

    public async Task DeleteAsync(byte[] tokenHash, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("DELETE FROM sesiones WHERE token_hash = @hash", conn);
        cmd.Parameters.AddWithValue("@hash", tokenHash);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Borra las vencidas. Se llama de vez en cuando desde el login: no hace falta un job aparte.</summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("DELETE FROM sesiones WHERE expira < UTC_TIMESTAMP(3)", conn);
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
