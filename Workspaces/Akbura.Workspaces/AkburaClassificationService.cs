using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

internal sealed class AkburaClassificationService : IAkburaClassificationService
{

    private readonly EmbeddedCSharpClassificationService _embeddedCSharp = new();

    private readonly EmbeddedCSharpSemanticClassificationService _semanticCSharp = new();

    public ImmutableArray<AkburaClassifiedSpan> GetClassifications(
        AkburaDocumentContext context,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var document = context.Document;

        var span = ClampSpan(requestedSpan, document.Text.Length);

        if (span.Length == 0)
        {
            return [];
        }

        var syntacticBuilder = ImmutableArray.CreateBuilder<AkburaClassifiedSpan>();

        var semanticBuilder = ImmutableArray.CreateBuilder<AkburaClassifiedSpan>();

        var root = document.SyntaxTree.GetRoot();

        foreach (var token in root.DescendantTokens(span))
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddTrivia(
                token.LeadingTrivia,
                span,
                syntacticBuilder,
                cancellationToken);

            AddToken(
                token,
                span,
                syntacticBuilder,
                cancellationToken);

            AddTrivia(
                token.TrailingTrivia,
                span,
                syntacticBuilder,
                cancellationToken);
        }

        var semanticModel =
            context.Project.Compilation
                .GetSemanticModel(
                    document.SyntaxTree);

        _semanticCSharp.AddClassifications(
            semanticModel,
            root,
            span,
            semanticBuilder,
            cancellationToken);

        var semanticSpans = semanticBuilder.ToImmutable();

        var semanticSpanSet =
            new HashSet<TextSpan>(
                semanticSpans.Select(
                    static item => item.Span));

        var items =
            new List<AkburaClassifiedSpan>(
                syntacticBuilder.Count +
                semanticSpans.Length);

        items.AddRange(semanticSpans);

        foreach (var syntactic in syntacticBuilder)
        {
            if (!semanticSpanSet.Contains(
                    syntactic.Span))
            {
                items.Add(syntactic);
            }
        }

        items.Sort(
            static (left, right) =>
            {
                var start =
                    left.Span.Start.CompareTo(
                        right.Span.Start);

                return start != 0
                    ? start
                    : left.Span.Length.CompareTo(
                        right.Span.Length);
            });

        return [.. items];
    }

    private void AddToken(
        SyntaxToken token,
        TextSpan requestedSpan,
        ImmutableArray<AkburaClassifiedSpan>.Builder builder,
        CancellationToken cancellationToken)
    {
        if (token.Kind == SyntaxKind.CSharpRawToken &&
            _embeddedCSharp.TryAddClassifications(
                token,
                requestedSpan,
                builder,
                cancellationToken))
        {
            return;
        }

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

    private void AddTrivia(
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
                    builder,
                    cancellationToken);
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
