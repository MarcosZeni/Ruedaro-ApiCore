using ApiCore.Data;
using ApiCore.Models;

namespace ApiCore.Endpoints;

/// <summary>
/// Pedidos y ofertas: <c>/api/v1</c>. El comprador maneja sus pedidos en
/// <c>/mis-pedidos</c>; el revendedor ve los de todos en <c>/pedidos</c> y
/// oferta. Cada ruta exige su rol: un comprador no puede listar pedidos ajenos
/// ni un revendedor publicar uno.
/// </summary>
public static class PedidoEndpoints
{
    public static IEndpointRouteBuilder MapPedidos(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");

        // ---- Comprador ----
        api.MapGet("/mis-pedidos", async (HttpContext http, PedidoRepository pedidos, CancellationToken ct)
            => Results.Ok(await pedidos.DelCompradorAsync(http.Usuario().Id, ct)))
            .RequireRol("comprador");

        api.MapPost("/mis-pedidos", async (PedidoRequest req, HttpContext http, PedidoRepository pedidos, CotizacionService dolar, CancellationToken ct) =>
        {
            var (ok, error) = ValidarContenido.Pedido(req);
            if (error is not null) return error;
            ulong id = await pedidos.InsertAsync(http.Usuario().Id, ok!, await dolar.ActualAsync(ct), ct);
            return Results.Json(new { id = id.ToString(System.Globalization.CultureInfo.InvariantCulture) }, statusCode: StatusCodes.Status201Created);
        }).RequireRol("comprador");

        api.MapGet("/mis-pedidos/{id:long}", async (ulong id, HttpContext http, PedidoRepository pedidos, OfertaRepository ofertas, CancellationToken ct) =>
        {
            // Un pedido ajeno da 404 y no 403: no se confirma que exista.
            var pedido = await pedidos.DelCompradorAsync(http.Usuario().Id, id, ct);
            return pedido is null
                ? NoEncontrado()
                : Results.Ok(new PedidoConOfertasDto(pedido, await ofertas.DePedidoAsync(id, ct)));
        }).RequireRol("comprador");

        api.MapPost("/mis-pedidos/{id:long}/cerrar", async (ulong id, CerrarPedidoRequest req, HttpContext http, PedidoRepository pedidos, CancellationToken ct) =>
        {
            if (req.Motivo is not ("encontrado" or "ya_no_busca"))
                return Errores.Validacion("motivo", "Elegí por qué lo cerrás.");
            return await pedidos.CerrarAsync(http.Usuario().Id, id, req.Motivo, ct)
                ? Results.NoContent()
                : NoEncontrado();
        }).RequireRol("comprador");

        // ---- Revendedor ----
        api.MapGet("/pedidos", async (HttpContext http, PedidoRepository pedidos, CancellationToken ct)
            => Results.Ok(await pedidos.DisponiblesAsync(http.Usuario().Id, Filtros.Leer(http.Request.Query), null, ct)))
            .RequireRol("revendedor");

        api.MapGet("/pedidos/{id:long}", async (ulong id, HttpContext http, PedidoRepository pedidos, CancellationToken ct) =>
        {
            var pedido = (await pedidos.DisponiblesAsync(http.Usuario().Id, Filtros.Ninguno, id, ct)).FirstOrDefault();
            return pedido is null ? NoEncontrado() : Results.Ok(pedido);
        }).RequireRol("revendedor");

        api.MapPost("/pedidos/{id:long}/ofertas", async (
            ulong id, OfertaRequest req, HttpContext http, PedidoRepository pedidos, OfertaRepository ofertas, CotizacionService dolar,
            CancellationToken ct) =>
        {
            var yo = http.Usuario();

            // Sin WhatsApp, el comprador no tendría cómo contestarle.
            if (string.IsNullOrEmpty(yo.Telefono))
                return Errores.Json(StatusCodes.Status409Conflict, "falta_telefono",
                    "Antes de ofertar cargá tu WhatsApp en Mi cuenta: es como te va a contactar el comprador.");

            var pedido = (await pedidos.DisponiblesAsync(yo.Id, Filtros.Ninguno, id, ct)).FirstOrDefault();
            if (pedido is null) return NoEncontrado();

            var (ok, error) = ValidarContenido.Oferta(req, pedido.Tipo);
            if (error is not null) return error;

            return await ofertas.CrearAsync(id, yo.Id, ok!, await dolar.ActualAsync(ct), ct) switch
            {
                ResultadoOferta.Creada => Results.StatusCode(StatusCodes.Status201Created),
                ResultadoOferta.YaOferto => Errores.Json(StatusCodes.Status409Conflict, "ya_oferto",
                    "Ya ofertaste en este pedido. Se puede ofertar una sola vez."),
                ResultadoOferta.Lleno => Errores.Json(StatusCodes.Status409Conflict, "pedido_lleno",
                    "Este pedido ya recibió todas las ofertas posibles."),
                _ => NoEncontrado(),
            };
        }).RequireRol("revendedor");

        api.MapGet("/mis-ofertas", async (HttpContext http, OfertaRepository ofertas, CancellationToken ct)
            => Results.Ok(await ofertas.DeRevendedorAsync(http.Usuario().Id, ct)))
            .RequireRol("revendedor");

        return app;
    }

    internal static IResult NoEncontrado()
        => Errores.Json(StatusCodes.Status404NotFound, "no_encontrado", "No encontramos lo que buscás. Puede que ya no esté disponible.");
}

/// <summary>
/// Filtros de los listados, leídos de la query string. Lo que no está en el
/// catálogo se ignora: un filtro inválido no rompe el listado ni llega al SQL.
/// </summary>
internal static class Filtros
{
    public static readonly FiltrosListado Ninguno = new(null, null, null, null, null);

    private static readonly string[] Vendedores = ["agencia", "particular"];

    public static string? Vendedor(string? valor) => Vendedores.FirstOrDefault(v => v == valor);

    public static FiltrosListado Leer(IQueryCollection q)
    {
        string? tipo = q["tipo"].FirstOrDefault();
        if (!Catalogo.TipoValido(tipo)) tipo = null;

        string? marca = q["marca"].FirstOrDefault();
        if (tipo is null || !Catalogo.MarcaValida(tipo, marca)) marca = null;

        string? zona = q["zona"].FirstOrDefault();
        if (!Catalogo.ZonaValida(zona)) zona = null;

        long? hasta = long.TryParse(q["hasta"].FirstOrDefault(), out long h) && h > 0 ? h : null;
        int? desde = int.TryParse(q["desde"].FirstOrDefault(), out int d) && Catalogo.AnioValido(d) ? d : null;

        return new FiltrosListado(tipo, marca, zona, hasta, desde, Vendedor(q["vendedor"].FirstOrDefault()));
    }
}
