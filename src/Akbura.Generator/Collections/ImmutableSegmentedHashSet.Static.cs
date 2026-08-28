// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Collections;

internal static class ImmutableSegmentedHashSet
{
    public static ImmutableSegmentedHashSet<T> Create<T>()
    {
        return ImmutableSegmentedHashSet<T>.Empty;
    }

    public static ImmutableSegmentedHashSet<T> Create<T>(T item)
    {
        return ImmutableSegmentedHashSet<T>.Empty.Add(item);
    }

    public static ImmutableSegmentedHashSet<T> Create<T>(params T[] items)
    {
        return ImmutableSegmentedHashSet<T>.Empty.Union(items);
    }

    public static ImmutableSegmentedHashSet<T> Create<T>(IEqualityComparer<T>? equalityComparer)
    {
        return ImmutableSegmentedHashSet<T>.Empty.WithComparer(equalityComparer);
    }

    public static ImmutableSegmentedHashSet<T> Create<T>(
        IEqualityComparer<T>? equalityComparer,
        T item)
    {
        return ImmutableSegmentedHashSet<T>.Empty.WithComparer(equalityComparer).Add(item);
    }

    public static ImmutableSegmentedHashSet<T> Create<T>(
        IEqualityComparer<T>? equalityComparer,
        params T[] items)
    {
        return ImmutableSegmentedHashSet<T>.Empty.WithComparer(equalityComparer).Union(items);
    }

    public static ImmutableSegmentedHashSet<T>.Builder CreateBuilder<T>()
    {
        return ImmutableSegmentedHashSet<T>.Empty.ToBuilder();
    }

    public static ImmutableSegmentedHashSet<T>.Builder CreateBuilder<T>(
        IEqualityComparer<T>? equalityComparer)
    {
        return ImmutableSegmentedHashSet<T>.Empty.WithComparer(equalityComparer).ToBuilder();
    }

    public static ImmutableSegmentedHashSet<T> CreateRange<T>(IEnumerable<T> items)
    {
        if (items is ImmutableSegmentedHashSet<T> existingSet)
        {
            return existingSet.WithComparer(null);
        }

        return ImmutableSegmentedHashSet<T>.Empty.Union(items);
    }

    public static ImmutableSegmentedHashSet<T> CreateRange<T>(
        IEqualityComparer<T>? equalityComparer,
        IEnumerable<T> items)
    {
        if (items is ImmutableSegmentedHashSet<T> existingSet)
        {
            return existingSet.WithComparer(equalityComparer);
        }

        return ImmutableSegmentedHashSet<T>.Empty.WithComparer(equalityComparer).Union(items);
    }

    public static ImmutableSegmentedHashSet<TSource> ToImmutableSegmentedHashSet<TSource>(
        this IEnumerable<TSource> source)
    {
        if (source is ImmutableSegmentedHashSet<TSource> existingSet)
        {
            return existingSet.WithComparer(null);
        }

        return ImmutableSegmentedHashSet<TSource>.Empty.Union(source);
    }

    public static ImmutableSegmentedHashSet<TSource> ToImmutableSegmentedHashSet<TSource>(
        this IEnumerable<TSource> source,
        IEqualityComparer<TSource>? equalityComparer)
    {
        if (source is ImmutableSegmentedHashSet<TSource> existingSet)
        {
            return existingSet.WithComparer(equalityComparer);
        }

        return ImmutableSegmentedHashSet<TSource>.Empty.WithComparer(equalityComparer).Union(source);
    }

    public static ImmutableSegmentedHashSet<TSource> ToImmutableSegmentedHashSet<TSource>(
        this ImmutableSegmentedHashSet<TSource>.Builder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.ToImmutable();
    }
}
