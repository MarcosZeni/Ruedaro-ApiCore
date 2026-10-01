using System.Globalization;
using System.Text.Json;
using MySqlConnector;

namespace ApiCore.Data;

/// <summary>Pesos por dólar según el BCRA, y de qué fecha es.</summary>
public sealed record Cotizacion(decimal Valor, DateOnly Fecha);

/// <summary>
/// Dólar oficial del BCRA (API pública de estadísticas cambiarias, sin clave).
///
/// <para><b>Nunca frena una publicación.</b> Se consulta al BCRA como mucho una
/// vez por hora; si no contesta, se usa la última cotización guardada en la
/// base; si no hay ninguna, el aviso se publica igual sin monto en dólares. Que
/// el BCRA esté caído no puede impedir que alguien publique su auto.</para>
///
/// <para><b>Plazo corto.</b> La consulta tiene 5 segundos de techo: está en el
/// camino de publicar, y un servicio externo lento no puede colgarlo.</para>
/// </summary>
public sealed class CotizacionService(HttpClient http, Database db, ILogger<CotizacionService> log)
{
    public const string UrlBcra = "https://api.bcra.gob.ar/estadisticascambiarias/v1.0/Cotizaciones/USD";

    private static readonly TimeSpan Vigencia = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _una = new(1, 1);
    private Cotizacion? _cache;
    private DateTime _consultada = DateTime.MinValue;

    /// <summary>La cotización vigente, o null si nunca se pudo obtener ninguna.</summary>
    public async Task<Cotizacion?> ActualAsync(CancellationToken ct)
    {
        if (_cache is not null && DateTime.UtcNow - _consultada < Vigencia) return _cache;

        // Una sola consulta al BCRA a la vez: si llegan veinte publicaciones
        // juntas con la cache vencida, consulta la primera y las demás esperan.
        await _una.WaitAsync(ct);
        try
        {
            if (_cache is not null && DateTime.UtcNow - _consultada < Vigencia) return _cache;

            var nueva = await ConsultarBcraAsync(ct);
            if (nueva is not null)
            {
                await GuardarAsync(nueva, ct);
                _cache = nueva;
            }
            else
            {
                // Sin BCRA: la última guardada. Se reintenta en la próxima hora.
                _cache ??= await UltimaGuardadaAsync(ct);
            }
            _consultada = DateTime.UtcNow;
            return _cache;
        }
        finally
        {
            _una.Release();
        }
    }

    /// <summary>
    /// Monto en dólares: igual si ya está en dólares, convertido si está en
    /// pesos. Redondeado a dólares enteros, como se habla de precios de autos.
    /// </summary>
    public static long? EnDolares(long monto, string moneda, Cotizacion? cot)
        => moneda == "USD" ? monto
         : cot is null || cot.Valor <= 0 ? null
         : (long)Math.Round(monto / cot.Valor, MidpointRounding.AwayFromZero);

    private async Task<Cotizacion?> ConsultarBcraAsync(CancellationToken ct)
    {
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(5));

            await using var stream = await http.GetStreamAsync(UrlBcra, limite.Token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: limite.Token);

            // { "results": [ { "fecha": "2026-09-30", "detalle": [ { "codigoMoneda": "USD", "tipoCotizacion": 1517.0 } ] } ] }
            var resultado = json.RootElement.GetProperty("results")[0];
            var fecha = DateOnly.Parse(resultado.GetProperty("fecha").GetString()!, CultureInfo.InvariantCulture);
            decimal valor = resultado.GetProperty("detalle")[0].GetProperty("tipoCotizacion").GetDecimal();

            // Un valor absurdo (cero, negativo) es un error del lado del BCRA:
            // mejor la cotización de ayer que convertir precios con basura.
            if (valor <= 0) return null;
            return new Cotizacion(valor, fecha);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException
                                      or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException
                                      or FormatException)
        {
            log.LogWarning(e, "No se pudo obtener la cotización del BCRA; se usa la última guardada.");
            return null;
        }
    }

    private async Task GuardarAsync(Cotizacion c, CancellationToken ct)
    {
        try
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new MySqlCommand(
                """
                INSERT INTO cotizaciones (fecha, valor, obtenida) VALUES (@fecha, @valor, UTC_TIMESTAMP(3))
                ON DUPLICATE KEY UPDATE valor = VALUES(valor), obtenida = VALUES(obtenida)
                """, conn);
            cmd.Parameters.AddWithValue("@fecha", c.Fecha.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@valor", c.Valor);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (MySqlException e)
        {
            log.LogWarning(e, "No se pudo guardar la cotización del {Fecha}", c.Fecha);
        }
    }

    private async Task<Cotizacion?> UltimaGuardadaAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new MySqlCommand("SELECT fecha, valor FROM cotizaciones ORDER BY fecha DESC LIMIT 1", conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            return await r.ReadAsync(ct) ? new Cotizacion(r.GetDecimal(1), DateOnly.FromDateTime(r.GetDateTime(0))) : null;
        }
        catch (MySqlException e)
        {
            log.LogWarning(e, "No se pudo leer la última cotización guardada");
            return null;
        }
    }
}
