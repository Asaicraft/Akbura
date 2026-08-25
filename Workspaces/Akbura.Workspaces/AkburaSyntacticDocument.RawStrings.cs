using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces;

public sealed partial class AkburaSyntacticDocument
{
    internal bool TryGetRawStringInfo(
        int position,
        out AkburaRawStringInfo info,
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
        if (token.Parent is LiteralExpressionSyntax)
        {
            return TryCreateLiteralRawStringInfo(
                projection,
                token,
                position,
                out info);
        }

        var interpolated = token.Parent;
        while (interpolated != null &&
               interpolated is not InterpolatedStringExpressionSyntax)
        {
            interpolated = interpolated.Parent;
        }

        return interpolated is InterpolatedStringExpressionSyntax expression &&
            TryCreateInterpolatedRawStringInfo(
                projection,
                expression,
                position,
                out info);
    }

    private static bool TryCreateLiteralRawStringInfo(
        AkburaCSharpPairProjection projection,
        SyntaxToken token,
        int position,
        out AkburaRawStringInfo info)
    {
        var projectedText = projection.Root.SyntaxTree!
            .GetText()
            .ToString();
        var tokenText = token.Text;
        var suffixLength = tokenText.EndsWith(
                "u8",
                StringComparison.Ordinal)
            ? 2
            : 0;
        if (!TryGetLiteralRawDelimiterLength(
                token,
                suffixLength,
                out var quoteCount))
        {
            quoteCount = CountBeforeEnd(
                projectedText,
                projection.ProjectedPosition,
                '"');
            if (quoteCount < 3 ||
                projection.ProjectedPosition - quoteCount != token.Span.Start)
            {
                info = default;
                return false;
            }
        }

        var openingStart = token.Span.Start;
        var quotesAtCaret = CountFromPosition(
            projectedText,
            projection.ProjectedPosition,
            '"');
        TextSpan closingProjectedSpan;
        if (quotesAtCaret >= quoteCount)
        {
            closingProjectedSpan = new TextSpan(
                projection.ProjectedPosition,
                quoteCount);
        }
        else
        {
            var trailingQuotes = CountBeforeEnd(
                tokenText,
                tokenText.Length - suffixLength,
                '"');
            var hasClosing =
                tokenText.Length - suffixLength >= quoteCount * 2 &&
                trailingQuotes >= quoteCount;
            closingProjectedSpan = hasClosing
                ? new TextSpan(
                    token.Span.End - suffixLength - quoteCount,
                    quoteCount)
                : default;
        }

        return TryMapRawStringInfo(
            projection,
            dollarCount: 0,
            quoteCount,
            new TextSpan(openingStart, quoteCount),
            closingProjectedSpan,
            position,
            out info);
    }
    private static bool TryCreateInterpolatedRawStringInfo(
        AkburaCSharpPairProjection projection,
        InterpolatedStringExpressionSyntax interpolated,
        int position,
        out AkburaRawStringInfo info)
    {
        var startToken = interpolated.StringStartToken;
        var startText = startToken.Text;
        var dollarCount = CountFromStart(startText, '$');
        var openingStart = startToken.Span.Start + dollarCount;
        var projectedText = projection.Root.SyntaxTree!
            .GetText()
            .ToString();
        var quoteCount = projection.ProjectedPosition - openingStart;
        if (quoteCount < 3 ||
            projection.ProjectedPosition > startToken.Span.End ||
            CountFromPosition(
                projectedText,
                openingStart,
                '"') < quoteCount)
        {
            quoteCount = CountFromPosition(
                startText,
                dollarCount,
                '"');
        }

        if (dollarCount == 0 || quoteCount < 3)
        {
            info = default;
            return false;
        }

        var quotesAtCaret = CountFromPosition(
            projectedText,
            projection.ProjectedPosition,
            '"');
        TextSpan closingProjectedSpan;
        if (quotesAtCaret >= quoteCount)
        {
            closingProjectedSpan = new TextSpan(
                projection.ProjectedPosition,
                quoteCount);
        }
        else
        {
            var endToken = interpolated.StringEndToken;
            var endText = endToken.Text;
            var trailingQuotes = CountBeforeEnd(
                endText,
                endText.Length,
                '"');
            closingProjectedSpan =
                !endToken.IsMissing && trailingQuotes >= quoteCount
                    ? new TextSpan(
                        endToken.Span.End - quoteCount,
                        quoteCount)
                    : default;
        }

        return TryMapRawStringInfo(
            projection,
            dollarCount,
            quoteCount,
            new TextSpan(openingStart, quoteCount),
            closingProjectedSpan,
            position,
            out info);
    }
    private static bool TryMapRawStringInfo(
        AkburaCSharpPairProjection projection,
        int dollarCount,
        int quoteCount,
        TextSpan openingProjectedSpan,
        TextSpan closingProjectedSpan,
        int position,
        out AkburaRawStringInfo info)
    {
        if (!projection.ProjectedSpan.Contains(openingProjectedSpan))
        {
            info = default;
            return false;
        }

        var openingSpan = new TextSpan(
            projection.MapProjectedPositionToHost(
                openingProjectedSpan.Start),
            quoteCount);
        var closingSpan = default(TextSpan);
        if (closingProjectedSpan.Length != 0)
        {
            if (!projection.ProjectedSpan.Contains(closingProjectedSpan))
            {
                info = default;
                return false;
            }

            closingSpan = new TextSpan(
                projection.MapProjectedPositionToHost(
                    closingProjectedSpan.Start),
                quoteCount);
        }

        info = new AkburaRawStringInfo(
            dollarCount,
            quoteCount,
            openingSpan,
            closingSpan,
            position);
        return true;
    }

