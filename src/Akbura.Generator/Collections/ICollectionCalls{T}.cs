// This file is ported and adapted from the Roslyn (dotnet/roslyn)

using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Collections;

/// <summary>
/// Provides static methods to invoke <see cref="ICollection{T}"/> members on value types that explicitly implement
/// the member.
/// </summary>
/// <remarks>
/// Normally, invocation of explicit interface members requires boxing or copying the value type, which is
/// especially problematic for operations that mutate the value. Invocation through these helpers behaves like a
/// normal call to an implicitly implemented member.
/// </remarks>
internal static class ICollectionCalls<T>
{
    public static bool IsReadOnly<TCollection>(ref TCollection collection)
        where TCollection : ICollection<T>
        => collection.IsReadOnly;

    public static void Add<TCollection>(ref TCollection collection, T item)
        where TCollection : ICollection<T>
        => collection.Add(item);

    public static void CopyTo<TCollection>(ref TCollection collection, T[] array, int arrayIndex)
        where TCollection : ICollection<T>
        => collection.CopyTo(array, arrayIndex);
}