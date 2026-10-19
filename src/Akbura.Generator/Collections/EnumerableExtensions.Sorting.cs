using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Akbura.Collections;

public static partial class EnumerableExtensions
{
    /// <summary>
    /// Copies a known-size collection into an owned immutable result and sorts it.
    /// Equal-key order is unspecified; callers must not require a stable sort.
    /// </summary>
    internal static ImmutableArray<T> ToSortedImmutableArray<T>(this ICollection<T> source, IComparer<T> comparer)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }
        if (comparer == null)
        {
            throw new ArgumentNullException(nameof(comparer));
        }
        if (source.Count == 0)
        {
            return [];
        }
        var result = new T[source.Count];
        source.CopyTo(result, 0);
        Array.Sort(result, comparer);
        return ImmutableCollectionsMarshal.AsImmutableArray(result);
    }
}
