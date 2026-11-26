using System.Collections.Immutable;

namespace Akbura.Language;

/// <summary>
/// Contains bound utility definitions and unresolved imports or definitions found during lookup.
/// </summary>
internal readonly record struct TailwindUtilityDefinitionLookup(
    ImmutableArray<TailwindUtilityDefinition> Definitions,
    bool HasUnresolvedDefinitions,
    bool HasUnresolvedImports);
