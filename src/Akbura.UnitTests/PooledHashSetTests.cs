using Akbura.Collections;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

public sealed class PooledHashSetTests
{
    [Fact]
    public void Rental_AddContainsRemoveAndReuse_DoNotCarryOldEntries()
    {
        var pool = PooledHashSet<string>.CreatePool(StringComparer.Ordinal, 4);
        var first = pool.Allocate();
        try
        {
            Assert.Empty(first);
            Assert.True(first.Add("name"));
            Assert.False(first.Add("name"));
            Assert.True(first.Add("Name"));
            Assert.Contains("name", first);
            Assert.True(first.Remove("name"));
            Assert.DoesNotContain("name", first);
        }
        finally { first.Free(); }

        for (var iteration = 0; iteration < 64; iteration++)
        {
            var next = pool.Allocate();
            try
            {
                Assert.Same(first, next);
                Assert.Empty(next);
                Assert.Same(StringComparer.Ordinal, next.Comparer);
                Assert.True(next.Add(iteration.ToString()));
            }
            finally { next.Free(); }
        }
    }

    [Fact]
    public void DefaultStringEquality_IsOrdinalAndCaseSensitive()
    {
        var set = PooledHashSet<string>.GetInstance();
        try
        {
            Assert.Same(EqualityComparer<string>.Default, set.Comparer);
            Assert.True(set.Add("i"));
            Assert.False(set.Add(new string('i', 1)));
            Assert.True(set.Add("I"));
            Assert.True(set.Add("İ"));
            Assert.True(set.Add("ı"));
            Assert.True(set.Add(null!));
            Assert.False(set.Add(null!));
        }
        finally { set.Free(); }
    }

    [Fact]
    public void CustomPools_PreserveCaseInsensitiveAndReferenceComparers()
    {
        var casePool = PooledHashSet<string>.CreatePool(StringComparer.OrdinalIgnoreCase);
        var names = casePool.Allocate();
        try
        {
            Assert.True(names.Add("name"));
            Assert.False(names.Add("NAME"));
            Assert.Same(StringComparer.OrdinalIgnoreCase, names.Comparer);
        }
        finally { names.Free(); }

        var comparer = new IdentityComparer();
        var identityPool = PooledHashSet<ValueKey>.CreatePool(comparer);
        var identities = identityPool.Allocate();
        try
        {
            Assert.Same(comparer, identities.Comparer);
            var first = new ValueKey(1);
            Assert.True(identities.Add(first));
            Assert.False(identities.Add(first));
            Assert.True(identities.Add(new ValueKey(1)));
        }
        finally { identities.Free(); }
    }

