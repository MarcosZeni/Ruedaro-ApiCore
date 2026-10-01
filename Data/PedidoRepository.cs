using System.Text;
using ApiCore.Models;
using MySqlConnector;

namespace ApiCore.Data;

/// <summary>Filtros de listados. <c>Vendedor</c> ("agencia"/"particular") solo aplica al catálogo.</summary>
public sealed record FiltrosListado(string? Tipo, string? Marca, string? Zona, long? HastaUsd, int? AnioDesde, string? Vendedor = null);

public sealed class PedidoRepository(Database db)
{
    /// <summary>Tope de filas por listado. Sin paginado todavía: alcanza para arrancar.</summary>
    public const int MaxFilas = 100;

    // Ofertas visibles: las dadas de baja por un admin no cuentan para el tope
    // ni para el número que ve el comprador.
    private const string CantidadOfertas =
        "(SELECT COUNT(*) FROM ofertas o WHERE o.pedido_id = p.id AND o.estado = 'visible') AS cantidad_ofertas";

    public async Task<ulong> InsertAsync(ulong compradorId, PedidoRequest req, Cotizacion? cot, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            """
            INSERT INTO pedidos (comprador_id, tipo, marca, modelo, anio_desde, anio_hasta, presupuesto, moneda,
                                 presupuesto_usd, cotizacion, zona, comentarios, estado, creado, actualizado)
            VALUES (@uid, @tipo, @marca, @modelo, @desde, @hasta, @presupuesto, @moneda,
                    @usd, @cot, @zona, @comentarios, 'activo', UTC_TIMESTAMP(3), UTC_TIMESTAMP(3))
            """, conn);
        cmd.Parameters.AddWithValue("@uid", compradorId);
        cmd.Parameters.AddWithValue("@tipo", req.Tipo);
        cmd.Parameters.AddWithValue("@marca", req.Marca);
        cmd.Parameters.AddWithValue("@modelo", req.Modelo);
        cmd.Parameters.AddWithValue("@desde", req.AnioDesde);
        cmd.Parameters.AddWithValue("@hasta", req.AnioHasta);
        cmd.Parameters.AddWithValue("@presupuesto", req.Presupuesto);
        cmd.Parameters.AddWithValue("@moneda", req.Moneda);
        cmd.Parameters.AddWithValue("@usd", (object?)CotizacionService.EnDolares(req.Presupuesto!.Value, req.Moneda!, cot) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cot", (object?)cot?.Valor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@zona", req.Zona);
        cmd.Parameters.AddWithValue("@comentarios", (object?)req.Comentarios ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
        return (ulong)cmd.LastInsertedId;
    }

