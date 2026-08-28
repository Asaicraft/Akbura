using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Outlining;

/// <summary>
/// Describes one syntactic region that can be collapsed by an editor.
/// </summary>
public readonly struct AkburaOutliningRegion
{
    public AkburaOutliningRegion(
        TextSpan span,
        string collapsedText)
    {
        if (span.Length <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(span));
        }

        Span = span;
        CollapsedText = collapsedText ??
            throw new ArgumentNullException(
                nameof(collapsedText));
    }

    public TextSpan Span { get; }

    public string CollapsedText { get; }
}
