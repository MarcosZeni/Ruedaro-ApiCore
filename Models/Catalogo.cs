namespace ApiCore.Models;

/// <summary>
/// Listas válidas de tipos, marcas y zonas.
///
/// <para>⚠ Las mismas listas están en <c>Web/src/lib/catalogo.ts</c> (la web
/// las necesita para armar los desplegables sin preguntarle a la API). Si se
/// cambia una, se cambian las dos. La que manda es esta: lo que no está acá se
/// rechaza aunque la web lo deje elegir.</para>
/// </summary>
public static class Catalogo
{
    public static readonly string[] Tipos = ["auto", "moto", "camion", "lancha"];

    public static readonly IReadOnlyDictionary<string, string[]> Marcas = new Dictionary<string, string[]>
    {
        ["auto"] = ["Chevrolet", "Citroën", "Fiat", "Ford", "Honda", "Hyundai", "Jeep", "Kia",
                    "Nissan", "Peugeot", "Renault", "Toyota", "Volkswagen", "Otra"],
        ["moto"] = ["Bajaj", "Benelli", "BMW", "Corven", "Gilera", "Honda", "Kawasaki", "KTM",
                    "Motomel", "Suzuki", "Yamaha", "Zanella", "Otra"],
        ["camion"] = ["Citroën", "Fiat", "Ford", "Iveco", "Mercedes-Benz", "Peugeot", "Renault",
                      "Scania", "Toyota", "Volkswagen", "Otra"],
        ["lancha"] = ["Bermuda", "Canestrari", "Klase A", "Quicksilver", "Regnicoli", "Tracker", "Otra"],
    };

    public static readonly string[] Zonas =
    [
        "CABA", "GBA Norte", "GBA Oeste", "GBA Sur", "La Plata", "Interior de Buenos Aires",
        "Córdoba", "Santa Fe", "Mendoza", "Tucumán", "Resto del país",
    ];

    public static readonly string[] Monedas = ["USD", "ARS"];

    /// <summary>
    /// Tope de ofertas por pedido, para que al comprador no le llegue spam.
    /// Provisorio (igual que en la web).
    /// </summary>
    public const int MaxOfertasPorPedido = 10;

    public const int AnioMinimo = 1980;

    /// <summary>Hasta el año que viene: los 0 km del modelo siguiente salen antes.</summary>
    public static int AnioMaximo => DateTime.UtcNow.Year + 1;

    public static bool TipoValido(string? tipo) => tipo is not null && Tipos.Contains(tipo);

    public static bool MarcaValida(string tipo, string? marca)
        => marca is not null && Marcas.TryGetValue(tipo, out var lista) && lista.Contains(marca);

    public static bool ZonaValida(string? zona) => zona is not null && Zonas.Contains(zona);

    public static bool MonedaValida(string? moneda) => moneda is not null && Monedas.Contains(moneda);

    public static bool AnioValido(int? anio) => anio >= AnioMinimo && anio <= AnioMaximo;
}
