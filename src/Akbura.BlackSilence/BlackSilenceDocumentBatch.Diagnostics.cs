using Akbura.Diagnostics;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System;
using System.Collections.Immutable;
using System.Threading;

namespace Akbura.BlackSilence;

internal sealed partial class BlackSilenceDocumentBatch
{
    public ImmutableArray<AkburaDiagnosticRecord> Diagnostics { get; }

    public static BlackSilenceDocumentBatch GenerateSafely(
        BlackSilenceGenerationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Generate(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.GeneratedDiagnosticCreated);
#endif
            // Generate publishes only after every source and diagnostic succeeds.
            // A failed attempt must not poison that snapshot or become silent.
            return new BlackSilenceDocumentBatch([], [], [],
            [
                new AkburaDiagnosticRecord
                {
                    Id = "AKBURA_GENERATOR_FAILURE",
                    Severity = AkburaDiagnosticSeverity.Error,
                    Message = "BlackSilence generation failed: " + exception,
                    FilePath = string.Empty,
                    Span = default,
                    LineSpan = default,
                    Kind = AkburaDiagnosticKind.Infrastructure,
                    Provenance = DiagnosticProvenance.BlackSilenceGenerator,
                    DocumentVersion = request.Version,
                },
            ]);
        }
    }

    private static bool CanImportDiagnosticSeed(BlackSilenceGenerationRequest request, string path)
    {
        if (!request.ComputeDiagnostics)
        {
            return true;
        }

        foreach (var diagnostic in request.Diagnostics)
        {
            if (diagnostic.Document.FilePath == path)
            {
                return diagnostic.Previous != null;
            }
        }

        return false;
    }

    private static ImmutableDictionary<string, DiagnosticDocumentEntry> CollectDiagnostics(
        BlackSilenceGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.ComputeDiagnostics)
        {
            return ImmutableDictionary<string, DiagnosticDocumentEntry>.Empty;
        }

#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.DiagnosticBatch);
        GenerationStatistics.Increment(GenerationStatisticCounter.DiagnosticBatchCreated);
#endif
        var entries = ImmutableDictionary.CreateBuilder<string, DiagnosticDocumentEntry>(StringComparer.Ordinal);
        foreach (var diagnosticRequest in request.Diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = diagnosticRequest.Document;
            if (diagnosticRequest.Previous is { } previous)
            {
                entries.Add(document.FilePath, previous);
#if STATS
                GenerationStatistics.Increment(GenerationStatisticCounter.DiagnosticDocumentReused);
#endif
                continue;
            }

            var tree = document.SyntaxTree;
            AkburaSemanticModel? model = null;
            if (!GlobalUsings.IsComponentFile(tree) && !GlobalUsings.IsAkcssFile(tree))
            {
#if STATS
                if (!request.Index.Compilation.HasCreatedSemanticModel(tree))
                {
                    GenerationStatistics.Increment(GenerationStatisticCounter.DiagnosticSemanticModelCreated);
                }
#endif
                model = request.Index.Compilation.GetSemanticModel(tree);
            }

            var diagnostics = AkburaDiagnosticEngine.Collect(tree, model, cancellationToken: cancellationToken);
            if (!diagnostics.IsEmpty)
            {
                var detached = new AkburaDiagnosticRecord[diagnostics.Length];
                for (var i = 0; i < detached.Length; i++)
                {
                    detached[i] = diagnostics[i] with
                    {
                        Provenance = DiagnosticProvenance.BlackSilenceGenerator,
                        DocumentVersion = request.Version,
                    };
                }

                diagnostics = detached.ToImmutableArrayUnsafe();
            }

            entries.Add(document.FilePath, new DiagnosticDocumentEntry(diagnosticRequest.Version, diagnostics));
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.DiagnosticDocumentEvaluated);
#endif
        }

        return entries.ToImmutable();
    }

    private static ImmutableArray<AkburaDiagnosticRecord> GetDiagnostics(
        BlackSilenceGenerationRequest request,
        ImmutableDictionary<string, DiagnosticDocumentEntry> entries)
    {
        using var diagnostics = ImmutableArrayBuilder<AkburaDiagnosticRecord>.Rent();
        foreach (var document in request.Diagnostics)
        {
            diagnostics.AddRange(entries[document.Document.FilePath].Diagnostics);
        }

        if (diagnostics.Count == 0)
        {
            return [];
        }

        // Imported AKCSS can expose one source diagnostic through multiple owners.
        // Merge only complete canonical identities, never just ID or start line.
        return AkburaDiagnosticCollection.Deduplicate(diagnostics.ToImmutable())
            .Sort(AkburaDiagnosticCanonicalComparer.Instance);
    }
}
