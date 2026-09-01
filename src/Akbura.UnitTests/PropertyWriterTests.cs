using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using CSharpPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.UnitTests;

public sealed class PropertyWriterTests
{
    [Fact]
    public void Write_ClrProperty_WritesTypedAssignment()
    {
        var fixture = CreateFixture();

        var output = WriteProperty(
            fixture,
            CreateClrProperty(fixture),
            "button",
            "\"hello\"");

        Assert.Equal(
            "((global::Demo.Control)button).Text = \"hello\";",
            output);
    }

    [Fact]
    public void Write_AvaloniaProperty_WritesSetValueInvocation()
    {
        var fixture = CreateFixture();

        var output = WriteProperty(
            fixture,
            CreateAvaloniaProperty(fixture),
            "button",
            "42");

        Assert.Equal(
            "((global::Avalonia.AvaloniaObject)button).SetValue(" +
            "global::Demo.Control.CountProperty, 42);",
            output);
    }

    [Fact]
    public void Write_AttachedAccessor_WritesStaticSetterInvocation()
    {
        var fixture = CreateFixture();

        var output = WriteProperty(
            fixture,
            CreateAttachedProperty(fixture),
            "button",
            "3");

        Assert.Equal(
            "global::Demo.Grid.SetRow(" +
            "(global::Demo.Control)button, 3);",
            output);
    }

    [Fact]
    public void Write_Parameter_WritesEscapedDirectMemberAssignment()
    {
        var fixture = CreateFixture();
        var property = CreateParameterProperty(fixture);
        var plan = PropertyWritePlan.Create(property);

        var output = WriteProperty(
            fixture,
            in plan,
            "button",
            "7");

        Assert.Equal(PropertyWriteKind.DirectMember, plan.Kind);
        Assert.Equal("button.@class = 7;", output);
    }

    [Fact]
    public void Write_Command_WritesDirectMemberAssignment()
    {
        var fixture = CreateFixture();
        var property = CreateCommandProperty(fixture);
        var plan = PropertyWritePlan.Create(property);

        var output = WriteProperty(
            fixture,
            in plan,
            "button",
            "null");

        Assert.Equal(PropertyWriteKind.DirectMember, plan.Kind);
        Assert.Equal("button.Execute = null;", output);
    }

    [Fact]
    public void WriteStartAndEnd_AllowDirectValueEmission()
    {
        var fixture = CreateFixture();
        var plan = PropertyWritePlan.Create(
            CreateAvaloniaProperty(fixture));
        using var codeWriter = new CodeWriter("\n");
        var propertyWriter = new PropertyWriter(codeWriter);

        var end = propertyWriter.WriteStart(
            in plan,
            "button");

        Assert.Equal(
            "((global::Avalonia.AvaloniaObject)button).SetValue(" +
            "global::Demo.Control.CountProperty, ",
            codeWriter.GetText().ToString());

        codeWriter.Write("CreateValue()");
        propertyWriter.WriteEnd(end);

        Assert.Equal(
            "((global::Avalonia.AvaloniaObject)button).SetValue(" +
            "global::Demo.Control.CountProperty, CreateValue());",
            codeWriter.GetText().ToString());
    }

    [Fact]
    public void GeneratedStatements_Compile()
    {
        var fixture = CreateFixture();
        var statements = new[]
        {
            WriteProperty(
                fixture,
                CreateClrProperty(fixture),
                "button",
                "\"hello\""),
            WriteProperty(
                fixture,
                CreateAvaloniaProperty(fixture),
                "button",
                "42"),
            WriteProperty(
                fixture,
                CreateAttachedProperty(fixture),
                "button",
                "3"),
            WriteProperty(
                fixture,
                CreateParameterProperty(fixture),
                "button",
                "7"),
            WriteProperty(
                fixture,
                CreateCommandProperty(fixture),
                "button",
                "null"),
        };
        var generatedStatements = string.Join(
            "\n        ",
            statements);
        var generatedSource =
            $$"""
            #nullable enable

            namespace Generated;

            internal static class PropertyWriterOutput
            {
                private static int CreateValue() => 1;

                public static void Apply(global::Demo.Control button)
                {
                    {{generatedStatements}}
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(
                LanguageVersion.Preview),
            path: "PropertyWriterOutput.g.cs");
        var compilation = fixture.Compilation.AddSyntaxTrees(
            syntaxTree);
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic =>
                    diagnostic.ToString())));
    }

    private static TestFixture CreateFixture()
    {
        const string csharpSource =
            """
            #nullable enable

            namespace Demo;

            public sealed class Control : global::Avalonia.AvaloniaObject
            {
                public static readonly global::Avalonia.StyledProperty<int>
                    CountProperty =
                        global::Avalonia.AvaloniaProperty.Register<Control, int>(
                            "Count");

                public string Text { get; set; } = "";

                public int @class { get; set; }

                public object? Execute { get; set; }
            }

            public static class Grid
            {
                public static void SetRow(Control target, int value)
                {
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            assemblyName: "PropertyWriterTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    csharpSource,
                    CSharpParseOptions.Default.WithLanguageVersion(
                        LanguageVersion.Preview)),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions:
                    NullableContextOptions.Enable));

