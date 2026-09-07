using Akbura.Language.CodeGeneration;
using Akbura.Pools;
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Akbura.BlackSilence;

internal sealed partial class BlackSilenceDocumentBatch
{
    private BlackSilenceDocumentBatch(
        ImmutableArray<GeneratedSource> components,
        ImmutableArray<GeneratedSource> externalAkcss,
        ImmutableArray<GeneratedSource> inlineAkcss,
        ImmutableArray<Akbura.Diagnostics.AkburaDiagnosticRecord> diagnostics)
    {
        Components = components;
        ExternalAkcss = externalAkcss;
        InlineAkcss = inlineAkcss;
        Diagnostics = diagnostics;
    }

    public ImmutableArray<GeneratedSource> Components { get; }
    public ImmutableArray<GeneratedSource> ExternalAkcss { get; }
    public ImmutableArray<GeneratedSource> InlineAkcss { get; }

    public static BlackSilenceDocumentBatch Generate(
        BlackSilenceGenerationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.DocumentBatch);
#endif
        var componentCount = request.Components.Length;
        var externalCount = request.ExternalAkcss.Length;
        var total = componentCount + externalCount + request.InlineAkcss.Length;
        var results = new GeneratedDocumentEntry?[total];
        var prepared = new PreparedDocument[total];
        using var dirtyIndices = ImmutableArrayBuilder<int>.Rent(total);

        // Import only proven-equivalent, owner-independent results. Registration
        // is lazy: a clean document does not get a semantic model just for caching.
        if (request.PreviousSnapshot is { } previousSnapshot &&
            !ReferenceEquals(request.Index, previousSnapshot.Index))
        {
            for (var i = 0; i < componentCount; i++)
            {
                var component = request.Components[i];
                if (component.Previous != null && CanImportDiagnosticSeed(request, component.Descriptor.SyntaxTree.FilePath) &&
                    previousSnapshot.SemanticStates.TryGetValue(component.Descriptor.SyntaxTree, out var state))
                {
                    request.Index.Compilation.TryImportSemanticState(component.Descriptor.SyntaxTree, state);
                }
            }

            for (var i = componentCount; i < total; i++)
            {
                var module = GetAkcssRequest(i);
                if (module.Previous != null && CanImportDiagnosticSeed(request, module.Descriptor.SyntaxTree.FilePath) &&
                    previousSnapshot.SemanticStates.TryGetValue(module.Descriptor.SyntaxTree, out var state))
                {
                    request.Index.Compilation.TryImportSemanticState(module.Descriptor.SyntaxTree, state);
                }
            }
        }

        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = i < componentCount
                ? request.Components[i].Previous
                : GetAkcssRequest(i).Previous;

