using Akbura.Language.CodeGeneration;
using CSharpSymbolDefinition = Akbura.Language.Symbols.CSharpSymbolDefinition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class CSharpValueWriterTests
{
    [Theory]
    [InlineData("value", "value")]
    [InlineData("class", "@class")]
    [InlineData("record", "@record")]
    public void WriteIdentifier_EscapesKeywords(string identifier, string expected)
    {
        Assert.Equal(expected, WriteIdentifier(identifier));
    }

    [Fact]
    public void WriteTypeName_WritesFullyQualifiedTypeAndFallsBackForErrors()
    {
        var fixture = CreateFixture();
        var payloadType = fixture.GetRequiredType("Demo.Payload");
        var containerType = fixture.GetRequiredType("Demo.Container`1").Construct(payloadType);
        var errorType = CreateContainingErrorType();

        Assert.Equal("global::Demo.Container<global::Demo.Payload>", WriteTypeName(containerType));
        Assert.Equal("global::System.Object", WriteTypeName((ITypeSymbol?)null));
        Assert.Equal("global::System.Object", WriteTypeName(errorType));
    }

    [Fact]
    public void WriteTypeNameWithNullableAnnotation_PreservesNullableReferenceTypes()
    {
        var fixture = CreateFixture();
        var payloadType = fixture.GetRequiredType("Demo.Payload");
        var nullablePayload = payloadType.WithNullableAnnotation(NullableAnnotation.Annotated);
        using var codeWriter = new CodeWriter("\n");
        var valueWriter = new CSharpValueWriter(codeWriter);

        valueWriter.WriteTypeNameWithNullableAnnotation(nullablePayload);

        Assert.Equal("global::Demo.Payload?", codeWriter.GetText().ToString());
    }

    [Fact]
    public void StaticMemberReferences_WriteQualifiedEscapedNamesAndRejectOthers()
    {
        var fixture = CreateFixture();
        var constantsType = fixture.GetRequiredType("Demo.Constants");
        var field = Assert.Single(constantsType.GetMembers("event").OfType<IFieldSymbol>());
        var property = Assert.Single(constantsType.GetMembers("class").OfType<IPropertySymbol>());
        var membersType = fixture.GetRequiredType("Demo.Members");
        var instanceField = Assert.Single(membersType.GetMembers("Instance").OfType<IFieldSymbol>());
        var method = Assert.Single(membersType.GetMembers("Method").OfType<IMethodSymbol>());

        Assert.Equal("global::Demo.Constants.@event", WriteStaticMemberReference(field));
        Assert.Equal("global::Demo.Constants.@class", WriteStaticMemberReference(property));
        Assert.Equal("global::Demo.Constants.@event", WriteConstant(new CSharpSymbolDefinition(field)));

        using var codeWriter = new CodeWriter("\n");
        var valueWriter = new CSharpValueWriter(codeWriter);

        Assert.False(valueWriter.TryWriteStaticMemberReference(instanceField));
        Assert.False(valueWriter.TryWriteStaticMemberReference(method));
        Assert.False(valueWriter.TryWriteStaticMemberReference(null));
        Assert.Equal(string.Empty, codeWriter.GetText().ToString());
    }

    [Fact]
    public void WriteConstant_WritesNonNumericValues()
    {
        var fixture = CreateFixture();
        var payloadType = fixture.GetRequiredType("Demo.Payload");

        Assert.Equal("null", WriteConstant(null));
        Assert.Equal("true", WriteConstant(true));
        Assert.Equal("\"line\\n\\\"quoted\\\"\"", WriteConstant("line\n\"quoted\""));
        Assert.Equal("'\\0'", WriteConstant('\0'));
        Assert.Equal("'\\n'", WriteConstant('\n'));
        Assert.Equal("'\\\''", WriteConstant('\''));
        Assert.Equal("'\\\\'", WriteConstant('\\'));
        Assert.Equal("'\\u0001'", WriteConstant('\u0001'));
        Assert.Equal("typeof(global::Demo.Payload)", WriteConstant(payloadType));
    }

    [Fact]
    public void WriteConstant_WritesIntegralAndDecimalBoundaries()
    {
        Assert.Equal("255", WriteConstant(byte.MaxValue));
        Assert.Equal("-128", WriteConstant(sbyte.MinValue));
        Assert.Equal("-2147483648", WriteConstant(int.MinValue));
        Assert.Equal("2147483647u", WriteConstant((uint)int.MaxValue));
        Assert.Equal("4294967295u", WriteConstant(uint.MaxValue));
        Assert.Equal("-9223372036854775808L", WriteConstant(long.MinValue));
        Assert.Equal("9223372036854775807L", WriteConstant(long.MaxValue));
        Assert.Equal("2147483647UL", WriteConstant((ulong)int.MaxValue));
        Assert.Equal("18446744073709551615UL", WriteConstant(ulong.MaxValue));
        Assert.Equal("-79228162514264337593543950335m", WriteConstant(decimal.MinValue));
        Assert.Equal("79228162514264337593543950335m", WriteConstant(decimal.MaxValue));
    }

    [Fact]
    public void WriteConstant_WritesRoundTripFloatingPointValues()
    {
        Assert.Equal("1E-45f", WriteConstant(float.Epsilon));
        Assert.Equal("3.4028235E+38f", WriteConstant(float.MaxValue));
        Assert.Equal("-0f", WriteConstant(-0.0f));
        Assert.Equal("5E-324d", WriteConstant(double.Epsilon));
        Assert.Equal("1.7976931348623157E+308d", WriteConstant(double.MaxValue));
        Assert.Equal("-0d", WriteConstant(-0.0d));
    }

    [Fact]
    public void WriteConstant_WritesNamedFloatingPointValues()
    {
        Assert.Equal("global::System.Single.NaN", WriteConstant(float.NaN));
        Assert.Equal("global::System.Single.PositiveInfinity", WriteConstant(float.PositiveInfinity));
        Assert.Equal("global::System.Single.NegativeInfinity", WriteConstant(float.NegativeInfinity));
        Assert.Equal("global::System.Double.NaN", WriteConstant(double.NaN));
        Assert.Equal("global::System.Double.PositiveInfinity", WriteConstant(double.PositiveInfinity));
        Assert.Equal("global::System.Double.NegativeInfinity", WriteConstant(double.NegativeInfinity));
    }

    [Fact]
    public void WriteConstant_WritesSignedAndUnsignedEnumCasts()
    {
        var fixture = CreateFixture();
        var signedEnum = fixture.GetRequiredType("Demo.SignedEnum");
        var unsignedEnum = fixture.GetRequiredType("Demo.UnsignedEnum");
        var nullableSignedEnum = fixture.Compilation
            .GetSpecialType(SpecialType.System_Nullable_T)
            .Construct(signedEnum);

        Assert.Equal("(global::Demo.SignedEnum)(-1L)", WriteConstant(-1L, signedEnum));
        Assert.Equal(
            "(global::Demo.SignedEnum)(-9223372036854775808L)",
            WriteConstant(long.MinValue, signedEnum));
        Assert.Equal(
            "unchecked((global::Demo.UnsignedEnum)" + "18446744073709551615UL)",
            WriteConstant(ulong.MaxValue, unsignedEnum));
        Assert.Equal("(global::Demo.SignedEnum)1L", WriteConstant(1, nullableSignedEnum));
    }

    [Fact]
    public void GeneratedValueExpressions_Compile()
    {
        var fixture = CreateFixture();
        var constantsType = fixture.GetRequiredType("Demo.Constants");
        var staticField = Assert.Single(constantsType.GetMembers("event").OfType<IFieldSymbol>());
        var payloadType = fixture.GetRequiredType("Demo.Payload");
        var signedEnum = fixture.GetRequiredType("Demo.SignedEnum");
        var unsignedEnum = fixture.GetRequiredType("Demo.UnsignedEnum");
        var nullableSignedEnum = fixture.Compilation
            .GetSpecialType(SpecialType.System_Nullable_T)
            .Construct(signedEnum);
        var expressions = new[]
        {
            WriteConstant(new CSharpSymbolDefinition(staticField)),
            WriteConstant(payloadType),
            WriteConstant("line\n\"quoted\""),
            WriteConstant('\ud800'),
            WriteConstant(uint.MaxValue),
            WriteConstant(long.MinValue),
            WriteConstant(ulong.MaxValue),
            WriteConstant(decimal.MinValue),
            WriteConstant(float.Epsilon),
            WriteConstant(float.NaN),
            WriteConstant(double.MaxValue),
            WriteConstant(double.NegativeInfinity),
            WriteConstant(long.MinValue, signedEnum),
            WriteConstant(ulong.MaxValue, unsignedEnum),
            WriteConstant(1, nullableSignedEnum),
        };
        var generatedExpressions = string.Join(",\n            ", expressions);
        var generatedSource = $$"""
            #nullable enable

            namespace Generated;

            internal static class CSharpValueWriterOutput
            {
                public static object?[] CreateValues()
                {
                    return new object?[]
                    {
                        {{generatedExpressions}}
                    };
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "CSharpValueWriterOutput.g.cs");
        var compilation = fixture.Compilation.AddSyntaxTrees(syntaxTree);
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
    }

    private static TestFixture CreateFixture()
    {
        const string source = """
            #nullable enable

            namespace Demo;

            public sealed class Payload
            {
            }

            public sealed class Container<T>
            {
            }

            public static class Constants
            {
                public static readonly int @event = 42;

                public static int @class => 43;
            }

            public sealed class Members
            {
                public int Instance;

                public static void Method()
                {
                }
            }

            public enum SignedEnum : long
            {
                Min = long.MinValue,
            }

            public enum UnsignedEnum : ulong
            {
                Max = ulong.MaxValue,
            }
            """;
        var compilation = CSharpCompilation.Create(
            assemblyName: "CSharpValueWriterTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return new TestFixture(compilation);
    }

    private static ITypeSymbol CreateContainingErrorType()
    {
        const string source = """
            namespace Broken;

            public sealed class Outer<T>
            {
                public sealed class Inner<TValue>
                {
                }
            }

            public sealed class Holder
            {
                public Outer<Missing>.Inner<int> Value = null!;
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            assemblyName: "CSharpValueWriterErrorTypes",
            syntaxTrees: [syntaxTree],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var holder = Assert.IsType<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName("Broken.Holder"),
            exactMatch: false);
        var field = Assert.Single(holder.GetMembers("Value").OfType<IFieldSymbol>());

        return field.Type;
    }

    private static string WriteTypeName(ITypeSymbol? type)
    {
        using var codeWriter = new CodeWriter("\n");
        var valueWriter = new CSharpValueWriter(codeWriter);

        valueWriter.WriteTypeName(type);

        return codeWriter.GetText().ToString();
    }

    private static string WriteIdentifier(string identifier)
    {
        using var codeWriter = new CodeWriter("\n");
        var valueWriter = new CSharpValueWriter(codeWriter);

        valueWriter.WriteIdentifier(identifier);

        return codeWriter.GetText().ToString();
    }

    private static string WriteStaticMemberReference(ISymbol symbol)
    {
        using var codeWriter = new CodeWriter("\n");
        var valueWriter = new CSharpValueWriter(codeWriter);

        valueWriter.WriteStaticMemberReference(symbol);

        return codeWriter.GetText().ToString();
    }

    private static string WriteConstant(object? value, ISymbol? targetType = null)
    {
        using var codeWriter = new CodeWriter("\n");
        var valueWriter = new CSharpValueWriter(codeWriter);

        valueWriter.WriteConstant(value, targetType);

        return codeWriter.GetText().ToString();
    }

    private sealed class TestFixture(CSharpCompilation compilation)
    {
        public CSharpCompilation Compilation { get; } = compilation;

        public INamedTypeSymbol GetRequiredType(string metadataName)
        {
            return Assert.IsType<INamedTypeSymbol>(
                Compilation.GetTypeByMetadataName(metadataName),
                exactMatch: false);
        }
    }
}
