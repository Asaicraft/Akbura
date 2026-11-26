namespace Akbura.Mcp;

/// <summary>
/// Describes a utility parameter's name, type, and optionality.
/// </summary>
internal sealed record UtilityParameterResponse(string Name, string Type, bool IsOptional);
