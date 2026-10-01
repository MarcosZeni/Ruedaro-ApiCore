using ApiCore.Models;
using MySqlConnector;

namespace ApiCore.Data;

/// <summary>Fila de usuarios con el hash: solo circula dentro de la API.</summary>
public sealed record UsuarioRow(
    ulong Id, string Nombre, string Email, string? Telefono, string Rol, string Estado, DateTime Creado,
    string PasswordHash)
{
    public UsuarioDto ToDto() => new(Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Nombre, Email, Telefono, Rol, Estado, DateTime.SpecifyKind(Creado, DateTimeKind.Utc));
}

public sealed class UserRepository(Database db)
{
    private const string Columnas = "id, nombre, email, telefono, rol, estado, creado, password_hash";

    public async Task<UsuarioRow?> GetByEmailAsync(string email, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand($"SELECT {Columnas} FROM usuarios WHERE email = @email", conn);
        cmd.Parameters.AddWithValue("@email", email);
        return await LeerUnoAsync(cmd, ct);
    }

    public async Task<UsuarioRow?> GetByIdAsync(ulong id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand($"SELECT {Columnas} FROM usuarios WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return await LeerUnoAsync(cmd, ct);
    }

    /// <summary>
    /// Crea el usuario. Devuelve null si el email ya existe: lo decide el
    /// índice único de la base y no un SELECT previo, porque entre el SELECT y
    /// el INSERT otro pedido podría crear la misma cuenta.
    /// </summary>
    public async Task<ulong?> InsertAsync(
        string nombre, string email, string? telefono, string rol, string passwordHash, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            """
            INSERT INTO usuarios (email, password_hash, nombre, telefono, rol, estado, creado, actualizado)
            VALUES (@email, @hash, @nombre, @telefono, @rol, 'activo', UTC_TIMESTAMP(3), UTC_TIMESTAMP(3))
            """, conn);
        cmd.Parameters.AddWithValue("@email", email);
        cmd.Parameters.AddWithValue("@hash", passwordHash);
        cmd.Parameters.AddWithValue("@nombre", nombre);
        cmd.Parameters.AddWithValue("@telefono", (object?)telefono ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rol", rol);

        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
            return (ulong)cmd.LastInsertedId;
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return null;
        }
    }

    public async Task UpdatePerfilAsync(ulong id, string nombre, string? telefono, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            "UPDATE usuarios SET nombre = @nombre, telefono = @telefono, actualizado = UTC_TIMESTAMP(3) WHERE id = @id",
            conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@nombre", nombre);
        cmd.Parameters.AddWithValue("@telefono", (object?)telefono ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Re-hash con parámetros más fuertes, cuando el guardado quedó viejo.</summary>
    public async Task UpdatePasswordHashAsync(ulong id, string passwordHash, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            "UPDATE usuarios SET password_hash = @hash, actualizado = UTC_TIMESTAMP(3) WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@hash", passwordHash);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<UsuarioRow?> LeerUnoAsync(MySqlCommand cmd, CancellationToken ct)
    {
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        return new UsuarioRow(
            r.GetUInt64(0),
            r.GetString(1),
            r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.GetString(4),
            r.GetString(5),
            r.GetDateTime(6),
            r.GetString(7));
    }
}
