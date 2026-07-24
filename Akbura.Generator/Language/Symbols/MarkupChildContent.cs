using Akbura.Language.Syntax;
using System;

namespace Akbura.Language.Symbols;

internal readonly struct MarkupChildContent
{
    public MarkupChildContent(
        MarkupContentSyntax syntax,
        MarkupChildKind kind,
        CSharpSymbolDefinition type,
        IMarkupComponentSymbol? componentSymbol = null,
        string text = "",
        string? rawText = null,
        MarkupWhitespaceMode whitespaceMode =
            MarkupWhitespaceMode.Default)
    {
        Syntax = syntax ??
            throw new ArgumentNullException(nameof(syntax));

        Kind = kind;
        Type = type;
        ComponentSymbol = componentSymbol;

        Text = text ?? string.Empty;
        RawText = rawText ?? Text;
        WhitespaceMode = whitespaceMode;
    }

    public MarkupContentSyntax Syntax { get; }

    public MarkupChildKind Kind { get; }

    public CSharpSymbolDefinition Type { get; }

    public IMarkupComponentSymbol? ComponentSymbol { get; }

    public string Text { get; }

    public string RawText { get; }

    public MarkupWhitespaceMode WhitespaceMode { get; }
}