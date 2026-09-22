using Akbura.Language.Syntax;
using Akbura.Pools;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
#if DEBUG
using System.Diagnostics;
#endif

namespace Akbura.Workspaces.Classification;

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

    public ImmutableArray<AkburaClassifiedSpan> GetSyntacticClassifications(SourceText text, string filePath, TextSpan requestedSpan, CancellationToken cancellationToken = default)
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

    public ImmutableArray<AkburaClassifiedSpan> GetSyntacticClassifications(AkburaSyntacticDocument document, TextSpan requestedSpan, CancellationToken cancellationToken = default)
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

    public ImmutableArray<AkburaClassifiedSpan> GetClassifications(AkburaDocumentContext context, TextSpan requestedSpan, CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var document = context.Document;

#if DEBUG
        var totalTimer = Stopwatch.StartNew();
        var stageTimer = Stopwatch.StartNew();
        var activeStage = "Clamp span";
        var outcome = "completed";
        var resultCount = 0;

        try
        {
#endif
        var span = ClampSpan(requestedSpan, document.Text.Length);

        if (span.Length == 0)
        {
#if DEBUG
            activeStage = "Empty span";
#endif
            return [];
        }

        var root = document.SyntaxTree.GetRootSyntax();

#if DEBUG
        activeStage = "Syntactic classifications";
        stageTimer.Restart();
#endif
        var syntacticSpans =
            GetSyntacticClassifications(
                root,
                document.Text.Length,
                span,
                cancellationToken);

#if DEBUG
        resultCount = syntacticSpans.Length;
        WriteClassificationStage(
            document.FilePath,
            span,
            activeStage,
            stageTimer.Elapsed,
            syntacticSpans.Length);
#endif

        using var semanticBuilder =
            ImmutableArrayBuilder<AkburaClassifiedSpan>.Rent();

#if DEBUG
        activeStage = "GetSemanticModel";
        stageTimer.Restart();
#endif
        var semanticModel =
            context.Project.Compilation
                .GetSemanticModel(
                    document.SyntaxTree);

#if DEBUG
        WriteClassificationStage(
            document.FilePath,
            span,
            activeStage,
            stageTimer.Elapsed,
            count: null);

        activeStage = "Embedded C# classifications";
        stageTimer.Restart();
#endif
        _semanticCSharp.AddClassifications(
            semanticModel,
            root,
            span,
            semanticBuilder,
            cancellationToken);

        AddMarkupDataTypeClassifications(
            semanticModel,
            document.Text,
            root,
            span,
            semanticBuilder,
            cancellationToken);

#if DEBUG
        var csharpCount = semanticBuilder.Count;
        WriteClassificationStage(
            document.FilePath,
            span,
            activeStage,
            stageTimer.Elapsed,
            csharpCount);

        activeStage = "AKCSS classifications";
        stageTimer.Restart();
#endif
        _semanticAkcss.AddClassifications(
            context,
            semanticModel,
            root,
            span,
            semanticBuilder,
            cancellationToken);

        AddMarkupPropertyReferenceClassifications(semanticModel, root, span,
            semanticBuilder, cancellationToken);

#if DEBUG
        WriteClassificationStage(
            document.FilePath,
            span,
            activeStage,
            stageTimer.Elapsed,
            semanticBuilder.Count - csharpCount);

        activeStage = "Merge classifications";
        stageTimer.Restart();
#endif
        var result = MergeClassifications(
            syntacticSpans,
            semanticBuilder.ToImmutable());

#if DEBUG
        resultCount = result.Length;
        WriteClassificationStage(
            document.FilePath,
            span,
            activeStage,
            stageTimer.Elapsed,
            result.Length);
#endif

        return result;
#if DEBUG
        }
        catch (OperationCanceledException)
        {
            outcome = "canceled";
            throw;
        }
        catch
        {
            outcome = "failed";
            throw;
        }
        finally
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Classification,
                $"Semantic classification total: " +
                $"file='{document.FilePath}', " +
                $"requestedSpan={requestedSpan}, " +
                $"activeStage='{activeStage}', " +
                $"outcome={outcome}, " +
                $"elapsed={totalTimer.Elapsed.TotalMilliseconds:F2} ms, " +
                $"spans={resultCount}.");
        }
#endif
    }

#if DEBUG
    private static void WriteClassificationStage(
        string filePath,
        TextSpan span,
        string stage,
        TimeSpan elapsed,
        int? count)
    {
        var countSuffix = count is { } value
            ? $", spans={value}."
            : ".";

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Classification,
            $"Semantic classification stage: " +
            $"file='{filePath}', " +
            $"span={span}, " +
            $"stage='{stage}', " +
            $"elapsed={elapsed.TotalMilliseconds:F2} ms" +
            countSuffix);
    }
