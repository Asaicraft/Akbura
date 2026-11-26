using System.Collections.Immutable;

namespace Akbura.Workspaces.Utilities;

/// <summary>
/// Describes an applicable utility definition, its signature, origin, and availability.
/// </summary>
public sealed record AkburaUtilityDescriptor(
    string Id,
    string Name,
    string UsageSyntax,
    ImmutableArray<AkburaUtilityParameterDescriptor> Parameters,
    string? DeclaredTargetType,
    AkburaUtilityAvailability Availability,
    ImmutableArray<string> RequiredImports,
    AkburaUtilityOriginKind OriginKind,
    string Origin,
    AkburaUtilitySourceLocation? SourceLocation,
    string? Category,
    string? Description,
    string? Example);
