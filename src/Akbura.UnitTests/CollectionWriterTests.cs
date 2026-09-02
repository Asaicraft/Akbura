using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.UnitTests;

public sealed class CollectionWriterTests
{
    [Fact]
    public void Write_ClrCollection_WritesTypedAdd()
    {
        var fixture = CreateFixture();
        var plan = CreatePropertyPlan(CreateClrProperty(fixture), fixture.CollectionType);

        var output = Write(plan, "__target", "1");

        Assert.Equal(
            "((global::System.Collections.Generic.List<int>)" +
            "((global::Demo.Control)__target).Items!).Add(1);",
            output);
    }

    [Fact]
    public void Write_AvaloniaCollection_WritesTypedGetValueAdd()
    {
        var fixture = CreateFixture();
        var plan = CreatePropertyPlan(CreateAvaloniaProperty(fixture), fixture.CollectionType);

        var output = Write(plan, "__target", "2");

        Assert.Equal(
            "((global::System.Collections.Generic.List<int>)" +
            "((global::Avalonia.AvaloniaObject)__target).GetValue(" +
            "global::Demo.Control.ItemsProperty)!).Add(2);",
            output);
    }

    [Fact]
    public void Write_AttachedCollection_WritesTypedGetterAdd()
    {
        var fixture = CreateFixture();
        var plan = CreatePropertyPlan(CreateAttachedProperty(fixture), fixture.CollectionType);

        var output = Write(plan, "__target", "3");

        Assert.Equal(
            "((global::System.Collections.Generic.List<int>)" +
            "global::Demo.Attached.GetItems((global::Demo.Control)__target)!).Add(3);",
            output);
    }

    [Fact]
    public void Write_DirectCollection_WritesEscapedMemberAdd()
    {
        var fixture = CreateFixture();
        var plan = CreatePropertyPlan(CreateDirectProperty(fixture), fixture.CollectionType);

        var output = Write(plan, "__component", "4");

        Assert.Equal(
            "((global::System.Collections.Generic.List<int>)__component.@class!).Add(4);",
            output);
    }

    [Fact]
    public void Write_ComponentParameter_WritesPrecomputedHelper()
    {
        var fixture = CreateFixture();
        var plan = CollectionWritePlan.CreateComponentParameter(
            fixture.CollectionType,
            "__AkburaAddCollection_class");

        var output = Write(plan, "__component", "5");

        Assert.Equal("__component.__AkburaAddCollection_class(5);", output);
    }

