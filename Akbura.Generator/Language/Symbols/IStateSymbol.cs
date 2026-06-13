using Akbura.Language.Syntax;

namespace Akbura.Language.Symbols;

internal interface IStateSymbol : ISymbol
{
    StateDeclarationSyntax DeclarationSyntax { get; }

    CSharpSymbolDefinition Type { get; }

    bool HasExplicitType { get; }
}
