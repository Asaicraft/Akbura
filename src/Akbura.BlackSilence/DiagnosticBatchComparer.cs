using Akbura.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.BlackSilence;

internal sealed class DiagnosticBatchComparer : IEqualityComparer<ImmutableArray<AkburaDiagnosticRecord>>
{
    public static readonly DiagnosticBatchComparer Instance = new();

    public bool Equals(ImmutableArray<AkburaDiagnosticRecord> left, ImmutableArray<AkburaDiagnosticRecord> right)
    {
        if (left == right)
        {
            return true;
        }

        if (left.IsDefault || right.IsDefault || left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!AkburaDiagnosticCanonicalComparer.Instance.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(ImmutableArray<AkburaDiagnosticRecord> diagnostics) => diagnostics.IsDefault ? 0 : diagnostics.Length;
}
