using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

internal sealed class AkburaClassificationService :
    IAkburaClassificationService
{
    public ImmutableArray<AkburaClassifiedSpan> GetClassifications(
        AkburaDocumentSnapshot document,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var span = ClampSpan(
            requestedSpan,
            document.Text.Length);

        if (span.Length == 0)
        {
            return ImmutableArray<AkburaClassifiedSpan>.Empty;
        }

        var builder =
            ImmutableArray.CreateBuilder<AkburaClassifiedSpan>();

        var root = document.SyntaxTree.GetRoot();

        foreach (var token in root.DescendantTokens(span))
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddTrivia(
                token.LeadingTrivia,
                span,
                builder,
                cancellationToken);

            AddToken(
                token,
                span,
                builder);

            AddTrivia(
                token.TrailingTrivia,
                span,
                builder,
                cancellationToken);
        }

        var items = builder.ToArray();

        Array.Sort(
            items,
            static (left, right) =>
            {
                var start = left.Span.Start.CompareTo(
                    right.Span.Start);

                return start != 0
                    ? start
                    : left.Span.Length.CompareTo(
                        right.Span.Length);
            });

        return ImmutableArray.Create(items);
    }

    private static void AddToken(
        SyntaxToken token,
        TextSpan requestedSpan,
        ImmutableArray<AkburaClassifiedSpan>.Builder builder)
    {
        var classification =
            AkburaSyntaxClassificationFacts.GetClassification(token);

        if (classification is null ||
            token.Span.Length == 0 ||
            !token.Span.OverlapsWith(requestedSpan))
        {
            return;
        }

        builder.Add(new AkburaClassifiedSpan(
            token.Span,
            classification.Value));
    }

    private static void AddTrivia(
        SyntaxTriviaList triviaList,
        TextSpan requestedSpan,
        ImmutableArray<AkburaClassifiedSpan>.Builder builder,
        CancellationToken cancellationToken)
    {
        foreach (var trivia in triviaList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var classification =
                AkburaSyntaxClassificationFacts.GetClassification(
                    trivia);

            if (classification is not null &&
                trivia.FullSpan.Length > 0 &&
                trivia.FullSpan.OverlapsWith(requestedSpan))
            {
                builder.Add(new AkburaClassifiedSpan(
                    trivia.FullSpan,
                    classification.Value));
            }

            if (trivia.Kind != SyntaxKind.SkippedTokensTrivia)
            {
                continue;
            }

            foreach (var skippedToken in trivia.SkippedTokens)
            {
                AddToken(
                    skippedToken,
                    requestedSpan,
                    builder);
            }
        }
    }

    private static TextSpan ClampSpan(
        TextSpan span,
        int textLength)
    {
        var start = Math.Max(
            0,
            Math.Min(span.Start, textLength));

        var end = Math.Max(
            start,
            Math.Min(span.End, textLength));

        return TextSpan.FromBounds(start, end);
    }
}
