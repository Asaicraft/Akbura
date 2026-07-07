// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

using System.Runtime.CompilerServices;
using System.Threading;

namespace Akbura.Collections;

internal readonly partial struct ImmutableSegmentedHashSet<T>
{
    internal static class PrivateMarshal
    {
        internal static ImmutableSegmentedHashSet<T> VolatileRead(in ImmutableSegmentedHashSet<T> location)
        {
            var set = Volatile.Read(ref Unsafe.AsRef(in location._set));
            return set is null
                ? default
                : new ImmutableSegmentedHashSet<T>(set);
        }

        internal static ImmutableSegmentedHashSet<T> InterlockedExchange(
            ref ImmutableSegmentedHashSet<T> location,
            ImmutableSegmentedHashSet<T> value)
        {
            var set = Interlocked.Exchange(ref Unsafe.AsRef(in location._set), value._set);
            return set is null
                ? default
                : new ImmutableSegmentedHashSet<T>(set);
        }

        internal static ImmutableSegmentedHashSet<T> InterlockedCompareExchange(
            ref ImmutableSegmentedHashSet<T> location,
            ImmutableSegmentedHashSet<T> value,
            ImmutableSegmentedHashSet<T> comparand)
        {
            var set = Interlocked.CompareExchange(
                ref Unsafe.AsRef(in location._set),
                value._set,
                comparand._set);
            return set is null
                ? default
                : new ImmutableSegmentedHashSet<T>(set);
        }

        internal static ImmutableSegmentedHashSet<T> AsImmutableSegmentedHashSet(SegmentedHashSet<T>? set)
        {
            return set is not null
                ? new ImmutableSegmentedHashSet<T>(set)
                : default;
        }

        internal static SegmentedHashSet<T>? AsSegmentedHashSet(ImmutableSegmentedHashSet<T> set)
        {
            return set._set;
        }
    }
}
