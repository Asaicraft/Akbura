namespace Akbura.Language.CodeGeneration;

/// <summary>
/// A syntactic dependency candidate, not the result of name binding.
/// Unresolved and ambiguous candidates must remain in the project dependency graph.
/// </summary>
internal readonly record struct DocumentDependencyName(
    DocumentDependencyKind Kind,
    string Name,
    string Alias = "");

internal enum DocumentDependencyKind
{
    Component,
    AkcssModule,
    AkcssApply,
    Namespace,
    StaticUsing,
    Alias,
    Identifier,
}
