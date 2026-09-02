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
    public void WriteField_ForLocal_WritesNothing()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("__element0", isLocal: true);

        Assert.Equal(
            string.Empty,
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteField(plan),
                element));
    }

    [Fact]
    public void WriteCreation_ForLocal_WritesVarDeclaration()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("__element0", isLocal: true);

        Assert.Equal(
            "var __element0 = new global::Demo.Widget();\r\n",
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteCreation(plan),
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
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteCreation(plan),
                element));
    }

    [Fact]
    public void WriteCreation_EscapesKeywordIdentifier()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("class", isLocal: true);

        Assert.Equal(
            "var @class = new global::Demo.Widget();\r\n",
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteCreation(plan),
                element));
    }

    [Fact]
    public void WriteCreation_WritesFullyQualifiedGenericType()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("items", fixture.GenericWidgetType, isLocal: true);

        Assert.Equal(
            "var items = new global::Demo.Widget<string>();\r\n",
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteCreation(plan),
                element));
    }

    [Fact]
    public void WriteBeginInit_WhenSupported_WritesInvocation()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("class", supportsInitialize: true);

        Assert.Equal(
            "((global::System.ComponentModel.ISupportInitialize)@class).BeginInit();\r\n",
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
            "((global::System.ComponentModel.ISupportInitialize)@class).EndInit();\r\n",
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteEndInit(plan),
                element));
    }

    [Fact]
    public void InitializationMethods_WhenUnsupported_WriteNothing()
    {
        var fixture = CreateFixture();
        var element = fixture.CreateElement("__element0", isLocal: true);

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
        var element = fixture.CreateElement("__element0", isLocal: true);

        Assert.Equal(
            "#line (1,1)-(1,11) 0 \"Views/Widget.akbura\"\r\n" +
            "var __element0 = new global::Demo.Widget();\r\n" +
            "#line default\r\n" +
            "#line hidden\r\n",
            Write(
                fixture,
                static (ElementWriter writer, in ComponentElementPlan plan) => writer.WriteCreation(plan),
                element));
    }

    [Fact]
    public void GeneratedElementFragment_WithExplicitInitializationImplementation_Compiles()
    {
        var fixture = CreateFixture();
        var field = fixture.CreateElement("_widget", supportsInitialize: true);
        var local = fixture.CreateElement("local", supportsInitialize: true, isLocal: true);
        using var codeWriter = new CodeWriter();
        var writer = new ElementWriter(codeWriter, new ComponentGenerationSourceMap(fixture.SyntaxTree));

        codeWriter.WriteLine("namespace Demo.Generated;");
        codeWriter.WriteLine();
        codeWriter.WriteLine("public sealed class GeneratedComponent");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 4;
        writer.WriteField(field);
        codeWriter.WriteLine();
        codeWriter.WriteLine("public void Build()");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 8;
        writer.WriteCreation(field);
        writer.WriteBeginInit(field);
        writer.WriteCreation(local);
        writer.WriteBeginInit(local);
        writer.WriteEndInit(local);
        writer.WriteEndInit(field);
        codeWriter.CurrentIndent = 4;
        codeWriter.WriteLine("}");
        codeWriter.CurrentIndent = 0;
        codeWriter.WriteLine("}");

        var generatedSource = codeWriter.GetText().ToString();
        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var diagnostics = fixture.Compilation.AddSyntaxTrees(generatedTree).GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + generatedSource);
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
            using System.ComponentModel;

            namespace Demo;

            public sealed class Widget : ISupportInitialize
            {
                void ISupportInitialize.BeginInit()
                {
                }

                void ISupportInitialize.EndInit()
                {
                }
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

        return new TestFixture(compilation, syntaxTree, syntax, widgetType, genericWidgetType);
    }

    private delegate void WriteAction(ElementWriter writer, in ComponentElementPlan element);

    private readonly record struct TestFixture(
        CSharpCompilation Compilation,
        ComponentSyntaxTree SyntaxTree,
        MarkupElementSyntax Syntax,
        ITypeSymbol WidgetType,
        ITypeSymbol GenericWidgetType)
    {
        public ComponentElementPlan CreateElement(
            string identifier,
            ITypeSymbol? type = null,
            bool supportsInitialize = false,
            bool isLocal = false)
        {
            var flags = supportsInitialize
                ? ComponentElementFlags.SupportsInitialize
                : ComponentElementFlags.None;
            if (isLocal)
            {
                flags |= ComponentElementFlags.IsLocal |
                    ComponentElementFlags.RequiresLocalMarkupContext;
            }

            return new ComponentElementPlan(
                id: 0,
                Syntax,
                type ?? WidgetType,
                identifier,
                parentId: -1,
                scopeId: isLocal ? 1 : 0,
                isLocal ? ComponentElementScopeKind.DataTemplate : ComponentElementScopeKind.Component,
                flags,
                children: default,
                propertyWrites: default,
                propertyElements: default,
                akcss: default);
        }
    }
}
