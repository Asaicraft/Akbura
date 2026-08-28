using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Collections;


#region ArraySortHelper for single arrays

public static class SegmentedArraySortHelper<T>
{
    public static void Sort(SegmentedArraySegment<T> keys, IComparer<T>? comparer)
    {
        // Add a try block here to detect IComparers (or their
        // underlying IComparables, etc) that are bogus.
        try
        {
            comparer ??= Comparer<T>.Default;
            IntrospectiveSort(keys, comparer.Compare);
        }
        catch (IndexOutOfRangeException)
        {
            ThrowHelper.BadComparer(comparer);
        }
        catch (Exception e)
        {
            ThrowHelper.IComparerFailed(e);
        }
    }


    public static int BinarySearch(SegmentedArray<T> array, int index, int length, T value, IComparer<T>? comparer)
    {
        try
        {
            comparer ??= Comparer<T>.Default;
            return InternalBinarySearch(array, index, length, value, comparer);
        }
        catch (Exception e)
        {
            ThrowHelper.IComparerFailed(e);
            return 0;
        }
    }


    public static void Sort(SegmentedArraySegment<T> keys, Comparison<T> comparer)
    {
        AkburaDebug.Assert(comparer != null, "Check the arguments in the caller!");

        // Add a try block here to detect bogus comparisons
        try
        {
            IntrospectiveSort(keys, comparer);
        }
        catch (IndexOutOfRangeException)
        {
            ThrowHelper.BadComparer(comparer);
        }
        catch (Exception e)
        {
            ThrowHelper.IComparerFailed(e);
        }
    }


    public static int InternalBinarySearch(SegmentedArray<T> array, int index, int length, T value, IComparer<T> comparer)
    {
        AkburaDebug.Assert(index >= 0 && length >= 0 && (array.Length - index >= length), "Check the arguments in the caller!");

        var lo = index;
        var hi = index + length - 1;
        while (lo <= hi)
        {
            var i = lo + ((hi - lo) >> 1);
            var order = comparer.Compare(array[i], value);

            if (order == 0)
            {
                return i;
            }

            if (order < 0)
            {
                lo = i + 1;
            }
            else
            {
                hi = i - 1;
            }
        }

        return ~lo;
    }

