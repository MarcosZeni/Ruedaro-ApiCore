namespace ApiCore.Models;

/// <summary>Respuesta de <c>/health</c>. <c>Base</c> es "ok" o "caida".</summary>
public sealed record HealthResponse(string Estado, string Base);
