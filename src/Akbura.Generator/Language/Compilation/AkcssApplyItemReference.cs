using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Language;

internal readonly struct AkcssApplyItemReference
{
    public AkcssApplyItemReference(
        TextSpan sourceSpan,
        string text,
        IAkcssSymbol? symbol)
    {
        SourceSpan = sourceSpan;
        Text = text;
        Symbol = symbol;
    }

    public TextSpan SourceSpan { get; }

    public string Text { get; }

    public IAkcssSymbol? Symbol { get; }

    public bool IsResolved => Symbol != null;
}
