#if STATS
using Akbura.Language.CodeGeneration;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Akbura.BlackSilence;

/// <summary>
/// Observes decisions without transferring cached state. Weak identities do not
/// root old compiler graphs. Fingerprints are report labels, never cache keys.
/// Events describe completed boundaries, not successful completion of a driver run.
/// </summary>
internal static class BlackSilenceInvalidationTrace
{
    private static readonly ConditionalWeakTable<object, Identity> s_identities = new();
    private static long s_nextIdentity;

    public static long Id(object? value) => value == null ? 0 : s_identities.GetValue(value,
        static _ => new Identity(Interlocked.Increment(ref s_nextIdentity))).Value;

    public static void Record(string kind, string path, string decision, ImmutableArray<string> reasons,
        params (string Key, object? Value)[] data)
    {
        GenerationStatistics.RecordInvalidation(new GenerationInvalidationEvent(kind, path, decision, reasons,
            data.ToImmutableDictionary(static pair => pair.Key,
                static pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty)));
    }

    public static void Requests(BlackSilenceGenerationRequest request)
    {
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.InvalidationTrace);
        var previous = request.PreviousSnapshot;
        Record("ProjectContext", string.Empty, previous == null ? "NoPreviousSnapshot" : "PreviousSnapshotAvailable", [],
            ("State", Id(request.State)), ("CSharpCompilation", Id(request.State.CSharpCompilation)),
            ("Snapshot", Id(previous)), ("Index", Id(request.Index)),
            ("IndexReused", ReferenceEquals(request.Index, previous?.Index)),
            ("AkburaCompilation", Id(request.Index.Compilation)),
            ("PreviousEntries", previous?.Entries.Count ?? 0), ("PreviousSemanticStates", previous?.SemanticStates.Count ?? 0),
            ("PreviousDiagnostics", previous?.DiagnosticEntries.Count ?? 0),
            ("DeclarationEnvironment", Id(request.DeclarationEnvironment)), ("Options", request.Options));

        foreach (var document in request.Documents)
        {
            var old = previous?.Documents.FirstOrDefault(candidate => candidate.FilePath == document.FilePath);
            Record("DocumentInputs", document.FilePath, old == null ? "PreviousDocumentUnavailable" : "Compared", [],
                ("Source", Fingerprint(document.SyntaxTree.Text.ToString())),
                ("GenerationShape", Fingerprint(document.GenerationShape)),
                ("Surface", Fingerprint(string.Join("\0", document.SurfaceShape))),
                ("DependencyNames", Fingerprint(string.Join("\0", document.DependencyNames))),
                ("SourceChanged", old == null ? "Unknown" : (!old.SyntaxTree.Text.ContentEquals(document.SyntaxTree.Text)).ToString()),
                ("GenerationShapeChanged", old == null ? "Unknown" : (!document.HasSameGenerationShape(old)).ToString()),
                ("SurfaceChanged", old == null ? "Unknown" : (!document.HasSameSurface(old)).ToString()),
                ("CanReuse", document.CanReuse), ("ConservativeDependencies", document.RequiresConservativeDependencies));
        }

        if (previous != null)
        {
            foreach (var removed in previous.Documents.Where(old => !request.Documents.Any(doc => doc.FilePath == old.FilePath)))
            {
                Record("DocumentInputs", removed.FilePath, "Removed", ["DocumentRemoved"]);
            }
        }

        foreach (var component in request.Components)
        {
            Generation(component.Identity, component.Version, component.Previous != null);
        }

        foreach (var module in request.ExternalAkcss.Concat(request.InlineAkcss))
        {
            Generation(module.Identity, module.Version, module.Previous != null);
        }

        foreach (var diagnostic in request.Diagnostics)
        {
            DiagnosticDocumentEntry? old = null;
            previous?.DiagnosticEntries.TryGetValue(diagnostic.Document.FilePath, out old);
            Record("DiagnosticDecision", diagnostic.Document.FilePath, diagnostic.Previous == null ? "Evaluate" : "Reuse",
                old == null ? [diagnostic.Previous != null ? "DetachedArtifactValidated" : previous == null ? "PreviousSnapshotUnavailable" : "PreviousDiagnosticEntryUnavailable"] :
                    diagnostic.Version.DescribeChanges(old.Version),
                ("EntryAvailable", old != null), ("CSharpCompilation", Id(request.State.CSharpCompilation)));
        }

        void Generation(string identity, DocumentGenerationVersion version, bool reused)
        {
            GeneratedDocumentEntry? old = null;
            previous?.Entries.TryGetValue(identity, out old);
            Record("GenerationDecision", version.Document.FilePath, reused ? "Reuse" : "Generate",
                old == null ? [reused ? "DetachedArtifactValidated" : previous == null ? "PreviousSnapshotUnavailable" : "PreviousGeneratedEntryUnavailable"] :
                    version.DescribeChanges(old.Version),
                ("Identity", identity), ("EntryAvailable", old != null),
                ("Dependencies", version.DescribeDependencies()), ("DeclarationEnvironment", Id(request.DeclarationEnvironment)));
        }
    }

    public static void DocumentVersion(DocumentSyntaxVersion document)
    {
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.InvalidationTrace);
        Record("DocumentVersion", document.FilePath, "ConstructedBeforeComparison", [],
            ("Source", Fingerprint(document.SyntaxTree.Text.ToString())),
            ("GenerationShape", Fingerprint(document.GenerationShape)),
            ("Surface", Fingerprint(string.Join("\0", document.SurfaceShape))),
            ("DependencyNames", Fingerprint(string.Join("\0", document.DependencyNames))),
            ("CanReuse", document.CanReuse));
    }

    public static string Fingerprint(string text)
    {
        using var algorithm = SHA256.Create();
        return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
    }

    private sealed record Identity(long Value);
}
#endif
