using System;
using System.Collections.Generic;
using Akbura.Language.CodeGeneration;

namespace Akbura.BlackSilence;

internal sealed class CSharpEnvironmentSnapshotComparer : IEqualityComparer<CSharpEnvironmentSnapshot>
{
    public static CSharpEnvironmentSnapshotComparer Instance { get; } = new();

    public bool Equals(CSharpEnvironmentSnapshot? x, CSharpEnvironmentSnapshot? y)
    {
        var equal = ReferenceEquals(x, y) || (x != null && y != null && x.HasSameEnvironment(y));
#if STATS
        if (GenerationStatistics.IsInvalidationTrackingEnabled && x != null && y != null)
        {
            x.TraceComparison(y, equal);
        }
#endif
        return equal;
    }

    public int GetHashCode(CSharpEnvironmentSnapshot obj)
    {
        // Hashing is only a bucket hint. Equality always compares complete retained inputs.
        return HashCode.Combine(
            obj.Compilation.AssemblyName,
            obj.Compilation.Options,
            obj.RetainedSyntaxTrees.Length);
    }
}
