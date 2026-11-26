using System.Collections.Immutable;

namespace Akbura.Workspaces.Layout;

/// <summary>
/// Describes a bundled recipe and the requirements and behavior of its reference component.
/// </summary>
public sealed record AkburaLayoutRecipeDescriptor(
    string Id,
    int Revision,
    string Title,
    string Description,
    ImmutableArray<string> Tags,
    string Format,
    string SuggestedFileName,
    ImmutableArray<string> RequiredImports,
    ImmutableArray<string> RequiredTypes,
    ImmutableArray<AkburaLayoutUtilityRequirement> RequiredUtilities,
    ImmutableArray<AkburaLayoutVariantRequirement> RequiredVariants,
    ImmutableArray<string> Preconditions,
    ImmutableArray<string> ExpectedBehavior,
    ImmutableArray<string> IntegrationNotes,
    ImmutableArray<string> Limitations);
