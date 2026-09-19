using System.Threading;

namespace Akbura.Workspaces.Documents;

public sealed partial class AkburaSyntacticDocument
{
    /// <summary>
    /// Adapts native brace-completion coordinates to the shared after-typing
    /// convention. This only recognizes a brace which is already in the text.
    /// String/comment braces are rejected by ShouldAutoCloseCurlyBrace.
    /// </summary>
    internal bool TryGetStructuralCurlyBraceAtCompletionPoint(
        int position,
        out int openingPosition,
        CancellationToken cancellationToken = default)
    {
        ValidatePosition(position);
        cancellationToken.ThrowIfCancellationRequested();

        if (position < Text.Length && Text[position] == '{' &&
            ShouldAutoCloseCurlyBrace(position + 1, cancellationToken))
        {
            openingPosition = position;
            return true;
        }

        if (position > 0 && Text[position - 1] == '{' &&
            ShouldAutoCloseCurlyBrace(position, cancellationToken))
        {
            openingPosition = position - 1;
            return true;
        }

        openingPosition = -1;
        return false;
    }

    internal bool IsStructuralCurlyBraceAtCompletionPoint(
        int position,
        CancellationToken cancellationToken = default) =>
        TryGetStructuralCurlyBraceAtCompletionPoint(position, out _, cancellationToken);
}
