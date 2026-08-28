using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Collections;
public static partial class SpecializedCollections
{
    private partial class Empty
    {
        public static class BoxedImmutableArray<T>
        {
            // empty boxed immutable array
            public static readonly IReadOnlyList<T> Instance = [];
        }

        public class List<T> : Collection<T>, IList<T>, IReadOnlyList<T>
        {
            public static readonly List<T> Instance = new();

            protected List()
            {
            }

            public new int IndexOf(T item)
            {
                return -1;
            }

            public new void Insert(int index, T item)
            {
                throw new NotSupportedException();
            }

            public new void RemoveAt(int index)
            {
                throw new NotSupportedException();
            }

            public new T this[int index]
            {
                get => throw new ArgumentOutOfRangeException(nameof(index));

                set
                {
                    throw new NotSupportedException();
                }
            }
        }
    }
}
