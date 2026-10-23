using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Akbura.Collections;

public static partial class EnumerableExtensions
{
    /// <summary>
    /// Copies and sorts a collection into an independently owned immutable array.
    /// Equal elements have no guaranteed relative order; callers must not require a stable sort.
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

        var count = source.Count;
        if (count == 0)
        {
            return [];
        }

        var result = new T[count];
        source.CopyTo(result, 0);
        Array.Sort(result, comparer);
        return ImmutableCollectionsMarshal.AsImmutableArray(result);
    }
}
