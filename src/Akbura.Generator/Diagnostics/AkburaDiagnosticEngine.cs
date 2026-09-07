using Akbura.Language;
using Akbura.Language.Syntax;
#if STATS
using Akbura.Language.CodeGeneration;
#endif
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;

namespace Akbura.Diagnostics;

internal static class AkburaDiagnosticEngine
{
    public static ImmutableArray<AkburaDiagnosticRecord> Collect(
        AkburaSyntaxTree tree,
        AkburaSemanticModel? semanticModel = null,
        bool includeSemantic = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = tree.GetRootSyntax();
        List<AkburaDiagnosticRecord>? result = null;
        if (root.ContainsDiagnostics)
        {
            AddSyntaxDiagnostics(tree, ref result, cancellationToken);
        }

        var isGlobalUsings = GlobalUsings.IsComponentFile(tree) || GlobalUsings.IsAkcssFile(tree);
        if (isGlobalUsings)
        {
            AddGlobalUsingsDiagnostics(tree, ref result, cancellationToken);
        }
        else if (includeSemantic && semanticModel != null)
        {
            if (!ReferenceEquals(tree, semanticModel.SyntaxTree))
            {
                throw new ArgumentException("The semantic model must belong to the source tree.", nameof(semanticModel));
            }

            ImmutableArray<AkburaSemanticDiagnostic> diagnostics;
#if STATS
            using (GenerationStatistics.Measure(GenerationStatisticStage.DiagnosticSemantic))
#endif
            {
                diagnostics = semanticModel.GetSemanticDiagnostics(root);
            }

            foreach (var diagnostic in diagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Add(ref result, CreateSemanticRecord(tree, semanticModel, diagnostic));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (result == null)
        {
            return [];
        }

        var unique = AkburaDiagnosticCollection.Deduplicate(result).ToArray();
        Array.Sort(unique, AkburaDiagnosticCanonicalComparer.Instance);
        cancellationToken.ThrowIfCancellationRequested();
        return unique.ToImmutableArrayUnsafe();
    }

    internal static AkburaDiagnosticRecord CreateSemanticRecord(
        AkburaSyntaxTree tree,
        AkburaSemanticModel semanticModel,
        AkburaSemanticDiagnostic diagnostic)
    {
        var owner = GetSourceOwner(tree, semanticModel, diagnostic.Syntax);
        var record = CreateRecord(owner.Tree, diagnostic, diagnostic.Span, AkburaDiagnosticKind.Semantic);
        if (owner.ModuleIdentity != null)
        {
            return record with { Properties = record.Properties.Add("akbura.module-identity", owner.ModuleIdentity) };
        }

        return owner.ModuleReference != null
            ? record with { Properties = record.Properties.Add("akbura.module-reference", owner.ModuleReference) }
            : record;
    }

    private static DiagnosticSourceOwner GetSourceOwner(
        AkburaSyntaxTree tree,
        AkburaSemanticModel semanticModel,
        AkburaSyntax syntax)
    {
        var root = syntax.Root;
        if (ReferenceEquals(tree.GetRootSyntax(), root) &&
            (semanticModel.Compilation.SyntaxTrees.Contains(tree) ||
             tree is AkcssSyntaxTree akcss && semanticModel.Compilation.AkcssSyntaxTrees.Contains(akcss)))
        {
            return new(tree);
        }

        return FindSourceTree(semanticModel.Compilation, syntax, []) ??
            throw new InvalidOperationException("The diagnostic source tree is not part of the current compilation or its references.");
    }

    private static DiagnosticSourceOwner? FindSourceTree(
        AkburaCompilation compilation,
        AkburaSyntax syntax,
        HashSet<AkburaCompilation> visited)
    {
        if (!visited.Add(compilation))
        {
            return null;
        }

        var root = syntax.Root;
        foreach (var candidate in compilation.SyntaxTrees)
        {
            if (ReferenceEquals(candidate.GetRootSyntax(), root))
            {
                return new(candidate);
            }
        }

        foreach (var candidate in compilation.AkcssSyntaxTrees)
        {
            if (ReferenceEquals(candidate.GetRootSyntax(), root))
            {
                return new(candidate);
            }
        }

        foreach (var reference in compilation.CompilationReferences)
        {
            if (FindSourceTree(reference.Compilation, syntax, visited) is { } referencedTree)
            {
                return referencedTree;
            }
        }

        foreach (var module in compilation.ReferencedModules)
        {
            if (module.TryGetSource(syntax, out var source))
            {
                // TryGetSource matches only an already materialized root, so this
                // does not parse unrelated embedded documents while mapping a span.
                var assembly = compilation.CSharpCompilation.GetAssemblyOrModuleSymbol(module.Reference)
                    as Microsoft.CodeAnalysis.IAssemblySymbol;
                var identity = assembly?.Identity.ToString();
                var referencePath = module.Reference.FilePath;
                if (identity == null && string.IsNullOrEmpty(referencePath))
                {
                    throw new InvalidOperationException("The embedded diagnostic source has no module identity.");
                }

                return new(source.GetSyntaxTree(), identity, referencePath);
            }
        }

        return null;
    }

    private readonly record struct DiagnosticSourceOwner(
        AkburaSyntaxTree Tree,
        string? ModuleIdentity = null,
        string? ModuleReference = null);

    private static void AddSyntaxDiagnostics(
        AkburaSyntaxTree tree,
        ref List<AkburaDiagnosticRecord>? result,
        CancellationToken cancellationToken)
    {
        foreach (var nodeOrToken in tree.GetRootSyntax().DescendantNodesAndTokensAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddSyntaxDiagnostics(tree, nodeOrToken.GetDiagnostics(), nodeOrToken.SpanStart,
                nodeOrToken.Span, ref result, cancellationToken);
            if (!nodeOrToken.IsToken)
            {
                continue;
            }

            var token = nodeOrToken.AsToken();
            AddTriviaDiagnostics(tree, token.LeadingTrivia, ref result, cancellationToken);
            AddTriviaDiagnostics(tree, token.TrailingTrivia, ref result, cancellationToken);
        }
    }

    private static void AddTriviaDiagnostics(
        AkburaSyntaxTree tree,
        SyntaxTriviaList triviaList,
        ref List<AkburaDiagnosticRecord>? result,
        CancellationToken cancellationToken)
    {
        foreach (var trivia in triviaList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddSyntaxDiagnostics(tree, trivia.GetDiagnostics(), trivia.SpanStart,
                trivia.Span, ref result, cancellationToken);
        }
    }

    private static void AddSyntaxDiagnostics(
        AkburaSyntaxTree tree,
        IEnumerable<AkburaDiagnostic> diagnostics,
        int spanStart,
        TextSpan fallbackSpan,
        ref List<AkburaDiagnosticRecord>? result,
        CancellationToken cancellationToken)
    {
        foreach (var diagnostic in diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var span = diagnostic is SyntaxDiagnosticInfo syntaxDiagnostic
                ? ClampSpan((long)spanStart + syntaxDiagnostic.Position, Math.Max(0, syntaxDiagnostic.Width), tree.Text.Length)
                : fallbackSpan;
            Add(ref result, CreateRecord(tree, diagnostic, span, AkburaDiagnosticKind.Syntax));
        }
    }

    private static void AddGlobalUsingsDiagnostics(
        AkburaSyntaxTree tree,
        ref List<AkburaDiagnosticRecord>? result,
        CancellationToken cancellationToken)
    {
        if (tree is ComponentSyntaxTree component)
        {
            foreach (var member in component.GetRoot().Members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (member is not UsingDirectiveSyntax)
                {
                    AddInvalidGlobalUsing(tree, member, ref result);
                }
            }
        }
        else if (tree is AkcssSyntaxTree akcss)
        {
            foreach (var member in akcss.GetRoot().Members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (member is not AkcssUsingDirectiveSyntax)
                {
                    AddInvalidGlobalUsing(tree, member, ref result);
                }
            }
        }
    }

    private static void AddInvalidGlobalUsing(
        AkburaSyntaxTree tree,
        AkburaSyntax syntax,
        ref List<AkburaDiagnosticRecord>? result)
    {
        // Preserve the existing build diagnostic's message, including punctuation.
        var message = $"Global usings file '{Path.GetFileName(tree.FilePath)}' may contain only using directives";
        var span = ClampSpan(syntax.Span.Start, syntax.Span.Length, tree.Text.Length);
        Add(ref result, new()
        {
            Id = ErrorCodes.AKBURA_SEMANTIC_GlobalUsingsFileContainsNonUsing,
            Severity = AkburaDiagnosticSeverity.Error,
            Message = message,
            FilePath = tree.FilePath,
            Span = span,
            LineSpan = tree.Text.Lines.GetLinePositionSpan(span),
            Kind = AkburaDiagnosticKind.Semantic,
        });
    }

    private static AkburaDiagnosticRecord CreateRecord(
        AkburaSyntaxTree tree,
        AkburaDiagnostic diagnostic,
        TextSpan span,
        AkburaDiagnosticKind kind)
    {
        var clamped = ClampSpan(span.Start, span.Length, tree.Text.Length);
        string message;
        try
        {
            message = diagnostic.Message;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A malformed message argument must not break the entire editor snapshot.
            message = diagnostic.Code;
        }

        return new()
        {
            Id = diagnostic.Code,
            Severity = diagnostic.Severity,
            Message = message,
            FilePath = tree.FilePath,
            Span = clamped,
            LineSpan = tree.Text.Lines.GetLinePositionSpan(clamped),
            Kind = kind,
        };
    }

    private static TextSpan ClampSpan(long start, int length, int textLength)
    {
        var clampedStart = (int)Math.Max(0L, Math.Min(start, textLength));
        var clampedEnd = (int)Math.Max(clampedStart, Math.Min(start + length, textLength));
        return TextSpan.FromBounds(clampedStart, clampedEnd);
    }

    private static void Add(ref List<AkburaDiagnosticRecord>? result, AkburaDiagnosticRecord diagnostic)
    {
#if STATS
        GenerationStatistics.Increment(GenerationStatisticCounter.GeneratedDiagnosticCreated);
#endif
        (result ??= []).Add(diagnostic);
    }
}
