using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
#if DEBUG
using System.Diagnostics;
#endif

namespace Akbura.Workspaces;

internal sealed class AkburaDiagnosticService :
    IAkburaDiagnosticService
{
    public ImmutableArray<AkburaDiagnosticSpan>
        GetSyntacticDiagnostics(
            AkburaSyntacticDocument document,
            TextSpan requestedSpan,
            CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return GetSyntacticDiagnostics(
            document.SyntaxTree.GetRootSyntax(),
            document.Text.Length,
            requestedSpan,
            cancellationToken);
    }

    public ImmutableArray<AkburaDiagnosticSpan> GetDiagnostics(
        AkburaDocumentContext context,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var document = context.Document;

#if DEBUG
        var totalTimer = Stopwatch.StartNew();
        var stageTimer = Stopwatch.StartNew();
        var activeStage = "Get syntax root";
        var outcome = "completed";
        var resultCount = 0;

        try
        {
#endif
        var root = document.SyntaxTree.GetRootSyntax();
        var requested = ClampSpan(
            requestedSpan,
            document.Text.Length);
        var result = new HashSet<AkburaDiagnosticSpan>();

#if DEBUG
        activeStage = "Syntactic diagnostics";
        stageTimer.Restart();
#endif
        AddSyntacticDiagnostics(
            root,
            document.Text.Length,
            requested,
            result,
            cancellationToken);

#if DEBUG
        resultCount = result.Count;
        WriteDiagnosticsStage(
            document.FilePath,
            requested,
            activeStage,
            stageTimer.Elapsed,
            result.Count);

        activeStage = "GetSemanticModel";
        stageTimer.Restart();
#endif
        var semanticModel = context.Project.Compilation
            .GetSemanticModel(document.SyntaxTree);

#if DEBUG
        WriteDiagnosticsStage(
            document.FilePath,
            requested,
            activeStage,
            stageTimer.Elapsed,
            count: null);

        activeStage = "GetSemanticDiagnostics";
        stageTimer.Restart();
#endif
        var semanticDiagnostics =
            semanticModel.GetSemanticDiagnostics(root);

#if DEBUG
        WriteDiagnosticsStage(
            document.FilePath,
            requested,
            activeStage,
            stageTimer.Elapsed,
            semanticDiagnostics.Length);

        activeStage = "Map semantic diagnostics";
        stageTimer.Restart();
        var countBeforeMapping = result.Count;
#endif
        foreach (var diagnostic in semanticDiagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddDiagnostic(
                diagnostic,
                diagnostic.Span,
                document.Text.Length,
                requested,
                result);
        }

#if DEBUG
        resultCount = result.Count;
        WriteDiagnosticsStage(
            document.FilePath,
            requested,
            activeStage,
            stageTimer.Elapsed,
            result.Count - countBeforeMapping);

        activeStage = "Sort diagnostics";
        stageTimer.Restart();
#endif
        var diagnostics = ToSortedImmutableArray(result);

#if DEBUG
        resultCount = diagnostics.Length;
        WriteDiagnosticsStage(
            document.FilePath,
            requested,
            activeStage,
            stageTimer.Elapsed,
            diagnostics.Length);
#endif

        return diagnostics;
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
                AkburaWorkspaceDiagnostics.Category.Diagnostics,
                $"Semantic diagnostics total: " +
                $"file='{document.FilePath}', " +
                $"requestedSpan={requestedSpan}, " +
                $"activeStage='{activeStage}', " +
                $"outcome={outcome}, " +
                $"elapsed={totalTimer.Elapsed.TotalMilliseconds:F2} ms, " +
                $"diagnostics={resultCount}.");
        }
#endif
    }

#if DEBUG
    private static void WriteDiagnosticsStage(
        string filePath,
        TextSpan span,
        string stage,
        TimeSpan elapsed,
        int? count)
    {
        var countSuffix = count is { } value
            ? $", diagnostics={value}."
            : ".";

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Diagnostics,
            $"Semantic diagnostics stage: " +
            $"file='{filePath}', " +
            $"span={span}, " +
            $"stage='{stage}', " +
            $"elapsed={elapsed.TotalMilliseconds:F2} ms" +
            countSuffix);
    }
