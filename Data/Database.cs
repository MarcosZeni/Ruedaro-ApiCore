using MySqlConnector;

namespace ApiCore.Data;

/// <summary>
/// Fábrica de conexiones. El pool lo maneja MySqlConnector solo: abrir y cerrar
/// por operación es lo correcto, no un error de eficiencia.
/// </summary>
public sealed class Database(string connectionString)
{
    public async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    /// <summary>Ping para el <c>/health</c>: que la API conteste "ok" sin saber
    /// si la base está viva no le sirve a nadie.</summary>
    public async Task<bool> CanConnectAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new MySqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(ct);
            return true;
        }
        catch (Exception e) when (e is MySqlException or OperationCanceledException)
        {
            return false;
        }
    }
}
