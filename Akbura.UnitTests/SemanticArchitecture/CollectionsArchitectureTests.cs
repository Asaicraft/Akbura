using Akbura.Language;
using Akbura.Collections;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Declarations;
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
