using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces;

public sealed partial class AkburaSyntacticDocument
{
    /// <summary>
    /// Determines whether <paramref name="position"/> is inside an embedded
    /// C# fragment that can be projected for Roslyn completion.
    /// </summary>
    public bool TryGetCSharpCompletionContext(
        int position,
        out AkburaCSharpCompletionContext context,
        CancellationToken cancellationToken = default)
    {
        ValidatePosition(position);
        if (SyntaxTree.Kind == SyntaxTreeKind.Akcss ||
            Text.Length == 0)
        {
            context = default;
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var root = SyntaxTree.GetRootSyntax();
        InlineExpressionSyntax? best = null;
        TextSpan bestHostSpan = default;

        if (position < Text.Length)
        {
            FindExpressionAtPosition(position);
        }

        if (position > 0)
        {
            FindExpressionAtPosition(position - 1);
        }

        if (best == null)
        {
            context = default;
            return false;
        }

        context = new AkburaCSharpCompletionContext(
            AkburaCSharpCompletionContextKind.Expression,
            best.FullSpan,
            bestHostSpan,
            position);
        return true;

        void FindExpressionAtPosition(int tokenPosition)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = root.FindTokenInternal(tokenPosition);
            for (var current = token.Parent;
                 current != null;
                 current = current.Parent)
            {
                if (current is not InlineExpressionSyntax candidate)
                {
                    continue;
                }

                var hostSpan = candidate.Expression.Tokens.FullSpan;
                if (position < hostSpan.Start ||
                    position > hostSpan.End)
                {
                    continue;
                }

                if (best == null ||
                    candidate.FullSpan.Length < best.FullSpan.Length)
                {
                    best = candidate;
                    bestHostSpan = hostSpan;
                }
            }
        }
    }
}
