using ApiCore.Data;
using ApiCore.Models;

namespace ApiCore.Endpoints;

/// <summary>
/// Catálogo (pestaña "Buscar"), en dos secciones: agencias y particulares.
/// <c>/api/v1/publicaciones</c> es público, sin sesión;
/// <c>/api/v1/mis-publicaciones</c> es de quien publica (particular o agencia).
/// </summary>
public static class PublicacionEndpoints
{
    private static readonly string[] EstadosDelDueno = ["activa", "pausada", "vendida"];

    public static IEndpointRouteBuilder MapPublicaciones(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");

        // ---- Público ----
        api.MapGet("/cotizacion", async (CotizacionService dolar, CancellationToken ct)
            => await dolar.ActualAsync(ct) is { } c
                ? Results.Ok(c)
                : Errores.Json(StatusCodes.Status503ServiceUnavailable, "sin_cotizacion", "No hay cotización disponible por ahora."));

        api.MapGet("/publicaciones", async (HttpContext http, PublicacionRepository repo, CancellationToken ct) =>
        {
            int limite = int.TryParse(http.Request.Query["limite"].FirstOrDefault(), out int l) ? l : PedidoRepository.MaxFilas;
            return Results.Ok(await repo.CatalogoAsync(Filtros.Leer(http.Request.Query), limite, ct));
        });

        api.MapGet("/publicaciones/cantidades", async (string? vendedor, PublicacionRepository repo, CancellationToken ct)
            => Results.Ok(await repo.CantidadPorTipoAsync(Filtros.Vendedor(vendedor), ct)));

        api.MapGet("/publicaciones/carrusel", async (string? vendedor, PublicacionRepository repo, CancellationToken ct)
            => Filtros.Vendedor(vendedor) is { } v
                ? Results.Ok(await repo.CarruselAsync(v, ct))
                : Errores.Validacion("vendedor", "Indicá agencia o particular."));

        api.MapGet("/publicaciones/{id:long}", async (ulong id, PublicacionRepository repo, CancellationToken ct)
            => await repo.VisibleAsync(id, ct) is { } p ? Results.Ok(p) : PedidoEndpoints.NoEncontrado());

        // ---- Quien publica: particulares y agencias ----
        api.MapGet("/mis-publicaciones", async (HttpContext http, PublicacionRepository repo, CancellationToken ct)
            => Results.Ok(await repo.DeUsuarioAsync(http.Usuario(), ct)))
            .RequireRol("comprador", "revendedor");

        api.MapPost("/mis-publicaciones", async (
            PublicacionRequest req, HttpContext http, PublicacionRepository repo, CotizacionService dolar, CancellationToken ct) =>
        {
            var yo = http.Usuario();
            if (string.IsNullOrEmpty(yo.Telefono))
                return Errores.Json(StatusCodes.Status409Conflict, "falta_telefono",
                    "Antes de publicar cargá tu WhatsApp en Mi cuenta: es como te van a contactar.");

            var (ok, error) = ValidarContenido.Publicacion(req);
            if (error is not null) return error;

            ulong? id = await repo.InsertAsync(yo.Id, ok!, await dolar.ActualAsync(ct), ct);

            // La misma patente en otro aviso vigente (de esta persona o de otra):
            // evita publicar el mismo vehículo varias veces y que alguien publique
            // uno ajeno con fotos copiadas.
            if (id is null)
                return Errores.Json(StatusCodes.Status409Conflict, "patente_existente",
                    "Ya hay un vehículo publicado con esa patente.", "patente");

            return Results.Json(new { id = id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                statusCode: StatusCodes.Status201Created);
        }).RequireRol("comprador", "revendedor");

        api.MapPost("/mis-publicaciones/{id:long}/estado", async (ulong id, EstadoRequest req, HttpContext http, PublicacionRepository repo, CancellationToken ct) =>
        {
            if (!EstadosDelDueno.Contains(req.Estado))
                return Errores.Validacion("estado", "Estado inválido.");
            return await repo.CambiarEstadoAsync(http.Usuario().Id, id, req.Estado!, ct)
                ? Results.NoContent()
                : PedidoEndpoints.NoEncontrado();
        }).RequireRol("comprador", "revendedor");

        api.MapPost("/mis-publicaciones/{id:long}/destacar", async (ulong id, DestacarRequest req, HttpContext http, PublicacionRepository repo, CancellationToken ct)
            => await repo.DestacarAsync(http.Usuario().Id, id, req.Destacada, ct) switch
            {
                ResultadoDestacar.Ok => Results.NoContent(),
                ResultadoDestacar.SinCupo => Errores.Json(StatusCodes.Status409Conflict, "sin_cupo",
                    http.Usuario().PlanVigente
                        ? $"Ya usaste tus {PublicacionRepository.DestacadosPlan} destacados. Sacá uno para destacar este."
                        : "Ya usaste tu destacado gratis. Sacá el otro o pedí el plan de 10 destacados."),
                _ => PedidoEndpoints.NoEncontrado(),
            })
            .RequireRol("comprador", "revendedor");

        return app;
    }
}
