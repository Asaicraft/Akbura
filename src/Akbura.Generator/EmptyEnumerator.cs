using System;
using System.Collections.Generic;

namespace Akbura;
public static class EmptyEnumerator
{
    public static IEnumerator<T> For<T>()
    {
        return EmptyEnumeratorImpl<T>.Instance;
    }

    private static class EmptyEnumeratorImpl<T>
    {
        public readonly static IEnumerator<T> Instance = ((IEnumerable<T>)Array.Empty<T>()).GetEnumerator();
    }
}
