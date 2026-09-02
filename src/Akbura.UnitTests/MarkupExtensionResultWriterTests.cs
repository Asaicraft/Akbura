using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class MarkupExtensionResultWriterTests
{
    private const string TargetExpression = "__element0";

    private const string PropertyExpression =
        "global::Avalonia.Controls.Border.BackgroundProperty";

    private const string ServiceProviderExpression =
        "CreateMarkupServiceProvider(" +
        "targetObject: __element0, " +
        "targetProperty: global::Avalonia.Controls.Border.BackgroundProperty, " +
        "intermediateRootObject: __root, " +
        "baseUri: __baseUri, " +
        "directParentsStack: __parents)";

    [Fact]
    public void BindingBaseWriteStartAndEnd_WriteBindWrapper()
    {
        var fixture = CreateFixture();
        using var codeWriter = new CodeWriter("\n");
        var environment = fixture.BindingEnvironment;
        var target = fixture.CreateTarget();
        var resultWriter = new BindingBaseResultWriter(
            codeWriter,
            in environment);

        resultWriter.WriteStart(target);
        codeWriter.Write("CreateBinding()");
        resultWriter.WriteEnd();

        Assert.Equal(
            "((global::Avalonia.AvaloniaObject)__element0).Bind(" +
            "global::Avalonia.Controls.Border.BackgroundProperty, " +
            "CreateBinding());",
            codeWriter.GetText().ToString());
    }

    [Fact]
    public void RuntimeWriteStartAndEnd_WriteApplyWrapper()
    {
        var fixture = CreateFixture();
        using var codeWriter = new CodeWriter("\n");
        var environment = fixture.BindingEnvironment;
        var target = fixture.CreateTarget();
        var resultWriter = new RuntimeMarkupExtensionResultWriter(
            codeWriter,
            in environment);

        resultWriter.WriteStart(target);
        codeWriter.Write("CreateValue()");
        resultWriter.WriteEnd();

        Assert.Equal(
            "ApplyMarkupExtensionResult(" +
            "(global::Avalonia.AvaloniaObject)__element0, " +
            "global::Avalonia.Controls.Border.BackgroundProperty, " +
            "CreateValue());",
            codeWriter.GetText().ToString());
    }

    [Fact]
    public void BindingBaseWriteBinding_WritesPlannedReflectionBinding()
    {
        var fixture = CreateFixture();
        var extension = CreateReflectionBinding(fixture);
        var environment = fixture.BindingEnvironment;
        var plan = BindingWritePlan.CreateInline(
            in environment,
            extension,
            scopeId: 0,
            nameScopeExpression: null);
        using var codeWriter = new CodeWriter("\n");
        var target = fixture.CreateTarget();
        var context = CreateWriteContext();
        var resultWriter = new BindingBaseResultWriter(
            codeWriter,
            in environment);

        Assert.True(plan.IsValid);

        resultWriter.WriteBinding(
            target,
            in plan,
            in context);

        Assert.Equal(
            "((global::Avalonia.AvaloniaObject)__element0).Bind(" +
            "global::Avalonia.Controls.Border.BackgroundProperty, " +
            "new global::Avalonia.Data.Binding(\"Name\"));",
            codeWriter.GetText().ToString());
    }

    [Fact]
    public void BindingBaseWriteMarkupExtension_WritesEvaluatedBindingResult()
    {
        var fixture = CreateFixture();
        var extension = CreateDeclaredExtension(
            fixture,
            "Demo.BindingResultExtension");
        var output = WriteBindingBaseMarkupExtension(
            fixture,
            extension);

        Assert.Equal(
            "((global::Avalonia.AvaloniaObject)__element0).Bind(" +
            "global::Avalonia.Controls.Border.BackgroundProperty, " +
            "(new global::Demo.BindingResultExtension()).ProvideValue(" +
            ServiceProviderExpression +
            "));",
            output);
    }

    [Fact]
    public void MarkupExtensionWrite_DefaultTargetProperty_WritesNullServiceProviderTarget()
    {
        var fixture = CreateFixture();
        var extension = CreateDeclaredExtension(
            fixture,
            "Demo.ValueResultExtension");
        var environment = fixture.BindingEnvironment;
        var context = CreateWriteContext(targetProperty: default);
        using var codeWriter = new CodeWriter("\n");
        var writer = new MarkupExtensionWriter(
            codeWriter,
            in environment);

        writer.Write(extension, context);

        Assert.Equal(
            "(new global::Demo.ValueResultExtension()).ProvideValue(" +
            "CreateMarkupServiceProvider(" +
            "targetObject: __element0, " +
            "targetProperty: null!, " +
            "intermediateRootObject: __root, " +
            "baseUri: __baseUri, " +
            "directParentsStack: __parents))",
            codeWriter.GetText().ToString());
    }

    [Fact]
    public void DynamicResourceWrite_EvaluatesProvideValueInsideBind()
    {
        var fixture = CreateFixture();
        var extension = CreateResourceExtension(
            fixture,
            "Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension");
        var output = WriteDynamicResource(
            fixture,
            extension);

        Assert.Equal(
            "((global::Avalonia.AvaloniaObject)__element0).Bind(" +
            "global::Avalonia.Controls.Border.BackgroundProperty, " +
            "(new global::Avalonia.Markup.Xaml.MarkupExtensions." +
            "DynamicResourceExtension(\"AccentBrush\")).ProvideValue(" +
            ServiceProviderExpression +
            "));",
            output);
    }

    [Fact]
    public void StaticResourceWrite_EvaluatesProvideValueInsideRuntimeApplication()
    {
        var fixture = CreateFixture();
        var extension = CreateResourceExtension(
            fixture,
            "Avalonia.Markup.Xaml.MarkupExtensions.StaticResourceExtension");
        var output = WriteStaticResource(
            fixture,
            extension);

        Assert.Equal(
            "ApplyMarkupExtensionResult(" +
            "(global::Avalonia.AvaloniaObject)__element0, " +
            "global::Avalonia.Controls.Border.BackgroundProperty, " +
            "(new global::Avalonia.Markup.Xaml.MarkupExtensions." +
            "StaticResourceExtension(\"AccentBrush\")).ProvideValue(" +
            ServiceProviderExpression +
            "));",
            output);
    }

    [Fact]
    public void GetResultKind_DynamicResourcePrecedesBindingBaseClassification()
    {
        var fixture = CreateFixture();
        var extension = CreateResourceExtension(
            fixture,
            "Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension");
        var environment = fixture.ResultEnvironment;

        Assert.Equal(
            "Avalonia.Data.BindingBase",
            extension.ResultType.Symbol?.ToDisplayString());
        Assert.Equal(
            MarkupExtensionResultKind.DynamicResource,
            environment.GetResultKind(extension));
    }

    [Fact]
    public void GetResultKind_ClassifiesSpecialAndRuntimeResultTypes()
    {
        var fixture = CreateFixture();
        var environment = fixture.ResultEnvironment;
        var missingResult = WithoutResultType(
            CreateDeclaredExtension(
                fixture,
                "Demo.ValueResultExtension"));

        Assert.Equal(
            MarkupExtensionResultKind.StaticResource,
            environment.GetResultKind(
                CreateResourceExtension(
                    fixture,
                    "Avalonia.Markup.Xaml.MarkupExtensions.StaticResourceExtension")));
        Assert.Equal(
            MarkupExtensionResultKind.BindingBase,
            environment.GetResultKind(
                CreateDeclaredExtension(
                    fixture,
                    "Demo.BindingResultExtension")));
        Assert.Equal(
            MarkupExtensionResultKind.Runtime,
            environment.GetResultKind(
                CreateDeclaredExtension(
                    fixture,
                    "Demo.ObjectResultExtension")));
        Assert.Equal(
            MarkupExtensionResultKind.Runtime,
            environment.GetResultKind(
                CreateDeclaredExtension(
                    fixture,
                    "Demo.DynamicResultExtension")));
        Assert.Equal(
            MarkupExtensionResultKind.Runtime,
            environment.GetResultKind(
                CreateDeclaredExtension(
                    fixture,
                    "Demo.UnsetResultExtension")));
        Assert.Equal(
            MarkupExtensionResultKind.Runtime,
            environment.GetResultKind(missingResult));
        Assert.Equal(
            MarkupExtensionResultKind.Value,
            environment.GetResultKind(
                CreateDeclaredExtension(
                    fixture,
                    "Demo.ValueResultExtension")));
    }

    [Fact]
    public void GetResultKind_UsesSymbolIdentityInsteadOfShortName()
    {
        var fixture = CreateFixture();
        var extension = CreateDeclaredExtension(
            fixture,
            "Demo.DynamicResourceExtension");
        var environment = fixture.ResultEnvironment;

        Assert.Equal(
            "DynamicResourceExtension",
            extension.ExtensionType.Symbol?.Name);
        Assert.Equal(
            MarkupExtensionResultKind.Value,
            environment.GetResultKind(extension));
    }

    [Fact]
    public void CreatePlan_PreservesExtensionKindAndValidity()
    {
        var fixture = CreateFixture();
        var extension = CreateResourceExtension(
            fixture,
            "Avalonia.Markup.Xaml.MarkupExtensions.StaticResourceExtension");
        var environment = fixture.ResultEnvironment;

        var plan = MarkupExtensionResultPlan.Create(
            in environment,
            extension);

        Assert.True(plan.IsValid);
        Assert.Same(extension, plan.Extension);
        Assert.Equal(
            MarkupExtensionResultKind.StaticResource,
            plan.Kind);
        Assert.False(default(MarkupExtensionResultPlan).IsValid);
    }

    [Fact]
    public void GeneratedDynamicAndStaticResourceStatements_Compile()
    {
        var fixture = CreateFixture();
        var dynamicStatement = WriteDynamicResource(
            fixture,
            CreateResourceExtension(
                fixture,
                "Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension"));
        var staticStatement = WriteStaticResource(
            fixture,
            CreateResourceExtension(
                fixture,
                "Avalonia.Markup.Xaml.MarkupExtensions.StaticResourceExtension"));
        var generatedSource =
            $$"""
            #nullable enable

            namespace Generated;

            internal static class MarkupExtensionResultOutput
            {
                public static void Apply(
                    global::Avalonia.Controls.Border __element0,
                    object __root,
                    object __baseUri,
                    object __parents)
                {
                    {{dynamicStatement}}
                    {{staticStatement}}
                }

                private static global::System.IServiceProvider
                    CreateMarkupServiceProvider(
                        object targetObject,
                        object targetProperty,
                        object intermediateRootObject,
                        object baseUri,
                        object directParentsStack)
                {
                    return null!;
                }

                private static void ApplyMarkupExtensionResult(
                    global::Avalonia.AvaloniaObject target,
                    global::Avalonia.AvaloniaProperty property,
                    object? value)
                {
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(
                LanguageVersion.Preview),
            path: "MarkupExtensionResultOutput.g.cs");
        var compilation = fixture.Compilation.AddSyntaxTrees(
            syntaxTree);
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
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

            public partial class ResultWriterHost
            {
            }

            public sealed class BindingResultExtension
            {
                public global::Avalonia.Data.Binding ProvideValue(
                    global::System.IServiceProvider serviceProvider)
                {
                    return new global::Avalonia.Data.Binding();
                }
            }

            public sealed class ObjectResultExtension
            {
                public object ProvideValue(
                    global::System.IServiceProvider serviceProvider)
                {
                    return new object();
                }
            }

            public sealed class DynamicResultExtension
            {
                public dynamic ProvideValue(
                    global::System.IServiceProvider serviceProvider)
                {
                    return new object();
                }
            }

            public sealed class UnsetResultExtension
            {
                public global::Avalonia.UnsetValueType ProvideValue(
                    global::System.IServiceProvider serviceProvider)
                {
                    return default!;
                }
            }

            public sealed class ValueResultExtension
            {
                public string ProvideValue(
                    global::System.IServiceProvider serviceProvider)
                {
                    return string.Empty;
                }
            }

            public sealed class DynamicResourceExtension
            {
                public string ProvideValue(
                    global::System.IServiceProvider serviceProvider)
                {
                    return string.Empty;
                }
            }
            """;
        var csharpCompilation = CSharpCompilation.Create(
            assemblyName: "MarkupExtensionResultWriterTests",
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
        var componentTree = ComponentSyntaxTree.ParseText(
            "using Avalonia.Controls; <Border />",
            "ResultWriterHost.akbura");
        var compilation = new AkburaCompilation(
            csharpCompilation,
            [componentTree],
            rootNamespace: "Demo");
        var semanticModel = compilation.GetSemanticModel(
            componentTree);
        var component = Assert.IsType<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(
                componentTree.GetRoot()).Symbol,
            exactMatch: false);
        var bindingEnvironment = BindingWriterEnvironment.Create(
            semanticModel,
            component);
        var resultEnvironment =
            MarkupExtensionResultEnvironment.Create(
                semanticModel);

        return new TestFixture(
            csharpCompilation,
            bindingEnvironment,
            resultEnvironment);
    }

    private static MarkupExtensionValue CreateResourceExtension(
        TestFixture fixture,
        string metadataName)
    {
        var extensionType = fixture.GetRequiredType(metadataName);
        var constructor = Assert.Single(
            extensionType.InstanceConstructors,
            static constructor =>
                constructor.DeclaredAccessibility ==
                    Accessibility.Public &&
                constructor.Parameters.Length == 1);
        var provideValue = Assert.Single(
            extensionType.GetMembers("ProvideValue")
                .OfType<IMethodSymbol>());
        var parameter = constructor.Parameters[0];
        const string extensionSuffix = "Extension";
        var name = extensionType.Name[..^extensionSuffix.Length];

        return new MarkupExtensionValue(
            rawText: name + " AccentBrush",
            name,
            new CSharpSymbolDefinition(extensionType),
            new CSharpSymbolDefinition(constructor),
            new CSharpSymbolDefinition(provideValue),
            new CSharpSymbolDefinition(provideValue.ReturnType),
            arguments:
            [
                new MarkupExtensionArgumentValue(
                    text: "AccentBrush",
                    new CSharpSymbolDefinition(parameter),
                    new CSharpSymbolDefinition(parameter.Type),
                    operation: default,
                    conversion: default,
                    convertedValue: "AccentBrush",
                    nestedValue: null),
            ],
            properties: []);
    }

    private static MarkupExtensionValue CreateDeclaredExtension(
        TestFixture fixture,
        string metadataName)
    {
        var extensionType = fixture.GetRequiredType(metadataName);
        var constructor = Assert.Single(
            extensionType.InstanceConstructors,
            static constructor =>
                constructor.DeclaredAccessibility ==
                    Accessibility.Public &&
                constructor.Parameters.Length == 0);
        var provideValue = Assert.Single(
            extensionType.GetMembers("ProvideValue")
                .OfType<IMethodSymbol>());

        return new MarkupExtensionValue(
            rawText: extensionType.Name,
            name: extensionType.Name,
            new CSharpSymbolDefinition(extensionType),
            new CSharpSymbolDefinition(constructor),
            new CSharpSymbolDefinition(provideValue),
            new CSharpSymbolDefinition(provideValue.ReturnType),
            arguments: [],
            properties: []);
    }

    private static MarkupExtensionValue WithoutResultType(
        MarkupExtensionValue extension)
    {
        return new MarkupExtensionValue(
            extension.RawText,
            extension.Name,
            extension.ExtensionType,
            extension.Constructor,
            extension.ProvideValueMethod,
            resultType: default,
            extension.Arguments,
            extension.Properties,
            extension.Binding,
            extension.IsUpdateDependent);
    }

    private static MarkupExtensionValue CreateReflectionBinding(
        TestFixture fixture)
    {
        var bindingType = fixture.GetRequiredType(
            "Avalonia.Data.Binding");
        var objectType = fixture.Compilation.GetSpecialType(
            SpecialType.System_Object);
        var binding = new MarkupBindingValue(
            MarkupBindingKind.Reflection,
            path: "Name",
            new CSharpSymbolDefinition(bindingType),
            new CSharpSymbolDefinition(objectType),
            new CSharpSymbolDefinition(objectType),
            pathElements: []);

        return new MarkupExtensionValue(
            rawText: "Binding Name",
            name: "Binding",
            new CSharpSymbolDefinition(bindingType),
            constructor: default,
            provideValueMethod: default,
            new CSharpSymbolDefinition(bindingType),
            arguments: [],
            properties: [],
            binding);
    }

    private static string WriteBindingBaseMarkupExtension(
        TestFixture fixture,
        MarkupExtensionValue extension)
    {
        using var codeWriter = new CodeWriter("\n");
        var environment = fixture.BindingEnvironment;
        var target = fixture.CreateTarget();
        var context = CreateWriteContext();
        var resultWriter = new BindingBaseResultWriter(
            codeWriter,
            in environment);

        resultWriter.WriteMarkupExtension(
            target,
            extension,
            in context);

        return codeWriter.GetText().ToString();
    }

    private static string WriteDynamicResource(
        TestFixture fixture,
        MarkupExtensionValue extension)
    {
        using var codeWriter = new CodeWriter("\n");
        var environment = fixture.BindingEnvironment;
        var target = fixture.CreateTarget();
        var context = CreateWriteContext();
        var resultEnvironment = fixture.ResultEnvironment;
        var plan = MarkupExtensionResultPlan.Create(
            in resultEnvironment,
            extension);
        var resultWriter = new DynamicResourceWriter(
            codeWriter,
            in environment);

        resultWriter.Write(
            target,
            in plan,
            in context);

        return codeWriter.GetText().ToString();
    }

    private static string WriteStaticResource(
        TestFixture fixture,
        MarkupExtensionValue extension)
    {
        using var codeWriter = new CodeWriter("\n");
        var environment = fixture.BindingEnvironment;
        var target = fixture.CreateTarget();
        var context = CreateWriteContext();
        var resultEnvironment = fixture.ResultEnvironment;
        var plan = MarkupExtensionResultPlan.Create(
            in resultEnvironment,
            extension);
        var resultWriter = new StaticResourceWriter(
            codeWriter,
            in environment);

        resultWriter.Write(
            target,
            in plan,
            in context);

        return codeWriter.GetText().ToString();
    }

    private static MarkupExtensionWriteContext CreateWriteContext()
    {
        var targetProperty = MarkupTargetPropertyPlan.CreateExpression(PropertyExpression);
        return CreateWriteContext(targetProperty);
    }

    private static MarkupExtensionWriteContext CreateWriteContext(
        MarkupTargetPropertyPlan targetProperty)
    {
        return new MarkupExtensionWriteContext(
            targetObjectExpression: TargetExpression,
            targetProperty: targetProperty,
            intermediateRootExpression: "__root",
            baseUriExpression: "__baseUri",
            directParentsStackExpression: "__parents",
            fallbackServiceProviderExpression: null,
            nameScopeExpression: null,
            scopeId: 0);
    }

    private sealed class TestFixture(
        CSharpCompilation compilation,
        BindingWriterEnvironment bindingEnvironment,
        MarkupExtensionResultEnvironment resultEnvironment)
    {
        public CSharpCompilation Compilation { get; } =
            compilation;

        public BindingWriterEnvironment BindingEnvironment { get; } =
            bindingEnvironment;

        public MarkupExtensionResultEnvironment ResultEnvironment { get; } =
            resultEnvironment;

        public AvaloniaPropertyWriteTarget CreateTarget()
        {
            var borderType = GetRequiredType(
                "Avalonia.Controls.Border");
            var property = Assert.Single(
                borderType.GetMembers("BackgroundProperty"),
                static symbol =>
                    symbol is IFieldSymbol { IsStatic: true } or
                        Microsoft.CodeAnalysis.IPropertySymbol
                        {
                            IsStatic: true,
                        });

            return new AvaloniaPropertyWriteTarget(
                TargetExpression,
                property);
        }

        public INamedTypeSymbol GetRequiredType(
            string metadataName)
        {
            return Assert.IsType<INamedTypeSymbol>(
                Compilation.GetTypeByMetadataName(metadataName),
                exactMatch: false);
        }
    }
}
