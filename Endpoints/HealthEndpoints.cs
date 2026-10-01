using ApiCore.Data;
using ApiCore.Models;

namespace ApiCore.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (Database db, CancellationToken ct) =>
        {
            // Techo corto: un health que tarda lo mismo que el timeout del que lo
            // consulta no distingue "lento" de "caído".
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(3));

            bool baseOk = await db.CanConnectAsync(limite.Token);

            // Sin el mensaje de MySQL en la respuesta: el motivo va al log del
            // arranque, no a quien pregunte.
            return baseOk
                ? Results.Ok(new HealthResponse("ok", "ok"))
                : Results.Json(new HealthResponse("degradado", "caida"),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }
}
