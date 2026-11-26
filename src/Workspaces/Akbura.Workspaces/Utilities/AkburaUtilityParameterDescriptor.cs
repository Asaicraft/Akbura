namespace Akbura.Workspaces.Utilities;

/// <summary>
/// Describes the declared name, type, and optionality of a utility parameter.
/// </summary>
public sealed record AkburaUtilityParameterDescriptor(string Name, string Type, bool IsOptional);
