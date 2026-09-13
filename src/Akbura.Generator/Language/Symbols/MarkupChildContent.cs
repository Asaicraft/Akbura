using Akbura.Language.Syntax;
using Akbura.Language.Operations;
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
            MarkupWhitespaceMode.Default,
        bool isDeferred = false,
        Microsoft.CodeAnalysis.IMethodSymbol? insertionMethod = null,
        IMarkupIfOperation? conditionalOperation = null)
    {
        Syntax = syntax ??
            throw new ArgumentNullException(nameof(syntax));

        Kind = kind;
        Type = type;
        ComponentSymbol = componentSymbol;

        Text = text ?? string.Empty;
        RawText = rawText ?? Text;
        WhitespaceMode = whitespaceMode;
        IsDeferred = isDeferred;
        InsertionMethod = insertionMethod;
        ConditionalOperation = conditionalOperation;
    }

    public MarkupContentSyntax Syntax { get; }

    public MarkupChildKind Kind { get; }

    public CSharpSymbolDefinition Type { get; }

    public IMarkupComponentSymbol? ComponentSymbol { get; }

    public string Text { get; }

    public string RawText { get; }

    public MarkupWhitespaceMode WhitespaceMode { get; }

    /// <summary>
    /// Indicates that this markup node must be created
    /// by a deferred template factory, not eagerly.
    /// </summary>
    public bool IsDeferred { get; }

    /// <summary>The statically selected content Add overload, when applicable.</summary>
    public Microsoft.CodeAnalysis.IMethodSymbol? InsertionMethod { get; }

    public IMarkupIfOperation? ConditionalOperation { get; }

    public MarkupChildContent WithInsertionMethod(Microsoft.CodeAnalysis.IMethodSymbol method) =>
        new(Syntax, Kind, Type, ComponentSymbol, Text, RawText, WhitespaceMode, IsDeferred, method, ConditionalOperation);
}
