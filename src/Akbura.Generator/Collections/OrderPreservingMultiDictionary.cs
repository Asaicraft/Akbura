// This file is adopted and ported from (dotnet/roslyn)
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using Akbura.Pools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Akbura.Collections;

/// <summary>
/// A multidictionary that allows only adding and preserves the order of values added to the dictionary.
/// Thread-safe for reading, but not for adding.
/// </summary>
/// <remarks>
/// Always uses the default comparer.
/// </remarks>
internal sealed class OrderPreservingMultiDictionary<TKey, TValue> :
    IEnumerable<KeyValuePair<TKey, OrderPreservingMultiDictionary<TKey, TValue>.ValueSet>>
    where TKey : notnull
    where TValue : notnull
{
    private static readonly Dictionary<TKey, ValueSet> s_emptyDictionary = new();
    private static readonly ObjectPool<OrderPreservingMultiDictionary<TKey, TValue>> s_poolInstance = CreatePool();

    private readonly ObjectPool<OrderPreservingMultiDictionary<TKey, TValue>>? _pool;
    private Dictionary<TKey, ValueSet>? _dictionary;

    public OrderPreservingMultiDictionary()
    {
    }

    private OrderPreservingMultiDictionary(ObjectPool<OrderPreservingMultiDictionary<TKey, TValue>> pool)
    {
        _pool = pool;
    }

    public bool IsEmpty => _dictionary == null;

    public Dictionary<TKey, ValueSet>.KeyCollection Keys
        => _dictionary == null ? s_emptyDictionary.Keys : _dictionary.Keys;

    public ImmutableArray<TValue> this[TKey key]
    {
        get
        {
            if (_dictionary != null &&
                _dictionary.TryGetValue(key, out var valueSet))
            {
                Debug.Assert(valueSet.Count >= 1);
                return valueSet.Items;
            }

            return ImmutableArray<TValue>.Empty;
        }
    }

    public static ObjectPool<OrderPreservingMultiDictionary<TKey, TValue>> CreatePool()
    {
        ObjectPool<OrderPreservingMultiDictionary<TKey, TValue>>? pool = null;
        pool = new ObjectPool<OrderPreservingMultiDictionary<TKey, TValue>>(
            () => new OrderPreservingMultiDictionary<TKey, TValue>(pool!),
            size: 16);
        return pool;
    }

    public static OrderPreservingMultiDictionary<TKey, TValue> GetInstance()
    {
        var instance = s_poolInstance.Allocate();
        Debug.Assert(instance.IsEmpty);
        return instance;
    }

    public void Free()
    {
        if (_dictionary != null)
        {
            foreach (var keyValuePair in _dictionary)
            {
                keyValuePair.Value.Free();
            }

            _dictionary.Clear();
            _dictionary = null;
        }

        _pool?.Free(this);
    }

    public void Add(TKey key, TValue value)
    {
        if (_dictionary != null &&
            _dictionary.TryGetValue(key, out var valueSet))
        {
            Debug.Assert(valueSet.Count >= 1);
            _dictionary[key] = valueSet.WithAddedItem(value);
            return;
        }

        _dictionary ??= new Dictionary<TKey, ValueSet>();
        _dictionary[key] = new ValueSet(value);
    }

    public bool Contains(TKey key, TValue value)
    {
        return _dictionary != null &&
               _dictionary.TryGetValue(key, out var valueSet) &&
               valueSet.Contains(value);
    }

    public OneOrMany<TValue> GetAsOneOrMany(TKey key)
    {
        if (_dictionary != null &&
            _dictionary.TryGetValue(key, out var valueSet))
        {
            Debug.Assert(valueSet.Count >= 1);
            return valueSet.Count == 1
                ? OneOrMany.Create(valueSet[0])
                : OneOrMany.Create(valueSet.Items);
        }

        return OneOrMany<TValue>.Empty;
    }

    public Dictionary<TKey, ValueSet>.Enumerator GetEnumerator()
    {
        return _dictionary == null
            ? s_emptyDictionary.GetEnumerator()
            : _dictionary.GetEnumerator();
    }

    IEnumerator<KeyValuePair<TKey, ValueSet>> IEnumerable<KeyValuePair<TKey, ValueSet>>.GetEnumerator()
        => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    public readonly struct ValueSet : IEnumerable<TValue>
    {
        private readonly object _value;

        internal ValueSet(TValue value)
        {
            _value = value;
        }

        private ValueSet(ArrayBuilder<TValue> values)
        {
            _value = values;
        }

        internal int Count => (_value as ArrayBuilder<TValue>)?.Count ?? 1;

        internal ImmutableArray<TValue> Items
        {
            get
            {
                Debug.Assert(Count >= 1);

                if (_value is ArrayBuilder<TValue> builder)
                {
                    return builder.ToImmutable();
                }

                Debug.Assert(_value is TValue);
                return ImmutableArray.Create((TValue)_value);
            }
        }

        internal TValue this[int index]
        {
            get
            {
                Debug.Assert(Count >= 1);

                if (_value is ArrayBuilder<TValue> builder)
                {
                    return builder[index];
                }

                if (index == 0)
                {
                    return (TValue)_value;
                }

                throw new IndexOutOfRangeException();
            }
        }

        internal bool Contains(TValue item)
        {
            Debug.Assert(Count >= 1);

            return _value is ArrayBuilder<TValue> builder
                ? builder.Contains(item)
                : EqualityComparer<TValue>.Default.Equals(item, (TValue)_value);
        }

        internal ValueSet WithAddedItem(TValue item)
        {
            Debug.Assert(Count >= 1);

            if (_value is ArrayBuilder<TValue> builder)
            {
                builder.Add(item);
                return this;
            }

            var promoted = ArrayBuilder<TValue>.GetInstance(capacity: 2);
            promoted.Add((TValue)_value);
            promoted.Add(item);
            return new ValueSet(promoted);
        }

        internal void Free()
        {
            if (_value is ArrayBuilder<TValue> builder)
            {
                builder.Free();
            }
        }

        public Enumerator GetEnumerator()
            => new(this);

        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
            => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        public struct Enumerator : IEnumerator<TValue>
        {
            private readonly ValueSet _valueSet;
            private readonly int _count;
            private int _index;

            internal Enumerator(ValueSet valueSet)
            {
                _valueSet = valueSet;
                _count = valueSet.Count;
                Debug.Assert(_count >= 1);
                _index = -1;
            }

            public readonly TValue Current => _valueSet[_index];

            readonly object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                _index++;
                return _index < _count;
            }

            public void Reset()
            {
                _index = -1;
            }

            public void Dispose()
            {
            }
        }
    }
}

#nullable restore
