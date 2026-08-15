using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

internal sealed class AkburaClassificationService : IAkburaClassificationService
{

    private readonly EmbeddedCSharpClassificationService _embeddedCSharp = new();

    private readonly EmbeddedCSharpSemanticClassificationService _semanticCSharp = new();

    private readonly AkcssSemanticClassificationService _semanticAkcss;

    public AkburaClassificationService(AkcssReferenceResolver referenceResolver)
    {
        _semanticAkcss = new AkcssSemanticClassificationService(
            referenceResolver ??
            throw new ArgumentNullException(nameof(referenceResolver)));
    }

    public ImmutableArray<AkburaClassifiedSpan> GetSyntacticClassifications(
        SourceText text,
        string filePath,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var syntaxTree =
            AkburaDocumentSnapshot.CreateSyntaxTree(
                text,
                filePath ?? string.Empty,
                rootNamespace: string.Empty,
                projectDirectory: string.Empty,
                cancellationToken);

        return GetSyntacticClassifications(
            syntaxTree.GetRootSyntax(),
            text.Length,
            requestedSpan,
            cancellationToken);
    }

    public ImmutableArray<AkburaClassifiedSpan> GetSyntacticClassifications(
        AkburaSyntacticDocument document,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(
                nameof(document));
        }

        return GetSyntacticClassifications(
            document.SyntaxTree.GetRootSyntax(),
            document.Text.Length,
            requestedSpan,
            cancellationToken);
    }

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

        var root = document.SyntaxTree.GetRootSyntax();

        var syntacticSpans =
            GetSyntacticClassifications(
                root,
                document.Text.Length,
                span,
                cancellationToken);

        using var semanticBuilder =
            ImmutableArrayBuilder<AkburaClassifiedSpan>.Rent();

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

        _semanticAkcss.AddClassifications(
            context,
            semanticModel,
            root,
            span,
            semanticBuilder,
            cancellationToken);

        return MergeClassifications(
            syntacticSpans,
            semanticBuilder.ToImmutable());
    }

    private ImmutableArray<AkburaClassifiedSpan>
        GetSyntacticClassifications(
            AkburaSyntax root,
            int textLength,
            TextSpan requestedSpan,
            CancellationToken cancellationToken)
    {
        var span = ClampSpan(
            requestedSpan,
            textLength);

        if (span.Length == 0)
        {
            return [];
        }

        using var builder =
            ImmutableArrayBuilder<AkburaClassifiedSpan>.Rent();

        AddEmbeddedCSharpNodes(
            root,
            span,
            builder,
            cancellationToken);

        foreach (var token in root.DescendantTokens(span))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsClassifiedAsEmbeddedCSharpNode(token))
            {
                continue;
            }

            AddTrivia(
                token.LeadingTrivia,
                span,
                builder,
                cancellationToken);

            AddToken(
                token,
                span,
                builder,
                cancellationToken);

            AddTrivia(
                token.TrailingTrivia,
                span,
                builder,
                cancellationToken);
        }

        var items = builder.ToArray();

        Array.Sort(items, CompareClassifications);

        return ImmutableArray.Create(items);
    }

    private static ImmutableArray<AkburaClassifiedSpan>
        MergeClassifications(
            ImmutableArray<AkburaClassifiedSpan> syntacticSpans,
            ImmutableArray<AkburaClassifiedSpan> semanticSpans)
    {

        var orderedSemantic = semanticSpans.ToArray();
        Array.Sort(orderedSemantic, CompareClassifications);
        var prefixMaximumEnd = new int[orderedSemantic.Length];
        var maximumEnd = 0;
        for (var index = 0; index < orderedSemantic.Length; index++)
        {
            maximumEnd = Math.Max(
                maximumEnd,
                orderedSemantic[index].Span.End);
            prefixMaximumEnd[index] = maximumEnd;
        }

        var items =
            new List<AkburaClassifiedSpan>(
                syntacticSpans.Length +
                semanticSpans.Length);

        items.AddRange(orderedSemantic);

        foreach (var syntactic in syntacticSpans)
        {
            if (!IsCoveredBySemanticSpan(
                    syntactic.Span,
                    orderedSemantic,
                    prefixMaximumEnd))
            {
                items.Add(syntactic);
            }
        }

        items.Sort(CompareClassifications);

        return [.. items];
    }

    private static bool IsCoveredBySemanticSpan(
        TextSpan syntacticSpan,
        AkburaClassifiedSpan[] semanticSpans,
        int[] prefixMaximumEnd)
    {
        var low = 0;
        var high = semanticSpans.Length - 1;
        var candidate = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (semanticSpans[middle].Span.Start <= syntacticSpan.Start)
            {
                candidate = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return candidate >= 0 &&
            prefixMaximumEnd[candidate] >= syntacticSpan.End;
    }

    private static int CompareClassifications(
        AkburaClassifiedSpan left,
        AkburaClassifiedSpan right)
    {
        var start =
            left.Span.Start.CompareTo(
                right.Span.Start);

        return start != 0
            ? start
            : left.Span.Length.CompareTo(
                right.Span.Length);
    }

    private static bool IsClassifiedAsEmbeddedCSharpNode(SyntaxToken token)
    {
        for (var node = token.Parent;
             node != null;
             node = node.Parent)
        {
            switch (node)
            {
                case CSharpStatementSyntax:
                case CSharpExpressionSyntax:
                    return true;

                case CSharpTypeSyntax type:
                    return IsEmbeddedCSharpType(
                        type);
            }
        }

        return false;
    }

    private void AddEmbeddedCSharpNodes(
        AkburaSyntax root,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder,
        CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!node.FullSpan.OverlapsWith(
                    requestedSpan))
            {
                continue;
            }

            switch (node)
            {
                case CSharpStatementSyntax statement:
                    _embeddedCSharp.AddClassifications(
                        statement,
                        requestedSpan,
                        builder,
                        cancellationToken);
                    break;

                case CSharpTypeSyntax type
                     when IsEmbeddedCSharpType(type):
                    _embeddedCSharp.AddClassifications(
                        type,
                        requestedSpan,
                        builder,
                        cancellationToken);
                    break;

                case CSharpExpressionSyntax expression:
                    _embeddedCSharp.AddClassifications(
                        expression,
                        requestedSpan,
                        builder,
                        cancellationToken);
                    break;
            }
        }
    }

    private void AddToken(
        SyntaxToken token,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder,
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
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder,
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

    private static bool IsEmbeddedCSharpType(CSharpTypeSyntax type)
    {
        return type.Parent is not
            AkcssAssignmentSyntax and not
            AkcssUsingDirectiveSyntax;
    }
}
