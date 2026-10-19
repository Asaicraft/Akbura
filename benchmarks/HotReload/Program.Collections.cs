using Akbura.Collections;
using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.CodeGeneration;
using Akbura.Pools;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    private static object? s_collectionResult;
    private static readonly IComparer<DetachedCSharpTypeIdentity> CollectionTypeComparer = Comparer<DetachedCSharpTypeIdentity>.Create(static (left, right) =>
    {
        var assembly = StringComparer.Ordinal.Compare(left.AssemblyIdentity, right.AssemblyIdentity);
        return assembly != 0 ? assembly : StringComparer.Ordinal.Compare(left.MetadataName, right.MetadataName);
    });
    private static readonly IComparer<DetachedCSharpLookupDependency> CollectionLookupComparer = Comparer<DetachedCSharpLookupDependency>.Create(
        static (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));

    private static void MeasureCollections()
    {
        Write(new { Kind = "collection-provenance", GeneratorSha256 = FileDigest(typeof(DocumentSemanticDependencyCollector).Assembly.Location),
            ToolSha256 = FileDigest(typeof(Program).Assembly.Location), MaximumRetainedSetCount = PooledHashSet<string>.MaximumRetainedCount });
        foreach (var size in new[] { 0, 1, 2, 4, 8, 16, 32, 64, 128, 256, 1024 })
        {
            var values = Enumerable.Range(0, size).Reverse().Select(index => "value|\0é" + index.ToString("D4")).ToArray();
            var strings = new HashSet<string>(values, StringComparer.Ordinal);
            var types = values.Select(value => new DetachedCSharpTypeIdentity("assembly" + value.Length, value)).ToHashSet();
            var lookups = values.ToDictionary(value => value, value => new DetachedCSharpLookupDependency(value, value, value, "scope", "symbol", 0, [], [], false), StringComparer.Ordinal);
            var syntax = AkburaSyntaxTree.ParseText("<TextBlock />", filePath: "Fixture.akbura");
            var compilation = new AkburaCompilation(CSharpCompilation.Create("Fixture"), [syntax]) { CollectCSharpSemanticDependencies = true };
            var collector = new DocumentSemanticDependencyCollector(compilation.GetSemanticModel(syntax));
            collector.Import(new(DetachedCSharpDependencySet.CurrentSchema, true, true,
                types.ToImmutableArray(), values.ToImmutableArray(), lookups.Values.ToImmutableArray(), values.ToImmutableArray(), []));
            Measure("sort-strings", "linq", size, false, () => Box(strings.OrderBy(static value => value, StringComparer.Ordinal).ToImmutableArray()));
            Measure("sort-strings", "direct", size, false, () => Box(strings.ToSortedImmutableArray(StringComparer.Ordinal)));
            Measure("sort-types", "linq", size, false, () => Box(types.OrderBy(static value => value.AssemblyIdentity, StringComparer.Ordinal)
                .ThenBy(static value => value.MetadataName, StringComparer.Ordinal).ToImmutableArray()));
            Measure("sort-types", "direct", size, false, () => Box(types.ToSortedImmutableArray(CollectionTypeComparer)));
            Measure("sort-lookups", "linq", size, false, () => Box(lookups.Values.OrderBy(static value => value.Key, StringComparer.Ordinal).ToImmutableArray()));
            Measure("sort-lookups", "direct", size, false, () => Box(lookups.Values.ToSortedImmutableArray(CollectionLookupComparer)));
            var incomplete = new HashSet<string>(StringComparer.Ordinal);
            DetachedCSharpDependencySet OriginalDetach() => new(1, true, true,
                types.OrderBy(static value => value.AssemblyIdentity, StringComparer.Ordinal).ThenBy(static value => value.MetadataName, StringComparer.Ordinal).ToImmutableArray(),
                strings.OrderBy(static value => value, StringComparer.Ordinal).ToImmutableArray(),
                lookups.Values.OrderBy(static value => value.Key, StringComparer.Ordinal).ToImmutableArray(),
                strings.OrderBy(static value => value, StringComparer.Ordinal).ToImmutableArray(),
                incomplete.OrderBy(static value => value, StringComparer.Ordinal).ToImmutableArray());
            Assert.Equal(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(OriginalDetach()), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(collector.Detach()));
            Measure("detach", "collector", size, false, () => s_collectionResult = collector.Detach());
            Measure("detach", "linq-five-arrays", size, false, () => s_collectionResult = OriginalDetach());
            foreach (var duplicates in new[] { false, true })
            {
                var candidates = duplicates && size != 0 ? Enumerable.Range(0, size).Select(index => values[index % Math.Max(1, size / 8)]).ToArray() : values;
                var expected = candidates.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToImmutableArray();
                Assert.Equal<string>(expected, CollectionArrayCandidates(candidates));
                Assert.Equal<string>(expected, CollectionSetCandidates(candidates));
                Measure("candidates", "linq", size, duplicates, () => Box(candidates.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToImmutableArray()));
                Measure("candidates", "array-builder", size, duplicates, () => Box(CollectionArrayCandidates(candidates)));
                Measure("candidates", "pooled-set", size, duplicates, () => Box(CollectionSetCandidates(candidates)));
                var keys = candidates.Select(value => (Id: value, Message: value, Span: new TextSpan(value.Length, 1))).ToArray();
                Assert.Equal<(string Id, string Message, TextSpan Span)>(CollectionDiagnosticKeys(keys, pooled: false), CollectionDiagnosticKeys(keys, pooled: true));
                Measure("diagnostics", "hashset", size, duplicates, () => Box(CollectionDiagnosticKeys(keys, pooled: false)));
                Measure("diagnostics", "pooled-set", size, duplicates, () => Box(CollectionDiagnosticKeys(keys, pooled: true)));
                Assert.Equal(candidates.Distinct(StringComparer.Ordinal), CollectionOrderedCandidates(candidates));
                Measure("ordered-distinct", "linq", size, duplicates, () => Box(candidates.Distinct(StringComparer.Ordinal).ToImmutableArray()));
                Measure("ordered-distinct", "pooled-set-builder", size, duplicates, () => Box(CollectionOrderedCandidates(candidates)));
            }
        }
        MeasureCollectionRetention();

        static void Box<T>(ImmutableArray<T> result)
        {
            // Store the backing array rather than boxing ImmutableArray inside the timed loop.
            s_collectionResult = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(result);
        }

        static void Measure(string operation, string variant, int size, bool duplicates, Action action)
        {
            var iterations = Math.Max(128, 32_768 / Math.Max(1, size));
            for (var index = 0; index < 256; index++)
            {
                action();
            }
            for (var sample = 0; sample < Samples; sample++)
            {
                var gen0 = GC.CollectionCount(0);
                var gen1 = GC.CollectionCount(1);
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                for (var index = 0; index < iterations; index++)
                {
                    action();
                }
                var elapsed = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Write(new { Kind = "collection-sample", Operation = operation, Variant = variant, Size = size, Duplicates = duplicates,
                    Sample = sample, Iterations = iterations, NanosecondsPerOperation = elapsed, BytesPerOperation = (double)allocated / iterations,
                    Gen0 = GC.CollectionCount(0) - gen0, Gen1 = GC.CollectionCount(1) - gen1 });
            }
        }
    }

    private static ImmutableArray<string> CollectionArrayCandidates(string[] values)
    {
        var builder = ArrayBuilder<string>.GetInstance();
        try
        {
            foreach (var value in values)
            {
                builder.Add(value);
            }
            builder.SortAndRemoveDuplicates(StringComparer.Ordinal);
            return builder.ToImmutable();
        }
        finally
        {
            builder.Free();
        }
    }

    private static ImmutableArray<string> CollectionSetCandidates(string[] values)
    {
        var set = PooledStringHashSet.GetInstance();
        try
        {
            foreach (var value in values)
            {
                set.Add(value);
            }
            return set.ToSortedImmutableArray(StringComparer.Ordinal);
        }
        finally
        {
            set.Free();
        }
    }

    private static ImmutableArray<string> CollectionOrderedCandidates(string[] values)
    {
        var set = PooledStringHashSet.GetInstance();
        var builder = ArrayBuilder<string>.GetInstance();
        try
        {
            foreach (var value in values)
            {
                if (set.Add(value))
                {
                    builder.Add(value);
                }
            }
            return builder.ToImmutable();
        }
        finally
        {
            builder.Free();
            set.Free();
        }
    }

    private static ImmutableArray<(string Id, string Message, TextSpan Span)> CollectionDiagnosticKeys(
        (string Id, string Message, TextSpan Span)[] keys, bool pooled)
    {
        using var builder = ImmutableArrayBuilder<(string Id, string Message, TextSpan Span)>.Rent();
        HashSet<(string Id, string Message, TextSpan Span)>? set = null;
        try
        {
            foreach (var key in keys)
            {
                set ??= pooled ? PooledHashSet<(string Id, string Message, TextSpan Span)>.GetInstance() : [];
                if (set.Add(key))
                {
                    builder.Add(key);
                }
            }
            return builder.ToImmutable();
        }
        finally
        {
            if (set is PooledHashSet<(string Id, string Message, TextSpan Span)> rental)
            {
                rental.Free();
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MeasureCollectionRetention()
    {
        foreach (var size in new[] { 16, 1024, 65_536 })
        {
            foreach (var bounded in new[] { false, true })
            {
                MeasurePolicy(size, bounded);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void MeasurePolicy(int size, bool bounded)
        {
            var values = Enumerable.Range(0, size).Select(static index => index.ToString()).ToArray();
            var oldPool = new ObjectPool<HashSet<string>>(static () => new HashSet<string>(StringComparer.Ordinal), size: 1);
            var newPool = PooledHashSet<string>.CreatePool(StringComparer.Ordinal, size: 1);
            Func<HashSet<string>> allocate = bounded ? () => newPool.Allocate() : () => oldPool.Allocate();
            Action<HashSet<string>> release = bounded ? static set => ((PooledHashSet<string>)set).Free() : set =>
            {
                set.Clear();
                oldPool.Free(set);
            };
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var baselineHeap = GC.GetTotalMemory(true);
            var allocated = GC.GetTotalAllocatedBytes(true);
            for (var iteration = 0; iteration < 8; iteration++)
            {
                Fill(allocate, release, values);
            }
            var allocationDelta = GC.GetTotalAllocatedBytes(true) - allocated;
            s_collectionResult = null;
            var heap = GC.GetTotalMemory(true);
            var rental = allocate();
            try
            {
                Write(new { Kind = "collection-retention", Size = size, Policy = bounded ? "bounded" : "previous-clear-only", Iterations = 8,
                    AllocatedBytes = allocationDelta, PostGcHeapBytes = heap, HeapDeltaBytes = heap - baselineHeap,
                    RetainedCapacity = rental.EnsureCapacity(0), RetainedElements = rental.Count });
            }
            finally
            {
                release(rental);
            }
            GC.KeepAlive(oldPool);
            GC.KeepAlive(newPool);
            GC.KeepAlive(values);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Fill(Func<HashSet<string>> allocate, Action<HashSet<string>> release, string[] values)
        {
            var set = allocate();
            try
            {
                foreach (var value in values)
                {
                    set.Add(value);
                }
            }
            finally
            {
                release(set);
            }
        }
    }
}
