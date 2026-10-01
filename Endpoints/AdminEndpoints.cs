using ApiCore.Data;
using ApiCore.Models;

namespace ApiCore.Endpoints;

/// <summary>
/// Administración: <c>/api/v1/admin</c>, solo rol admin. Moderar es ocultar,
/// nunca borrar: lo dado de baja se puede volver a mostrar y queda para revisar.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        admin.MapGet("/resumen", async (
            UserRepository users, PedidoRepository pedidos, OfertaRepository ofertas, PublicacionRepository publicaciones,
            CancellationToken ct) =>
        {
            var (compradores, revendedores, suspendidos) = await users.ContarAsync(ct);
            var ultimos = await pedidos.UltimosAsync(3, ct);
            int activos = await pedidos.ContarActivosAsync(ct);
            return Results.Ok(new ResumenAdminDto(
                compradores, revendedores, suspendidos, activos,
                await ofertas.ContarAsync(ct), await publicaciones.ContarVisiblesAsync(ct), ultimos));
        }).RequireRol("admin");

        admin.MapGet("/usuarios", async (string? q, UserRepository users, CancellationToken ct)
            => Results.Ok(await users.ListarAsync(q is { Length: > 100 } ? q[..100] : q, ct)))
            .RequireRol("admin");

        admin.MapPost("/usuarios/{id:long}/estado", async (ulong id, EstadoRequest req, UserRepository users, CancellationToken ct) =>
        {
            if (req.Estado is not ("activo" or "suspendido")) return Errores.Validacion("estado", "Estado inválido.");
            return await users.SetEstadoAsync(id, req.Estado, ct) ? Results.NoContent() : PedidoEndpoints.NoEncontrado();
        }).RequireRol("admin");

        admin.MapPost("/usuarios/{id:long}/plan", async (ulong id, PlanRequest req, UserRepository users, CancellationToken ct) =>
        {
            if (req.Dias is < 0 or > 366) return Errores.Validacion("dias", "Entre 0 y 366 días.");
            return await users.SetPlanAsync(id, req.Dias, ct) ? Results.NoContent() : PedidoEndpoints.NoEncontrado();
        }).RequireRol("admin");

        admin.MapGet("/pedidos", async (PedidoRepository repo, CancellationToken ct) => Results.Ok(await repo.TodosAsync(ct)))
            .RequireRol("admin");
        admin.MapPost("/pedidos/{id:long}/estado", (ulong id, EstadoRequest req, PedidoRepository repo, CancellationToken ct)
            => Moderar(req, visible => repo.SetVisibleAsync(id, visible, ct)))
            .RequireRol("admin");

        admin.MapGet("/ofertas", async (OfertaRepository repo, CancellationToken ct) => Results.Ok(await repo.TodasAsync(ct)))
            .RequireRol("admin");
        admin.MapPost("/ofertas/{id:long}/estado", (ulong id, EstadoRequest req, OfertaRepository repo, CancellationToken ct)
            => Moderar(req, visible => repo.SetVisibleAsync(id, visible, ct)))
            .RequireRol("admin");

        admin.MapGet("/publicaciones", async (PublicacionRepository repo, CancellationToken ct) => Results.Ok(await repo.TodasAsync(ct)))
            .RequireRol("admin");
        admin.MapPost("/publicaciones/{id:long}/estado", (ulong id, EstadoRequest req, PublicacionRepository repo, CancellationToken ct)
            => Moderar(req, visible => repo.SetVisibleAsync(id, visible, ct)))
            .RequireRol("admin");

        return app;
    }

    /// <summary>"visible" o "baja". Si no cambió nada (ya estaba así o no existe) igual es 204: el resultado es el pedido.</summary>
    private static async Task<IResult> Moderar(EstadoRequest req, Func<bool, Task<bool>> aplicar)
    {
        if (req.Estado is not ("visible" or "baja")) return Errores.Validacion("estado", "Estado inválido.");
        await aplicar(req.Estado == "visible");
        return Results.NoContent();
    }
}
