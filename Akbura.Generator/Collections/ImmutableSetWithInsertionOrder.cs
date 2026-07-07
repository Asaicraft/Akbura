// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Collections;

internal sealed class ImmutableSetWithInsertionOrder<T> : IEnumerable<T>
    where T : notnull
{
    public static readonly ImmutableSetWithInsertionOrder<T> Empty =
        new(ImmutableDictionary.Create<T, uint>(), 0u);

    private readonly ImmutableDictionary<T, uint> _map;
    private readonly uint _nextElementValue;

    private ImmutableSetWithInsertionOrder(
        ImmutableDictionary<T, uint> map,
        uint nextElementValue)
    {
        _map = map;
        _nextElementValue = nextElementValue;
    }

    public int Count => _map.Count;

    public bool Contains(T value)
    {
        return _map.ContainsKey(value);
    }

    public ImmutableSetWithInsertionOrder<T> Add(T value)
    {
        if (_map.ContainsKey(value))
        {
            return this;
        }

        return new ImmutableSetWithInsertionOrder<T>(
            _map.Add(value, _nextElementValue),
            _nextElementValue + 1u);
    }

    public ImmutableSetWithInsertionOrder<T> AddRange(List<T> values)
    {
        ImmutableDictionary<T, uint>.Builder? builder = null;
        var nextElementValue = _nextElementValue;

        foreach (var value in values)
        {
            if (builder == null)
            {
                if (_map.ContainsKey(value))
                {
                    continue;
                }

                builder = _map.ToBuilder();
            }
            else if (builder.ContainsKey(value))
            {
                continue;
            }

            builder.Add(value, nextElementValue);
            nextElementValue++;
        }

        if (builder == null)
        {
            return this;
        }

        return new ImmutableSetWithInsertionOrder<T>(
            builder.ToImmutable(),
            nextElementValue);
    }

    public ImmutableSetWithInsertionOrder<T> Remove(T value)
    {
        var modifiedMap = _map.Remove(value);
        if (modifiedMap == _map)
        {
            return this;
        }

        return Count == 1
            ? Empty
            : new ImmutableSetWithInsertionOrder<T>(modifiedMap, _nextElementValue);
    }

    public ImmutableSetWithInsertionOrder<T> RemoveRange(List<T> values)
    {
        ImmutableDictionary<T, uint>.Builder? builder = null;

        foreach (var value in values)
        {
            if (builder == null)
            {
                if (!_map.ContainsKey(value))
                {
                    continue;
                }

                builder = _map.ToBuilder();
            }

            builder.Remove(value);
        }

        if (builder == null)
        {
            return this;
        }

        return new ImmutableSetWithInsertionOrder<T>(
            builder.ToImmutable(),
            _nextElementValue);
    }

    public IEnumerable<T> InInsertionOrder => _map.OrderBy(static kv => kv.Value).Select(static kv => kv.Key);

    public override string ToString()
    {
        return "{" + string.Join(", ", this) + "}";
    }

    public IEnumerator<T> GetEnumerator()
    {
        return _map.Keys.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _map.Keys.GetEnumerator();
    }
}
