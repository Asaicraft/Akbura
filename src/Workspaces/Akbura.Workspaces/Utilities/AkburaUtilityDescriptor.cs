using System.Collections.Immutable;

namespace Akbura.Workspaces.Utilities;

public enum AkburaUtilityAvailability
{
    Available,
    RequiresImport,
}

public enum AkburaUtilityOriginKind
{
    Source,
    Library,
}

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

public sealed record AkburaUtilityParameterDescriptor(string Name, string Type, bool IsOptional);

/// <summary>A definition span in zero-based UTF-16 offsets.</summary>
public sealed record AkburaUtilitySourceLocation(string FilePath, int Start, int Length);
