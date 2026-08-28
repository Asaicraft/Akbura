using Akbura;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Collections;

public readonly struct SegmentedArraySegment<T>
{
    public SegmentedArray<T> Array { get; }
    public int Start { get; }
    public int Length { get; }

    public SegmentedArraySegment(SegmentedArray<T> array, int start, int length)
    {
        Array = array;
        Start = start;
        Length = length;
    }

    public ref T this[int index]
    {
        get
        {
            ThrowHelper.ThrowIfGreaterThanOrEqual((uint)index, (uint)Length);

            return ref Array[index + Start];
        }
    }

    public SegmentedArraySegment<T> Slice(int start)
    {
        if ((uint)start >= (uint)Length)
        {
            ThrowHelper.ThrowArgumentOutOfRangeException(nameof(start), "Start index is out of range.");
        }

        return new SegmentedArraySegment<T>(Array, Start + start, Length - start);
    }

    public SegmentedArraySegment<T> Slice(int start, int length)
    {
        // Since start and length are both 32-bit, their sum can be computed across a 64-bit domain
        // without loss of fidelity. The cast to uint before the cast to ulong ensures that the
        // extension from 32- to 64-bit is zero-extending rather than sign-extending. The end result
        // of this is that if either input is negative or if the input sum overflows past Int32.MaxValue,
        // that information is captured correctly in the comparison against the backing _length field.
        if ((uint)start + (ulong)(uint)length > (uint)Length)
        {
            ThrowHelper.ThrowArgumentOutOfRangeException(nameof(length), "Start index and length exceed the segment bounds.");
        }

        return new SegmentedArraySegment<T>(Array, Start + start, length);
    }
}