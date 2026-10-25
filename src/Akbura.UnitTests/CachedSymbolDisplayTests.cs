using Akbura.Collections;
using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

public sealed class CachedSymbolDisplayTests
{
    private static readonly MetadataReference s_core = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly Func<ITypeSymbol, ITypeSymbol, bool> s_compare = typeof(AkburaSemanticModel)
        .GetMethod("IsSameType", BindingFlags.Static | BindingFlags.NonPublic)!
        .CreateDelegate<Func<ITypeSymbol, ITypeSymbol, bool>>();

    private const string Shapes = """
        #nullable enable
        using System.Collections.Generic;
        namespace Shapes;
        public class Outer<T> { public class Inner<U> {} }
        public interface IContract {}
        public struct Point {}
        public unsafe class Container<T>
        {
            public int Primitive;
            public Point Struct;
            public IContract Interface;
            public Outer<int>.Inner<string> Nested;
            public List<T> Generic;
            public Dictionary<string, List<int?>> Constructed;
            public int[] Array;
            public int[,,] MultidimensionalArray;
            public int? NullableValue;
            public string? NullableReference;
            public (int Value, string? Name) Tuple;
            public T TypeParameter;
            public int* Pointer;
            public dynamic Dynamic;
            public MissingType Error;
            public delegate*<int, void> FunctionPointer;
            public object Metadata;
        }
        """;

