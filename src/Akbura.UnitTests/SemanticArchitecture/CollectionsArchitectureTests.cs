using Akbura.Language;
using Akbura.Collections;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AkburaOperation = Akbura.Language.Operations.IOperation;
using AkburaOperationKind = Akbura.Language.Operations.OperationKind;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using AkburaSymbolKind = Akbura.Language.Symbols.SymbolKind;
using AkburaSymbolVisitor = Akbura.Language.Symbols.SymbolVisitor;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using BinderType = Akbura.Language.Binder.Binder;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.UnitTests;

public sealed class CollectionsArchitectureTests : SemanticArchitectureTestBase
{
    [Fact]
    public void SmallDictionary_AddUpdateLookupAndEnumerate()
    {
        var dictionary = new SmallDictionary<string, int>();

        dictionary.Add("first", 1);
        dictionary.Add("second", 2);
        dictionary["second"] = 22;

        Assert.True(dictionary.ContainsKey("first"));
        Assert.True(dictionary.TryGetValue("second", out var second));
        Assert.Equal(22, second);
        Assert.False(dictionary.TryGetValue("missing", out _));
        Assert.Equal(1, dictionary["first"]);

        var keys = new HashSet<string>();
        foreach (var key in dictionary.Keys)
        {
            keys.Add(key);
        }

        var values = new HashSet<int>();
        foreach (var value in dictionary.Values)
        {
            values.Add(value);
        }

        Assert.Equal(2, keys.Count);
        Assert.Contains("first", keys);
        Assert.Contains("second", keys);
        Assert.Equal(2, values.Count);
        Assert.Contains(1, values);
        Assert.Contains(22, values);
    }


    [Fact]
    public void SmallDictionary_HandlesHashCollisions()
    {
        var dictionary = new SmallDictionary<string, int>(new ConstantHashComparer());

        dictionary.Add("first", 1);
        dictionary.Add("second", 2);
        dictionary.Add("third", 3);
        dictionary["second"] = 20;

        Assert.Equal(1, dictionary["first"]);
        Assert.Equal(20, dictionary["second"]);
        Assert.Equal(3, dictionary["third"]);
        Assert.Throws<InvalidOperationException>(() => dictionary.Add("third", 30));

        var pairs = new Dictionary<string, int>();
        foreach (var pair in dictionary)
        {
            pairs.Add(pair.Key, pair.Value);
        }

        Assert.Equal(3, pairs.Count);
        Assert.Equal(20, pairs["second"]);
    }


    [Fact]
    public void SegmentedHashSet_MatchesSetSemanticsAndComparer()
    {
        var set = new SegmentedHashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.True(set.Add("alpha"));
        Assert.False(set.Add("ALPHA"));
        Assert.True(set.Add("beta"));
        Assert.Equal(2, set.Count);
        Assert.Contains("alpha", set);
        Assert.Contains("ALPHA", set);
        Assert.True(set.TryGetValue("ALPHA", out var actual));
        Assert.Equal("alpha", actual);

        set.ExceptWith(["BETA"]);
        Assert.Single(set);
        Assert.Contains("alpha", set);

        var comparer = SegmentedHashSet<string>.CreateSetComparer();
        Assert.True(comparer.Equals(
            set,
            new SegmentedHashSet<string>(["alpha"], StringComparer.OrdinalIgnoreCase)));
    }


    [Fact]
    public void ImmutableSegmentedHashSet_AddRemoveAndBuilderPreserveImmutability()
    {
        var empty = ImmutableSegmentedHashSet<string>.Empty;
        var first = empty.Add("first");
        var second = first.Add("second");
        var removed = second.Remove("first");

        Assert.True(empty.IsEmpty);
        Assert.Single(first);
        Assert.Contains("first", first);
        Assert.DoesNotContain("second", first);
        Assert.Equal(2, second.Count);
        Assert.Contains("first", second);
        Assert.Contains("second", second);
        Assert.Single(removed);
        Assert.DoesNotContain("first", removed);
        Assert.Contains("second", removed);

        var builder = second.ToBuilder();
        Assert.False(builder.Add("second"));
        Assert.True(builder.Add("third"));

        var built = builder.ToImmutable();
        Assert.Equal(3, built.Count);
        Assert.Contains("first", built);
        Assert.Contains("second", built);
        Assert.Contains("third", built);
        Assert.Equal(2, second.Count);
    }


    [Fact]
    public void ImmutableSegmentedHashSet_FactoryRangeAndComparerMatchImmutableSetShape()
    {
        var set = ImmutableSegmentedHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "first",
            "FIRST",
            "second");

        Assert.Equal(2, set.Count);
        Assert.Contains("first", set);
        Assert.Contains("FIRST", set);
        Assert.True(set.TryGetValue("FIRST", out var actual));
        Assert.Equal("first", actual);

