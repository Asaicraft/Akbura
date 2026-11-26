namespace Akbura.Workspaces.Utilities;

/// <summary>
/// Locates a utility definition by file path and zero-based UTF-16 span.
/// </summary>
public sealed record AkburaUtilitySourceLocation(string FilePath, int Start, int Length);