        return new TestFixture(compilation);
    }

    private static PropertySymbol CreateClrProperty(
        TestFixture fixture)
    {
        var property = GetProperty(
            fixture.ControlType,
            "Text");

        return new PropertySymbol(
            property.Name,
            new CSharpSymbolDefinition(property.Type),
            clrPropertyDefinition:
                new CSharpSymbolDefinition(property));
    }

    private static PropertySymbol CreateAvaloniaProperty(
        TestFixture fixture)
    {
        var field = Assert.Single(
            fixture.ControlType.GetMembers("CountProperty")
                .OfType<IFieldSymbol>());
        var intType = fixture.Compilation.GetSpecialType(
            SpecialType.System_Int32);

        return new PropertySymbol(
            "Count",
            new CSharpSymbolDefinition(intType),
            avaloniaPropertyDefinition:
                new CSharpSymbolDefinition(field));
    }

    private static PropertySymbol CreateAttachedProperty(
        TestFixture fixture)
    {
        var gridType = fixture.GetRequiredType("Demo.Grid");
        var setter = Assert.Single(
            gridType.GetMembers("SetRow")
                .OfType<IMethodSymbol>());
        var intType = fixture.Compilation.GetSpecialType(
            SpecialType.System_Int32);

        return new PropertySymbol(
            "Row",
            new CSharpSymbolDefinition(intType),
            attachedSetterDefinition:
                new CSharpSymbolDefinition(setter),
            attachedTargetType:
                new CSharpSymbolDefinition(fixture.ControlType));
    }

    private static PropertySymbol CreateParameterProperty(
        TestFixture fixture)
    {
        var syntaxTree = ComponentSyntaxTree.ParseText(
            "param int @class;");
        var syntax = Assert.Single(
            syntaxTree.GetRoot().Members
                .OfType<ParamDeclarationSyntax>());
        var intType = fixture.Compilation.GetSpecialType(
            SpecialType.System_Int32);
        var parameter = new ParamSymbol(
            syntax,
            new CSharpSymbolDefinition(intType),
            defaultValueType: default,
            hasExplicitType: true,
            bindingKind: ParamBindingKind.Default);

        return new PropertySymbol(
            "class",
            new CSharpSymbolDefinition(intType),
            parameter: parameter);
    }

    private static PropertySymbol CreateCommandProperty(
        TestFixture fixture)
    {
        var syntaxTree = ComponentSyntaxTree.ParseText(
            "command void Execute();");
        var syntax = Assert.Single(
            syntaxTree.GetRoot().Members
                .OfType<CommandDeclarationSyntax>());
        var voidType = fixture.Compilation.GetSpecialType(
            SpecialType.System_Void);
        var command = new CommandSymbol(
            syntax,
            new CSharpSymbolDefinition(voidType),
            new CSharpSymbolDefinition(voidType),
            ImmutableArray<ICommandParameterSymbol>.Empty,
            isVoid: true,
            isAsyncLike: false,
            hasResult: false);
        var objectType = fixture.Compilation.GetSpecialType(
            SpecialType.System_Object);

        return new PropertySymbol(
            "Execute",
            new CSharpSymbolDefinition(objectType),
            command: command);
    }

    private static CSharpPropertySymbol GetProperty(
        INamedTypeSymbol type,
        string name)
    {
        return Assert.Single(
            type.GetMembers(name)
                .OfType<CSharpPropertySymbol>());
    }

    private static string WriteProperty(
        TestFixture fixture,
        PropertySymbol property,
        string targetExpression,
        string valueExpression)
    {
        var plan = PropertyWritePlan.Create(property);

        return WriteProperty(
            fixture,
            in plan,
            targetExpression,
            valueExpression);
    }

    private static string WriteProperty(
        TestFixture fixture,
        in PropertyWritePlan plan,
        string targetExpression,
        string valueExpression)
    {
        using var codeWriter = new CodeWriter("\n");
        var propertyWriter = new PropertyWriter(codeWriter);

        var end = propertyWriter.WriteStart(
            in plan,
            targetExpression);
        codeWriter.Write(valueExpression);
        propertyWriter.WriteEnd(end);

        return codeWriter.GetText().ToString();
    }

    private sealed class TestFixture(
        CSharpCompilation compilation)
    {
        public CSharpCompilation Compilation { get; } =
            compilation;

        public INamedTypeSymbol ControlType =>
            GetRequiredType("Demo.Control");

        public INamedTypeSymbol GetRequiredType(
            string metadataName)
        {
            return Assert.IsType<INamedTypeSymbol>(
                Compilation.GetTypeByMetadataName(metadataName),
                exactMatch: false);
        }
    }
}
