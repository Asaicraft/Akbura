using Akbura.Language.Symbols;

namespace Akbura.Language;

/// <summary>
/// Pairs a bound AKCSS utility symbol with an import needed to resolve it.
/// </summary>
internal readonly record struct TailwindUtilityDefinition(
    ITailwindUtilitySymbol Symbol,
    string? RequiredImport);