            if (previous != null)
            {
                results[i] = previous;
#if STATS
                GenerationStatistics.Increment(i < componentCount
                    ? GenerationStatisticCounter.ComponentReused
                    : GenerationStatisticCounter.AkcssReused);
#endif
            }
            else
            {
                dirtyIndices.Add(i);
            }
        }

        // Resolve only dirty inputs, in stable order. Concurrent first-time binding
        // can redundantly construct the same dependency's symbols and probe trees;
        // parallelism is useful after those shared semantic inputs are initialized.
        for (var i = 0; i < dirtyIndices.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = dirtyIndices.WrittenSpan[i];
            if (index < componentCount)
            {
                if (request.Index.TryResolveComponent(request.Components[index].Descriptor, out var input, cancellationToken))
                {
                    prepared[index] = new PreparedDocument(input);
                }
            }
            else if (request.Index.TryResolveAkcss(GetAkcssRequest(index).Descriptor, out var input, cancellationToken))
            {
                prepared[index] = new PreparedDocument(input);
            }
        }

        // Evaluate the diagnostic-only dirty set through the same compilation.
        // Existing source models are reused, and shared lazy binding completes
        // before source writers enter the parallel queue.
        var diagnosticEntries = CollectDiagnostics(request, cancellationToken);

        // Small edits should not pay ThreadPool scheduling costs. Cold generation
        // uses one queue for components and both kinds of AKCSS documents.
        if (dirtyIndices.Count <= 2)
        {
            for (var i = 0; i < dirtyIndices.Count; i++)
            {
                GenerateDocument(dirtyIndices.WrittenSpan[i]);
            }
        }
        else
        {
            var dirty = dirtyIndices.ToImmutable();
            Parallel.For(
                0,
                dirty.Length,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Math.Max(1, ProcessorCountHelper.GetProcessorCount() / 4),
                },
                i => GenerateDocument(dirty[i]));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var entries = ImmutableDictionary.CreateBuilder<string, GeneratedDocumentEntry>(StringComparer.Ordinal);
        for (var i = 0; i < results.Length; i++)
        {
            if (results[i] is { } entry)
            {
                entries.Add(entry.Identity, entry);
            }
        }

        var batch = new BlackSilenceDocumentBatch(
            GetSources(results, 0, componentCount),
            GetSources(results, componentCount, externalCount),
            GetSources(results, componentCount + externalCount, request.InlineAkcss.Length),
            GetDiagnostics(request, diagnosticEntries));
        var snapshot = new BlackSilenceProjectSnapshot(
            request.Version,
            request.Options,
            request.Index,
            request.Documents,
            request.DeclarationEnvironment,
            entries.ToImmutable(),
            request.Index.Compilation.GetReusableSemanticStates(),
            diagnosticEntries);

        cancellationToken.ThrowIfCancellationRequested();
        request.State.Publish(snapshot);
        return batch;

        AkcssGenerationRequest GetAkcssRequest(int index)
        {
            return index < componentCount + externalCount
                ? request.ExternalAkcss[index - componentCount]
                : request.InlineAkcss[index - componentCount - externalCount];
        }

        void GenerateDocument(int index)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!prepared[index].IsValid)
            {
                return;
            }

            if (index < componentCount)
            {
                var component = request.Components[index];
                var input = prepared[index].Component;
                var text = ComponentDocumentWriter.Generate(
                    input.Component,
                    input.SemanticModel,
                    input.SourcePath,
                    request.Index.AkcssModuleTypeNames,
                    cancellationToken);
                results[index] = new GeneratedDocumentEntry(
                    component.Identity,
                    component.Version,
                    ComponentDocumentWriter.GetHintName(input.Component, input.SourcePath),
                    text);
            }
            else
            {
                var module = GetAkcssRequest(index);
                var input = prepared[index].Akcss;
                var text = AkcssDocumentWriter.Generate(
                    input,
                    request.Index.AkcssSourceMap,
                    request.Index.RootNamespace,
                    cancellationToken);
                results[index] = new GeneratedDocumentEntry(
                    module.Identity,
                    module.Version,
                    AkcssDocumentWriter.GetHintName(input),
                    text);
            }
        }
    }

    private readonly struct PreparedDocument
    {
        public PreparedDocument(ComponentGenerationInput component)
        {
            IsValid = true;
            Component = component;
            Akcss = default;
        }

        public PreparedDocument(AkcssGenerationInput akcss)
        {
            IsValid = true;
            Component = default;
            Akcss = akcss;
        }

        public bool IsValid { get; }
        public ComponentGenerationInput Component { get; }
        public AkcssGenerationInput Akcss { get; }
    }

    private static ImmutableArray<GeneratedSource> GetSources(GeneratedDocumentEntry?[] entries, int start, int count)
    {
        using var sources = ImmutableArrayBuilder<GeneratedSource>.Rent(count);
        for (var i = start; i < start + count; i++)
        {
            if (entries[i] is { } entry)
            {
                sources.Add(entry.Source);
            }
        }

        return sources.ToImmutable();
    }
}
