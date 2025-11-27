using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Collections;

public readonly partial struct SegmentedArray<T>
{
    /// <summary>
    /// Private helper class for use only by <see cref="SegmentedCollectionsMarshal"/>.
    /// </summary>
    public static class PrivateMarshal
    {
        /// <inheritdoc cref="SegmentedCollectionsMarshal.AsSegments{T}(SegmentedArray{T})"/>
        public static T[][] AsSegments(SegmentedArray<T> array)
            => array._items;

        public static SegmentedArray<T> AsSegmentedArray(int length, T[][] segments)
            => new(length, segments);
    }
}