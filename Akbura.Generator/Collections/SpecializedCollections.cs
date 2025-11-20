using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Collections;
public static partial class SpecializedCollections
{
    public static IReadOnlyList<T> EmptyBoxedImmutableArray<T>()
    {
        return Empty.BoxedImmutableArray<T>.Instance;
    }
}
