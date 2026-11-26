namespace Akbura.Mcp;

/// <summary>
/// Describes a resolved utility definition and the imports needed to use it.
/// </summary>
internal sealed record UtilityResponse(
    string Id,
    string Name,
    string UsageSyntax,
    UtilityParameterResponse[] Parameters,
    string? DeclaredTargetType,
    string Availability,
    string[] RequiredImports,
    string OriginKind,
    string Origin,
    UtilityLocationResponse? SourceLocation,
    string? Category,
    string? Description,
    string? Example);
