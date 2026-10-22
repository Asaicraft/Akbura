#if STATS
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace Akbura.BlackSilence;

/// <summary>
/// Explicitly opted-in analyzer-side observations for live watch investigations.
/// The pipeline owns this recorder; previous identities are weak, emitted records
/// are scalar, and each measurement restores its ambient scope before returning.
/// No semantic cache or production marker is introduced.
/// </summary>
internal sealed class BlackSilenceWatchObservation(string? directory)
{
    private readonly string _observer = Guid.NewGuid().ToString("N");
    private readonly object _gate = new();
    private WeakReference<CSharpCompilation>? _previousInput;
    private WeakReference<BlackSilenceProjectState>? _previousState;
    private long _sequence;

#pragma warning disable RS1035 // Opt-in STATS benchmark sink, absent from production builds.
    internal static BlackSilenceWatchObservation FromEnvironment() => new(Environment.GetEnvironmentVariable("AKBURA_WATCH_OBSERVATIONS"));

    internal CSharpEnvironmentSnapshot ObserveEnvironment(CSharpEnvironmentSnapshot environment, ImmutableArray<DocumentSyntaxVersion> documents)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return environment;
        }
        var record = Header("Environment", null);
        lock (_gate)
        {
            record["InputCompilation"] = Scalar(BlackSilenceInvalidationTrace.Id(environment.Compilation));
            record["SameInputCompilationReference"] = Scalar(_previousInput != null && _previousInput.TryGetTarget(out var previous) &&
                ReferenceEquals(previous, environment.Compilation));
            _previousInput = new WeakReference<CSharpCompilation>(environment.Compilation);
        }
        record["RetainedCSharpTrees"] = Scalar(environment.RetainedSyntaxTrees.Length);
        AddSources(record, documents);
        Write(record);
        return environment;
    }

    internal BlackSilenceGenerationRequest? CreateRequest(ImmutableArray<DocumentSyntaxVersion> documents,
        BlackSilenceProjectState? state, GeneratorProjectOptions options, CancellationToken token, bool computeDiagnostics)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return BlackSilenceGenerationRequestBuilder.Create(documents, state, options, token, computeDiagnostics);
        }
        using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
        var request = BlackSilenceGenerationRequestBuilder.Create(documents, state, options, token, computeDiagnostics);
        Complete("Request", request, measurement);
        return request;
    }

    internal BlackSilenceDocumentBatch Generate(BlackSilenceGenerationRequest request, CancellationToken token)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return BlackSilenceDocumentBatch.GenerateSafely(request, token);
        }
        var record = Header("Generation", request);
        lock (_gate)
        {
            record["SameProjectStateReference"] = Scalar(_previousState != null && _previousState.TryGetTarget(out var previous) &&
                ReferenceEquals(previous, request.State));
            _previousState = new WeakReference<BlackSilenceProjectState>(request.State);
        }
        record["PreviousGenerationVersion"] = Scalar(request.State.ProbeStore?.Latest?.Version ?? 0);
        record["PreviousSemanticEntries"] = Scalar(request.State.ProbeStore?.Latest?.SemanticEntryCount ?? 0);
        using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
        try
        {
            var batch = BlackSilenceDocumentBatch.GenerateSafely(request, token);
            record["PublishedGenerationVersion"] = Scalar(request.State.ProbeStore?.Latest?.Version ?? 0);
            record["PublishedSemanticEntries"] = Scalar(request.State.ProbeStore?.Latest?.SemanticEntryCount ?? 0);
            record["PublishedProjectionEntries"] = Scalar(request.State.ProbeStore?.Latest?.EntryCount ?? 0);
            record["PublishedSnapshotVersion"] = Scalar(request.State.TryGetSnapshot(request.Options)?.Version ?? 0);
            var operations = request.State.ProbeStore?.Latest?.Documents.Values.SelectMany(document => document.Entries.Values)
                .Where(entry => entry.Binding != null).Select(entry => entry.Binding!.Result.OperationDefinition.Operation!).ToArray() ?? [];
            record["LiveOperationRoots"] = Scalar(operations.Select(operation => BlackSilenceInvalidationTrace.Id(operation)).Distinct().Count());
            record["LiveProbeModels"] = Scalar(operations.Select(operation => BlackSilenceInvalidationTrace.Id(operation.SemanticModel)).Distinct().Count());
            record["LiveProbeCompilations"] = Scalar(operations.Select(operation => BlackSilenceInvalidationTrace.Id(operation.SemanticModel?.Compilation)).Distinct().Count());
            record["LiveProbeSyntaxTrees"] = Scalar(operations.Select(operation => BlackSilenceInvalidationTrace.Id(operation.Syntax.SyntaxTree)).Distinct().Count());
            record["ErrorDiagnostics"] = Scalar(batch.Diagnostics.Count(diagnostic => diagnostic.Severity == Akbura.Language.Syntax.AkburaDiagnosticSeverity.Error));
            Complete("Generation", request, measurement, record);
            return batch;
        }
        catch (OperationCanceledException)
        {
            record["Canceled"] = "True";
            Complete("Generation", request, measurement, record);
            throw;
        }
    }

    private void Complete(string kind, BlackSilenceGenerationRequest? request, GenerationStatisticsSession measurement,
        Dictionary<string, string>? record = null)
    {
        var snapshot = measurement.GetSnapshot();
        record ??= Header(kind, request);
        foreach (GenerationStatisticCounter counter in Enum.GetValues(typeof(GenerationStatisticCounter)))
        {
            if (counter != GenerationStatisticCounter.Count)
            {
                record["counter." + counter] = Scalar(snapshot.GetCounterCount(counter));
            }
        }
        foreach (var stage in new[] { GenerationStatisticStage.ExpressionFactoryBinding, GenerationStatisticStage.CrossRevisionBindingValidation,
            GenerationStatisticStage.CrossRevisionSemanticContributionImport })
        {
            record["stageCount." + stage] = Scalar(snapshot.GetStageInvocationCount(stage));
            record["stageMs." + stage] = Scalar(snapshot.GetStageElapsed(stage).TotalMilliseconds);
        }
        var index = 0;
        foreach (var observation in snapshot.InvalidationEvents)
        {
            var prefix = "event." + index++ + ".";
            record[prefix + "Kind"] = observation.Kind;
            record[prefix + "Path"] = observation.Path;
            record[prefix + "Decision"] = observation.Decision;
            record[prefix + "Reasons"] = string.Join(";", observation.Reasons);
            foreach (var pair in observation.Data)
            {
                record[prefix + "data." + pair.Key] = pair.Value;
            }
        }
        record["EventCount"] = Scalar(index);
        var forceCollection = Environment.GetEnvironmentVariable("AKBURA_WATCH_GC_OBSERVATIONS") == "1";
        record["ManagedHeapBytes"] = Scalar(GC.GetTotalMemory(forceCollection));
        using var process = Process.GetCurrentProcess();
        record["WorkingSetBytes"] = Scalar(process.WorkingSet64);
        record["HeapCollectionRequested"] = Scalar(forceCollection);
        record["Gen0Collections"] = Scalar(GC.CollectionCount(0));
        record["Gen1Collections"] = Scalar(GC.CollectionCount(1));
        record["Gen2Collections"] = Scalar(GC.CollectionCount(2));
        Write(record);
    }

    private Dictionary<string, string> Header(string kind, BlackSilenceGenerationRequest? request)
    {
        using var process = Process.GetCurrentProcess();
        var record = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Kind"] = kind,
            ["Observer"] = _observer,
            ["Pid"] = Scalar(process.Id),
            ["Sequence"] = Scalar(Interlocked.Increment(ref _sequence)),
            ["Utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["CompilerMvid"] = typeof(AkburaBlackSilenceGenerator).Module.ModuleVersionId.ToString(),
            ["GeneratorMvid"] = typeof(GenerationStatistics).Module.ModuleVersionId.ToString(),
            ["HostCommandLine"] = Environment.CommandLine,
        };
        if (request != null)
        {
            record["Version"] = Scalar(request.Version);
            record["Compilation"] = Scalar(BlackSilenceInvalidationTrace.Id(request.State.CSharpCompilation));
            record["ProjectState"] = Scalar(BlackSilenceInvalidationTrace.Id(request.State));
            record["ProbeStore"] = Scalar(BlackSilenceInvalidationTrace.Id(request.State.ProbeStore));
            AddSources(record, request.Documents);
        }
        return record;
    }

    private static void AddSources(Dictionary<string, string> record, ImmutableArray<DocumentSyntaxVersion> documents)
    {
        foreach (var document in documents)
        {
            record["source." + Path.GetFileName(document.FilePath)] = BlackSilenceInvalidationTrace.Fingerprint(document.SyntaxTree.Text.ToString());
        }
    }

    private void Write(Dictionary<string, string> record)
    {
        try
        {
            Directory.CreateDirectory(directory!);
            using var buffer = new MemoryStream();
            new DataContractJsonSerializer(typeof(Dictionary<string, string>), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true })
                .WriteObject(buffer, record);
            var path = Path.Combine(directory!, _observer + "-" + record["Sequence"] + "-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(path + ".tmp", Encoding.UTF8.GetString(buffer.ToArray()) + "\r\n", new UTF8Encoding(false));
            File.Move(path + ".tmp", path + ".json");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Missing telemetry fails the benchmark's observation gate, never the user's generation.
        }
    }

    private static string Scalar(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
#pragma warning restore RS1035
}
#endif