    [Theory]
    [InlineData("Primitive")]
    [InlineData("Struct")]
    [InlineData("Interface")]
    [InlineData("Nested")]
    [InlineData("Generic")]
    [InlineData("Constructed")]
    [InlineData("Array")]
    [InlineData("MultidimensionalArray")]
    [InlineData("NullableValue")]
    [InlineData("NullableReference")]
    [InlineData("Tuple")]
    [InlineData("TypeParameter")]
    [InlineData("Pointer")]
    [InlineData("Dynamic")]
    [InlineData("Error")]
    [InlineData("FunctionPointer")]
    [InlineData("Metadata")]
    public void FullyQualifiedName_MatchesRoslynForSupportedShapes(string field)
    {
        var compilation = Compile(Shapes);
        var container = compilation.GetTypeByMetadataName("Shapes.Container`1")!;
        var symbol = ((IFieldSymbol)container.GetMembers(field).Single()).Type;
        Assert.Equal(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), CachedSymbolDisplay.GetFullyQualifiedName(symbol));
    }

    [Fact]
    public void FullyQualifiedName_MatchesNamedClassAndGenericDefinition()
    {
        var compilation = Compile(Shapes);
        foreach (var name in new[] { "Shapes.Outer`1", "Shapes.Container`1" })
        {
            var symbol = compilation.GetTypeByMetadataName(name)!;
            Assert.Equal(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), CachedSymbolDisplay.GetFullyQualifiedName(symbol));
        }
    }

    [Fact]
    public void RepeatedLookup_ReturnsTheStoredStringInstance()
    {
        var symbol = Compile("namespace Display; public class Repeated {}").GetTypeByMetadataName("Display.Repeated")!;
        var first = CachedSymbolDisplay.GetFullyQualifiedName(symbol);
        for (var i = 0; i < 100; i++) Assert.Same(first, CachedSymbolDisplay.GetFullyQualifiedName(symbol));
    }

    [Fact]
    public void DifferentTupleElementNames_PreserveEachDisplay()
    {
        var compilation = Compile("public class Tuples { public (int Left, string Right) A; public (int First, string Second) B; }");
        var type = compilation.GetTypeByMetadataName("Tuples")!;
        var left = ((IFieldSymbol)type.GetMembers("A").Single()).Type;
        var right = ((IFieldSymbol)type.GetMembers("B").Single()).Type;
        Assert.NotSame(left, right);
        var leftExpected = left.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var rightExpected = right.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        Assert.NotEqual(leftExpected, rightExpected);
        Assert.Equal(leftExpected, CachedSymbolDisplay.GetFullyQualifiedName(left));
        Assert.Equal(rightExpected, CachedSymbolDisplay.GetFullyQualifiedName(right));
        Assert.Equal(leftExpected, CachedSymbolDisplay.GetFullyQualifiedName(left));
    }

    [Fact]
    public void CrossCompilationDisplayFallback_PreservesTheOriginalComparisonContract()
    {
        const string source = "namespace Shared; public interface IContract {} public class Base {} public class Derived : Base, IContract {} public class Other {}";
        var left = Compile(source);
        var right = Compile(source);
        var leftType = left.GetTypeByMetadataName("Shared.Derived")!;
        var rightType = right.GetTypeByMetadataName("Shared.Derived")!;
        Assert.NotSame(leftType, rightType);
        Assert.False(SymbolEqualityComparer.Default.Equals(leftType, rightType));
        Assert.True(s_compare(leftType, rightType));
        Assert.Equal(leftType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), CachedSymbolDisplay.GetFullyQualifiedName(leftType));
        Assert.Equal(rightType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), CachedSymbolDisplay.GetFullyQualifiedName(rightType));
        Assert.False(s_compare(leftType, right.GetTypeByMetadataName("Shared.Other")!));
        Assert.False(s_compare(leftType, right.GetTypeByMetadataName("Shared.Base")!));
        Assert.True(s_compare(leftType.BaseType!, right.GetTypeByMetadataName("Shared.Base")!));
        Assert.True(s_compare(leftType.Interfaces.Single(), right.GetTypeByMetadataName("Shared.IContract")!));
        Assert.True(AkburaSemanticModel.IsAssignableTo(leftType, right.GetTypeByMetadataName("Shared.Base")!));
        Assert.True(AkburaSemanticModel.IsAssignableTo(leftType, right.GetTypeByMetadataName("Shared.IContract")!));
        Assert.False(AkburaSemanticModel.IsAssignableTo(leftType, right.GetTypeByMetadataName("Shared.Other")!));
    }

    [Fact]
    public void SymbolEqualityFastPath_PreservesNullableEquivalence()
    {
        var compilation = Compile("public class Tuples { public (int Left, string Right) A; public (int First, string Second) B; }");
        var type = compilation.GetTypeByMetadataName("Tuples")!;
        var left = ((IFieldSymbol)type.GetMembers("A").Single()).Type;
        var right = ((IFieldSymbol)type.GetMembers("B").Single()).Type;
        Assert.NotEqual(CachedSymbolDisplay.GetFullyQualifiedName(left), CachedSymbolDisplay.GetFullyQualifiedName(right));
        Assert.False(s_compare(left, right));
        var annotated = type.WithNullableAnnotation(NullableAnnotation.Annotated);
        Assert.True(SymbolEqualityComparer.Default.Equals(type, annotated));
        Assert.True(s_compare(type, annotated));
    }

    [Fact]
    public void ReferenceIdentity_IgnoresOverriddenObjectEquality()
    {
        var compilation = Compile("namespace Identity; public class First {} public class Second {}");
        var first = DispatchProxy.Create<ITypeSymbol, EqualTypeProxy>();
        var second = DispatchProxy.Create<ITypeSymbol, EqualTypeProxy>();
        ((EqualTypeProxy)(object)first).Target = compilation.GetTypeByMetadataName("Identity.First")!;
        ((EqualTypeProxy)(object)second).Target = compilation.GetTypeByMetadataName("Identity.Second")!;
        Assert.True(((object)first).Equals(second));
        var firstExpected = first.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var secondExpected = second.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        Assert.NotEqual(firstExpected, secondExpected);
        Assert.Equal(firstExpected, CachedSymbolDisplay.GetFullyQualifiedName(first));
        Assert.Equal(secondExpected, CachedSymbolDisplay.GetFullyQualifiedName(second));
        Assert.Equal(firstExpected, CachedSymbolDisplay.GetFullyQualifiedName(first));
    }

    [Fact]
    public void ConcurrentRequests_PreserveContentsAndPublishedStringInstances()
    {
        var compilation = Compile(Shapes);
        var container = compilation.GetTypeByMetadataName("Shapes.Container`1")!;
        var symbols = container.GetMembers().OfType<IFieldSymbol>().Select(field => field.Type).Append(container).ToArray();
        var expected = symbols.Select(symbol => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToArray();
        var results = new string[4096];
        Parallel.For(0, results.Length, index =>
        {
            var symbolIndex = index % symbols.Length;
            results[index] = CachedSymbolDisplay.GetFullyQualifiedName(symbols[symbolIndex]);
        });
        for (var i = 0; i < results.Length; i++)
        {
            Assert.Equal(expected[i % symbols.Length], results[i]);
            Assert.Same(CachedSymbolDisplay.GetFullyQualifiedName(symbols[i % symbols.Length]), results[i]);
        }
    }

    [Fact]
    public void CachedValue_DoesNotRootTheSymbolOrItsCompilation()
    {
        var discarded = CreateDiscardedSymbol();
        for (var attempt = 0; attempt < 10 && (discarded.Symbol.IsAlive || discarded.Compilation.IsAlive); attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        Assert.False(discarded.Symbol.IsAlive);
        Assert.False(discarded.Compilation.IsAlive);
        GC.KeepAlive(discarded.Display);
    }

    [Fact]
    public void NullSymbol_IsRejectedByTheHelper()
    {
        Assert.Equal("symbol", Assert.Throws<ArgumentNullException>(() => CachedSymbolDisplay.GetFullyQualifiedName(null!)).ParamName);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Symbol, WeakReference Compilation, string Display) CreateDiscardedSymbol()
    {
        var compilation = Compile("namespace Isolated; public class Discarded {}");
        var symbol = compilation.GetTypeByMetadataName("Isolated.Discarded")!;
        var display = CachedSymbolDisplay.GetFullyQualifiedName(symbol);
        return (new WeakReference(symbol), new WeakReference(compilation), display);
    }

    private static CSharpCompilation Compile(string source) => CSharpCompilation.Create(
        "SymbolDisplay_" + Guid.NewGuid().ToString("N"),
        [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))],
        [s_core], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));

    public class EqualTypeProxy : DispatchProxy
    {
        public ITypeSymbol Target { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Invoke(Target, args);
        public override bool Equals(object? obj) => obj is EqualTypeProxy;
        public override int GetHashCode() => 1;
    }
}
