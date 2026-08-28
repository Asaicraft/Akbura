using Akbura.Language.Syntax;

namespace Akbura.Language.Symbols;

internal interface IMarkupItemSymbol : ISymbol
{
    CSharpSymbolDefinition Type { get; }

    MarkupAttachedPropertyAttributeSyntax DeclarationSyntax { get; }
}
