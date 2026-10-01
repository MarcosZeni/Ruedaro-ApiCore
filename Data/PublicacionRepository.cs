using System.Text;
using ApiCore.Models;
using MySqlConnector;

namespace ApiCore.Data;

public enum ResultadoDestacar { Ok, NoEncontrada, SinCupo }

public sealed class PublicacionRepository(Database db)
{
    /// <summary>Destacados de un usuario sin plan, y con el plan vigente.</summary>
    public const int DestacadosGratis = 1;
    public const int DestacadosPlan = 10;

    /// <summary>Cuántos vehículos muestra el carrusel.</summary>
    public const int TamanoCarrusel = 10;

    private const string Select =
        """
        SELECT pu.*, u.nombre AS rev_nombre, u.telefono AS rev_telefono, u.rol AS rev_rol
        FROM publicaciones pu JOIN usuarios u ON u.id = pu.revendedor_id
        """;

    /// <summary>Lo que se ve en el catálogo: activas y de usuarios no suspendidos.</summary>
    private const string Visible = "pu.estado = 'activa' AND u.estado = 'activo'";

    /// <summary>Vendedor ("agencia"/"particular") → rol de la tabla usuarios.</summary>
    private static string RolDe(string vendedor) => vendedor == "agencia" ? "revendedor" : "comprador";

    /// <summary>
    /// Crea el aviso. Devuelve null si la patente ya está en otro aviso vigente:
    /// lo decide el índice único de la base (columna patente_vigente), no un
    /// SELECT previo, así dos publicaciones simultáneas no pueden colarse.
    /// </summary>
    public async Task<ulong?> InsertAsync(ulong usuarioId, PublicacionRequest req, Cotizacion? cot, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            """
            INSERT INTO publicaciones (revendedor_id, tipo, marca, modelo, version, anio, uso, precio, moneda,
                                       precio_usd, cotizacion, zona, patente, descripcion, estado, creado, actualizado)
            VALUES (@rev, @tipo, @marca, @modelo, @version, @anio, @uso, @precio, @moneda,
                    @usd, @cot, @zona, @patente, @descripcion, 'activa', UTC_TIMESTAMP(3), UTC_TIMESTAMP(3))
            """, conn);
        cmd.Parameters.AddWithValue("@rev", usuarioId);
        cmd.Parameters.AddWithValue("@tipo", req.Tipo);
        cmd.Parameters.AddWithValue("@marca", req.Marca);
        cmd.Parameters.AddWithValue("@modelo", req.Modelo);
        cmd.Parameters.AddWithValue("@version", req.Version);
        cmd.Parameters.AddWithValue("@anio", req.Anio);
        cmd.Parameters.AddWithValue("@uso", req.Uso);
        cmd.Parameters.AddWithValue("@precio", req.Precio);
        cmd.Parameters.AddWithValue("@moneda", req.Moneda);
        cmd.Parameters.AddWithValue("@usd", (object?)CotizacionService.EnDolares(req.Precio!.Value, req.Moneda!, cot) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cot", (object?)cot?.Valor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@zona", req.Zona);
        cmd.Parameters.AddWithValue("@patente", req.Patente);
        cmd.Parameters.AddWithValue("@descripcion", (object?)req.Descripcion ?? DBNull.Value);

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

    /// <summary>Catálogo público con filtros. WHERE con fragmentos fijos; valores siempre como parámetros.</summary>
    public async Task<List<PublicacionDto>> CatalogoAsync(FiltrosListado f, int limite, CancellationToken ct)
    {
        var sql = new StringBuilder($"{Select} WHERE {Visible}");
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand { Connection = conn };

        if (f.Vendedor is not null) { sql.Append(" AND u.rol = @rol"); cmd.Parameters.AddWithValue("@rol", RolDe(f.Vendedor)); }
        if (f.Tipo is not null) { sql.Append(" AND pu.tipo = @tipo"); cmd.Parameters.AddWithValue("@tipo", f.Tipo); }
        if (f.Marca is not null) { sql.Append(" AND pu.marca = @marca"); cmd.Parameters.AddWithValue("@marca", f.Marca); }
        if (f.Zona is not null) { sql.Append(" AND pu.zona = @zona"); cmd.Parameters.AddWithValue("@zona", f.Zona); }
        if (f.AnioDesde is not null) { sql.Append(" AND pu.anio >= @desde"); cmd.Parameters.AddWithValue("@desde", f.AnioDesde); }
        if (f.HastaUsd is not null)
        {
            // Contra el precio convertido al publicar: los avisos en pesos entran igual.
            sql.Append(" AND pu.precio_usd <= @hasta");
            cmd.Parameters.AddWithValue("@hasta", f.HastaUsd);
        }

        sql.Append(" ORDER BY pu.creado DESC LIMIT @limite");
        cmd.Parameters.AddWithValue("@limite", Math.Clamp(limite, 1, PedidoRepository.MaxFilas));
        cmd.CommandText = sql.ToString();
        return await cmd.ListaAsync(r => r.Publicacion(), ct);
    }