        var builder = ImmutableSegmentedHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(builder.Add("alpha"));
        Assert.False(builder.Add("ALPHA"));
        Assert.True(builder.TryGetValue("ALPHA", out var builderActual));
        Assert.Equal("alpha", builderActual);

        var range = new[] { "one", "two", "two" }.ToImmutableSegmentedHashSet();
        Assert.Equal(2, range.Count);
        Assert.Contains("one", range);
        Assert.Contains("two", range);
    }


    [Fact]
    public void ImmutableSegmentedHashSet_MarshalExposesSegmentedBacking()
    {
        var set = ImmutableSegmentedHashSet.Create("first", "second");
        var backing = SegmentedCollectionsMarshal.AsSegmentedHashSet(set);

        Assert.NotNull(backing);
        Assert.Equal(2, backing.Count);
        Assert.Contains("first", backing);
        Assert.Contains("second", backing);

        var roundTrip = SegmentedCollectionsMarshal.AsImmutableSegmentedHashSet(backing);
        Assert.Equal(2, roundTrip.Count);
        Assert.Contains("first", roundTrip);
        Assert.Contains("second", roundTrip);
    }


    [Fact]
    public void LookupResult_PooledInstancesResetBeforeReuse()
    {
        const string code = "state int count = 0;";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var state = Assert.IsType<StateDeclarationSyntax>(tree.GetRoot().Members[0]);
        var stateSymbol = Assert.IsAssignableFrom<IStateSymbol>(model.GetSymbolInfo(state).Symbol);
        var result = LookupResult.GetInstance();
        result.SetSymbol(stateSymbol);
        result.Free();

        var reused = LookupResult.GetInstance();
        try
        {
            Assert.True(reused.IsClear);
            Assert.Null(reused.Symbol);
            Assert.Equal(AkburaCandidateReason.NotFound, reused.CandidateReason);
        }
        finally
        {
            reused.Free();
        }
    }


    [Theory]
    [InlineData(new int[] { })]
    [InlineData(new int[] { 7 })]
    [InlineData(new int[] { 1, 2, 3, 4 })]
    [InlineData(new int[] { 4, 3, 2, 1 })]
    [InlineData(new int[] { 3, 1, 3, 2, 1, 2 })]
    public void ToSortedImmutableArray_MatchesLinqForKnownSizeCollections(int[] values)
    {
        foreach (var comparer in new[] { Comparer<int>.Default, Comparer<int>.Create(static (x, y) => y.CompareTo(x)) })
        {
            var expected = values.OrderBy(static value => value, comparer).ToImmutableArray();
            foreach (var source in new ICollection<int>[] { values, values.ToList() })
            {
                var result = source.ToSortedImmutableArray(comparer);
                Assert.False(result.IsDefault);
                Assert.Equal<int>(expected, result);
                Assert.Equal<int>(result, source.ToSortedImmutableArray(comparer));
            }

            var set = new HashSet<int>(values);
            Assert.Equal<int>(set.OrderBy(static value => value, comparer), set.ToSortedImmutableArray(comparer));
        }
    }

    [Fact]
    public void ToSortedImmutableArray_UsesTheSuppliedStringComparer()
    {
        string[] values = ["\u00e9", "beta", "Alpha", "alpha", "BETA", "Alpha"];
        Assert.Equal<string>(values.OrderBy(static value => value, StringComparer.Ordinal),
            values.ToSortedImmutableArray(StringComparer.Ordinal));

        // Ignore-case ties in this input are identical values, so their relative order is immaterial.
        string[] interchangeable = ["beta", "Alpha", "\u00e9", "Alpha", "beta"];
        Assert.Equal<string>(interchangeable.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase),
            interchangeable.ToSortedImmutableArray(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void ToSortedImmutableArray_DoesNotPromiseStableOrderForDistinctElementsWithEqualKeys()
    {
        var source = Enumerable.Range(0, 64).Select(index => (Key: index % 3, Id: index)).ToArray();
        var byKey = Comparer<(int Key, int Id)>.Create(static (x, y) => x.Key.CompareTo(y.Key));
        var result = source.ToSortedImmutableArray(byKey);
        var stable = source.OrderBy(static value => value.Key).ToImmutableArray();

        Assert.Equal<int>(stable.Select(static value => value.Key), result.Select(static value => value.Key));
        Assert.Equal<(int Key, int Id)>(source, result.OrderBy(static value => value.Id));
        Assert.Equal<int>(source.Where(static value => value.Key == 0).Select(static value => value.Id),
            stable.Where(static value => value.Key == 0).Select(static value => value.Id));
        // Only LINQ promises the Id order above. Callers needing it must keep a stable sort
        // or include an explicit tie-breaker in the comparer.
        var byKeyAndId = Comparer<(int Key, int Id)>.Create(static (x, y) =>
            x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Id.CompareTo(y.Id));
        Assert.Equal<(int Key, int Id)>(stable, source.ToSortedImmutableArray(byKeyAndId));
    }

    [Fact]
    public void ToSortedImmutableArray_OwnsItsResultAndDoesNotReorderTheSource()
    {
        int[] source = [3, 1, 2];
        var list = source.ToList();
        var fromArray = source.ToSortedImmutableArray(Comparer<int>.Default);
        var fromList = list.ToSortedImmutableArray(Comparer<int>.Default);
        Assert.Equal<int>([3, 1, 2], source);
        Assert.Equal<int>(source, list);
        source[0] = 99;
        list.Clear();
        Assert.Equal<int>([1, 2, 3], fromArray);
        Assert.Equal<int>(fromArray, fromList);
    }

    [Fact]
    public void ToSortedImmutableArray_ValidatesArgumentsEvenForEmptyCollections()
    {
        Assert.Equal("source", Assert.Throws<ArgumentNullException>(() =>
            Akbura.Collections.EnumerableExtensions.ToSortedImmutableArray<int>(null!, Comparer<int>.Default)).ParamName);
        Assert.Equal("comparer", Assert.Throws<ArgumentNullException>(() =>
            Array.Empty<int>().ToSortedImmutableArray(null!)).ParamName);
        Assert.Equal(ImmutableArray<int>.Empty, Array.Empty<int>().ToSortedImmutableArray(Comparer<int>.Default));
    }

    [Fact]
    public void ToSortedImmutableArray_ComparerFailureDoesNotMutateTheSource()
    {
        int[] source = [3, 1, 2];
        var failure = new InvalidOperationException("Comparer failed.");
        var comparer = Comparer<int>.Create((_, _) => throw failure);
        var exception = Assert.Throws<InvalidOperationException>(() => source.ToSortedImmutableArray(comparer));
        Assert.Same(failure, exception.InnerException);
        Assert.Equal<int>([3, 1, 2], source);
        Assert.Equal<int>([1, 2, 3], source.ToSortedImmutableArray(Comparer<int>.Default));
    }

    [Theory]
    [InlineData(new int[] { })]
    [InlineData(new int[] { 1 })]
    [InlineData(new int[] { 3, 1, 2, 1, 4, 5 })]
    public void ImmutableArrayMappingAndFiltering_MatchLinqOrderAndEmptyBehavior(int[] values)
    {
        var source = values.ToImmutableArray();
        Assert.Equal<string>(source.Select(static value => value.ToString()),
            source.SelectAsArray(static value => value.ToString()));
        Assert.Equal<int>(source.Select(static value => value + 10),
            source.SelectAsArray(static (value, offset) => value + offset, 10));
        foreach (var minimum in new[] { 0, 2, 10 })
        {
            Assert.Equal<int>(source.Where(value => value >= minimum),
                source.WhereAsArray(static (value, threshold) => value >= threshold, minimum));
        }
    }

    [Fact]
    public void ImmutableArrayMappingAndFiltering_PreserveDefaultAndCallbackFailures()
    {
        var source = default(ImmutableArray<int>);
        Assert.Equal(Record.Exception(() => source.Select(static value => value).ToImmutableArray())!.GetType(),
            Record.Exception(() => source.SelectAsArray(static value => value))!.GetType());
        Assert.Equal(Record.Exception(() => source.Where(static value => value > 0).ToImmutableArray())!.GetType(),
            Record.Exception(() => source.WhereAsArray(static (value, threshold) => value > threshold, 0))!.GetType());

        source = [3, 1, 2, 4];
        var failure = new InvalidOperationException("Callback failed.");
        var visits = new List<int>();
        int Map(int value)
        {
            visits.Add(value);
            return value == 2 ? throw failure : value * 2;
        }

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => source.Select(Map).ToImmutableArray()));
        var expectedVisits = visits.ToArray();
        visits.Clear();
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => source.SelectAsArray(Map)));
        Assert.Equal<int>(expectedVisits, visits);
        visits.Clear();
        bool Filter(int value)
        {
            visits.Add(value);
            return value == 2 ? throw failure : value > 1;
        }

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => source.Where(Filter).ToImmutableArray()));
        expectedVisits = visits.ToArray();
        visits.Clear();
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => source.WhereAsArray(Filter)));
        Assert.Equal<int>(expectedVisits, visits);
    }

    private sealed class ConstantHashComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y)
        {
            return string.Equals(x, y, StringComparison.Ordinal);
        }

        public int GetHashCode(string obj)
        {
            return 1;
        }
    }
}
