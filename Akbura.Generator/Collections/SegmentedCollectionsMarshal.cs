using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Collections;
/// <summary>
/// An unsafe class that provides a set of methods to access the underlying data representations of immutable segmented
/// collections.
/// </summary>
public static class SegmentedCollectionsMarshal
{
    /// <summary>
    /// Gets the backing storage array for a <see cref="SegmentedArray{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of elements stored in the array.</typeparam>
    /// <param name="array">The segmented array.</param>
    /// <returns>The backing storage array for the segmented array. Note that replacing segments within the returned
    /// value will invalidate the <see cref="SegmentedArray{T}"/> data structure.</returns>
    public static T[][] AsSegments<T>(SegmentedArray<T> array)
        => SegmentedArray<T>.PrivateMarshal.AsSegments(array);

    /// <summary>
    /// Gets a <see cref="SegmentedArray{T}"/> value wrapping the input T[][].
    /// </summary>
    /// <typeparam name="T">The type of elements in the input.</typeparam>
    /// <param name="length">The combined length of the input arrays</param>
    /// <param name="segments">The input array to wrap in the returned <see cref="SegmentedArray{T}"/> value.</param>
    /// <returns>A <see cref="SegmentedArray{T}"/> value wrapping <paramref name="segments"/>.</returns>
    /// <remarks>
    /// <para>
    /// When using this method, callers should take extra care to ensure that they're the sole owners of the input
    /// array, and that it won't be modified once the returned <see cref="SegmentedArray{T}"/> value starts
    /// being used. Doing so might cause undefined behavior in code paths which don't expect the contents of a given
    /// <see cref="SegmentedArray{T}"/> values to change outside their control.
    /// </para>
    /// </remarks>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="segments"/> is <see langword="null"/></exception>
    public static SegmentedArray<T> AsSegmentedArray<T>(int length, T[][] segments)
        => SegmentedArray<T>.PrivateMarshal.AsSegmentedArray(length, segments);
}