    public async Task<PublicacionDto?> VisibleAsync(ulong id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand($"{Select} WHERE pu.id = @id AND {Visible}", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return (await cmd.ListaAsync(r => r.Publicacion(), ct)).FirstOrDefault();
    }

    /// <summary>Avisos visibles por tipo (de un tipo de vendedor, si se indica), para las pestañas.</summary>
    public async Task<Dictionary<string, int>> CantidadPorTipoAsync(string? vendedor, CancellationToken ct)
    {
        var cuenta = Catalogo.Tipos.ToDictionary(t => t, _ => 0);
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            $"""
            SELECT pu.tipo, COUNT(*) FROM publicaciones pu JOIN usuarios u ON u.id = pu.revendedor_id
            WHERE {Visible} {(vendedor is null ? "" : "AND u.rol = @rol")} GROUP BY pu.tipo
            """, conn);
        if (vendedor is not null) cmd.Parameters.AddWithValue("@rol", RolDe(vendedor));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) cuenta[r.GetString(0)] = r.GetInt32(1);
        return cuenta;
    }

    /// <summary>
    /// El carrusel de un tipo de vendedor: hasta 10 al azar.
    ///
    /// <para>Primero los destacados que entran en el cupo de su dueño (1 sin
    /// plan, 10 con el plan vigente; si el plan vence, cuenta solo el primero que
    /// destacó). Si no llegan a 10, se completa con otros avisos al azar: el
    /// carrusel nunca queda con huecos mientras haya avisos.</para>
    ///
    /// <para>ORDER BY RAND() recorre todos los candidatos: alcanza mientras sean
    /// cientos. Con miles conviene cachear la selección unos minutos.</para>
    /// </summary>
    public async Task<List<PublicacionDto>> CarruselAsync(string vendedor, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);

        List<PublicacionDto> destacados;
        await using (var cmd = new MySqlCommand(
            $"""
            WITH elegibles AS (
                SELECT pu.id, pu.estado,
                       ROW_NUMBER() OVER (PARTITION BY pu.revendedor_id ORDER BY pu.destacada_desde) AS n,
                       CASE WHEN u.plan_vence > UTC_TIMESTAMP(3) THEN {DestacadosPlan} ELSE {DestacadosGratis} END AS cupo
                FROM publicaciones pu JOIN usuarios u ON u.id = pu.revendedor_id
                WHERE pu.destacada = 1 AND pu.estado IN ('activa', 'pausada') AND u.estado = 'activo' AND u.rol = @rol
            )
            {Select}
            JOIN elegibles e ON e.id = pu.id
            WHERE e.n <= e.cupo AND e.estado = 'activa'
            ORDER BY RAND() LIMIT {TamanoCarrusel}
            """, conn))
        {
            cmd.Parameters.AddWithValue("@rol", RolDe(vendedor));
            destacados = await cmd.ListaAsync(r => r.Publicacion(), ct);
        }

        int faltan = TamanoCarrusel - destacados.Count;
        if (faltan <= 0) return destacados;

        // Relleno: avisos activos que no salieron como destacados. Se marcan sin
        // destacar (aunque lo estén pero fuera de cupo) para que la web no les
        // ponga la etiqueta.
        await using (var cmd = new MySqlCommand { Connection = conn })
        {
            var sql = new StringBuilder($"{Select} WHERE {Visible} AND u.rol = @rol");
            cmd.Parameters.AddWithValue("@rol", RolDe(vendedor));
            for (int i = 0; i < destacados.Count; i++)
            {
                sql.Append(i == 0 ? " AND pu.id NOT IN (" : ", ").Append("@x").Append(i);
                cmd.Parameters.AddWithValue($"@x{i}", destacados[i].Id);
            }
            if (destacados.Count > 0) sql.Append(')');
            sql.Append(" ORDER BY RAND() LIMIT @faltan");
            cmd.Parameters.AddWithValue("@faltan", faltan);
            cmd.CommandText = sql.ToString();

            var relleno = await cmd.ListaAsync(r => r.Publicacion() with { Destacada = false }, ct);
            return [.. destacados, .. relleno];
        }
    }

    /// <summary>Avisos propios (con patente) y el uso de destacados.</summary>
    public async Task<MisPublicacionesDto> DeUsuarioAsync(UsuarioRow usuario, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            $"{Select} WHERE pu.revendedor_id = @rev ORDER BY pu.creado DESC LIMIT {PedidoRepository.MaxFilas}", conn);
        cmd.Parameters.AddWithValue("@rev", usuario.Id);
        var avisos = await cmd.ListaAsync(r => r.Publicacion(conPatente: true), ct);

        int usados = avisos.Count(a => a.Destacada && a.Estado is "activa" or "pausada");
        return new MisPublicacionesDto(avisos, usados,
            usuario.PlanVigente ? DestacadosPlan : DestacadosGratis,
            usuario.ToDto().PlanVence);
    }

    /// <summary>
    /// Cambio de estado por su dueño. "vendida" es final y "baja" es del
    /// admin: ninguna se puede deshacer desde acá (lo dice el WHERE). Al
    /// venderse deja de ser destacado y libera su lugar.
    /// </summary>
    public async Task<bool> CambiarEstadoAsync(ulong usuarioId, ulong id, string estado, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            """
            UPDATE publicaciones
            SET estado = @estado,
                destacada = IF(@estado = 'vendida', 0, destacada),
                actualizado = UTC_TIMESTAMP(3)
            WHERE id = @id AND revendedor_id = @rev AND estado IN ('activa', 'pausada')
            """, conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@rev", usuarioId);
        cmd.Parameters.AddWithValue("@estado", estado);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>
    /// Destacar o quitar el destacado de un aviso propio, respetando el cupo.
    ///
    /// <para>Con transacción y la fila del usuario bloqueada: dos clics rápidos
    /// en dos avisos distintos no pueden pasarse del cupo contando los dos a la
    /// vez "0 de 1".</para>
    /// </summary>
    public async Task<ResultadoDestacar> DestacarAsync(ulong usuarioId, ulong id, bool destacar, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        int cupo;
        await using (var cmd = new MySqlCommand(
            $"SELECT CASE WHEN plan_vence > UTC_TIMESTAMP(3) THEN {DestacadosPlan} ELSE {DestacadosGratis} END FROM usuarios WHERE id = @uid FOR UPDATE",
            conn, tx))
        {
            cmd.Parameters.AddWithValue("@uid", usuarioId);
            cupo = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }

        if (destacar)
        {
            await using var cmd = new MySqlCommand(
                """
                SELECT COUNT(*) FROM publicaciones
                WHERE revendedor_id = @uid AND destacada = 1 AND estado IN ('activa', 'pausada') AND id <> @id
                """, conn, tx);
            cmd.Parameters.AddWithValue("@uid", usuarioId);
            cmd.Parameters.AddWithValue("@id", id);
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) >= cupo)
                return ResultadoDestacar.SinCupo;
        }

        await using (var cmd = new MySqlCommand(
            """
            UPDATE publicaciones
            SET destacada = @d,
                destacada_desde = IF(@d = 1, IFNULL(destacada_desde, UTC_TIMESTAMP(3)), NULL)
            WHERE id = @id AND revendedor_id = @uid AND estado IN ('activa', 'pausada')
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("@d", destacar);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@uid", usuarioId);
            if (await cmd.ExecuteNonQueryAsync(ct) == 0)
            {
                // Puede ser que ya estuviera así (0 filas cambiadas) o que no exista.
                await using var existe = new MySqlCommand(
                    "SELECT 1 FROM publicaciones WHERE id = @id AND revendedor_id = @uid AND estado IN ('activa', 'pausada')", conn, tx);
                existe.Parameters.AddWithValue("@id", id);
                existe.Parameters.AddWithValue("@uid", usuarioId);
                if (await existe.ExecuteScalarAsync(ct) is null) return ResultadoDestacar.NoEncontrada;
            }
        }

        await tx.CommitAsync(ct);
        return ResultadoDestacar.Ok;
    }

    // ---- Administración ------------------------------------------------------

    public async Task<List<PublicacionDto>> TodasAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            $"{Select} ORDER BY pu.creado DESC LIMIT {PedidoRepository.MaxFilas}", conn);
        return await cmd.ListaAsync(r => r.Publicacion(conPatente: true), ct);
    }

    /// <summary>
    /// Baja por un admin, o volver a mostrarla (queda activa). Volver a
    /// mostrarla puede chocar con la patente si mientras tanto se publicó otro
    /// aviso con la misma: en ese caso queda dado de baja (false).
    /// </summary>
    public async Task<bool> SetVisibleAsync(ulong id, bool visible, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            visible
                ? "UPDATE publicaciones SET estado = 'activa', actualizado = UTC_TIMESTAMP(3) WHERE id = @id AND estado = 'baja'"
                : "UPDATE publicaciones SET estado = 'baja', destacada = 0, actualizado = UTC_TIMESTAMP(3) WHERE id = @id AND estado <> 'baja'",
            conn);
        cmd.Parameters.AddWithValue("@id", id);
        try
        {
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    public async Task<int> ContarVisiblesAsync(CancellationToken ct)
        => (await CantidadPorTipoAsync(null, ct)).Values.Sum();
}