    [Fact]
    public void SymbolPool_UsesRoslynEqualityForConstructedSymbols()
    {
        var compilation = CSharpCompilation.Create("Symbols", [CSharpSyntaxTree.ParseText("class C<T> { }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        var definition = compilation.GetTypeByMetadataName("C`1")!;
        var first = definition.Construct(compilation.GetSpecialType(SpecialType.System_Int32));
        var second = definition.Construct(compilation.GetSpecialType(SpecialType.System_Int32));
        var other = definition.Construct(compilation.GetSpecialType(SpecialType.System_String));
        var pool = PooledHashSet<INamedTypeSymbol>.CreatePool(SymbolEqualityComparer.Default);
        var set = pool.Allocate();
        try
        {
            Assert.Same(SymbolEqualityComparer.Default, set.Comparer);
            Assert.True(set.Add(first));
            Assert.False(set.Add(second));
            Assert.True(set.Add(other));
        }
        finally { set.Free(); }
    }

    [Fact]
    public void IndependentAndNestedRentals_DoNotShareActiveSets()
    {
        var pool = PooledHashSet<int>.CreatePool(EqualityComparer<int>.Default, 2);
        var outer = pool.Allocate();
        try
        {
            outer.Add(1);
            var inner = pool.Allocate();
            try
            {
                Assert.NotSame(outer, inner);
                Assert.Empty(inner);
                inner.Add(2);
                Assert.DoesNotContain(2, outer);
            }
            finally { inner.Free(); }
            Assert.Equal(1, Assert.Single(outer));
        }
        finally { outer.Free(); }
    }

    [Fact]
    public void ConcurrentRentals_AreIndependentDespiteBestEffortReturns()
    {
        var pool = PooledHashSet<int>.CreatePool(EqualityComparer<int>.Default, 8);
        var active = new ConcurrentDictionary<PooledHashSet<int>, byte>();
        Parallel.For(0, 2048, index =>
        {
            var set = pool.Allocate();
            try
            {
                Assert.True(active.TryAdd(set, 0));
                Assert.Empty(set);
                for (var value = 0; value < 16; value++)
                {
                    Thread.Yield();
                    Assert.True(set.Add(index * 16 + value));
                }
                Assert.Equal(16, set.Count);
                Assert.All(set, value => Assert.InRange(value, index * 16, index * 16 + 15));
                Assert.True(active.TryRemove(set, out _));
            }
            finally { set.Free(); }
        });
        Assert.Empty(active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallerFinally_ReturnsSetAfterExceptionOrCancellation(bool cancel)
    {
        var pool = PooledHashSet<int>.CreatePool(EqualityComparer<int>.Default);
        var rented = pool.Allocate();
        Exception expected = cancel ? new OperationCanceledException() : new InvalidOperationException();
        var actual = Record.Exception((Action)(() =>
        {
            try
            {
                rented.Add(42);
                throw expected;
            }
            finally { rented.Free(); }
        }));
        Assert.Same(expected, actual);
        var next = pool.Allocate();
        try
        {
            Assert.Same(rented, next);
            Assert.Empty(next);
        }
        finally { next.Free(); }
    }

    [Fact]
    public void Clear_RemovesSyntaxTreeReferencesWhileRetainingReusableStorage()
    {
        var (set, reference, capacity) = RentTreeAndRelease();
        Collect();
        Assert.False(reference.IsAlive);
        Assert.Empty(set);
        Assert.Equal(capacity, set.EnsureCapacity(0));
        GC.KeepAlive(set);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (PooledHashSet<SyntaxTree> Set, WeakReference Reference, int Capacity) RentTreeAndRelease()
    {
        var pool = PooledHashSet<SyntaxTree>.CreatePool(EqualityComparer<SyntaxTree>.Default);
        var set = pool.Allocate();
        var tree = CSharpSyntaxTree.ParseText("class C { }");
        set.Add(tree);
        var capacity = set.EnsureCapacity(0);
        set.Free();
        return (set, new WeakReference(tree), capacity);
    }

    [Theory]
    [InlineData(8, 1024)]
    [InlineData(16, 1024)]
    [InlineData(32, 1024)]
    [InlineData(128, 1024)]
    [InlineData(8, 16384)]
    [InlineData(16, 16384)]
    [InlineData(32, 16384)]
    [InlineData(128, 16384)]
    public void ReleasedSets_RespectPoolCapacityAndDoNotRetainOversizedInstances(int capacity, int size)
    {
        var (pool, references) = ReleaseLargeBurst(capacity, size);
        Collect();
        var reusable = size <= PooledHashSet<int>.MaxPooledCount;
        Assert.Equal(reusable ? capacity : 0, references.Count(static reference => reference.IsAlive));
        var set = pool.Allocate();
        try
        {
            Assert.Empty(set);
            if (reusable) Assert.True(set.EnsureCapacity(0) >= size);
            else Assert.Equal(0, set.EnsureCapacity(0));
            Assert.True(set.Add(42));
        }
        finally { set.Free(); }
        GC.KeepAlive(pool);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ObjectPool<PooledHashSet<int>> Pool, WeakReference[] References) ReleaseLargeBurst(int capacity, int size)
    {
        var pool = PooledHashSet<int>.CreatePool(EqualityComparer<int>.Default, capacity);
        var sets = Enumerable.Range(0, capacity + 8).Select(_ => pool.Allocate()).ToArray();
        var references = sets.Select(static set => new WeakReference(set)).ToArray();
        foreach (var set in sets)
        {
            for (var key = 0; key < size; key++) set.Add(key);
            set.Free();
        }
        return (pool, references);
    }

    [Theory]
    [InlineData("duplicates")]
    [InlineData("subset")]
    [InlineData("remove")]
    [InlineData("select")]
    public void ExistingCollectionHelpers_ReturnTheirSetWhenUserCodeThrows(string operation)
    {
        var previous = PooledHashSet<ThrowingKey>.GetInstance();
        previous.Free();
        var error = new InvalidOperationException("user equality/selector failure");
        var values = ImmutableArray.Create(new ThrowingKey(1, null), new ThrowingKey(2, error),
            new ThrowingKey(3, null), new ThrowingKey(4, null));
        var actual = Record.Exception(() =>
        {
            if (operation == "duplicates") Akbura.Collections.ImmutableArrayExtensions.HasDuplicates(values);
            else if (operation == "subset") Akbura.Collections.ImmutableArrayExtensions.IsSubsetOf(values, values);
            else
            {
                var builder = ArrayBuilder<ThrowingKey>.GetInstance();
                try
                {
                    builder.AddRange(values);
                    if (operation == "remove") builder.RemoveDuplicates();
                    else builder.SelectDistinct(key => key.Error == null ? key : throw key.Error);
                }
                finally { builder.Free(); }
            }
        });
        Assert.Same(error, actual);
        var next = PooledHashSet<ThrowingKey>.GetInstance();
        try
        {
            Assert.Same(previous, next);
            Assert.Empty(next);
        }
        finally { next.Free(); }
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record ValueKey(int Value);
    private sealed class IdentityComparer : IEqualityComparer<ValueKey>
    {
        public bool Equals(ValueKey? x, ValueKey? y) => ReferenceEquals(x, y);
        public int GetHashCode(ValueKey obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private sealed record ThrowingKey(int Value, Exception? Error)
    {
        public override int GetHashCode() => Error == null ? Value : throw Error;
    }
}
