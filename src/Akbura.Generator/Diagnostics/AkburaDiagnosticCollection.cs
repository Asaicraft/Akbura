#if STATS
using Akbura.Language.CodeGeneration;
#endif
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Diagnostics;

public static class AkburaDiagnosticCollection
{
    public static ImmutableArray<AkburaDiagnosticRecord> Deduplicate(
        IEnumerable<AkburaDiagnosticRecord> diagnostics) =>
        Deduplicate(diagnostics, AkburaDiagnosticCanonicalComparer.Instance.GetHashCode);

    internal static ImmutableArray<AkburaDiagnosticRecord> Deduplicate(
        IEnumerable<AkburaDiagnosticRecord> diagnostics,
        Func<AkburaDiagnosticRecord, int> getBucketHash)
    {
        var result = ImmutableArray.CreateBuilder<AkburaDiagnosticRecord>();
        var buckets = new Dictionary<int, List<int>>();

        foreach (var diagnostic in diagnostics)
        {
            var hash = getBucketHash(diagnostic);
            if (!buckets.TryGetValue(hash, out var candidates))
            {
                candidates = [];
                buckets.Add(hash, candidates);
            }

            var duplicate = false;
            foreach (var index in candidates)
            {
                var candidate = result[index];
                if (!AkburaDiagnosticCanonicalComparer.Instance.Equals(candidate, diagnostic))
                {
                    continue;
                }

                result[index] = candidate with
                {
                    Provenance = candidate.Provenance | diagnostic.Provenance,
                    DocumentVersion = Math.Max(candidate.DocumentVersion, diagnostic.DocumentVersion),
                };
                duplicate = true;
#if STATS
                GenerationStatistics.Increment(GenerationStatisticCounter.DiagnosticDeduplicated);
                const DiagnosticProvenance workspaceOrigins = DiagnosticProvenance.Workspaces |
                    DiagnosticProvenance.LanguageServer | DiagnosticProvenance.VisualStudio;
                if (((candidate.Provenance & DiagnosticProvenance.BlackSilenceGenerator) != 0 &&
                     (diagnostic.Provenance & workspaceOrigins) != 0) ||
                    ((diagnostic.Provenance & DiagnosticProvenance.BlackSilenceGenerator) != 0 &&
                     (candidate.Provenance & workspaceOrigins) != 0))
                {
                    GenerationStatistics.Increment(GenerationStatisticCounter.DiagnosticWorkspaceCollision);
                }
#endif
                break;
            }

            if (!duplicate)
            {
                candidates.Add(result.Count);
                result.Add(diagnostic);
            }
        }

        return result.ToImmutable();
    }
}
