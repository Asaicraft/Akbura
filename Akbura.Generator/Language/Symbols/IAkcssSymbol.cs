using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal interface IAkcssSymbol : ISymbol
{
    AkburaSyntax DeclarationSyntax { get; }

    ImmutableArray<IAkcssOperation> Operations { get; }

    string? ClassName { get; }

    bool HasTargetType { get; }

    /// <summary>
    /// C# target control type for selectors such as <c>Button.myclass</c>.
    /// Default for global selectors such as <c>.myclass</c>.
    /// </summary>
    CSharpSymbolDefinition TargetType { get; }

    bool IsIntercepted { get; }

    CSharpSymbolDefinition InterceptType { get; }
}