    private static bool TryGetLiteralRawDelimiterLength(
        SyntaxToken token,
        int suffixLength,
        out int quoteCount)
    {
        quoteCount = default;
        if (token.Kind() is not (
                SyntaxKind.SingleLineRawStringLiteralToken or
                SyntaxKind.MultiLineRawStringLiteralToken or
                SyntaxKind.Utf8SingleLineRawStringLiteralToken or
                SyntaxKind.Utf8MultiLineRawStringLiteralToken))
        {
            return false;
        }

        var tokenText = token.Text;
        var openingQuoteRun = CountFromStart(tokenText, '"');
        if (openingQuoteRun < 3)
        {
            return false;
        }

        if (token.Kind() is
            SyntaxKind.MultiLineRawStringLiteralToken or
            SyntaxKind.Utf8MultiLineRawStringLiteralToken)
        {
            quoteCount = openingQuoteRun;
            return true;
        }

        var literalLength = tokenText.Length - suffixLength;
        var trailingQuoteRun = CountBeforeEnd(
            tokenText,
            literalLength,
            '"');
        for (var candidate = 3;
             candidate <= openingQuoteRun &&
             candidate <= trailingQuoteRun &&
             candidate * 2 <= literalLength;
             candidate++)
        {
            var contentLength = literalLength - candidate * 2;
            if (!string.Equals(
                    tokenText.Substring(candidate, contentLength),
                    token.ValueText,
                    StringComparison.Ordinal))
            {
                continue;
            }

            quoteCount = candidate;
            return true;
        }

        return false;
    }
    private static int CountFromStart(
        string text,
        char character)
    {
        var count = 0;
        while (count < text.Length && text[count] == character)
        {
            count++;
        }

        return count;
    }

    private static int CountFromPosition(
        string text,
        int start,
        char character)
    {
        var count = 0;
        while (start + count < text.Length &&
               text[start + count] == character)
        {
            count++;
        }

        return count;
    }
    private static int CountBeforeEnd(
        string text,
        int end,
        char character)
    {
        var count = 0;
        while (end > count && text[end - count - 1] == character)
        {
            count++;
        }

        return count;
    }
}
