using Akbura.Language.Syntax;

namespace Akbura.Language.Symbols;

internal interface IMarkupNameSymbol : ISymbol
{
    CSharpSymbolDefinition Type { get; }

    string IdentifierText { get; }

    MarkupAttachedPropertyAttributeSyntax DeclarationSyntax { get; }

    MarkupElementSyntax ElementSyntax { get; }
}
