using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Collections;
public static partial class SpecializedCollections
{
    public static IEnumerable<T> SingletonEnumerable<T>(T value)
    {
        return new Singleton.List<T>(value);
    }

    private static partial class Singleton
    {
        public sealed class List<T> : IReadOnlyList<T>, IList<T>, IReadOnlyCollection<T>
        {
            private readonly T _loneValue;

            public List(T value)
            {
                _loneValue = value;
            }

            public void Add(T item)
            {
                ThrowHelper.ThrowNotSupportedException();
            }

            public void Clear()
            {
                ThrowHelper.ThrowNotSupportedException();
            }

            public bool Contains(T item)
            {
                return EqualityComparer<T>.Default.Equals(_loneValue, item);
            }

            public void CopyTo(T[] array, int arrayIndex)
            {
                array[arrayIndex] = _loneValue;
            }

            public int Count => 1;

            public bool IsReadOnly => true;

            public bool Remove(T item)
            {
                return ThrowHelper.ThrowNotSupportedException<bool>();
            }

            public IEnumerator<T> GetEnumerator()
            {
                return new Enumerator<T>(_loneValue);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public T this[int index]
            {
                get
                {
                    if (index != 0)
                    {
                        ThrowHelper.ThrowIndexOutOfRangeException();
                    }

                    return _loneValue;
                }

                set => ThrowHelper.ThrowNotSupportedException();
            }

            public int IndexOf(T item)
            {
                if (Equals(_loneValue, item))
                {
                    return 0;
                }

                return -1;
            }

            public void Insert(int index, T item)
            {
                ThrowHelper.ThrowNotSupportedException();
            }

            public void RemoveAt(int index)
            {
                ThrowHelper.ThrowNotSupportedException();
            }
        }

        public sealed class Enumerator<T> : IEnumerator<T>
        {
            private readonly T _loneValue;
            private bool _moveNextCalled;

            public Enumerator(T value)
            {
                _loneValue = value;
                _moveNextCalled = false;
            }

            public T Current => _loneValue;

            object? IEnumerator.Current => _loneValue;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (!_moveNextCalled)
                {
                    _moveNextCalled = true;
                    return true;
                }

                return false;
            }

            public void Reset()
            {
                _moveNextCalled = false;
            }
        }
    }
}
