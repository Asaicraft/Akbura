using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Measurement-only scalar observations. Never store compilations, trees, symbols,
/// models, operations or delegates here. Reasons are not cache-equivalence rules.
/// An absent callback is observed through Roslyn tracked steps, not a synthetic event.
/// </summary>
internal sealed record GenerationInvalidationEvent(
    string Kind,
    string Path,
    string Decision,
    ImmutableArray<string> Reasons,
    ImmutableDictionary<string, string> Data);
