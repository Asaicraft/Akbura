using Akbura.Language.Syntax;
using System;

namespace Akbura.Language.Symbols;

internal readonly struct MarkupChildContent
{
    public MarkupChildContent(
        MarkupContentSyntax syntax,
        MarkupChildKind kind,
        CSharpSymbolDefinition type,
        MarkupComponentSymbol? componentSymbol = null,
        string text = "")
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Kind = kind;
        Type = type;
        ComponentSymbol = componentSymbol;
        Text = text ?? string.Empty;
    }

    public MarkupContentSyntax Syntax { get; }

    public MarkupChildKind Kind { get; }

    public CSharpSymbolDefinition Type { get; }

    public MarkupComponentSymbol? ComponentSymbol { get; }

    public string Text { get; }
}
