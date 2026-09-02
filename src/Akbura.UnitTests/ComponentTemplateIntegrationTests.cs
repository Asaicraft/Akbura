using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComponentTemplateIntegrationTests
{
    [Fact]
    public async Task ComponentWriter_TypedTemplateRemainsLazyAndBuildsFreshTrees()
    {
        var fixture = CreateRuntimeFixture();

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var owner = fixture.CreateOwner();
                var beforeFactory = fixture.CreatedTreeCount;

                var template = fixture.CreateTemplate(owner);

                Assert.Equal(beforeFactory, fixture.CreatedTreeCount);
                Assert.True(template.Match(fixture.CreateItem("match probe")));
                Assert.False(template.Match(new object()));

                var templateType = template.GetType();
                Assert.True(templateType.IsGenericType);
                Assert.Equal(
                    typeof(FuncDataTemplate<>),
                    templateType.GetGenericTypeDefinition());
                Assert.Equal(
                    fixture.ItemType,
                    Assert.Single(templateType.GetGenericArguments()));

                var item = fixture.CreateItem("Template item");
                var firstRoot = Assert.IsAssignableFrom<StackPanel>(
                    template.Build(item));
                var secondRoot = Assert.IsAssignableFrom<StackPanel>(
                    template.Build(item));

                Assert.Equal(beforeFactory + 2, fixture.CreatedTreeCount);
                Assert.Equal(fixture.TemplateRootType, firstRoot.GetType());
                Assert.Equal(fixture.TemplateRootType, secondRoot.GetType());
                Assert.NotSame(firstRoot, secondRoot);

                var firstText = Assert.IsType<TextBlock>(
                    Assert.Single(firstRoot.Children));
                var secondText = Assert.IsType<TextBlock>(
                    Assert.Single(secondRoot.Children));

                Assert.NotSame(firstText, secondText);
                Assert.Equal("Template item", firstText.Text);
                Assert.Equal("Template item", secondText.Text);
            },
            CancellationToken.None);
    }

    private static RuntimeFixture CreateRuntimeFixture()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Item" x.ItemName="item">
                    <CountingPanel>
                        <TextBlock x.Name="message" Text={item.Name} />
                    </CountingPanel>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        const string csharp =
            """
            using Akbura;
            using Akbura.ComponentTree;
            using Akbura.Engine;
            using Avalonia;
            using Avalonia.Controls;
            using System.Collections.Immutable;

            namespace Demo;

            public sealed class Item
            {
                public string Name { get; set; } = string.Empty;
            }

            public sealed class CountingPanel : StackPanel
            {
                public CountingPanel()
                {
                    CreatedCount++;
                }

                public static int CreatedCount { get; private set; }
            }

            public partial class PlannerView : AkburaControl
            {
                public PlannerView()
                    : base(AkburaEngine.Empty)
                {
                }

                protected override Control FirstUpdate() => new Border();

                protected override Control Update() => new Border();

                protected override ImmutableArray<Parameter> GetParameters() => [];

                protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];

                protected override ImmutableArray<InjectService> GetServices() => [];

                protected override ImmutableArray<State> GetStates() => [];
            }
            """;
        var semanticFixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            csharp);
        using var codeWriter = new CodeWriter("\r\n");
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticFixture.SemanticModel.GetSymbolInfo(
                semanticFixture.ComponentTree.GetRoot()).Symbol);
        var componentWriter = new ComponentWriter(
            codeWriter,
            componentSymbol,
            semanticFixture.SemanticModel,
            new Dictionary<AkburaSyntax, string>());
        ref readonly var plan = ref componentWriter.Plan;
        ref readonly var componentScope = ref plan.Scopes.ItemRef(0);
        var template = Assert.Single(plan.Templates);
        var rootId = plan.ScopeRootElementIds[componentScope.Roots.Start];
        ref readonly var root = ref plan.Elements.ItemRef(rootId);

        Assert.Equal(ComponentElementScopeKind.Component, componentScope.Kind);
        Assert.Equal(root.Id, template.OwnerElementId);
        Assert.NotEqual(componentScope.Id, template.ScopeId);

        codeWriter.WriteLine("#nullable enable");
        codeWriter.WriteLine();
        codeWriter.WriteLine("namespace Demo;");
        codeWriter.WriteLine();
        codeWriter.WriteLine("public partial class PlannerView");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 4;
        codeWriter.WriteLine(
            "private static readonly global::System.Uri __akburaBaseUri = " +
            "new(\"avares://Demo/PlannerView.akbura\");");
        componentWriter.WriteElementFields();
        codeWriter.WriteLine();
        codeWriter.WriteLine(
            "public global::Avalonia.Controls.Templates.IDataTemplate " +
            "CreateRuntimeTemplate()");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 8;

        var bindingEnvironment = semanticFixture.CreateBindingEnvironment();
        var sourceMap = new ComponentGenerationSourceMap(
            Assert.IsType<ComponentSyntaxTree>(semanticFixture.ComponentTree));
        var scopeWriter = new ComponentScopeWriter(
            codeWriter,
            in bindingEnvironment,
            sourceMap,
            "global::Demo.PlannerView");
        var scopeContext = new ComponentScopeWriteContext(
            intermediateRootExpression: "this",
            baseUriExpression: "__akburaBaseUri",
            fallbackServiceProviderExpression: null,
            nameScopeExpression: null,
            scopeId: componentScope.Id,
            parentStackTraversalKind: MarkupParentStackTraversalKind.ExactScope,
            elements: plan.Elements.AsSpan(),
            elementReferences: plan.ElementReferences.AsSpan());
        scopeWriter.WriteInitialState(plan, componentScope, scopeContext);

        codeWriter.Write("return ");
        var valueWriter = new CSharpValueWriter(codeWriter);
        valueWriter.WriteIdentifier(root.Identifier);
        codeWriter.WriteLine(".ItemTemplate!;");
        codeWriter.CurrentIndent = 4;
        codeWriter.WriteLine("}");
        codeWriter.CurrentIndent = 0;
        codeWriter.WriteLine("}");

        var generatedSource = codeWriter.GetText().ToString();
        AssertBalancedSourceMappings(generatedSource);
        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "PlannerView.Template.Runtime.g.cs");
        var runtimeCompilation = semanticFixture.CSharpCompilation
            .AddSyntaxTrees(generatedTree)
            .WithAssemblyName(
                "ComponentTemplateIntegration_" +
                Guid.NewGuid().ToString("N"));
        var diagnostics = runtimeCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(
                Environment.NewLine,
                diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine +
            generatedSource);

        using var assemblyStream = new MemoryStream();
        var emitResult = runtimeCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics) +
            Environment.NewLine +
            generatedSource);
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var ownerType = assembly.GetType("Demo.PlannerView");
        var itemType = assembly.GetType("Demo.Item");
        var templateRootType = assembly.GetType("Demo.CountingPanel");

        Assert.NotNull(ownerType);
        Assert.NotNull(itemType);
        Assert.NotNull(templateRootType);

        return new RuntimeFixture(ownerType!, itemType!, templateRootType!);
    }

    private static void AssertBalancedSourceMappings(string output)
    {
        var mappingCount = CountOccurrences(output, "#line (");

        Assert.NotEqual(0, mappingCount);
        Assert.Equal(mappingCount, CountOccurrences(output, "#line default"));
        Assert.Equal(mappingCount, CountOccurrences(output, "#line hidden"));
        Assert.Contains("\"PlannerView.akbura\"", output, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;

        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    private sealed class RuntimeFixture
    {
        private readonly Type _ownerType;
        private readonly PropertyInfo _createdCountProperty;
        private readonly PropertyInfo _itemNameProperty;
        private readonly MethodInfo _createTemplateMethod;

        public RuntimeFixture(
            Type ownerType,
            Type itemType,
            Type templateRootType)
        {
            _ownerType = ownerType;
            ItemType = itemType;
            TemplateRootType = templateRootType;

            var createdCountProperty = templateRootType.GetProperty(
                "CreatedCount",
                BindingFlags.Public | BindingFlags.Static);
            var itemNameProperty = itemType.GetProperty(
                "Name",
                BindingFlags.Public | BindingFlags.Instance);
            var createTemplateMethod = ownerType.GetMethod(
                "CreateRuntimeTemplate",
                BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(createdCountProperty);
            Assert.NotNull(itemNameProperty);
            Assert.NotNull(createTemplateMethod);

            _createdCountProperty = createdCountProperty;
            _itemNameProperty = itemNameProperty;
            _createTemplateMethod = createTemplateMethod;
        }

        public Type ItemType { get; }

        public Type TemplateRootType { get; }

        public int CreatedTreeCount => Assert.IsType<int>(
            _createdCountProperty.GetValue(obj: null));

        public object CreateOwner()
        {
            var owner = Activator.CreateInstance(_ownerType);

            Assert.NotNull(owner);
            return owner;
        }

        public object CreateItem(string name)
        {
            var item = Activator.CreateInstance(ItemType);

            Assert.NotNull(item);
            _itemNameProperty.SetValue(item, name);
            return item;
        }

        public IDataTemplate CreateTemplate(object owner)
        {
            return Assert.IsAssignableFrom<IDataTemplate>(
                _createTemplateMethod.Invoke(owner, parameters: null));
        }
    }
}
