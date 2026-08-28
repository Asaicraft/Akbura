using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Akbura;

public static class ArrayExtensions
{
    public static ImmutableArray<T> ToImmutableArrayUnsafe<T>(this T[] array)
    {
        return Unsafe.As<T[], ImmutableArray<T>>(ref array);
    }

    public static T[] ToArrayUnsafe<T>(this ImmutableArray<T> array)
    {
        return Unsafe.As<ImmutableArray<T>, T[]>(ref array);
    }

    public static T[] Copy<T>(this T[] array, int start, int length)
    {
        // It's ok for 'start' to equal 'array.Length'.  In that case you'll
        // just get an empty array back.
        Debug.Assert(start >= 0);
        Debug.Assert(start <= array.Length);

        if (start + length > array.Length)
        {
            length = array.Length - start;
        }

        var newArray = new T[length];
        Array.Copy(array, start, newArray, 0, length);
        return newArray;
    }

    public static T[] InsertAt<T>(this T[] array, int position, T item)
    {
        var newArray = new T[array.Length + 1];
        if (position > 0)
        {
            Array.Copy(array, newArray, position);
        }

        if (position < array.Length)
        {
            Array.Copy(array, position, newArray, position + 1, array.Length - position);
        }

        newArray[position] = item;
        return newArray;
    }

    public static T[] Append<T>(this T[] array, T item)
    {
        return array.InsertAt(array.Length, item);
    }

    public static T[] InsertAt<T>(this T[] array, int position, T[] items)
    {
        var newArray = new T[array.Length + items.Length];
        if (position > 0)
        {
            Array.Copy(array, newArray, position);
        }

        if (position < array.Length)
        {
            Array.Copy(array, position, newArray, position + items.Length, array.Length - position);
        }

        items.CopyTo(newArray, position);
        return newArray;
    }

    public static T[] Append<T>(this T[] array, T[] items)
    {
        return array.InsertAt(array.Length, items);
    }

    public static T[] RemoveAt<T>(this T[] array, int position)
    {
        return array.RemoveAt(position, 1);
    }

    public static T[] RemoveAt<T>(this T[] array, int position, int length)
    {
        if (position + length > array.Length)
        {
            length = array.Length - position;
        }

        var newArray = new T[array.Length - length];
        if (position > 0)
        {
            Array.Copy(array, newArray, position);
        }

        if (position < newArray.Length)
        {
            Array.Copy(array, position + length, newArray, position, newArray.Length - position);
        }

        return newArray;
    }

    public static T[] ReplaceAt<T>(this T[] array, int position, T item)
    {
        var newArray = new T[array.Length];
        Array.Copy(array, newArray, array.Length);
        newArray[position] = item;
        return newArray;
    }

    public static T[] ReplaceAt<T>(this T[] array, int position, int length, T[] items)
    {
        return array.RemoveAt(position, length).InsertAt(position, items);
    }

    public static void ReverseContents<T>(this T[] array)
    {
        array.ReverseContents(0, array.Length);
    }

    public static void ReverseContents<T>(this T[] array, int start, int count)
    {
        var end = start + count - 1;
        for (int i = start, j = end; i < j; i++, j--)
        {
            (array[j], array[i]) = (array[i], array[j]);
        }
    }

    // same as Array.BinarySearch, but without using IComparer to compare ints
    public static int BinarySearch(this int[] array, int value)
    {
        var low = 0;
        var high = array.Length - 1;

        while (low <= high)
        {
            var middle = low + (high - low >> 1);
            var midValue = array[middle];

            if (midValue == value)
            {
                return middle;
            }
            else if (midValue > value)
            {
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        return ~low;
    }

    public static bool SequenceEqual<T>(this T[]? first, T[]? second, Func<T, T, bool> comparer)
    {
        AkburaDebug.Assert(comparer != null);

        if (first == second)
        {
            return true;
        }

        if (first == null || second == null || first.Length != second.Length)
        {
            return false;
        }

        for (var i = 0; i < first.Length; i++)
        {
            if (!comparer(first[i], second[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Search a sorted integer array for the target value in O(log N) time.
    /// </summary>
    /// <param name="array">The array of integers which must be sorted in ascending order.</param>
    /// <param name="value">The target value.</param>
    /// <returns>An index in the array pointing to the position where <paramref name="value"/> should be
    /// inserted in order to maintain the sorted order. All values to the right of this position will be
    /// strictly greater than <paramref name="value"/>. Note that this may return a position off the end
    /// of the array if all elements are less than or equal to <paramref name="value"/>.</returns>
    public static int BinarySearchUpperBound(this int[] array, int value)
    {
        var low = 0;
        var high = array.Length - 1;

        while (low <= high)
        {
            var middle = low + (high - low >> 1);
            if (array[middle] > value)
            {
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }
}