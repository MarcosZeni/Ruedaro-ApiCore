namespace ApiCore.Models;

// Pedidos, ofertas y publicaciones: lo que entra y sale de la API. Los ids van
// como texto (BIGINT; JavaScript pierde precisión con enteros grandes).

// ---- Lo que sale -------------------------------------------------------------

/// <summary>Lo que ve el comprador del revendedor: con quién habla y a qué WhatsApp.</summary>
public sealed record RevendedorDto(string Nombre, string? Telefono);

public sealed record PedidoDto(
    string Id, string Tipo, string Marca, string Modelo, int AnioDesde, int AnioHasta,
    long Presupuesto, string Moneda, long? PresupuestoUsd, decimal? Cotizacion,
    string Zona, string? Comentarios, string Estado,
    DateTime Creado, int CantidadOfertas,
    // Solo para revendedores: si ya ofertó en este pedido. null para el resto.
    bool? YaOferte);

public sealed record OfertaDto(
    string Id, string PedidoId, string Tipo, RevendedorDto Revendedor, string Marca, string Modelo,
    string Version, int Anio, long Uso, long Precio, string Moneda, long? PrecioUsd, decimal? Cotizacion,
    string? Link, string? Comentario, string Estado, DateTime Creado);

/// <summary>
/// Aviso del catálogo. <c>Vendedor</c>: "agencia" o "particular" (sale del rol
/// de quien publica). <c>Patente</c> solo llega al dueño y a los admins; en el
/// catálogo público va en null.
/// </summary>
public sealed record PublicacionDto(
    string Id, string Tipo, string Marca, string Modelo, string Version, int Anio, long Uso,
    long Precio, string Moneda, long? PrecioUsd, decimal? Cotizacion, string Zona, string? Descripcion,
    RevendedorDto Revendedor, string Vendedor, string? Patente, bool Destacada, string Estado, DateTime Creado);

/// <summary>Avisos propios y cuántos destacados le quedan.</summary>
public sealed record MisPublicacionesDto(
    IReadOnlyList<PublicacionDto> Avisos, int DestacadosUsados, int DestacadosMax, DateTime? PlanVence);

public sealed record MiOfertaDto(OfertaDto Oferta, PedidoDto Pedido);

public sealed record PedidoConOfertasDto(PedidoDto Pedido, IReadOnlyList<OfertaDto> Ofertas);

public sealed record PedidoAdminDto(PedidoDto Pedido, string Autor);

public sealed record ResumenAdminDto(
    int Compradores, int Revendedores, int Suspendidos, int PedidosActivos, int Ofertas,
    int Publicaciones, IReadOnlyList<PedidoDto> UltimosPedidos);

// ---- Lo que entra --------------------------------------------------------------

public sealed record PedidoRequest(
    string? Tipo, string? Marca, string? Modelo, int? AnioDesde, int? AnioHasta,
    long? Presupuesto, string? Moneda, string? Zona, string? Comentarios);

public sealed record OfertaRequest(
    string? Marca, string? Modelo, string? Version, int? Anio, long? Uso, long? Precio,
    string? Moneda, string? Link, string? Comentario);

public sealed record PublicacionRequest(
    string? Tipo, string? Marca, string? Modelo, string? Version, int? Anio, long? Uso,
    long? Precio, string? Moneda, string? Zona, string? Patente, string? Descripcion);

public sealed record DestacarRequest(bool Destacada);

/// <summary>Plan de destacados por <c>Dias</c> desde hoy; 0 lo quita.</summary>
public sealed record PlanRequest(int Dias);

public sealed record CerrarPedidoRequest(string? Motivo);

public sealed record EstadoRequest(string? Estado);
