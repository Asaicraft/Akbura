#if STATS
using Akbura.Language.CodeGeneration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace Akbura.BlackSilence;

internal sealed partial class BlackSilenceWatchObservation
{
    private Dictionary<string, string>? _criticalOutputHashes;
    private long _criticalOutputSequence;

#pragma warning disable RS1035 // Explicit benchmark telemetry, excluded from non-STATS binaries.
    private void ObserveCriticalOutput(Dictionary<string, string> record, BlackSilenceDocumentBatch batch, BlackSilenceGenerationRequest request)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AKBURA_CRITICAL_REVISION")))
        {
            return;
        }
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var characters = 0L;
        foreach (var source in batch.Components.Concat(batch.ExternalAkcss).Concat(batch.InlineAkcss).Concat(batch.ProjectSources))
        {
            characters += source.SourceText.Length;
            hashes.Add(source.HintName, Fingerprint(source.SourceText.ToString()));
        }
        lock (_gate)
        {
            var sequence = long.Parse(record["Sequence"], System.Globalization.CultureInfo.InvariantCulture);
            if (sequence <= _criticalOutputSequence || request.State.TryGetSnapshot(request.Options)?.Version != request.Version)
            {
                return;
            }
            var changed = hashes.Count(pair => _criticalOutputHashes == null ||
                !_criticalOutputHashes.TryGetValue(pair.Key, out var previous) || previous != pair.Value);
            var removed = _criticalOutputHashes?.Keys.Count(key => !hashes.ContainsKey(key)) ?? 0;
            record["GeneratedCSharpCharacters"] = Scalar(characters);
            record["GeneratedSourceHintCount"] = Scalar(hashes.Count);
            record["ChangedGeneratedSourceHintCount"] = Scalar(changed + removed);
            _criticalOutputHashes = hashes;
            _criticalOutputSequence = sequence;
        }
    }

    private static void ObserveCriticalScope(Dictionary<string, string> record, BlackSilenceGenerationRequest? request, GenerationStatisticsSnapshot snapshot)
    {
        var path = Environment.GetEnvironmentVariable("AKBURA_CRITICAL_REVISION");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        record["ScopeElapsedMs"] = Scalar(snapshot.Elapsed.TotalMilliseconds);
        record["ScopeMeaning"] = "Inclusive Akbura request or generated-batch scope; not full GeneratorDriver or EnC time. Stage costs may overlap.";
        record["ProcessStopwatchTicks"] = Scalar(Stopwatch.GetTimestamp());
        record["ProcessStopwatchFrequency"] = Scalar(Stopwatch.Frequency);
        record["ComponentPlansCreated"] = Scalar(snapshot.GetStageInvocationCount(GenerationStatisticStage.ComponentPlanning));
        record["StageAllocatedBytesMeaning"] = "Unavailable; no cross-thread allocation callback supplied. Do not replace with application allocations.";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var metadata = (Dictionary<string, string>)new DataContractJsonSerializer(typeof(Dictionary<string, string>),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).ReadObject(stream)!;
            record["Revision"] = metadata["Revision"];
            record["RevisionSource"] = metadata["SourceName"];
            record["RevisionExpectedSha256"] = metadata["SourceSha256"];
            var sourceName = metadata["SourceName"];
            string? actual = null;
            if (record.TryGetValue("source." + sourceName, out var markupHash))
            {
                actual = markupHash;
            }
            else if (request != null)
            {
                var tree = request.State.CSharpCompilation.SyntaxTrees.SingleOrDefault(tree =>
                    string.Equals(Path.GetFullPath(tree.FilePath), Path.GetFullPath(metadata["SourcePath"]), StringComparison.OrdinalIgnoreCase));
                if (tree != null)
                {
                    actual = Fingerprint(tree.GetText().ToString());
                }
            }
            record["RevisionInputMatched"] = Scalar(actual == metadata["SourceSha256"]);
            if (record["Kind"] == "Generation" && actual == metadata["SourceSha256"])
            {
                CriticalPathEvents.Instance.Revision(record["Revision"], actual!);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.Serialization.SerializationException or KeyNotFoundException or ArgumentException)
        {
            record["RevisionCorrelationError"] = error.GetType().Name;
        }
    }

    private static string Fingerprint(string value)
    {
        using var algorithm = SHA256.Create();
        return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty);
    }

    [EventSource(Name = "Akbura-CriticalPath")]
    private sealed class CriticalPathEvents : EventSource
    {
        internal static readonly CriticalPathEvents Instance = new();

        [Event(1)]
        public void Revision(string revision, string sourceSha256)
        {
            WriteEvent(1, revision, sourceSha256);
        }
    }
#pragma warning restore RS1035
}
#endif
