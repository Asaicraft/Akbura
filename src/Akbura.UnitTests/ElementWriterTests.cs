using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class ElementWriterTests
{
    [Fact]
    public void WriteField_WritesTypedNullableInitializedField()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("__element0");

        Assert.Equal(
            "private global::Demo.Widget __element0 = null!;\r\n",
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteField(plan),
                element));
    }

    [Fact]
    public void WriteCreation_ForLocal_WritesVarDeclaration()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("__element0");

        Assert.Equal(
            "var __element0 = new global::Demo.Widget();\r\n",
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteCreation(plan, true),
                element));
    }

    [Fact]
    public void WriteCreation_ForField_WritesAssignment()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("Header");

        Assert.Equal(
            "Header = new global::Demo.Widget();\r\n",
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteCreation(plan, false),
                element));
    }

    [Fact]
    public void WriteCreation_EscapesKeywordIdentifier()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("class");

        Assert.Equal(
            "var @class = new global::Demo.Widget();\r\n",
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteCreation(plan, true),
                element));
    }

    [Fact]
    public void WriteCreation_WritesFullyQualifiedGenericType()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("items", fixture.GenericWidgetType);

        Assert.Equal(
            "var items = new global::Demo.Widget<string>();\r\n",
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteCreation(plan, true),
                element));
    }

    [Fact]
    public void WriteBeginInit_WhenSupported_WritesInvocation()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("class", supportsInitialize: true);

        Assert.Equal(
            "@class.BeginInit();\r\n",
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteBeginInit(plan),
                element));
    }

    [Fact]
    public void WriteEndInit_WhenSupported_WritesInvocation()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("class", supportsInitialize: true);

        Assert.Equal(
            "@class.EndInit();\r\n",
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteEndInit(plan),
                element));
    }

    [Fact]
    public void InitializationMethods_WhenUnsupported_WriteNothing()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("__element0");

        Assert.Equal(
            string.Empty,
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) =>
                {
                    writer.WriteBeginInit(plan);
                    writer.WriteEndInit(plan);
                },
                element));
    }

    [Fact]
    public void WriteCreation_WithSourcePath_WritesSourceMapping()
    {
        var fixture = CreateFixture("Views/Widget.akbura");
        var element = fixture.CreateElement("__element0");

        Assert.Equal(
            "#line (1,1)-(1,11) 0 \"Views/Widget.akbura\"\r\n" +
            "var __element0 = new global::Demo.Widget();\r\n" +
            "#line default\r\n" +
            "#line hidden\r\n",
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteCreation(plan, true),
                element));
    }

    private static string Write(TestFixture fixture, WriteAction action, in ComponentElementPlan element)
    {
        using var codeWriter = new CodeWriter();
        var writer = new ElementWriter(codeWriter, new ComponentGenerationSourceMap(fixture.SyntaxTree));
        action(writer, element);
        return codeWriter.GetText().ToString();
    }

    private static TestFixture CreateFixture(string path = "")
    {
        const string source =
            """
            namespace Demo;

            public sealed class Widget
            {
            }

            public sealed class Widget<T>
            {
            }
            """;
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "ElementWriterTests",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            SymbolTests.CreateAvaloniaReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var widgetType = compilation.GetTypeByMetadataName("Demo.Widget")!;
        var genericWidgetType = compilation.GetTypeByMetadataName("Demo.Widget`1")!
            .Construct(compilation.GetSpecialType(SpecialType.System_String));
        var syntaxTree = ComponentSyntaxTree.ParseText("<Widget />", path);
        var syntax = Assert.Single(syntaxTree.GetRoot().Members.OfType<MarkupRootSyntax>()).Element;

        return new TestFixture(syntaxTree, syntax, widgetType, genericWidgetType);
    }

    private delegate void WriteAction(ElementWriter writer, in ComponentElementPlan element);

    private readonly record struct TestFixture(
        ComponentSyntaxTree SyntaxTree,
        MarkupElementSyntax Syntax,
        ITypeSymbol WidgetType,
        ITypeSymbol GenericWidgetType)
    {
        public ComponentElementPlan CreateElement(
            string identifier,
            ITypeSymbol? type = null,
            bool supportsInitialize = false)
        {
            var flags = supportsInitialize
                ? ComponentElementFlags.SupportsInitialize
                : ComponentElementFlags.None;

            return new ComponentElementPlan(
                id: 0,
                Syntax,
                type ?? WidgetType,
                identifier,
                parentId: -1,
                scopeOwnerId: 0,
                ComponentElementScopeKind.Component,
                flags,
                children: default,
                propertyWrites: default,
                propertyElements: default,
                akcss: default);
        }
    }
}
