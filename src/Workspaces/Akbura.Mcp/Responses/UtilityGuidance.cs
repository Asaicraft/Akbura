namespace Akbura.Mcp;

/// <summary>
/// Provides utility usage instructions and an example supported by the project context.
/// </summary>
internal sealed record UtilityGuidance(
    string Instructions,
    string? Example,
    string? ExampleContext,
    string[] ExampleImports);
