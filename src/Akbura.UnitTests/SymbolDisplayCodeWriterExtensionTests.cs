using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class SymbolDisplayCodeWriterExtensionTests
{
    internal const string Shapes = """
        #nullable enable
        namespace @event.record
        {
            public class @class<@where> { public class @dynamic<T> {} }
            public class Р”Р°РЅРЅС‹Рµ {}
        }
        namespace Demo
        {
            public class Widget {}
            public struct Point {}
            public enum Choice { A }
            public interface Contract<out T> {}
            public delegate T Factory<T>();
            public class Outer<T> { public class Inner<U> { public class Leaf<V> {} } }
            public unsafe class Shapes<T>
            {
                public int Primitive;
                public void* VoidPointer;
                public object Object;
                public System.DateTime Metadata;
                public Widget Named;
                public Point Struct;
                public Choice Enum;
                public Contract<Widget> Interface;
                public Factory<T> Delegate;
                public Outer<int>.Inner<string>.Leaf<T> Nested;
                public Outer<Widget>.Inner<Widget[]> Generic;
                public int[] Array;
                public int[,,] Multi;
                public int[][,][,,] MixedRanks;
                public string[,][] ReversedRanks;
                public Outer<Widget>.Inner<T>[][,] GenericArrays;
                public T? AnnotatedParameter;
                public Widget? NullableReference;
                public int? NullableValue;
                public (int, string) Tuple;
                public (int first, string @event, T @class, long Item4, bool @record) NamedTuple;
                public (int,int,int,int,int,int,int,int,int) LongTuple;
                public ((int X, string Y) A, Widget B) NestedTuple;
                public T Parameter;
                public dynamic Dynamic;
                public nint NativeInt;
                public nuint NativeUInt;
                public System.IntPtr IntPtr;
                public System.UIntPtr UIntPtr;
                public int** Pointer;
                public int*[][,] PointerArrays;
                public global::@event.record.@class<int>.@dynamic<string> Keywords;
                public global::@event.record.Р”Р°РЅРЅС‹Рµ Unicode;
                public delegate*<int, void> FunctionPointer;
                public delegate* unmanaged[Cdecl, SuppressGCTransition]<ref int, out int, ref readonly long> CallingConventions;
                public MissingType Error;
                public Outer<MissingType>.Inner<int> GenericError;
                public void M() { var anon = new { Value = 1, Name = "name" }; }
            }
        }
        public class GlobalType {}
        file class FileType {}
        """;

    [Theory]
    [InlineData("Primitive")][InlineData("VoidPointer")][InlineData("Object")][InlineData("Metadata")]
    [InlineData("Named")][InlineData("Struct")][InlineData("Enum")][InlineData("Interface")][InlineData("Delegate")]
    [InlineData("Nested")][InlineData("Generic")][InlineData("Array")][InlineData("Multi")][InlineData("MixedRanks")]
    [InlineData("ReversedRanks")][InlineData("GenericArrays")][InlineData("AnnotatedParameter")]
    [InlineData("NullableReference")][InlineData("NullableValue")][InlineData("Tuple")][InlineData("NamedTuple")]
    [InlineData("LongTuple")][InlineData("NestedTuple")][InlineData("Parameter")][InlineData("Dynamic")]
    [InlineData("NativeInt")][InlineData("NativeUInt")][InlineData("IntPtr")][InlineData("UIntPtr")]
    [InlineData("Pointer")][InlineData("PointerArrays")][InlineData("Keywords")][InlineData("Unicode")]
    [InlineData("FunctionPointer")][InlineData("CallingConventions")][InlineData("Error")][InlineData("GenericError")]
    public void FieldTypes_ExactlyMatchRoslyn(string name)
    {
        var compilation = Compile();
        var symbol = ((IFieldSymbol)compilation.GetTypeByMetadataName("Demo.Shapes`1")!.GetMembers(name).Single()).Type;
        AssertDisplay(symbol);
    }

    [Theory]
    [InlineData("Demo.Outer`1")][InlineData("Demo.Outer`1+Inner`1+Leaf`1")]
    [InlineData("GlobalType")][InlineData("event.record.class`1+dynamic`1")]
    public void DefinitionsAndUnboundGenerics_ExactlyMatchRoslyn(string name)
    {
        var type = Compile().GetTypeByMetadataName(name)!;
        AssertDisplay(type);
        if (type.IsGenericType) AssertDisplay(type.ConstructUnboundGenericType());
    }

    [Fact]
    public void SpecialTypes_ArrayRanksAndConstructedShapes_MatchRoslyn()
    {
        var compilation = Compile();
        foreach (var kind in Enum.GetValues<SpecialType>().Where(kind => kind != SpecialType.None))
            AssertDisplay(compilation.GetSpecialType(kind));
        var widget = compilation.GetTypeByMetadataName("Demo.Widget")!;
        var generic = compilation.GetTypeByMetadataName("Demo.Outer`1")!;
        foreach (var rank in new[] { 1, 2, 3, 7 })
        {
            ITypeSymbol type = compilation.CreateArrayTypeSymbol(widget, rank);
            AssertDisplay(type);
            for (var depth = 0; depth < 12; depth++)
            {
                type = generic.Construct(compilation.CreateArrayTypeSymbol(type, depth % 3 + 1));
                AssertDisplay(type);
            }
        }
    }

    [Fact]
    public void AnonymousTypesAndNestedUnsupportedArguments_UseWholeNameFallback()
    {
        var compilation = Compile();
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<AnonymousObjectCreationExpressionSyntax>().Single();
        var anonymous = compilation.GetSemanticModel(tree).GetTypeInfo(syntax).Type!;
        var wrapper = compilation.GetTypeByMetadataName("Demo.Outer`1")!.Construct(anonymous);
        foreach (var type in new[] { anonymous, wrapper, compilation.CreateArrayTypeSymbol(wrapper) })
        {
            using var writer = new CodeWriter("\n");
            writer.Write("before<").WriteFullyQualifiedName(type).Write(">after");
            Assert.Equal("before<" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">after", writer.GetText().ToString());
            Assert.False(CachedSymbolDisplay.TryGetFullyQualifiedName(type, out _));
        }
    }

    [Fact]
    public void TupleRestFriendlyNamesAndNullableDefinition_MatchRoslyn()
    {
        var compilation = Compile();
        var rest = compilation.CreateTupleTypeSymbol(
            [compilation.GetSpecialType(SpecialType.System_Int32), compilation.GetSpecialType(SpecialType.System_String)],
            ["RestName", "AnotherName"]);
        var type = compilation.GetTypeByMetadataName("System.ValueTuple`8")!.Construct(
            Enumerable.Repeat<ITypeSymbol>(compilation.GetSpecialType(SpecialType.System_Int32), 7).Append(rest).ToArray());
        AssertDisplay(type);
        AssertDisplay(compilation.GetSpecialType(SpecialType.System_Nullable_T));
    }

    [Fact]
    public void SupportedMiss_DoesNotFormatAnIntermediateStringOrPopulateCache()
    {
        var target = Compile().GetTypeByMetadataName("Demo.Widget")!;
        var symbol = DispatchProxy.Create<INamedTypeSymbol, FormattingProxy>();
        var proxy = (FormattingProxy)(object)symbol;
        proxy.Target = target;
        proxy.ForbidDisplay = true;
        Assert.False(CachedSymbolDisplay.TryGetFullyQualifiedName(symbol, out _));
        AssertDisplay(symbol, target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Equal(0, proxy.DisplayCalls);
        Assert.False(CachedSymbolDisplay.TryGetFullyQualifiedName(symbol, out _));
    }

    [Fact]
    public void CachedHit_WritesTheExactPublishedStringWithoutTraversal()
    {
        var target = Compile().GetTypeByMetadataName("Demo.Widget")!;
        var symbol = DispatchProxy.Create<INamedTypeSymbol, FormattingProxy>();
        var proxy = (FormattingProxy)(object)symbol;
        proxy.Target = target;
        var cached = CachedSymbolDisplay.GetFullyQualifiedName(symbol);
        Assert.Equal(1, proxy.DisplayCalls);
        proxy.ForbidDisplay = true;
        proxy.ForbidName = true;
        using var writer = new CodeWriter("\n");
        Assert.Same(writer, writer.WriteFullyQualifiedName(symbol));
        Assert.Equal(cached, writer.GetText().ToString());
        var pages = (LinkedList<ReadOnlyMemory<char>[]>)typeof(CodeWriter)
            .GetField("_pages", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(writer)!;
        Assert.True(MemoryMarshal.TryGetString(pages.First!.Value[0], out var chunk, out _, out _));
        Assert.Same(cached, chunk);
        Assert.Equal(1, proxy.DisplayCalls);
    }

    [Fact]
    public void LookupMissAndDistinctInstances_PreserveIdentityAndOriginalApi()
    {
        var first = Compile().GetTypeByMetadataName("Demo.Widget")!;
        var second = Compile().GetTypeByMetadataName("Demo.Widget")!;
        Assert.NotSame(first, second);
        Assert.False(CachedSymbolDisplay.TryGetFullyQualifiedName(first, out _));
        Assert.False(CachedSymbolDisplay.TryGetFullyQualifiedName(first, out _));
        var name = CachedSymbolDisplay.GetFullyQualifiedName(first);
        Assert.True(CachedSymbolDisplay.TryGetFullyQualifiedName(first, out var published));
        Assert.Same(name, published);
        Assert.False(CachedSymbolDisplay.TryGetFullyQualifiedName(second, out _));
        AssertDisplay(second);
        Assert.False(CachedSymbolDisplay.TryGetFullyQualifiedName(second, out _));
    }

    [Fact]
    public void NullableFormat_IsKeptSeparateFromTheStandardCache()
    {
        var type = Compile().GetTypeByMetadataName("Demo.Widget")!.WithNullableAnnotation(NullableAnnotation.Annotated);
        var cached = CachedSymbolDisplay.GetFullyQualifiedName(type);
        using var writer = new CodeWriter("\n");
        var values = new CSharpValueWriter(writer);
        values.WriteTypeName(type);
        writer.Write(" | ");
        values.WriteTypeNameWithNullableAnnotation(type);
        var format = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        Assert.Equal(cached + " | " + type.ToDisplayString(format), writer.GetText().ToString());
    }

    [Fact]
    public void ValueWriter_PreservesNullAndNestedErrorFallbacks()
    {
        var compilation = Compile();
        var error = compilation.CreateErrorTypeSymbol(compilation.GlobalNamespace, "Missing", 0);
        ITypeSymbol[] types = [error, compilation.CreateArrayTypeSymbol(error), compilation.CreatePointerTypeSymbol(error),
            compilation.GetTypeByMetadataName("Demo.Outer`1")!.Construct(error)];
        foreach (var type in Enumerable.Append(types, (ITypeSymbol)null!))
        {
            using var writer = new CodeWriter();
            new CSharpValueWriter(writer).WriteTypeName(type);
            Assert.Equal("global::System.Object", writer.GetText().ToString());
        }
    }

    [Fact]
    public void ConcurrentAndAdjacentWrites_PreserveIndependentOutput()
    {
        var compilation = Compile();
        var symbols = compilation.GetTypeByMetadataName("Demo.Shapes`1")!.GetMembers().OfType<IFieldSymbol>()
            .Select(field => field.Type).ToArray();
        var expected = string.Join("; ", symbols.Select(type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        Parallel.For(0, 128, iteration =>
        {
            using var writer = new CodeWriter(iteration % 2 == 0 ? "\n" : "\r\n");
            for (var i = 0; i < symbols.Length; i++)
            {
                if (i != 0) writer.Write("; ");
                writer.WriteFullyQualifiedName(symbols[i]);
            }
            Assert.Equal(expected, writer.GetText().ToString());
        });
    }

    [Fact]
    public void Visitor_DoesNotRetainWriterSymbolOrCompilation()
    {
        var references = WriteAndRelease();
        for (var i = 0; i < 5; i++)
        {
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        }
        Assert.All(references, reference => Assert.False(reference.IsAlive));
    }

    [Fact]
    public void NullArguments_AreRejectedWithExistingParameterNames()
    {
        var type = Compile().GetTypeByMetadataName("Demo.Widget")!;
        Assert.Equal("writer", Assert.Throws<ArgumentNullException>(() => SymbolDisplayCodeWriterExtension.WriteFullyQualifiedName(null!, type)).ParamName);
        using var writer = new CodeWriter();
        Assert.Equal("symbol", Assert.Throws<ArgumentNullException>(() => writer.WriteFullyQualifiedName(null!)).ParamName);
        Assert.Equal("symbol", Assert.Throws<ArgumentNullException>(() => CachedSymbolDisplay.TryGetFullyQualifiedName(null!, out _)).ParamName);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] WriteAndRelease()
    {
        var compilation = Compile();
        var symbol = compilation.GetTypeByMetadataName("Demo.Widget")!;
        using var writer = new CodeWriter();
        writer.WriteFullyQualifiedName(symbol);
        return [new WeakReference(compilation), new WeakReference(symbol), new WeakReference(writer)];
    }

    private static void AssertDisplay(ITypeSymbol symbol, string? expected = null)
    {
        expected ??= symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        using var writer = new CodeWriter("\n");
        Assert.Same(writer, writer.WriteFullyQualifiedName(symbol));
        Assert.Equal(expected, writer.GetText().ToString());
    }

    internal static CSharpCompilation Compile() => CSharpCompilation.Create(
        "Streaming_" + Guid.NewGuid().ToString("N"),
        [CSharpSyntaxTree.ParseText(Shapes, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))],
        [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));

    public class FormattingProxy : DispatchProxy
    {
        public INamedTypeSymbol Target { get; set; } = null!;
        public bool ForbidDisplay { get; set; }
        public bool ForbidName { get; set; }
        public int DisplayCalls { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "ToDisplayString")
            {
                DisplayCalls++;
                if (ForbidDisplay) throw new InvalidOperationException("Unexpected string formatting on the streaming path.");
            }
            if (method.Name == "get_Name" && ForbidName) throw new InvalidOperationException("Cached names must not traverse symbols.");
            return method.Invoke(Target, args);
        }
    }
}