#endif

    private static ImmutableArray<AkburaDiagnosticSpan>
        GetSyntacticDiagnostics(
            AkburaSyntax root,
            int textLength,
            TextSpan requestedSpan,
            CancellationToken cancellationToken)
    {
        var requested = ClampSpan(
            requestedSpan,
            textLength);
        var result = new HashSet<AkburaDiagnosticSpan>();

        AddSyntacticDiagnostics(
            root,
            textLength,
            requested,
            result,
            cancellationToken);

        return ToSortedImmutableArray(result);
    }

    private static void AddSyntacticDiagnostics(
        AkburaSyntax root,
        int textLength,
        TextSpan requestedSpan,
        HashSet<AkburaDiagnosticSpan> result,
        CancellationToken cancellationToken)
    {
        foreach (var nodeOrToken in
                 root.DescendantNodesAndTokensAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddSyntaxDiagnostics(
                nodeOrToken.GetDiagnostics(),
                nodeOrToken.SpanStart,
                nodeOrToken.Span,
                textLength,
                requestedSpan,
                result);

            if (!nodeOrToken.IsToken)
            {
                continue;
            }

            var token = nodeOrToken.AsToken();
            AddTriviaDiagnostics(
                token.LeadingTrivia,
                textLength,
                requestedSpan,
                result);
            AddTriviaDiagnostics(
                token.TrailingTrivia,
                textLength,
                requestedSpan,
                result);
        }
    }

    private static void AddTriviaDiagnostics(
        SyntaxTriviaList triviaList,
        int textLength,
        TextSpan requestedSpan,
        HashSet<AkburaDiagnosticSpan> result)
    {
        foreach (var trivia in triviaList)
        {
            AddSyntaxDiagnostics(
                trivia.GetDiagnostics(),
                trivia.SpanStart,
                trivia.Span,
                textLength,
                requestedSpan,
                result);
        }
    }

    private static void AddSyntaxDiagnostics(
        IEnumerable<AkburaDiagnostic> diagnostics,
        int spanStart,
        TextSpan fallbackSpan,
        int textLength,
        TextSpan requestedSpan,
        HashSet<AkburaDiagnosticSpan> result)
    {
        foreach (var diagnostic in diagnostics)
        {
            var span = diagnostic is SyntaxDiagnosticInfo syntaxDiagnostic
                ? CreateClampedSpan(
                    (long)spanStart + syntaxDiagnostic.Position,
                    Math.Max(0, syntaxDiagnostic.Width),
                    textLength)
                : fallbackSpan;

            AddDiagnostic(
                diagnostic,
                span,
                textLength,
                requestedSpan,
                result);
        }
    }

    private static void AddDiagnostic(
        AkburaDiagnostic diagnostic,
        TextSpan span,
        int textLength,
        TextSpan requestedSpan,
        HashSet<AkburaDiagnosticSpan> result)
    {
        var clampedSpan = ClampSpan(span, textLength);
        if (!IntersectsOrTouches(
                clampedSpan,
                requestedSpan))
        {
            return;
        }

        result.Add(new AkburaDiagnosticSpan(
            clampedSpan,
            diagnostic.Code,
            GetMessage(diagnostic),
            diagnostic.Severity));
    }

    private static string GetMessage(
        AkburaDiagnostic diagnostic)
    {
        try
        {
            return diagnostic.Message;
        }
        catch (Exception)
        {
            // A malformed diagnostic must not break editor features.
            return diagnostic.Code;
        }
    }

    private static bool IntersectsOrTouches(
        TextSpan diagnostic,
        TextSpan requested)
    {
        if (diagnostic.Length != 0)
        {
            return diagnostic.OverlapsWith(requested);
        }

        return diagnostic.Start >= requested.Start &&
               diagnostic.Start <= requested.End;
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

    private static TextSpan CreateClampedSpan(
        long start,
        int length,
        int textLength)
    {
        var end = start + length;
        var clampedStart = (int)Math.Max(
            0L,
            Math.Min(start, textLength));
        var clampedEnd = (int)Math.Max(
            clampedStart,
            Math.Min(end, textLength));

        return TextSpan.FromBounds(
            clampedStart,
            clampedEnd);
    }

    private static ImmutableArray<AkburaDiagnosticSpan>
        ToSortedImmutableArray(
            HashSet<AkburaDiagnosticSpan> diagnostics)
    {
        var items = diagnostics.ToArray();

        Array.Sort(
            items,
            static (left, right) =>
            {
                var start = left.Span.Start.CompareTo(
                    right.Span.Start);
                if (start != 0)
                {
                    return start;
                }

                var length = left.Span.Length.CompareTo(
                    right.Span.Length);
                if (length != 0)
                {
                    return length;
                }

                var severity = right.Severity.CompareTo(
                    left.Severity);
                if (severity != 0)
                {
                    return severity;
                }

                return string.CompareOrdinal(
                    left.Code,
                    right.Code);
            });

        return ImmutableArray.Create(items);
    }
}
