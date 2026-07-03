// This file is adopted and ported from (dotnet/roslyn)
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using Akbura.Pools;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Akbura.Collections;

/// <summary>
/// Represents a single item or many items (including none).
/// </summary>
/// <remarks>
/// Used when a collection usually contains a single item but sometimes might contain multiple.
/// </remarks>
[CollectionBuilder(typeof(OneOrMany), methodName: "Create")]
[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
[DebuggerTypeProxy(typeof(OneOrMany<>.DebuggerProxy))]
internal readonly struct OneOrMany<T>
{
    public static readonly OneOrMany<T> Empty = new([]);

    private readonly T? _one;
    private readonly ImmutableArray<T> _many;

    public OneOrMany(T one)
    {
        _one = one;
        _many = default;
    }

    public OneOrMany(ImmutableArray<T> many)
    {
        if (many.IsDefault)
        {
            throw new ArgumentNullException(nameof(many));
        }

        if (many.Length == 1)
        {
            _one = many[0];
            _many = default;
        }
        else
        {
            _one = default;
            _many = many;
        }
    }

    /// <summary>
    /// True if the collection has a single item. This item is stored in <see cref="_one"/>.
    /// </summary>
    [MemberNotNullWhen(true, nameof(_one))]
    private bool HasOneItem
        => _many.IsDefault;

    public bool IsDefault
        => _one == null && _many.IsDefault;

    public T this[int index]
    {
        get
        {
            if (HasOneItem)
            {
                if (index != 0)
                {
                    throw new IndexOutOfRangeException();
                }

                return _one;
            }

            return _many[index];
        }
    }

    public int Count
        => HasOneItem ? 1 : _many.Length;

    public bool IsEmpty
        => Count == 0;

    public OneOrMany<T> Add(T item)
        => HasOneItem ? OneOrMany.Create(_one, item) :
           IsEmpty ? OneOrMany.Create(item) :
           OneOrMany.Create(_many.Add(item));

    public void AddRangeTo(ArrayBuilder<T> builder)
    {
        if (HasOneItem)
        {
            builder.Add(_one);
        }
        else
        {
            builder.AddRange(_many);
        }
    }

    public bool Contains(T item)
        => HasOneItem ? EqualityComparer<T>.Default.Equals(item, _one) : _many.Contains(item);

    public OneOrMany<T> RemoveAll(T item)
    {
        if (HasOneItem)
        {
            return EqualityComparer<T>.Default.Equals(item, _one) ? Empty : this;
        }

        var builder = ImmutableArray.CreateBuilder<T>(_many.Length);
        foreach (var value in _many)
        {
            if (!EqualityComparer<T>.Default.Equals(value, item))
            {
                builder.Add(value);
            }
        }

        return OneOrMany.Create(builder.ToImmutable());
    }

    public OneOrMany<TResult> Select<TResult>(Func<T, TResult> selector)
    {
        return HasOneItem
            ? OneOrMany.Create(selector(_one))
            : OneOrMany.Create(SelectAsArray(_many, selector));
    }

    public OneOrMany<TResult> Select<TResult, TArg>(Func<T, TArg, TResult> selector, TArg arg)
    {
        return HasOneItem
            ? OneOrMany.Create(selector(_one, arg))
            : OneOrMany.Create(SelectAsArray(_many, selector, arg));
    }

    public T First()
        => this[0];

    public T? FirstOrDefault()
        => HasOneItem ? _one : _many.FirstOrDefault();

    public T? FirstOrDefault(Func<T, bool> predicate)
    {
        if (HasOneItem)
        {
            return predicate(_one) ? _one : default;
        }

        return _many.FirstOrDefault(predicate);
    }

    public T? FirstOrDefault<TArg>(Func<T, TArg, bool> predicate, TArg arg)
    {
        if (HasOneItem)
        {
            return predicate(_one, arg) ? _one : default;
        }

        foreach (var item in _many)
        {
            if (predicate(item, arg))
            {
                return item;
            }
        }

        return default;
    }

    public static OneOrMany<T> CastUp<TDerived>(OneOrMany<TDerived> from)
        where TDerived : class, T
    {
        return from.HasOneItem
            ? new OneOrMany<T>(from._one)
            : new OneOrMany<T>(ImmutableArray<T>.CastUp(from._many));
    }

    public bool All(Func<T, bool> predicate)
        => HasOneItem ? predicate(_one) : _many.All(predicate);

    public bool All<TArg>(Func<T, TArg, bool> predicate, TArg arg)
    {
        if (HasOneItem)
        {
            return predicate(_one, arg);
        }

        foreach (var item in _many)
        {
            if (!predicate(item, arg))
            {
                return false;
            }
        }

        return true;
    }

    public bool Any()
        => !IsEmpty;

    public bool Any(Func<T, bool> predicate)
        => HasOneItem ? predicate(_one) : _many.Any(predicate);

    public bool Any<TArg>(Func<T, TArg, bool> predicate, TArg arg)
    {
        if (HasOneItem)
        {
            return predicate(_one, arg);
        }

        foreach (var item in _many)
        {
            if (predicate(item, arg))
            {
                return true;
            }
        }

        return false;
    }

    public ImmutableArray<T> ToImmutable()
        => HasOneItem ? [_one] : _many;

    public T[] ToArray()
        => HasOneItem ? [_one] : [.. _many];

    public bool SequenceEqual(OneOrMany<T> other, IEqualityComparer<T>? comparer = null)
    {
        comparer ??= EqualityComparer<T>.Default;

        if (Count != other.Count)
        {
            return false;
        }

        Debug.Assert(HasOneItem == other.HasOneItem);

        return HasOneItem
            ? comparer.Equals(_one, other._one!)
            : _many.SequenceEqual(other._many, comparer);
    }

    public bool SequenceEqual(ImmutableArray<T> other, IEqualityComparer<T>? comparer = null)
        => SequenceEqual(OneOrMany.Create(other), comparer);

    public bool SequenceEqual(IEnumerable<T> other, IEqualityComparer<T>? comparer = null)
    {
        comparer ??= EqualityComparer<T>.Default;

        if (!HasOneItem)
        {
            return _many.SequenceEqual(other, comparer);
        }

        var first = true;
        foreach (var otherItem in other)
        {
            if (!first || !comparer.Equals(_one, otherItem))
            {
                return false;
            }

            first = false;
        }

        return true;
    }

    public Enumerator GetEnumerator()
        => new(this);

    internal struct Enumerator
    {
        private readonly OneOrMany<T> _collection;
        private int _index;

        internal Enumerator(OneOrMany<T> collection)
        {
            _collection = collection;
            _index = -1;
        }

        public bool MoveNext()
        {
            _index++;
            return _index < _collection.Count;
        }

        public readonly T Current => _collection[_index];
    }

    private sealed class DebuggerProxy
    {
        private readonly OneOrMany<T> _instance;

        public DebuggerProxy(OneOrMany<T> instance)
        {
            _instance = instance;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Items => _instance.ToArray();
    }

    private string GetDebuggerDisplay()
        => "Count = " + Count;

    private static ImmutableArray<TResult> SelectAsArray<TResult>(
        ImmutableArray<T> items,
        Func<T, TResult> selector)
    {
        var builder = ImmutableArray.CreateBuilder<TResult>(items.Length);
        foreach (var item in items)
        {
            builder.Add(selector(item));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<TResult> SelectAsArray<TResult, TArg>(
        ImmutableArray<T> items,
        Func<T, TArg, TResult> selector,
        TArg arg)
    {
        var builder = ImmutableArray.CreateBuilder<TResult>(items.Length);
        foreach (var item in items)
        {
            builder.Add(selector(item, arg));
        }

        return builder.ToImmutable();
    }
}

internal static class OneOrMany
{
    public static OneOrMany<T> Create<T>(T one)
        => new(one);

    public static OneOrMany<T> Create<T>(T one, T two)
        => new(ImmutableArray.Create(one, two));

    public static OneOrMany<T> OneOrNone<T>(T? one)
        => one is null ? OneOrMany<T>.Empty : new OneOrMany<T>(one);

    public static OneOrMany<T> Create<T>(ImmutableArray<T> many)
        => new(many);

    public static OneOrMany<T> Create<T>(ReadOnlySpan<T> many)
    {
        switch (many.Length)
        {
            case 0:
                return OneOrMany<T>.Empty;
            case 1:
                return new OneOrMany<T>(many[0]);
            default:
            {
                var builder = ImmutableArray.CreateBuilder<T>(many.Length);
                for (var index = 0; index < many.Length; index++)
                {
                    builder.Add(many[index]);
                }

                return new OneOrMany<T>(builder.ToImmutable());
            }
        }
    }

    public static bool SequenceEqual<T>(
        this ImmutableArray<T> array,
        OneOrMany<T> other,
        IEqualityComparer<T>? comparer = null)
    {
        return Create(array).SequenceEqual(other, comparer);
    }

    public static bool SequenceEqual<T>(
        this IEnumerable<T> array,
        OneOrMany<T> other,
        IEqualityComparer<T>? comparer = null)
    {
        return other.SequenceEqual(array, comparer);
    }
}