    private static void SwapIfGreater(SegmentedArraySegment<T> keys, Comparison<T> comparer, int i, int j)
    {
        AkburaDebug.Assert(i != j);

        if (comparer(keys[i], keys[j]) > 0)
        {
            (keys[j], keys[i]) = (keys[i], keys[j]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Swap(SegmentedArraySegment<T> a, int i, int j)
    {
        AkburaDebug.Assert(i != j);

        (a[j], a[i]) = (a[i], a[j]);
    }

    public static void IntrospectiveSort(SegmentedArraySegment<T> keys, Comparison<T> comparer)
    {
        AkburaDebug.Assert(comparer != null);

        if (keys.Length > 1)
        {
            IntroSort(keys, 2 * (SegmentedArraySortUtils.Log2((uint)keys.Length) + 1), comparer!);
        }
    }

    // IntroSort is recursive; block it from being inlined into itself as
    // this is currenly not profitable.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void IntroSort(SegmentedArraySegment<T> keys, int depthLimit, Comparison<T> comparer)
    {
        AkburaDebug.Assert(keys.Length > 0);
        AkburaDebug.Assert(depthLimit >= 0);
        AkburaDebug.Assert(comparer != null);

        var partitionSize = keys.Length;
        while (partitionSize > 1)
        {
            if (partitionSize <= SegmentedArrayHelper.IntrosortSizeThreshold)
            {

                if (partitionSize == 2)
                {
                    SwapIfGreater(keys, comparer!, 0, 1);
                    return;
                }

                if (partitionSize == 3)
                {
                    SwapIfGreater(keys, comparer!, 0, 1);
                    SwapIfGreater(keys, comparer!, 0, 2);
                    SwapIfGreater(keys, comparer!, 1, 2);
                    return;
                }

                InsertionSort(keys[..partitionSize], comparer!);
                return;
            }

            if (depthLimit == 0)
            {
                HeapSort(keys[..partitionSize], comparer!);
                return;
            }
            depthLimit--;

            var p = PickPivotAndPartition(keys[..partitionSize], comparer!);

            // Note we've already partitioned around the pivot and do not have to move the pivot again.
            IntroSort(keys[(p + 1)..partitionSize], depthLimit, comparer!);
            partitionSize = p;
        }
    }

    private static int PickPivotAndPartition(SegmentedArraySegment<T> keys, Comparison<T> comparer)
    {
        AkburaDebug.Assert(keys.Length >= SegmentedArrayHelper.IntrosortSizeThreshold);
        AkburaDebug.Assert(comparer != null);

        var hi = keys.Length - 1;

        // Compute median-of-three.  But also partition them, since we've done the comparison.
        var middle = hi >> 1;

        // Sort lo, mid and hi appropriately, then pick mid as the pivot.
        SwapIfGreater(keys, comparer!, 0, middle);  // swap the low with the mid point
        SwapIfGreater(keys, comparer!, 0, hi);   // swap the low with the high
        SwapIfGreater(keys, comparer!, middle, hi); // swap the middle with the high

        var pivot = keys[middle];
        Swap(keys, middle, hi - 1);
        int left = 0, right = hi - 1;  // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.

        while (left < right)
        {
            while (comparer!(keys[++left], pivot) < 0)
            {
                // Intentionally empty
            }

            while (comparer(pivot, keys[--right]) < 0)
            {
                // Intentionally empty
            }

            if (left >= right)
            {
                break;
            }

            Swap(keys, left, right);
        }

        // Put pivot in the right location.
        if (left != hi - 1)
        {
            Swap(keys, left, hi - 1);
        }
        return left;
    }

    private static void HeapSort(SegmentedArraySegment<T> keys, Comparison<T> comparer)
    {
        AkburaDebug.Assert(comparer != null);
        AkburaDebug.Assert(keys.Length > 0);

        var n = keys.Length;
        for (var i = n >> 1; i >= 1; i--)
        {
            DownHeap(keys, i, n, comparer!);
        }

        for (var i = n; i > 1; i--)
        {
            Swap(keys, 0, i - 1);
            DownHeap(keys, 1, i - 1, comparer!);
        }
    }

    private static void DownHeap(SegmentedArraySegment<T> keys, int i, int n, Comparison<T> comparer)
    {
        AkburaDebug.Assert(comparer != null);

        var d = keys[i - 1];
        while (i <= n >> 1)
        {
            var child = 2 * i;
            if (child < n && comparer!(keys[child - 1], keys[child]) < 0)
            {
                child++;
            }

            if (!(comparer!(d, keys[child - 1]) < 0))
            {
                break;
            }

            keys[i - 1] = keys[child - 1];
            i = child;
        }

        keys[i - 1] = d;
    }

    private static void InsertionSort(SegmentedArraySegment<T> keys, Comparison<T> comparer)
    {
        for (var i = 0; i < keys.Length - 1; i++)
        {
            var t = keys[i + 1];

            var j = i;
            while (j >= 0 && comparer(t, keys[j]) < 0)
            {
                keys[j + 1] = keys[j];
                j--;
            }

            keys[j + 1] = t;
        }
    }
}

public static class SegmentedGenericArraySortHelper<T>
    where T : IComparable<T>
{
    public static void Sort(SegmentedArraySegment<T> keys, IComparer<T>? comparer)
    {
        try
        {
            if (comparer == null || comparer == Comparer<T>.Default)
            {
                if (keys.Length > 1)
                {
                    // For floating-point, do a pre-pass to move all NaNs to the beginning
                    // so that we can do an optimized comparison as part of the actual sort
                    // on the remainder of the values.
                    if (typeof(T) == typeof(double)
                        || typeof(T) == typeof(float))
                    {
                        var nanLeft = SegmentedArraySortUtils.MoveNansToFront(keys, default(Span<byte>));
                        if (nanLeft == keys.Length)
                        {
                            return;
                        }
                        keys = keys[nanLeft..];
                    }

                    IntroSort(keys, 2 * (SegmentedArraySortUtils.Log2((uint)keys.Length) + 1));
                }
            }
            else
            {
                SegmentedArraySortHelper<T>.IntrospectiveSort(keys, comparer.Compare);
            }
        }
        catch (IndexOutOfRangeException)
        {
            ThrowHelper.BadComparer(comparer);
        }
        catch (Exception e)
        {
            ThrowHelper.IComparerFailed(e);
        }
    }


    public static int BinarySearch(SegmentedArray<T> array, int index, int length, T value, IComparer<T>? comparer)
    {
        AkburaDebug.Assert(index >= 0 && length >= 0 && (array.Length - index >= length), "Check the arguments in the caller!");

        try
        {
            if (comparer == null || comparer == Comparer<T>.Default)
            {
                return BinarySearch(array, index, length, value);
            }
            else
            {
                return SegmentedArraySortHelper<T>.InternalBinarySearch(array, index, length, value, comparer);
            }
        }
        catch (Exception e)
        {
            ThrowHelper.IComparerFailed(e);
            return 0;
        }
    }


    // This function is called when the user doesn't specify any comparer.
    // Since T is constrained here, we can call IComparable<T>.CompareTo here.
    // We can avoid boxing for value type and casting for reference types.
    private static int BinarySearch(SegmentedArray<T> array, int index, int length, T value)
    {
        var lo = index;
        var hi = index + length - 1;
        while (lo <= hi)
        {
            var i = lo + ((hi - lo) >> 1);
            int order;
            if (array[i] == null)
            {
                order = (value == null) ? 0 : -1;
            }
            else
            {
                order = array[i].CompareTo(value!);
            }

            if (order == 0)
            {
                return i;
            }

            if (order < 0)
            {
                lo = i + 1;
            }
            else
            {
                hi = i - 1;
            }
        }

        return ~lo;
    }

    /// <summary>Swaps the values in the two references if the first is greater than the second.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SwapIfGreater(ref T i, ref T j)
    {
        if (i != null && GreaterThan(ref i, ref j))
        {
            Swap(ref i, ref j);
        }
    }

    /// <summary>Swaps the values in the two references, regardless of whether the two references are the same.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Swap(ref T i, ref T j)
    {
        AkburaDebug.Assert(!Unsafe.AreSame(ref i, ref j));

        (j, i) = (i, j);
    }

    // IntroSort is recursive; block it from being inlined into itself as
    // this is currenly not profitable.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void IntroSort(SegmentedArraySegment<T> keys, int depthLimit)
    {
        AkburaDebug.Assert(keys.Length > 0);
        AkburaDebug.Assert(depthLimit >= 0);

        var partitionSize = keys.Length;
        while (partitionSize > 1)
        {
            if (partitionSize <= SegmentedArrayHelper.IntrosortSizeThreshold)
            {
                if (partitionSize == 2)
                {
                    SwapIfGreater(ref keys[0], ref keys[1]);
                    return;
                }

                if (partitionSize == 3)
                {
                    ref var hiRef = ref keys[2];
                    ref var him1Ref = ref keys[1];
                    ref var loRef = ref keys[0];

                    SwapIfGreater(ref loRef, ref him1Ref);
                    SwapIfGreater(ref loRef, ref hiRef);
                    SwapIfGreater(ref him1Ref, ref hiRef);
                    return;
                }

                InsertionSort(keys[..partitionSize]);
                return;
            }

            if (depthLimit == 0)
            {
                HeapSort(keys[..partitionSize]);
                return;
            }
            depthLimit--;

            var p = PickPivotAndPartition(keys[..partitionSize]);

            // Note we've already partitioned around the pivot and do not have to move the pivot again.
            IntroSort(keys[(p + 1)..partitionSize], depthLimit);
            partitionSize = p;
        }
    }

    private static int PickPivotAndPartition(SegmentedArraySegment<T> keys)
    {
        AkburaDebug.Assert(keys.Length >= SegmentedArrayHelper.IntrosortSizeThreshold);

        // Use median-of-three to select a pivot. Grab a reference to the 0th, Length-1th, and Length/2th elements, and sort them.
        var zeroIndex = 0;
        var lastIndex = keys.Length - 1;
        var middleIndex = (keys.Length - 1) >> 1;
        SwapIfGreater(ref keys[zeroIndex], ref keys[middleIndex]);
        SwapIfGreater(ref keys[zeroIndex], ref keys[lastIndex]);
        SwapIfGreater(ref keys[middleIndex], ref keys[lastIndex]);

        // Select the middle value as the pivot, and move it to be just before the last element.
        var nextToLastIndex = keys.Length - 2;
        var pivot = keys[middleIndex];
        Swap(ref keys[middleIndex], ref keys[nextToLastIndex]);

        // Walk the left and right pointers, swapping elements as necessary, until they cross.
        int leftIndex = zeroIndex, rightIndex = nextToLastIndex;
        while (leftIndex < rightIndex)
        {
            if (pivot == null)
            {
                while (leftIndex < nextToLastIndex && keys[++leftIndex] == null)
                {
                    // Intentionally empty
                }

                while (rightIndex > zeroIndex && keys[--rightIndex] != null)
                {
                    // Intentionally empty
                }
            }
            else
            {
                while (leftIndex < nextToLastIndex && GreaterThan(ref pivot, ref keys[++leftIndex]))
                {
                    // Intentionally empty
                }

                while (rightIndex > zeroIndex && LessThan(ref pivot, ref keys[--rightIndex]))
                {
                    // Intentionally empty
                }
            }

            if (leftIndex >= rightIndex)
            {
                break;
            }

            Swap(ref keys[leftIndex], ref keys[rightIndex]);
        }

        // Put the pivot in the correct location.
        if (leftIndex != nextToLastIndex)
        {
            Swap(ref keys[leftIndex], ref keys[nextToLastIndex]);
        }

        return leftIndex;
    }

    private static void HeapSort(SegmentedArraySegment<T> keys)
    {
        AkburaDebug.Assert(keys.Length > 0);

        var n = keys.Length;
        for (var i = n >> 1; i >= 1; i--)
        {
            DownHeap(keys, i, n);
        }

        for (var i = n; i > 1; i--)
        {
            Swap(ref keys[0], ref keys[i - 1]);
            DownHeap(keys, 1, i - 1);
        }
    }

    private static void DownHeap(SegmentedArraySegment<T> keys, int i, int n)
    {
        var d = keys[i - 1];
        while (i <= n >> 1)
        {
            var child = 2 * i;
            if (child < n && (keys[child - 1] == null || LessThan(ref keys[child - 1], ref keys[child])))
            {
                child++;
            }

            if (keys[child - 1] == null || !LessThan(ref d, ref keys[child - 1]))
            {
                break;
            }

            keys[i - 1] = keys[child - 1];
            i = child;
        }

        keys[i - 1] = d;
    }

    private static void InsertionSort(SegmentedArraySegment<T> keys)
    {
        for (var i = 0; i < keys.Length - 1; i++)
        {
            var t = keys[i + 1];

            var j = i;
            while (j >= 0 && (t == null || LessThan(ref t, ref keys[j])))
            {
                keys[j + 1] = keys[j];
                j--;
            }

            keys[j + 1] = t!;
        }
    }

    // - These methods exist for use in sorting, where the additional operations present in
    //   the CompareTo methods that would otherwise be used on these primitives add non-trivial overhead,
    //   in particular for floating point where the CompareTo methods need to factor in NaNs.
    // - The floating-point comparisons here assume no NaNs, which is valid only because the sorting routines
    //   themselves special-case NaN with a pre-pass that ensures none are present in the values being sorted
    //   by moving them all to the front first and then sorting the rest.
    // - These are duplicated here rather than being on a helper type due to current limitations around generic inlining.

    [MethodImpl(MethodImplOptions.AggressiveInlining)] // compiles to a single comparison or method call
    private static bool LessThan(ref T left, ref T right)
    {
        if (typeof(T) == typeof(byte))
        {
            return (byte)(object)left < (byte)(object)right;
        }
        if (typeof(T) == typeof(sbyte))
        {
            return (sbyte)(object)left < (sbyte)(object)right;
        }
        if (typeof(T) == typeof(ushort))
        {
            return (ushort)(object)left < (ushort)(object)right;
        }
        if (typeof(T) == typeof(short))
        {
            return (short)(object)left < (short)(object)right;
        }
        if (typeof(T) == typeof(uint))
        {
            return (uint)(object)left < (uint)(object)right;
        }
        if (typeof(T) == typeof(int))
        {
            return (int)(object)left < (int)(object)right;
        }
        if (typeof(T) == typeof(ulong))
        {
            return (ulong)(object)left < (ulong)(object)right;
        }
        if (typeof(T) == typeof(long))
        {
            return (long)(object)left < (long)(object)right;
        }
        if (typeof(T) == typeof(UIntPtr))
        {
            return (nuint)(object)left < (nuint)(object)right;
        }
        if (typeof(T) == typeof(IntPtr))
        {
            return (nint)(object)left < (nint)(object)right;
        }
        if (typeof(T) == typeof(float))
        {
            return (float)(object)left < (float)(object)right;
        }
        if (typeof(T) == typeof(double))
        {
            return (double)(object)left < (double)(object)right;
        }

        return left.CompareTo(right) < 0;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)] // compiles to a single comparison or method call
    private static bool GreaterThan(ref T left, ref T right)
    {
        if (typeof(T) == typeof(byte))
        {
            return (byte)(object)left > (byte)(object)right;
        }
        if (typeof(T) == typeof(sbyte))
        {
            return (sbyte)(object)left > (sbyte)(object)right;
        }
        if (typeof(T) == typeof(ushort))
        {
            return (ushort)(object)left > (ushort)(object)right;
        }
        if (typeof(T) == typeof(short))
        {
            return (short)(object)left > (short)(object)right;
        }
        if (typeof(T) == typeof(uint))
        {
            return (uint)(object)left > (uint)(object)right;
        }
        if (typeof(T) == typeof(int))
        {
            return (int)(object)left > (int)(object)right;
        }
        if (typeof(T) == typeof(ulong))
        {
            return (ulong)(object)left > (ulong)(object)right;
        }
        if (typeof(T) == typeof(long))
        {
            return (long)(object)left > (long)(object)right;
        }
        if (typeof(T) == typeof(UIntPtr))
        {
            return (nuint)(object)left > (nuint)(object)right;
        }
        if (typeof(T) == typeof(IntPtr))
        {
            return (nint)(object)left > (nint)(object)right;
        }
        if (typeof(T) == typeof(float))
        {
            return (float)(object)left > (float)(object)right;
        }
        if (typeof(T) == typeof(double))
        {
            return (double)(object)left > (double)(object)right;
        }

        return left.CompareTo(right) > 0;
    }

}

#endregion

#region ArraySortHelper for paired key and value arrays

public static class SegmentedArraySortHelper<TKey, TValue>
{
    public static void Sort(SegmentedArraySegment<TKey> keys, Span<TValue> values, IComparer<TKey>? comparer)
    {
        // Add a try block here to detect IComparers (or their
        // underlying IComparables, etc) that are bogus.
        try
        {
            IntrospectiveSort(keys, values, comparer ?? Comparer<TKey>.Default);
        }
        catch (IndexOutOfRangeException)
        {
            ThrowHelper.BadComparer(comparer);
        }
        catch (Exception e)
        {
            ThrowHelper.IComparerFailed(e);
        }
    }


    private static void SwapIfGreaterWithValues(SegmentedArraySegment<TKey> keys, Span<TValue> values, IComparer<TKey> comparer, int i, int j)
    {
        AkburaDebug.Assert(comparer != null);
        AkburaDebug.Assert(0 <= i && i < keys.Length && i < values.Length);
        AkburaDebug.Assert(0 <= j && j < keys.Length && j < values.Length);
        AkburaDebug.Assert(i != j);

        if (comparer!.Compare(keys[i], keys[j]) > 0)
        {
            (keys[j], keys[i]) = (keys[i], keys[j]);
            (values[j], values[i]) = (values[i], values[j]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Swap(SegmentedArraySegment<TKey> keys, Span<TValue> values, int i, int j)
    {
        AkburaDebug.Assert(i != j);

        (keys[j], keys[i]) = (keys[i], keys[j]);
        (values[j], values[i]) = (values[i], values[j]);
    }

    public static void IntrospectiveSort(SegmentedArraySegment<TKey> keys, Span<TValue> values, IComparer<TKey> comparer)
    {
        AkburaDebug.Assert(comparer != null);
        AkburaDebug.Assert(keys.Length == values.Length);

        if (keys.Length > 1)
        {
            IntroSort(keys, values, 2 * (SegmentedArraySortUtils.Log2((uint)keys.Length) + 1), comparer!);
        }
    }

    private static void IntroSort(SegmentedArraySegment<TKey> keys, Span<TValue> values, int depthLimit, IComparer<TKey> comparer)
    {
        AkburaDebug.Assert(keys.Length > 0);
        AkburaDebug.Assert(values.Length == keys.Length);
        AkburaDebug.Assert(depthLimit >= 0);
        AkburaDebug.Assert(comparer != null);

        var partitionSize = keys.Length;
        while (partitionSize > 1)
        {
            if (partitionSize <= SegmentedArrayHelper.IntrosortSizeThreshold)
            {

                if (partitionSize == 2)
                {
                    SwapIfGreaterWithValues(keys, values, comparer!, 0, 1);
                    return;
                }

                if (partitionSize == 3)
                {
                    SwapIfGreaterWithValues(keys, values, comparer!, 0, 1);
                    SwapIfGreaterWithValues(keys, values, comparer!, 0, 2);
                    SwapIfGreaterWithValues(keys, values, comparer!, 1, 2);
                    return;
                }

                InsertionSort(keys[..partitionSize], values[..partitionSize], comparer!);
                return;
            }

            if (depthLimit == 0)
            {
                HeapSort(keys[..partitionSize], values[..partitionSize], comparer!);
                return;
            }
            depthLimit--;

            var p = PickPivotAndPartition(keys[..partitionSize], values[..partitionSize], comparer!);

            // Note we've already partitioned around the pivot and do not have to move the pivot again.
            IntroSort(keys[(p + 1)..partitionSize], values[(p + 1)..partitionSize], depthLimit, comparer!);
            partitionSize = p;
        }
    }

    private static int PickPivotAndPartition(SegmentedArraySegment<TKey> keys, Span<TValue> values, IComparer<TKey> comparer)
    {
        AkburaDebug.Assert(keys.Length >= SegmentedArrayHelper.IntrosortSizeThreshold);
        AkburaDebug.Assert(comparer != null);

        var hi = keys.Length - 1;

        // Compute median-of-three.  But also partition them, since we've done the comparison.
        var middle = hi >> 1;

        // Sort lo, mid and hi appropriately, then pick mid as the pivot.
        SwapIfGreaterWithValues(keys, values, comparer!, 0, middle);  // swap the low with the mid point
        SwapIfGreaterWithValues(keys, values, comparer!, 0, hi);   // swap the low with the high
        SwapIfGreaterWithValues(keys, values, comparer!, middle, hi); // swap the middle with the high

        var pivot = keys[middle];
        Swap(keys, values, middle, hi - 1);
        int left = 0, right = hi - 1;  // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.

        while (left < right)
        {
            while (comparer!.Compare(keys[++left], pivot) < 0)
            {
                // Intentionally empty
            }

            while (comparer.Compare(pivot, keys[--right]) < 0)
            {
                // Intentionally empty
            }

            if (left >= right)
            {
                break;
            }

            Swap(keys, values, left, right);
        }

        // Put pivot in the right location.
        if (left != hi - 1)
        {
            Swap(keys, values, left, hi - 1);
        }
        return left;
    }

    private static void HeapSort(SegmentedArraySegment<TKey> keys, Span<TValue> values, IComparer<TKey> comparer)
    {
        AkburaDebug.Assert(comparer != null);
        AkburaDebug.Assert(keys.Length > 0);

        var n = keys.Length;
        for (var i = n >> 1; i >= 1; i--)
        {
            DownHeap(keys, values, i, n, comparer!);
        }

        for (var i = n; i > 1; i--)
        {
            Swap(keys, values, 0, i - 1);
            DownHeap(keys, values, 1, i - 1, comparer!);
        }
    }

    private static void DownHeap(SegmentedArraySegment<TKey> keys, Span<TValue> values, int i, int n, IComparer<TKey> comparer)
    {
        AkburaDebug.Assert(comparer != null);

        var d = keys[i - 1];
        var dValue = values[i - 1];

        while (i <= n >> 1)
        {
            var child = 2 * i;
            if (child < n && comparer!.Compare(keys[child - 1], keys[child]) < 0)
            {
                child++;
            }

            if (!(comparer!.Compare(d, keys[child - 1]) < 0))
            {
                break;
            }

            keys[i - 1] = keys[child - 1];
            values[i - 1] = values[child - 1];
            i = child;
        }

        keys[i - 1] = d;
        values[i - 1] = dValue;
    }

    private static void InsertionSort(SegmentedArraySegment<TKey> keys, Span<TValue> values, IComparer<TKey> comparer)
    {
        AkburaDebug.Assert(comparer != null);

        for (var i = 0; i < keys.Length - 1; i++)
        {
            var t = keys[i + 1];
            var tValue = values[i + 1];

            var j = i;
            while (j >= 0 && comparer!.Compare(t, keys[j]) < 0)
            {
                keys[j + 1] = keys[j];
                values[j + 1] = values[j];
                j--;
            }

            keys[j + 1] = t;
            values[j + 1] = tValue;
        }
    }
}

public static class SegmentedGenericArraySortHelper<TKey, TValue>
    where TKey : IComparable<TKey>
{
    public static void Sort(SegmentedArraySegment<TKey> keys, Span<TValue> values, IComparer<TKey>? comparer)
    {
        // Add a try block here to detect IComparers (or their
        // underlying IComparables, etc) that are bogus.
        try
        {
            if (comparer == null || comparer == Comparer<TKey>.Default)
            {
                if (keys.Length > 1)
                {
                    // For floating-point, do a pre-pass to move all NaNs to the beginning
                    // so that we can do an optimized comparison as part of the actual sort
                    // on the remainder of the values.
                    if (typeof(TKey) == typeof(double)
                        || typeof(TKey) == typeof(float))
                    {
                        var nanLeft = SegmentedArraySortUtils.MoveNansToFront(keys, values);
                        if (nanLeft == keys.Length)
                        {
                            return;
                        }
                        keys = keys[nanLeft..];
                        values = values[nanLeft..];
                    }

                    IntroSort(keys, values, 2 * (SegmentedArraySortUtils.Log2((uint)keys.Length) + 1));
                }
            }
            else
            {
                SegmentedArraySortHelper<TKey, TValue>.IntrospectiveSort(keys, values, comparer);
            }
        }
        catch (IndexOutOfRangeException)
        {
            ThrowHelper.BadComparer(comparer);
        }
        catch (Exception e)
        {
            ThrowHelper.IComparerFailed(e);
        }
    }


    private static void SwapIfGreaterWithValues(SegmentedArraySegment<TKey> keys, Span<TValue> values, int i, int j)
    {
        AkburaDebug.Assert(i != j);

        ref var keyRef = ref keys[i];
        if (keyRef != null && GreaterThan(ref keyRef, ref keys[j]))
        {
            var key = keyRef;
            keys[i] = keys[j];
            keys[j] = key;

            (values[j], values[i]) = (values[i], values[j]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Swap(SegmentedArraySegment<TKey> keys, Span<TValue> values, int i, int j)
    {
        AkburaDebug.Assert(i != j);

        (keys[j], keys[i]) = (keys[i], keys[j]);
        (values[j], values[i]) = (values[i], values[j]);
    }

    private static void IntroSort(SegmentedArraySegment<TKey> keys, Span<TValue> values, int depthLimit)
    {
        AkburaDebug.Assert(keys.Length > 0);
        AkburaDebug.Assert(values.Length == keys.Length);
        AkburaDebug.Assert(depthLimit >= 0);

        var partitionSize = keys.Length;
        while (partitionSize > 1)
        {
            if (partitionSize <= SegmentedArrayHelper.IntrosortSizeThreshold)
            {

                if (partitionSize == 2)
                {
                    SwapIfGreaterWithValues(keys, values, 0, 1);
                    return;
                }

                if (partitionSize == 3)
                {
                    SwapIfGreaterWithValues(keys, values, 0, 1);
                    SwapIfGreaterWithValues(keys, values, 0, 2);
                    SwapIfGreaterWithValues(keys, values, 1, 2);
                    return;
                }

                InsertionSort(keys[..partitionSize], values[..partitionSize]);
                return;
            }

            if (depthLimit == 0)
            {
                HeapSort(keys[..partitionSize], values[..partitionSize]);
                return;
            }
            depthLimit--;

            var p = PickPivotAndPartition(keys[..partitionSize], values[..partitionSize]);

            // Note we've already partitioned around the pivot and do not have to move the pivot again.
            IntroSort(keys[(p + 1)..partitionSize], values[(p + 1)..partitionSize], depthLimit);
            partitionSize = p;
        }
    }

    private static int PickPivotAndPartition(SegmentedArraySegment<TKey> keys, Span<TValue> values)
    {
        AkburaDebug.Assert(keys.Length >= SegmentedArrayHelper.IntrosortSizeThreshold);

        var hi = keys.Length - 1;

        // Compute median-of-three.  But also partition them, since we've done the comparison.
        var middle = hi >> 1;

        // Sort lo, mid and hi appropriately, then pick mid as the pivot.
        SwapIfGreaterWithValues(keys, values, 0, middle);  // swap the low with the mid point
        SwapIfGreaterWithValues(keys, values, 0, hi);   // swap the low with the high
        SwapIfGreaterWithValues(keys, values, middle, hi); // swap the middle with the high

        var pivot = keys[middle];
        Swap(keys, values, middle, hi - 1);
        int left = 0, right = hi - 1;  // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.

        while (left < right)
        {
            if (pivot == null)
            {
                while (left < (hi - 1) && keys[++left] == null)
                {
                    // Intentionally empty
                }

                while (right > 0 && keys[--right] != null)
                {
                    // Intentionally empty
                }
            }
            else
            {
                while (GreaterThan(ref pivot, ref keys[++left]))
                {
                    // Intentionally empty
                }

                while (LessThan(ref pivot, ref keys[--right]))
                {
                    // Intentionally empty
                }
            }

            if (left >= right)
            {
                break;
            }

            Swap(keys, values, left, right);
        }

        // Put pivot in the right location.
        if (left != hi - 1)
        {
            Swap(keys, values, left, hi - 1);
        }
        return left;
    }

    private static void HeapSort(SegmentedArraySegment<TKey> keys, Span<TValue> values)
    {
        AkburaDebug.Assert(keys.Length > 0);

        var n = keys.Length;
        for (var i = n >> 1; i >= 1; i--)
        {
            DownHeap(keys, values, i, n);
        }

        for (var i = n; i > 1; i--)
        {
            Swap(keys, values, 0, i - 1);
            DownHeap(keys, values, 1, i - 1);
        }
    }

    private static void DownHeap(SegmentedArraySegment<TKey> keys, Span<TValue> values, int i, int n)
    {
        var d = keys[i - 1];
        var dValue = values[i - 1];

        while (i <= n >> 1)
        {
            var child = 2 * i;
            if (child < n && (keys[child - 1] == null || LessThan(ref keys[child - 1], ref keys[child])))
            {
                child++;
            }

            if (keys[child - 1] == null || !LessThan(ref d, ref keys[child - 1]))
            {
                break;
            }

            keys[i - 1] = keys[child - 1];
            values[i - 1] = values[child - 1];
            i = child;
        }

        keys[i - 1] = d;
        values[i - 1] = dValue;
    }

    private static void InsertionSort(SegmentedArraySegment<TKey> keys, Span<TValue> values)
    {
        for (var i = 0; i < keys.Length - 1; i++)
        {
            var t = keys[i + 1];
            var tValue = values[i + 1];

            var j = i;
            while (j >= 0 && (t == null || LessThan(ref t, ref keys[j])))
            {
                keys[j + 1] = keys[j];
                values[j + 1] = values[j];
                j--;
            }

            keys[j + 1] = t!;
            values[j + 1] = tValue;
        }
    }

    // - These methods exist for use in sorting, where the additional operations present in
    //   the CompareTo methods that would otherwise be used on these primitives add non-trivial overhead,
    //   in particular for floating point where the CompareTo methods need to factor in NaNs.
    // - The floating-point comparisons here assume no NaNs, which is valid only because the sorting routines
    //   themselves special-case NaN with a pre-pass that ensures none are present in the values being sorted
    //   by moving them all to the front first and then sorting the rest.
    // - These are duplicated here rather than being on a helper type due to current limitations around generic inlining.

    [MethodImpl(MethodImplOptions.AggressiveInlining)] // compiles to a single comparison or method call
    private static bool LessThan(ref TKey left, ref TKey right)
    {
        if (typeof(TKey) == typeof(byte))
        {
            return (byte)(object)left < (byte)(object)right;
        }
        if (typeof(TKey) == typeof(sbyte))
        {
            return (sbyte)(object)left < (sbyte)(object)right;
        }
        if (typeof(TKey) == typeof(ushort))
        {
            return (ushort)(object)left < (ushort)(object)right;
        }
        if (typeof(TKey) == typeof(short))
        {
            return (short)(object)left < (short)(object)right;
        }
        if (typeof(TKey) == typeof(uint))
        {
            return (uint)(object)left < (uint)(object)right;
        }
        if (typeof(TKey) == typeof(int))
        {
            return (int)(object)left < (int)(object)right;
        }
        if (typeof(TKey) == typeof(ulong))
        {
            return (ulong)(object)left < (ulong)(object)right;
        }
        if (typeof(TKey) == typeof(long))
        {
            return (long)(object)left < (long)(object)right;
        }
        if (typeof(TKey) == typeof(UIntPtr))
        {
            return (nuint)(object)left < (nuint)(object)right;
        }
        if (typeof(TKey) == typeof(IntPtr))
        {
            return (nint)(object)left < (nint)(object)right;
        }
        if (typeof(TKey) == typeof(float))
        {
            return (float)(object)left < (float)(object)right;
        }
        if (typeof(TKey) == typeof(double))
        {
            return (double)(object)left < (double)(object)right;
        }

        return left.CompareTo(right) < 0;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)] // compiles to a single comparison or method call
    private static bool GreaterThan(ref TKey left, ref TKey right)
    {
        if (typeof(TKey) == typeof(byte))
        {
            return (byte)(object)left > (byte)(object)right;
        }
        if (typeof(TKey) == typeof(sbyte))
        {
            return (sbyte)(object)left > (sbyte)(object)right;
        }
        if (typeof(TKey) == typeof(ushort))
        {
            return (ushort)(object)left > (ushort)(object)right;
        }
        if (typeof(TKey) == typeof(short))
        {
            return (short)(object)left > (short)(object)right;
        }
        if (typeof(TKey) == typeof(uint))
        {
            return (uint)(object)left > (uint)(object)right;
        }
        if (typeof(TKey) == typeof(int))
        {
            return (int)(object)left > (int)(object)right;
        }
        if (typeof(TKey) == typeof(ulong))
        {
            return (ulong)(object)left > (ulong)(object)right;
        }
        if (typeof(TKey) == typeof(long))
        {
            return (long)(object)left > (long)(object)right;
        }
        if (typeof(TKey) == typeof(UIntPtr))
        {
            return (nuint)(object)left > (nuint)(object)right;
        }
        if (typeof(TKey) == typeof(IntPtr))
        {
            return (nint)(object)left > (nint)(object)right;
        }
        if (typeof(TKey) == typeof(float))
        {
            return (float)(object)left > (float)(object)right;
        }
        if (typeof(TKey) == typeof(double))
        {
            return (double)(object)left > (double)(object)right;
        }

        return left.CompareTo(right) > 0;
    }

}

#endregion

/// <summary>Helper methods for use in array/span sorting routines.</summary>
public static class SegmentedArraySortUtils
{

    public static int MoveNansToFront<TKey, TValue>(SegmentedArraySegment<TKey> keys, Span<TValue> values) where TKey : notnull
    {
        AkburaDebug.Assert(typeof(TKey) == typeof(double) || typeof(TKey) == typeof(float));

        var left = 0;

        for (var i = 0; i < keys.Length; i++)
        {
            if ((typeof(TKey) == typeof(double) && double.IsNaN((double)(object)keys[i]))
                || (typeof(TKey) == typeof(float) && float.IsNaN((float)(object)keys[i]))
                )
            {
                (keys[i], keys[left]) = (keys[left], keys[i]);

                if ((uint)i < (uint)values.Length) // check to see if we have values
                {
                    (values[i], values[left]) = (values[left], values[i]);
                }

                left++;
            }
        }

        return left;
    }

    public static int Log2(uint value)
    {
        return BitOperations.Log2(value);
    }

}