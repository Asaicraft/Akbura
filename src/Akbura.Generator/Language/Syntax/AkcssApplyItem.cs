using Microsoft.CodeAnalysis.Text;

namespace Akbura.Language.Syntax;

internal readonly struct AkcssApplyItem
{
    public AkcssApplyItem(
        TextSpan span,
        string text)
    {
        Span = span;
        Text = text;
    }

    public TextSpan Span { get; }

    public string Text { get; }
}
