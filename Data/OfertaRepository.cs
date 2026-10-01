using ApiCore.Models;
using MySqlConnector;

namespace ApiCore.Data;

public enum ResultadoOferta { Creada, PedidoNoDisponible, Lleno, YaOferto }

public sealed class OfertaRepository(Database db)
{
    // Columnas de la oferta con prefijo, más el tipo del pedido y el revendedor:
    // lo que espera Lectura.Oferta().
    private const string Select =
        """
        SELECT o.id AS o_id, o.pedido_id, p.tipo AS pedido_tipo, o.marca AS o_marca, o.modelo AS o_modelo,
               o.version AS o_version, o.anio AS o_anio, o.uso AS o_uso, o.precio AS o_precio,
               o.moneda AS o_moneda, o.precio_usd AS o_precio_usd, o.cotizacion AS o_cotizacion, o.link AS o_link, o.comentario AS o_comentario,
               o.estado AS o_estado, o.creado AS o_creado,
               u.nombre AS rev_nombre, u.telefono AS rev_telefono
        FROM ofertas o
        JOIN pedidos p ON p.id = o.pedido_id
        JOIN usuarios u ON u.id = o.revendedor_id
        """;

    /// <summary>
    /// Crea la oferta respetando las reglas del pedido: que siga activo, que no
    /// haya llegado al tope y que el revendedor no haya ofertado antes.
    ///
    /// <para><b>Por qué la transacción con FOR UPDATE.</b> Sin el bloqueo, dos
    /// revendedores ofertando en el mismo instante leerían "9 ofertas" los dos y
    /// los dos entrarían: 11 ofertas en un pedido con tope de 10. Bloqueando la
    /// fila del pedido, el segundo espera a que el primero termine y cuenta bien.
    /// La regla de una oferta por revendedor la cuida además el índice único.</para>
    /// </summary>
    public async Task<ResultadoOferta> CrearAsync(ulong pedidoId, ulong revendedorId, OfertaRequest req, Cotizacion? cot, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var cmd = new MySqlCommand(
            """
            SELECT p.estado, u.estado AS comprador_estado
            FROM pedidos p JOIN usuarios u ON u.id = p.comprador_id
            WHERE p.id = @id FOR UPDATE
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("@id", pedidoId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct) || r.GetString(0) != "activo" || r.GetString(1) != "activo")
                return ResultadoOferta.PedidoNoDisponible;
        }

        await using (var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM ofertas WHERE pedido_id = @id AND estado = 'visible'", conn, tx))
        {
            cmd.Parameters.AddWithValue("@id", pedidoId);
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) >= Catalogo.MaxOfertasPorPedido)
                return ResultadoOferta.Lleno;
        }

        await using (var cmd = new MySqlCommand(
            """
            INSERT INTO ofertas (pedido_id, revendedor_id, marca, modelo, version, anio, uso, precio, moneda,
                                 precio_usd, cotizacion, link, comentario, estado, creado)
            VALUES (@pedido, @rev, @marca, @modelo, @version, @anio, @uso, @precio, @moneda,
                    @usd, @cot, @link, @comentario, 'visible', UTC_TIMESTAMP(3))
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("@pedido", pedidoId);
            cmd.Parameters.AddWithValue("@rev", revendedorId);
            cmd.Parameters.AddWithValue("@marca", req.Marca);
            cmd.Parameters.AddWithValue("@modelo", req.Modelo);
            cmd.Parameters.AddWithValue("@version", req.Version);
            cmd.Parameters.AddWithValue("@anio", req.Anio);
            cmd.Parameters.AddWithValue("@uso", req.Uso);
            cmd.Parameters.AddWithValue("@precio", req.Precio);
            cmd.Parameters.AddWithValue("@moneda", req.Moneda);
            cmd.Parameters.AddWithValue("@usd", (object?)CotizacionService.EnDolares(req.Precio!.Value, req.Moneda!, cot) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cot", (object?)cot?.Valor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@link", (object?)req.Link ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@comentario", (object?)req.Comentario ?? DBNull.Value);

            try
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
            {
                return ResultadoOferta.YaOferto;
            }
        }

        await tx.CommitAsync(ct);
        return ResultadoOferta.Creada;
    }

    /// <summary>Ofertas visibles de un pedido, para su comprador. Las de revendedores suspendidos no se muestran.</summary>
    public async Task<List<OfertaDto>> DePedidoAsync(ulong pedidoId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            $"{Select} WHERE o.pedido_id = @id AND o.estado = 'visible' AND u.estado = 'activo' ORDER BY o.precio",
            conn);
        cmd.Parameters.AddWithValue("@id", pedidoId);
        return await cmd.ListaAsync(r => r.Oferta(), ct);
    }

    /// <summary>Ofertas de un revendedor, con el pedido al que respondió.</summary>
    public async Task<List<MiOfertaDto>> DeRevendedorAsync(ulong revendedorId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            $"""
            SELECT x.*, p.*,
                   (SELECT COUNT(*) FROM ofertas c WHERE c.pedido_id = p.id AND c.estado = 'visible') AS cantidad_ofertas
            FROM ({Select} WHERE o.revendedor_id = @rev) x
            JOIN pedidos p ON p.id = x.pedido_id
            ORDER BY x.o_creado DESC LIMIT {PedidoRepository.MaxFilas}
            """, conn);
        cmd.Parameters.AddWithValue("@rev", revendedorId);
        return await cmd.ListaAsync(r => new MiOfertaDto(r.Oferta(), r.Pedido()), ct);
    }

    // ---- Administración ------------------------------------------------------

    public async Task<List<MiOfertaDto>> TodasAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            $"""
            SELECT x.*, p.*,
                   (SELECT COUNT(*) FROM ofertas c WHERE c.pedido_id = p.id AND c.estado = 'visible') AS cantidad_ofertas
            FROM ({Select}) x
            JOIN pedidos p ON p.id = x.pedido_id
            ORDER BY x.o_creado DESC LIMIT {PedidoRepository.MaxFilas}
            """, conn);
        return await cmd.ListaAsync(r => new MiOfertaDto(r.Oferta(), r.Pedido()), ct);
    }

    public async Task<bool> SetVisibleAsync(ulong id, bool visible, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("UPDATE ofertas SET estado = @estado WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@estado", visible ? "visible" : "baja");
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<int> ContarAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM ofertas WHERE estado = 'visible'", conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }
}
