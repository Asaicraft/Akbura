using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Collections;

public static class RoslynImmutableInterlocked
{

    /// <summary>
    /// Reads from an ImmutableArray location, ensuring that a read barrier is inserted to prevent any subsequent reads from being reordered before this read.
    /// </summary>
    /// <remarks>
    /// This method is not intended to be used to provide write barriers.
    /// </remarks>
    public static ImmutableArray<T> VolatileRead<T>(ref readonly ImmutableArray<T> location)
    {
        var value = location;
        // When Volatile.ReadBarrier() is available in .NET 10, it can be used here.
        Interlocked.MemoryBarrier();
        return value;
    }

    /// <summary>
    /// Writes to an ImmutableArray location, ensuring that a write barrier is inserted to prevent any prior writes from being reordered after this write.
    /// </summary>
    /// <remarks>
    /// This method is not intended to be used to provide read barriers.
    /// </remarks>
    public static void VolatileWrite<T>(ref ImmutableArray<T> location, ImmutableArray<T> value)
    {
        // When Volatile.WriteBarrier() is available in .NET 10, it can be used here.
        Interlocked.MemoryBarrier();
        location = value;
    }
}