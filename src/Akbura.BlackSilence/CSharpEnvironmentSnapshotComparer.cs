using System;
using System.Collections.Generic;

namespace Akbura.BlackSilence;

internal sealed class CSharpEnvironmentSnapshotComparer : IEqualityComparer<CSharpEnvironmentSnapshot>
{
    public static CSharpEnvironmentSnapshotComparer Instance { get; } = new();

    public bool Equals(CSharpEnvironmentSnapshot? x, CSharpEnvironmentSnapshot? y)
    {
        return ReferenceEquals(x, y) || (x != null && y != null && x.HasSameEnvironment(y));
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
