namespace Akbura.Workspaces.References;

/// <summary>
/// Stable identity for an Akbura or projected C# symbol inside a solution snapshot.
/// </summary>
public readonly record struct AkburaSymbolKey(
    AkburaProjectId ProjectId,
    string MetadataName,
    AkburaSymbolKind Kind,
    string? ContainingSymbol);
