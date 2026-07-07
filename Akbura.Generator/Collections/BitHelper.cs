// This file is ported and adapted from the Roslyn (dotnet/roslyn)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Akbura.Collections;

internal ref struct BitHelper
{
    private const int IntSize = sizeof(int) * 8;
    private readonly Span<int> _span;

    internal BitHelper(Span<int> span, bool clear)
    {
        if (clear)
        {
            span.Clear();
        }
        _span = span;
    }

    internal readonly void MarkBit(int bitPosition)
    {
        Debug.Assert(bitPosition >= 0);

        uint bitArrayIndex = (uint)bitPosition / IntSize;

        // Workaround for https://github.com/dotnet/runtime/issues/72004
        Span<int> span = _span;
        if (bitArrayIndex < (uint)span.Length)
        {
            span[(int)bitArrayIndex] |= (1 << (int)((uint)bitPosition % IntSize));
        }
    }

    internal readonly bool IsMarked(int bitPosition)
    {
        Debug.Assert(bitPosition >= 0);

        uint bitArrayIndex = (uint)bitPosition / IntSize;

        // Workaround for https://github.com/dotnet/runtime/issues/72004
        Span<int> span = _span;
        return
            bitArrayIndex < (uint)span.Length &&
            (span[(int)bitArrayIndex] & (1 << ((int)((uint)bitPosition % IntSize)))) != 0;
    }

    /// <summary>How many ints must be allocated to represent n bits. Returns (n+31)/32, but avoids overflow.</summary>
    internal static int ToIntArrayLength(int n) => n > 0 ? ((n - 1) / IntSize + 1) : 0;
}