// This file is ported and adapted from the Roslyn (dotnet/roslyn)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Collections;

/// <summary>
/// Provides static methods to invoke <see cref="ICollection"/> members on value types that explicitly implement the
/// member.
/// </summary>
/// <remarks>
/// Normally, invocation of explicit interface members requires boxing or copying the value type, which is
/// especially problematic for operations that mutate the value. Invocation through these helpers behaves like a
/// normal call to an implicitly implemented member.
/// </remarks>
internal static class ICollectionCalls
{
    public static bool IsSynchronized<TCollection>(ref TCollection collection)
        where TCollection : ICollection
        => collection.IsSynchronized;

    public static void CopyTo<TCollection>(ref TCollection collection, Array array, int index)
        where TCollection : ICollection
    {
        collection.CopyTo(array, index);
    }
}