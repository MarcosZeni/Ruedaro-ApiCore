using System.Globalization;
using ApiCore.Models;
using MySqlConnector;

namespace ApiCore.Data;

/// <summary>
/// Pasar filas a DTOs, en un solo lugar. Por nombre de columna y no por
/// posición: agregar una columna al SELECT no corre los índices de nadie.
/// </summary>
internal static class Lectura
{
    public static string Id(this MySqlDataReader r, string col) => r.GetUInt64(col).ToString(CultureInfo.InvariantCulture);

    public static string? StrONull(this MySqlDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    public static long? LongONull(this MySqlDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : (long)r.GetUInt64(i);
    }

    public static decimal? DecONull(this MySqlDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetDecimal(i);
    }

    /// <summary>Rol de quien publica → tipo de vendedor que ve el comprador.</summary>
    public static string Vendedor(string rol) => rol == "revendedor" ? "agencia" : "particular";

    /// <summary>Las fechas se guardan en UTC; se marcan como UTC para que el JSON lleve la "Z".</summary>
    public static DateTime Utc(this MySqlDataReader r, string col) => DateTime.SpecifyKind(r.GetDateTime(col), DateTimeKind.Utc);

    /// <summary>
    /// Pedido. Espera las columnas de pedidos (p.*) más <c>cantidad_ofertas</c>
    /// y, si se pidió, <c>ya_oferte</c>.
    /// </summary>
    public static PedidoDto Pedido(this MySqlDataReader r, bool conYaOferte = false) => new(
        r.Id("id"),
        r.GetString("tipo"),
        r.GetString("marca"),
        r.GetString("modelo"),
        r.GetUInt16("anio_desde"),
        r.GetUInt16("anio_hasta"),
        (long)r.GetUInt64("presupuesto"),
        r.GetString("moneda"),
        r.LongONull("presupuesto_usd"),
        r.DecONull("cotizacion"),
        r.GetString("zona"),
        r.StrONull("comentarios"),
        r.GetString("estado"),
        r.Utc("creado"),
        r.GetInt32("cantidad_ofertas"),
        conYaOferte ? r.GetInt32("ya_oferte") > 0 : null);

    /// <summary>Oferta. Las columnas del pedido llegan con prefijo: <c>pedido_tipo</c>; las del revendedor, <c>rev_nombre</c> y <c>rev_telefono</c>.</summary>
    public static OfertaDto Oferta(this MySqlDataReader r) => new(
        r.Id("o_id"),
        r.Id("pedido_id"),
        r.GetString("pedido_tipo"),
        new RevendedorDto(r.GetString("rev_nombre"), r.StrONull("rev_telefono")),
        r.GetString("o_marca"),
        r.GetString("o_modelo"),
        r.GetString("o_version"),
        r.GetUInt16("o_anio"),
        r.GetUInt32("o_uso"),
        (long)r.GetUInt64("o_precio"),
        r.GetString("o_moneda"),
        r.LongONull("o_precio_usd"),
        r.DecONull("o_cotizacion"),
        r.StrONull("o_link"),
        r.StrONull("o_comentario"),
        r.GetString("o_estado"),
        r.Utc("o_creado"));

    /// <summary>
    /// Publicación, con <c>rev_nombre</c>, <c>rev_telefono</c> y <c>rev_rol</c> de quien publica.
    /// La patente se lee solo con <paramref name="conPatente"/>: el catálogo
    /// público llama sin él, así no hay forma de que se escape por un descuido.
    /// </summary>
    public static PublicacionDto Publicacion(this MySqlDataReader r, bool conPatente = false) => new(
        r.Id("id"),
        r.GetString("tipo"),
        r.GetString("marca"),
        r.GetString("modelo"),
        r.GetString("version"),
        r.GetUInt16("anio"),
        r.GetUInt32("uso"),
        (long)r.GetUInt64("precio"),
        r.GetString("moneda"),
        r.LongONull("precio_usd"),
        r.DecONull("cotizacion"),
        r.GetString("zona"),
        r.StrONull("descripcion"),
        new RevendedorDto(r.GetString("rev_nombre"), r.StrONull("rev_telefono")),
        Vendedor(r.GetString("rev_rol")),
        conPatente ? r.GetString("patente") : null,
        r.GetBoolean("destacada"),
        r.GetString("estado"),
        r.Utc("creado"));

    public static async Task<List<T>> ListaAsync<T>(this MySqlCommand cmd, Func<MySqlDataReader, T> leer, CancellationToken ct)
    {
        var lista = new List<T>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(leer(r));
        return lista;
    }
}
