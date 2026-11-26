namespace Akbura.Mcp;

/// <summary>
/// Pairs a resolved package or referenced assembly name with its version.
/// </summary>
internal sealed record DependencyVersion(string Name, string Version);
