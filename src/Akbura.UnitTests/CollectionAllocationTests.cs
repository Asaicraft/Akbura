using Akbura.Collections;
using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.CodeGeneration;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Akbura.UnitTests;

public sealed class CollectionAllocationTests
{
    public static IEnumerable<object[]> Sizes()
    {
        foreach (var size in new[] { 0, 1, 2, 4, 8, 16, 32, 64, 128, 256, 1024 })
        {
            yield return [size];
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void SortedMaterialization_PreservesExactOrdinalValuesWithoutAliasing(int size)
    {
        var input = Enumerable.Range(0, size).Select(index => "é|\0" + index).Reverse().ToList();
        input.AddRange(["a", "A", "a", "e\u0301", "é", "\ud83d\ude00", "|", "\0"]);
        var expected = input.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var result = input.ToSortedImmutableArray(StringComparer.Ordinal);
        Assert.Equal(expected, result);
        Assert.Equal(input.Count, result.Length);
        input.Clear();
        input.Add("changed");
        Assert.Equal(expected, result);
        var set = new HashSet<string>(expected, StringComparer.Ordinal);
        Assert.Equal(set.OrderBy(static value => value, StringComparer.Ordinal), set.ToSortedImmutableArray(StringComparer.Ordinal));
    }

    [Fact]
    public void SortedMaterialization_EmptyIsNotDefaultAndSingleDoesNotAlias()
    {
        Assert.False(new List<string>().ToSortedImmutableArray(StringComparer.Ordinal).IsDefault);
        var input = new[] { "one" };
        var result = input.ToSortedImmutableArray(StringComparer.Ordinal);
        input[0] = "changed";
        Assert.Equal("one", result[0]);
        Assert.Throws<ArgumentNullException>(() => Akbura.Collections.EnumerableExtensions.ToSortedImmutableArray<string>(null!, StringComparer.Ordinal));
        Assert.Throws<ArgumentNullException>(() => input.ToSortedImmutableArray(null!));
    }

    [Fact]
    public void SortedMaterialization_EqualKeysPreserveEveryElementButDoNotPromiseStableOrder()
    {
        var input = Enumerable.Range(0, 256).Select(index => (Key: index % 3, Value: index)).ToArray();
        var result = input.ToSortedImmutableArray(Comparer<(int Key, int Value)>.Create(static (left, right) => left.Key.CompareTo(right.Key)));
        Assert.Equal(input.OrderBy(static value => value.Key).Select(static value => value.Key), result.Select(static value => value.Key));
        Assert.Equal(input.OrderBy(static value => value.Value), result.OrderBy(static value => value.Value));
        Assert.Equal(0, input[0].Value);
    }

    [Fact]
    public void SortedMaterialization_CopyAndComparerExceptionsDoNotMutateSource()
    {
        var source = new List<string> { "b", "a" };
        var comparer = Comparer<string>.Create(static (_, _) => throw new InvalidOperationException("comparison"));
        Assert.Throws<InvalidOperationException>(() => source.ToSortedImmutableArray(comparer));
        Assert.Equal<string>(["b", "a"], source);
        Assert.Throws<OperationCanceledException>(() => new ThrowingCollection().ToSortedImmutableArray(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void DetachedFacts_MatchOriginalLinqSerializationAcrossRepeatedDetachAndMutation(int size)
    {
        var values = Enumerable.Range(0, size).Select(index => "scope|\0é" + index).Reverse().Concat(["A", "a", "A"]).ToArray();
        var types = values.Select(value => new DetachedCSharpTypeIdentity(value.Length % 2 == 0 ? "A" : "a", value)).ToArray();
        var lookups = values.Select(value => new DetachedCSharpLookupDependency(value, "receiver|\0", value, "scope\nusing X;", "selected", 2,
            ["A", "a", "é|\0"], ["argument"], true)).ToArray();
        var collector = CreateCollector();
        collector.Import(new(1, true, true, [.. types], [.. values], [.. lookups], [.. values], []));
        var expected = new DetachedCSharpDependencySet(1, true, true,
            types.Distinct().OrderBy(static value => value.AssemblyIdentity, StringComparer.Ordinal).ThenBy(static value => value.MetadataName, StringComparer.Ordinal).ToImmutableArray(),
            values.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToImmutableArray(),
            lookups.GroupBy(static value => value.Key, StringComparer.Ordinal).Select(static group => group.Last()).OrderBy(static value => value.Key, StringComparer.Ordinal).ToImmutableArray(),
            values.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToImmutableArray(), []);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(expected);
        var snapshot = collector.Detach();
        Assert.Equal(bytes, JsonSerializer.SerializeToUtf8Bytes(snapshot));
        Assert.Equal(bytes, JsonSerializer.SerializeToUtf8Bytes(collector.Detach()));
        var cancellation = new CancellationToken(canceled: true);
        Assert.Throws<OperationCanceledException>(() => collector.Detach(cancellation));
        Assert.Equal(bytes, JsonSerializer.SerializeToUtf8Bytes(collector.Detach()));
        collector.MarkIncomplete("é");
        collector.MarkIncomplete("A");
        collector.MarkIncomplete("a");
        collector.MarkIncomplete("A");
        var later = collector.Detach();
        Assert.False(later.IsComplete);
        Assert.False(later.HasFixedExportedSurface);
        Assert.Equal<string>(["A", "a", "é"], later.IncompleteReasons);
        Assert.Equal(bytes, JsonSerializer.SerializeToUtf8Bytes(snapshot));
    }

    [Fact]
    public void DetachedFacts_DisabledCollectorStillReturnsMissing()
    {
        Assert.Same(DetachedCSharpDependencySet.Missing, CreateCollector(enabled: false).Detach());
    }

    [Fact]
    public void CandidateIdentities_PreserveOrdinalSetAndAreIndependentOfBuilderReuse()
    {
        var compilation = CSharpCompilation.Create("Candidates", [CSharpSyntaxTree.ParseText("class Api { void Read() {} void Read(string value) {} }")]);
        var members = compilation.GetTypeByMetadataName("Api")!.GetMembers("Read");
        var expected = members.Concat(members).Select(Identity).Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var result = DocumentSemanticDependencyCollector.CollectCandidateIdentities(members, members);
        Assert.Equal(expected, result);
        Assert.Empty(DocumentSemanticDependencyCollector.CollectCandidateIdentities([], []));
        for (var iteration = 0; iteration < 16; iteration++)
        {
            Assert.Equal(expected, DocumentSemanticDependencyCollector.CollectCandidateIdentities(members.Reverse().ToImmutableArray(), members));
        }
        Assert.Equal(expected, result);

        static string Identity(ISymbol symbol) => (symbol.ContainingAssembly?.Identity.ToString() ?? string.Empty) + "|" +
            (symbol.GetDocumentationCommentId() ?? symbol.ToDisplayString()) + "|" + symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandidateIdentities_ExceptionOrCancellationWhileComputingIdentityDoesNotPoisonNextRental(bool cancel)
    {
        var symbol = DispatchProxy.Create<ISymbol, ThrowingSymbolProxy>();
        ((ThrowingSymbolProxy)(object)symbol).Cancel = cancel;
        if (cancel)
        {
            Assert.Throws<OperationCanceledException>(() => DocumentSemanticDependencyCollector.CollectCandidateIdentities([symbol], []));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => DocumentSemanticDependencyCollector.CollectCandidateIdentities([], [symbol]));
        }
        Assert.Empty(DocumentSemanticDependencyCollector.CollectCandidateIdentities([], []));
    }

    [Fact]
    public void StringPool_IsOrdinalAndConcurrentRentalsAreIndependent()
    {
        Parallel.For(0, 128, index =>
        {
            var set = PooledStringHashSet.GetInstance();
            try
            {
                Assert.Empty(set);
                Assert.Same(StringComparer.Ordinal, set.Comparer);
                Assert.True(set.Add("A"));
                Assert.True(set.Add("a"));
                Assert.False(set.Add("A"));
                set.Add("é|\0" + index);
                Assert.Equal(3, set.Count);
                var result = set.ToSortedImmutableArray(StringComparer.Ordinal);
                Assert.Equal("A", result[0]);
            }
            finally
            {
                set.Free();
            }
        });
    }

    [Fact]
    public void SetPool_DropsLargeRentalsAndCompactsFormerlyLargeRentals()
    {
        var pool = PooledHashSet<string>.CreatePool(StringComparer.Ordinal, size: 1);
        var large = pool.Allocate();
        for (var index = 0; index < 65_536; index++)
        {
            large.Add(index.ToString());
        }
        large.Free();
        Assert.Empty(large);
        var next = pool.Allocate();
        Assert.NotSame(large, next);
        for (var index = 0; index < 65_536; index++)
        {
            next.Add(index.ToString());
        }
        next.Clear();
        next.Add("kept");
        next.Free();
        var reused = pool.Allocate();
        try
        {
            Assert.Same(next, reused);
            Assert.Empty(reused);
            Assert.InRange(reused.EnsureCapacity(0), 0, 7);
        }
        finally
        {
            reused.Free();
        }
    }

    [Fact]
    public void SetPool_DoesNotKeepOldElementsAlive()
    {
        var pool = PooledHashSet<object>.CreatePool(EqualityComparer<object>.Default, size: 1);
        var weak = AddAndFree(pool);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.IsAlive);
        GC.KeepAlive(pool);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddAndFree(ObjectPool<PooledHashSet<object>> pool)
    {
        var value = new object();
        var weak = new WeakReference(value);
        var set = pool.Allocate();
        try
        {
            set.Add(value);
        }
        finally
        {
            set.Free();
        }
        return weak;
    }

    private static DocumentSemanticDependencyCollector CreateCollector(bool enabled = true)
    {
        var syntax = AkburaSyntaxTree.ParseText("<TextBlock />", filePath: "Fixture.akbura");
        var compilation = new AkburaCompilation(CSharpCompilation.Create("Fixture"), [syntax]) { CollectCSharpSemanticDependencies = enabled };
        return new DocumentSemanticDependencyCollector(compilation.GetSemanticModel(syntax));
    }

    public class ThrowingSymbolProxy : DispatchProxy
    {
        internal bool Cancel { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(ISymbol.GetDocumentationCommentId))
            {
                if (Cancel)
                {
                    throw new OperationCanceledException();
                }
                throw new InvalidOperationException("identity");
            }
            return null;
        }
    }

    private sealed class ThrowingCollection : ICollection<string>
    {
        public int Count => 1;
        public bool IsReadOnly => true;
        public void CopyTo(string[] array, int arrayIndex) => throw new OperationCanceledException();
        public IEnumerator<string> GetEnumerator() => throw new NotSupportedException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Add(string item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(string item) => throw new NotSupportedException();
        public bool Remove(string item) => throw new NotSupportedException();
    }
}
