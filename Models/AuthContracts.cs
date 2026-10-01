namespace ApiCore.Models;

// Lo que entra y sale de /api/v1/auth. JSON en camelCase (default de .NET).

public sealed record RegistroRequest(string? Nombre, string? Email, string? Password, string? Telefono, string? Rol);

public sealed record IngresarRequest(string? Email, string? Password);

public sealed record PerfilRequest(string? Nombre, string? Telefono);

/// <summary>
/// Usuario tal como lo ve la web. Nunca lleva el hash de la contraseña. El id
/// va como texto: es BIGINT y JavaScript pierde precisión con enteros grandes.
/// </summary>
public sealed record UsuarioDto(
    string Id, string Nombre, string Email, string? Telefono, string Rol, string Estado, DateTime Creado);

/// <summary>Sesión recién creada. El token se muestra una sola vez: la base guarda solo su hash.</summary>
public sealed record SesionResponse(string Token, DateTime Expira, UsuarioDto Usuario);

/// <summary>
/// Error con un código estable (para que la web decida qué hacer) y un mensaje
/// en castellano listo para mostrar. <c>Campo</c> dice qué input marcar, si aplica.
/// </summary>
public sealed record ErrorResponse(string Codigo, string Mensaje, string? Campo = null);