#endif

    private ImmutableArray<AkburaClassifiedSpan> GetSyntacticClassifications(AkburaSyntax root, int textLength, TextSpan requestedSpan, CancellationToken cancellationToken)
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

    private static ImmutableArray<AkburaClassifiedSpan> MergeClassifications(ImmutableArray<AkburaClassifiedSpan> syntacticSpans, ImmutableArray<AkburaClassifiedSpan> semanticSpans)
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
            if (syntactic.Kind == AkburaClassificationKind.String)
            {
                AddUncoveredStringParts(syntactic, orderedSemantic, prefixMaximumEnd, items);
                continue;
            }

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

    private static void AddMarkupPropertyReferenceClassifications(Akbura.Language.AkburaSemanticModel semanticModel, AkburaSyntax root, TextSpan span, ImmutableArrayBuilder<AkburaClassifiedSpan> builder, CancellationToken cancellationToken)
    {
        foreach (var literal in root.DescendantNodes().OfType<MarkupLiteralAttributeValueSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!literal.Span.IntersectsWith(span))
            {
                continue;
            }

            if (semanticModel.GetMarkupAvaloniaPropertyReference(literal) is { } reference)
            {
                if (reference.OwnerSpan.Length > 0 && reference.OwnerSpan.IntersectsWith(span))
                {
                    builder.Add(new AkburaClassifiedSpan(reference.OwnerSpan,
                        AkburaClassificationKind.ClassName));
                }

                if (reference.PropertySpan.Length > 0 && reference.PropertySpan.IntersectsWith(span))
                {
                    builder.Add(new AkburaClassifiedSpan(reference.PropertySpan,
                        AkburaClassificationKind.FieldName));
                }
            }
            else if (semanticModel.GetMarkupSelectorLiteral(literal) is { } selector)
            {
                foreach (var branch in selector.Branches)
                {
                    foreach (var node in branch)
                    {
                        if (node.Type != null && node.Span.IntersectsWith(span))
                        {
                            builder.Add(new AkburaClassifiedSpan(node.Span,
                                AkburaClassificationKind.ClassName));
                        }
                    }
                }
            }
        }
    }

    private static void AddMarkupDataTypeClassifications(Akbura.Language.AkburaSemanticModel semanticModel, SourceText text, AkburaSyntax root, TextSpan span, ImmutableArrayBuilder<AkburaClassifiedSpan> builder, CancellationToken cancellationToken)
    {
        foreach (var attribute in root.DescendantNodes().OfType<MarkupAttributeSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Akbura.Language.AkburaSemanticModel
                    .IsMarkupDataTypeDirective(attribute) ||
                Akbura.Language.AkburaSemanticModel
                    .GetMarkupAttributeValue(attribute) is not
                    MarkupLiteralAttributeValueSyntax literal ||
                !AkburaMarkupSyntaxFacts
                    .TryGetAttributeLiteralContentSpan(
                        text,
                        literal,
                        out var contentSpan) ||
                !contentSpan.OverlapsWith(span))
            {
                continue;
            }

            var typeText = text.ToString(contentSpan);
            if (!semanticModel.TryBindMarkupDataTypeDirective(
                    typeText,
                    out var typeSymbol))
            {
                continue;
            }

            var typeSyntax = Microsoft.CodeAnalysis.CSharp
                .SyntaxFactory.ParseTypeName(typeText);
            EmbeddedCSharpSemanticClassificationService
                .AddTypeClassifications(
                    typeSyntax,
                    typeSymbol,
                    contentSpan.Start,
                    span,
                    builder);
        }
    }

    private static void AddUncoveredStringParts(AkburaClassifiedSpan syntactic, AkburaClassifiedSpan[] semantic, int[] prefixMaximumEnd, List<AkburaClassifiedSpan> items)
    {
        var start = syntactic.Span.Start;
        var low = 0;
        var high = semantic.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (prefixMaximumEnd[middle] <= start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (var index = low; index < semantic.Length; index++)
        {
            var classification = semantic[index];
            if (classification.Span.Start >= syntactic.Span.End)
            {
                break;
            }

            if (!classification.Span.IntersectsWith(syntactic.Span))
            {
                continue;
            }

            if (classification.Span.Start > start)
            {
                items.Add(new AkburaClassifiedSpan(TextSpan.FromBounds(start,
                    Math.Min(classification.Span.Start, syntactic.Span.End)), syntactic.Kind));
            }

            start = Math.Max(start, classification.Span.End);
            if (start >= syntactic.Span.End)
            {
                return;
            }
        }

        if (start < syntactic.Span.End)
        {
            items.Add(new AkburaClassifiedSpan(TextSpan.FromBounds(start, syntactic.Span.End),
                syntactic.Kind));
        }
    }

    private static bool IsCoveredBySemanticSpan(TextSpan syntacticSpan, AkburaClassifiedSpan[] semanticSpans, int[] prefixMaximumEnd)
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

    private static int CompareClassifications(AkburaClassifiedSpan left, AkburaClassifiedSpan right)
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
        for (var node = token.Parent; node != null; node = node.Parent)
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

    private void AddEmbeddedCSharpNodes(AkburaSyntax root, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder, CancellationToken cancellationToken)
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

    private void AddToken(SyntaxToken token, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder, CancellationToken cancellationToken)
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

        var tokenSpan = token.Parent is MarkupTextLiteralSyntax
            { Parent: MarkupLiteralAttributeValueSyntax literal }
            ? AkburaMarkupSyntaxFacts.GetAttributeLiteralSpan(literal)
            : token.Span;

        if (classification is null ||
            tokenSpan.Length == 0 ||
            !tokenSpan.OverlapsWith(requestedSpan))
        {
            return;
        }

        builder.Add(new AkburaClassifiedSpan(
            tokenSpan,
            classification.Value));
    }

    private void AddTrivia(SyntaxTriviaList triviaList, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder, CancellationToken cancellationToken)
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

    private static TextSpan ClampSpan(TextSpan span, int textLength)
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
