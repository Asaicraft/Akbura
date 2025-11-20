using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Akbura;
public static class EmptyEnumerator
{
    public static IEnumerator<T> For<T>()
    {
        return EmptyEnumeratorImpl<T>.Instance;
    }

    private static class EmptyEnumeratorImpl<T>
    {
        public readonly static IEnumerator<T> Instance = Unsafe.As<IEnumerator<T>>(Array.Empty<T>().GetEnumerator());
    }
}