    [Fact]
    public void WriteStartAndEnd_StreamEscapedElementReference()
    {
        var fixture = CreateFixture();
        var plan = CreatePropertyPlan(CreateClrProperty(fixture), fixture.CollectionType);
        using var codeWriter = new CodeWriter("\n");
        var collectionWriter = new CollectionWriter(codeWriter);
        var valueWriter = new ComponentValueWriter(codeWriter);

        Assert.True(collectionWriter.WriteStart(plan, "__target"));
        valueWriter.WriteElementReference("class");
        collectionWriter.WriteEnd();

        var output = codeWriter.GetText().ToString();
        Assert.Equal(
            "((global::System.Collections.Generic.List<int>)" +
            "((global::Demo.Control)__target).Items!).Add(@class);",
            output);
        Assert.DoesNotContain("dynamic", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_PreservesIndent()
    {
        var fixture = CreateFixture();
        var plan = CreatePropertyPlan(CreateClrProperty(fixture), fixture.CollectionType);

        var output = Write(plan, "__target", "1", indent: 6, out var finalIndent);

        Assert.Equal(6, finalIndent);
        Assert.StartsWith("((", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedStatements_Compile()
    {
        var fixture = CreateFixture();
        var statements = new[]
        {
            Write(CreatePropertyPlan(CreateClrProperty(fixture), fixture.CollectionType), "control", "1"),
            Write(CreatePropertyPlan(CreateAvaloniaProperty(fixture), fixture.CollectionType), "control", "2"),
            Write(CreatePropertyPlan(CreateAttachedProperty(fixture), fixture.CollectionType), "control", "3"),
            Write(CreatePropertyPlan(CreateDirectProperty(fixture), fixture.CollectionType), "component", "4"),
            Write(
                CollectionWritePlan.CreateComponentParameter(
                    fixture.CollectionType,
                    "__AkburaAddCollection_class"),
                "component",
                "5"),
        };
        var generatedStatements = string.Join("\n        ", statements);
        var generatedSource =
            $$"""
            #nullable enable

            namespace Generated;

            internal static class CollectionWriterOutput
            {
                public static void Apply(global::Demo.Control control, global::Demo.Component component)
                {
                    {{generatedStatements}}
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "CollectionWriterOutput.g.cs");
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
        const string source =
            """
            #nullable enable

            namespace Demo;

            public sealed class Control : global::Avalonia.AvaloniaObject
            {
                public static readonly global::Avalonia.StyledProperty<
                    global::System.Collections.Generic.List<int>> ItemsProperty =
                        global::Avalonia.AvaloniaProperty.Register<
                            Control,
                            global::System.Collections.Generic.List<int>>(
                                nameof(Items));

                public global::System.Collections.Generic.List<int> Items { get; } = new();
            }

            public static class Attached
            {
                public static global::System.Collections.Generic.List<int> GetItems(Control target)
                {
                    return target.Items;
                }
            }

            public sealed class Component
            {
                public global::System.Collections.Generic.List<int> @class { get; } = new();

                public void __AkburaAddCollection_class(int value)
                {
                    @class.Add(value);
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            assemblyName: "CollectionWriterTests",
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
        var controlType = GetRequiredType(compilation, "Demo.Control");
        var collectionType = Assert.IsAssignableFrom<ITypeSymbol>(
            Assert.Single(controlType.GetMembers("Items").OfType<RoslynPropertySymbol>()).Type);

        return new TestFixture(compilation, controlType, collectionType);
    }

    private static PropertySymbol CreateClrProperty(TestFixture fixture)
    {
        var property = Assert.Single(
            fixture.ControlType.GetMembers("Items").OfType<RoslynPropertySymbol>());

        return new PropertySymbol(
            property.Name,
            new CSharpSymbolDefinition(fixture.CollectionType),
            clrPropertyDefinition: new CSharpSymbolDefinition(property));
    }

    private static PropertySymbol CreateAvaloniaProperty(TestFixture fixture)
    {
        var field = Assert.Single(
            fixture.ControlType.GetMembers("ItemsProperty").OfType<IFieldSymbol>());

        return new PropertySymbol(
            "Items",
            new CSharpSymbolDefinition(fixture.CollectionType),
            avaloniaPropertyDefinition: new CSharpSymbolDefinition(field));
    }

    private static PropertySymbol CreateAttachedProperty(TestFixture fixture)
    {
        var attachedType = GetRequiredType(fixture.Compilation, "Demo.Attached");
        var getter = Assert.Single(attachedType.GetMembers("GetItems").OfType<IMethodSymbol>());

        return new PropertySymbol(
            "Items",
            new CSharpSymbolDefinition(fixture.CollectionType),
            attachedGetterDefinition: new CSharpSymbolDefinition(getter),
            attachedTargetType: new CSharpSymbolDefinition(fixture.ControlType));
    }

    private static PropertySymbol CreateDirectProperty(TestFixture fixture)
    {
        var syntaxTree = Akbura.Language.AkburaSyntaxTree.ParseText(
            "param bind object @class;");
        var syntax = Assert.Single(
            syntaxTree.GetRoot().Members.OfType<ParamDeclarationSyntax>());
        var parameter = new ParamSymbol(
            syntax,
            new CSharpSymbolDefinition(fixture.CollectionType),
            defaultValueType: default,
            hasExplicitType: true,
            bindingKind: ParamBindingKind.Bind);

        return new PropertySymbol(
            "class",
            new CSharpSymbolDefinition(fixture.CollectionType),
            parameter: parameter);
    }

    private static CollectionWritePlan CreatePropertyPlan(
        PropertySymbol property,
        ITypeSymbol collectionType)
    {
        var read = PropertyReadPlan.Create(property);
        Assert.True(read.IsValid);

        var plan = CollectionWritePlan.CreateProperty(read, collectionType);
        Assert.True(plan.IsValid);
        Assert.Equal(CollectionWriteKind.Property, plan.Kind);
        Assert.Same(collectionType, plan.CollectionType);

        return plan;
    }

    private static string Write(
        in CollectionWritePlan plan,
        string targetExpression,
        string valueExpression)
    {
        return Write(plan, targetExpression, valueExpression, indent: 0, out _);
    }

    private static string Write(
        in CollectionWritePlan plan,
        string targetExpression,
        string valueExpression,
        int indent,
        out int finalIndent)
    {
        using var codeWriter = new CodeWriter("\n")
        {
            CurrentIndent = indent,
        };
        var writer = new CollectionWriter(codeWriter);

        Assert.True(writer.WriteStart(plan, targetExpression));
        codeWriter.Write(valueExpression);
        writer.WriteEnd();
        finalIndent = codeWriter.CurrentIndent;

        var output = codeWriter.GetText().ToString();
        Assert.DoesNotContain("dynamic", output, StringComparison.Ordinal);
        return output;
    }

    private static INamedTypeSymbol GetRequiredType(
        CSharpCompilation compilation,
        string metadataName)
    {
        return Assert.IsType<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName(metadataName),
            exactMatch: false);
    }

    private sealed record TestFixture(
        CSharpCompilation Compilation,
        INamedTypeSymbol ControlType,
        ITypeSymbol CollectionType);
}
