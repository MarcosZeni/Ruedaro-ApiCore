using ApiCore.Models;
using MySqlConnector;

namespace ApiCore.Data;

/// <summary>Fila de usuarios con el hash: solo circula dentro de la API.</summary>
public sealed record UsuarioRow(
    ulong Id, string Nombre, string Email, string? Telefono, string Rol, string Estado, DateTime Creado,
    string PasswordHash, DateTime? PlanVence)
{
    public UsuarioDto ToDto() => new(Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Nombre, Email, Telefono, Rol, Estado, DateTime.SpecifyKind(Creado, DateTimeKind.Utc),
        PlanVence is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null);

    /// <summary>Plan de destacados vigente: hasta 10 en vez de 1.</summary>
    public bool PlanVigente => PlanVence > DateTime.UtcNow;
}

public sealed class UserRepository(Database db)
{
    private const string Columnas = "id, nombre, email, telefono, rol, estado, creado, password_hash, plan_vence";

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

    // ---- Administración ------------------------------------------------------

    /// <summary>Usuarios, del más nuevo al más viejo, filtrando por nombre o email si hay búsqueda.</summary>
    public async Task<List<UsuarioDto>> ListarAsync(string? busqueda, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand { Connection = conn };
        string where = "";
        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            // LIKE con parámetro; los comodines que escriba el usuario se escapan
            // para que "%" busque un "%" y no "cualquier cosa".
            string q = busqueda.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
            where = "WHERE nombre LIKE @q OR email LIKE @q";
            cmd.Parameters.AddWithValue("@q", $"%{q}%");
        }
        cmd.CommandText = $"SELECT {Columnas} FROM usuarios {where} ORDER BY creado DESC LIMIT 100";

        var lista = new List<UsuarioDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            lista.Add(new UsuarioRow(r.GetUInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4), r.GetString(5), r.GetDateTime(6), r.GetString(7), r.IsDBNull(8) ? null : r.GetDateTime(8)).ToDto());
        return lista;
    }

    /// <summary>
    /// Suspender o reactivar. Suspender corta sus sesiones en el próximo
    /// pedido (la sesión mira el estado) y oculta sus pedidos, ofertas y avisos
    /// (los listados filtran por usuario activo). Un admin no se suspende acá.
    /// </summary>
    public async Task<bool> SetEstadoAsync(ulong id, string estado, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            "UPDATE usuarios SET estado = @estado, actualizado = UTC_TIMESTAMP(3) WHERE id = @id AND rol <> 'admin'", conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@estado", estado);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>
    /// Plan de destacados por <paramref name="dias"/> desde ahora; 0 lo quita.
    /// Mientras no haya cobro integrado, lo activa un admin cuando el usuario pagó.
    /// </summary>
    public async Task<bool> SetPlanAsync(ulong id, int dias, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            dias > 0
                ? "UPDATE usuarios SET plan_vence = DATE_ADD(UTC_TIMESTAMP(3), INTERVAL @dias DAY), actualizado = UTC_TIMESTAMP(3) WHERE id = @id"
                : "UPDATE usuarios SET plan_vence = NULL, actualizado = UTC_TIMESTAMP(3) WHERE id = @id",
            conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@dias", dias);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<(int Compradores, int Revendedores, int Suspendidos)> ContarAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            """
            SELECT SUM(rol = 'comprador'), SUM(rol = 'revendedor'), SUM(estado = 'suspendido') FROM usuarios
            """, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        int N(int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture);
        return (N(0), N(1), N(2));
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
            r.GetString(7),
            r.IsDBNull(8) ? null : r.GetDateTime(8));
    }
}
