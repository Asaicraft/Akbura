using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Documents;

public sealed partial class AkburaSyntacticDocument
{
    internal bool TryGetInterpolationInfo(
        int position,
        out AkburaInterpolationInfo info,
        CancellationToken cancellationToken = default)
    {
        ValidatePosition(position);
        info = default;
        if (!TryGetEmbeddedCSharpContext(
                position,
                out var context,
                cancellationToken) ||
            !AkburaCSharpPairProjection.TryCreateCurrent(
                this,
                context,
                out var projection))
        {
            return false;
        }

        var token = projection.FindTokenAtOrBeforePosition();
        Microsoft.CodeAnalysis.SyntaxNode? current = token.Parent;
        InterpolationSyntax? interpolation = null;
        InterpolatedStringExpressionSyntax? interpolatedString = null;
        while (current != null)
        {
            interpolation ??= current as InterpolationSyntax;
            if (current is InterpolatedStringExpressionSyntax expression)
            {
                interpolatedString = expression;
                break;
            }

            current = current.Parent;
        }

        if (interpolation == null || interpolatedString == null)
        {
            return false;
        }

        var startText = interpolatedString.StringStartToken.Text;
        var dollarCount = CountFromStart(startText, '$');
        if (dollarCount == 0)
        {
            return false;
        }

        var quoteCount = CountFromStart(
            startText.Substring(dollarCount),
            '"');
        var isRaw = quoteCount >= 3;
        var requiredBraceCount = isRaw ? dollarCount : 1;
        var openingToken = interpolation.OpenBraceToken;
        if (openingToken.IsMissing ||
            openingToken.Span.Length != requiredBraceCount ||
            projection.ProjectedPosition != openingToken.Span.End ||
            !projection.ProjectedSpan.Contains(openingToken.Span))
        {
            return false;
        }

        var openingSpan = new TextSpan(
            projection.MapProjectedPositionToHost(
                openingToken.Span.Start),
            requiredBraceCount);
        var closingToken = interpolation.CloseBraceToken;
        var closingSpan = default(TextSpan);
        if (!closingToken.IsMissing &&
            closingToken.Span.Length == requiredBraceCount &&
            projection.ProjectedSpan.Contains(closingToken.Span))
        {
            closingSpan = new TextSpan(
                projection.MapProjectedPositionToHost(
                    closingToken.Span.Start),
                requiredBraceCount);
        }

        info = new AkburaInterpolationInfo(
            dollarCount,
            isRaw,
            openingSpan,
            closingSpan,
            position);
        return true;
    }
}