    /// <summary>Pedidos de un comprador (sin los dados de baja por un admin).</summary>
    public async Task<List<PedidoDto>> DelCompradorAsync(ulong compradorId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            $"""
            SELECT p.*, {CantidadOfertas} FROM pedidos p
            WHERE p.comprador_id = @uid AND p.estado <> 'baja'
            ORDER BY p.creado DESC LIMIT {MaxFilas}
            """, conn);
        cmd.Parameters.AddWithValue("@uid", compradorId);
        return await cmd.ListaAsync(r => r.Pedido(), ct);
    }

    /// <summary>Un pedido del comprador, o null si no existe o es de otro.</summary>
    public async Task<PedidoDto?> DelCompradorAsync(ulong compradorId, ulong id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            $"SELECT p.*, {CantidadOfertas} FROM pedidos p WHERE p.id = @id AND p.comprador_id = @uid AND p.estado <> 'baja'",
            conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@uid", compradorId);
        return (await cmd.ListaAsync(r => r.Pedido(), ct)).FirstOrDefault();
    }

    /// <summary>
    /// Cierra un pedido activo del comprador. Devuelve false si no era suyo o
    /// ya no estaba activo: el WHERE hace las dos comprobaciones en el mismo UPDATE.
    /// </summary>
    public async Task<bool> CerrarAsync(ulong compradorId, ulong id, string motivo, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            """
            UPDATE pedidos SET estado = 'cerrado', motivo_cierre = @motivo, actualizado = UTC_TIMESTAMP(3)
            WHERE id = @id AND comprador_id = @uid AND estado = 'activo'
            """, conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@uid", compradorId);
        cmd.Parameters.AddWithValue("@motivo", motivo);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>
    /// Pedidos activos para los revendedores, con filtros. Los de compradores
    /// suspendidos no aparecen.
    /// </summary>
    public async Task<List<PedidoDto>> DisponiblesAsync(ulong revendedorId, FiltrosListado f, ulong? id, CancellationToken ct)
    {
        // El WHERE se arma con fragmentos fijos escritos acá; los valores van
        // siempre como parámetros. Nada de lo que manda el usuario entra al SQL.
        var sql = new StringBuilder(
            $"""
            SELECT p.*, {CantidadOfertas},
                   EXISTS (SELECT 1 FROM ofertas o2 WHERE o2.pedido_id = p.id AND o2.revendedor_id = @yo) AS ya_oferte
            FROM pedidos p JOIN usuarios u ON u.id = p.comprador_id
            WHERE p.estado = 'activo' AND u.estado = 'activo'
            """);

        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand { Connection = conn };
        cmd.Parameters.AddWithValue("@yo", revendedorId);

        if (id is not null) { sql.Append(" AND p.id = @id"); cmd.Parameters.AddWithValue("@id", id); }
        if (f.Tipo is not null) { sql.Append(" AND p.tipo = @tipo"); cmd.Parameters.AddWithValue("@tipo", f.Tipo); }
        if (f.Marca is not null) { sql.Append(" AND p.marca = @marca"); cmd.Parameters.AddWithValue("@marca", f.Marca); }
        if (f.Zona is not null) { sql.Append(" AND p.zona = @zona"); cmd.Parameters.AddWithValue("@zona", f.Zona); }
        if (f.HastaUsd is not null)
        {
            // En dólares, contra el monto convertido al publicar: así un pedido
            // en pesos también entra en el filtro.
            sql.Append(" AND p.presupuesto_usd <= @hasta");
            cmd.Parameters.AddWithValue("@hasta", f.HastaUsd);
        }

        sql.Append($" ORDER BY p.creado DESC LIMIT {MaxFilas}");
        cmd.CommandText = sql.ToString();
        return await cmd.ListaAsync(r => r.Pedido(conYaOferte: true), ct);
    }

    // ---- Administración ------------------------------------------------------

    public async Task<List<PedidoAdminDto>> TodosAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            $"""
            SELECT p.*, {CantidadOfertas}, u.nombre AS autor
            FROM pedidos p JOIN usuarios u ON u.id = p.comprador_id
            ORDER BY p.creado DESC LIMIT {MaxFilas}
            """, conn);
        return await cmd.ListaAsync(r => new PedidoAdminDto(r.Pedido(), r.GetString("autor")), ct);
    }

    /// <summary>
    /// Baja o alta por un admin. Al volver a mostrarlo queda activo: si el
    /// comprador lo había cerrado antes de la baja, igual puede cerrarlo de nuevo.
    /// </summary>
    public async Task<bool> SetVisibleAsync(ulong id, bool visible, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            visible
                ? "UPDATE pedidos SET estado = 'activo', actualizado = UTC_TIMESTAMP(3) WHERE id = @id AND estado = 'baja'"
                : "UPDATE pedidos SET estado = 'baja', actualizado = UTC_TIMESTAMP(3) WHERE id = @id AND estado <> 'baja'",
            conn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<int> ContarActivosAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM pedidos WHERE estado = 'activo'", conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<List<PedidoDto>> UltimosAsync(int cantidad, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            $"SELECT p.*, {CantidadOfertas} FROM pedidos p ORDER BY p.creado DESC LIMIT @n", conn);
        cmd.Parameters.AddWithValue("@n", cantidad);
        return await cmd.ListaAsync(r => r.Pedido(), ct);
    }
}
