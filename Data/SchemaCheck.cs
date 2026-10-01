using MySqlConnector;

namespace ApiCore.Data;

/// <summary>
/// Comprueba al arrancar que la base tenga lo que la API va a usar.
///
/// <para><b>Por qué existe.</b> Los scripts de <c>sql/</c> se corren a mano y
/// nada los aplica solo. Sin este chequeo, un deploy con una migración olvidada
/// arranca bien y el error aparece recién con el primer usuario, como un 500 con
/// un mensaje de MySQL que no dice qué hacer. Acá aparece al arrancar, cuando el
/// que deploya todavía está mirando la consola.</para>
///
/// <para><b>Base caída y schema incompleto no son lo mismo.</b> Que MySQL no
/// conteste puede ser cuestión de segundos (el contenedor de la API suele
/// arrancar antes que el de la base) y de eso el servicio se recupera solo: se
/// avisa y se sigue. Una tabla que falta no se arregla esperando, así que ahí se
/// aborta.</para>
/// </summary>
public sealed class SchemaCheck(Database db)
{
    /// <summary>
    /// Lo que cada repositorio toca de verdad. Están las columnas y no solo las
    /// tablas: una migración a medio aplicar deja la tabla creada y sin alguna
    /// columna, y eso pasaría un chequeo que solo mirara nombres de tabla.
    /// Se agrega una entrada por cada script nuevo de <c>sql/</c>.
    /// </summary>
    private static readonly (string Tabla, string[] Columnas)[] Requerido =
    [
        ("usuarios",
            ["id", "email", "password_hash", "nombre", "telefono", "rol", "estado",
             "creado", "actualizado"]),
    ];

    public sealed record Resultado(bool Alcanzable, IReadOnlyList<string> Problemas, string? ErrorConexion)
    {
        public bool Ok => Alcanzable && Problemas.Count == 0;
    }

    public async Task<Resultado> RunAsync(CancellationToken ct)
    {
        // Un MySQL que acepta la conexión TCP pero no contesta colgaría el
        // arranque para siempre. Con techo, el peor caso es diez segundos.
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(10));

        var columnas = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        string sqlMode;

        try
        {
            await using var conn = await db.OpenAsync(limite.Token);

            await using (var cmd = new MySqlCommand("SELECT @@SESSION.sql_mode", conn))
                sqlMode = (await cmd.ExecuteScalarAsync(limite.Token)) as string ?? "";

            await using (var cmd = new MySqlCommand(
                """
                SELECT TABLE_NAME, COLUMN_NAME
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                """, conn))
            {
                await using var reader = await cmd.ExecuteReaderAsync(limite.Token);
                while (await reader.ReadAsync(limite.Token))
                {
                    string tabla = reader.GetString(0);
                    if (!columnas.TryGetValue(tabla, out var propias))
                        columnas[tabla] = propias = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    propias.Add(reader.GetString(1));
                }
            }
        }
        catch (Exception e) when (e is MySqlException or OperationCanceledException)
        {
            return new Resultado(Alcanzable: false, [], e.Message);
        }

        var problemas = new List<string>();

        // Sin modo estricto, MySQL trunca o convierte en silencio lo que no entra
        // en una columna: el INSERT sale bien y el dato queda roto (un hash
        // cortado, un precio en 0). Preferimos el error.
        if (!sqlMode.Contains("STRICT_TRANS_TABLES", StringComparison.OrdinalIgnoreCase)
            && !sqlMode.Contains("STRICT_ALL_TABLES", StringComparison.OrdinalIgnoreCase))
        {
            problemas.Add($"MySQL no está en modo estricto (sql_mode = '{sqlMode}'): falta STRICT_TRANS_TABLES");
        }

        foreach (var (tabla, requeridas) in Requerido)
        {
            // Si falta la tabla entera, decirlo una vez y no listar todas sus
            // columnas: el que lee la consola necesita el problema, no el detalle.
            if (!columnas.TryGetValue(tabla, out var presentes))
            {
                problemas.Add($"falta la tabla '{tabla}'");
                continue;
            }

            foreach (string columna in requeridas)
                if (!presentes.Contains(columna))
                    problemas.Add($"a '{tabla}' le falta la columna '{columna}'");
        }

        return new Resultado(Alcanzable: true, problemas, null);
    }
}
