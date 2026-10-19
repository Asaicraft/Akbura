using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Current document proof values, never compiler objects. Receiver-wide source
/// contracts deliberately cover competing and missing members, not just positives.
/// Completeness is meaningful only within the validated declaration-change subset.
/// </summary>
internal sealed record DetachedCSharpDependencySet(
    int Schema,
    bool IsComplete,
    bool HasFixedExportedSurface,
    ImmutableArray<DetachedCSharpTypeIdentity> SourceTypes,
    ImmutableArray<string> Symbols,
    ImmutableArray<DetachedCSharpLookupDependency> Lookups,
    ImmutableArray<string> Conversions,
    ImmutableArray<string> IncompleteReasons)
{
    internal const int CurrentSchema = 1;
    internal static readonly DetachedCSharpDependencySet Missing = new(CurrentSchema, false, false, [], [], [], [], ["MissingSemanticDependencies"]);

}

internal sealed record DetachedCSharpTypeIdentity(string AssemblyIdentity, string MetadataName);
internal sealed record DetachedCSharpLookupDependency(string Key, string Receiver, string Name, string Scope,
    string SelectedSymbol, int CandidateReason, ImmutableArray<string> Candidates, ImmutableArray<string> ArgumentTypes, bool IsNegative);
