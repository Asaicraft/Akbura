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
public sealed class ComponentDeferredContentIntegrationTests
{
    [Fact]
    public async Task ComponentWriter_DeferredContentRemainsLazyUntilBuild()
    {
        var fixture = CreateRuntimeFixture();

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var owner = fixture.CreateOwner();
                var beforeFactory = fixture.CreatedTreeCount;

                var content = fixture.CreateDeferredContent(owner);

                Assert.Equal(beforeFactory, fixture.CreatedTreeCount);

                var result = Assert.IsType<TemplateResult<Control>>(
                    content.Build(serviceProvider: null));
                var root = Assert.IsAssignableFrom<StackPanel>(result.Result);

                Assert.Equal(beforeFactory + 1, fixture.CreatedTreeCount);
                Assert.Single(root.Children);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task ComponentWriter_DeferredBuildCreatesFreshNamedTrees()
    {
        var fixture = CreateRuntimeFixture();

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var owner = fixture.CreateOwner();
                var content = fixture.CreateDeferredContent(owner);
                var beforeBuild = fixture.CreatedTreeCount;

                var firstResult = Assert.IsType<TemplateResult<Control>>(
                    content.Build(serviceProvider: null));
                var secondResult = Assert.IsType<TemplateResult<Control>>(
                    content.Build(serviceProvider: null));
                var firstRoot = Assert.IsAssignableFrom<StackPanel>(
                    firstResult.Result);
                var secondRoot = Assert.IsAssignableFrom<StackPanel>(
                    secondResult.Result);

                Assert.Equal(beforeBuild + 2, fixture.CreatedTreeCount);
                Assert.NotSame(firstRoot, secondRoot);
                var firstNamed = Assert.IsType<TextBlock>(
                    Assert.Single(firstRoot.Children));
                var secondNamed = Assert.IsType<TextBlock>(
                    Assert.Single(secondRoot.Children));
                Assert.NotSame(firstNamed, secondNamed);
                Assert.Equal("Deferred", firstNamed.Text);
                Assert.Equal("Deferred", secondNamed.Text);

                Assert.NotSame(firstResult.NameScope, secondResult.NameScope);
                Assert.Same(firstNamed, firstResult.NameScope.Find("message"));
                Assert.Same(secondNamed, secondResult.NameScope.Find("message"));
            },
            CancellationToken.None);
    }

    private static RuntimeFixture CreateRuntimeFixture()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            <DeferredHost>
                <CountingPanel>
                    <TextBlock x.Name="message" Text="Deferred" />
                </CountingPanel>
            </DeferredHost>
            """;
        const string csharp =
            """
            using Akbura;
            using Akbura.ComponentTree;
            using Akbura.Engine;
            using Avalonia;
            using Avalonia.Controls;
            using Avalonia.Metadata;
            using System.Collections.Immutable;

            namespace Demo;

            public sealed class DeferredHost : Control
            {
                public static readonly StyledProperty<object?> ContentProperty =
                    AvaloniaProperty.Register<DeferredHost, object?>(nameof(Content));

                [Content]
                [TemplateContent]
                public object? Content
                {
                    get => GetValue(ContentProperty);
                    set => SetValue(ContentProperty, value);
                }
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

                public void InitializeForTest() => base.OnInitialized();

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
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>());
        ref readonly var plan = ref componentWriter.Plan;
        ref readonly var componentScope = ref plan.Scopes.ItemRef(0);
        var deferred = Assert.Single(plan.DeferredContents);

        Assert.Equal(ComponentElementScopeKind.Component, componentScope.Kind);
        Assert.Equal(1, componentScope.Elements.Length);
        Assert.NotEqual(componentScope.Id, deferred.ScopeId);

        codeWriter.WriteLine("#nullable enable");
        codeWriter.WriteLine();
        codeWriter.WriteLine("namespace Demo;");
        codeWriter.WriteLine();
        codeWriter.WriteLine("public partial class PlannerView");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 4;
        componentWriter.WriteElementFields();
        codeWriter.WriteLine();
        if (componentWriter.WriteLifecycleFields())
        {
            codeWriter.WriteLine();
        }

        Assert.True(componentWriter.WriteDeferredContentBuilders());
        codeWriter.WriteLine();
        componentWriter.WriteLifecycleMembers();
        codeWriter.WriteLine();
        componentWriter.WriteDescriptorMembers();
        codeWriter.CurrentIndent = 0;
        codeWriter.WriteLine("}");

        var generatedSource = codeWriter.GetText().ToString();
        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "PlannerView.DeferredContent.Runtime.g.cs");
        var runtimeCompilation = semanticFixture.CSharpCompilation
            .AddSyntaxTrees(generatedTree)
            .WithAssemblyName(
                "ComponentDeferredContentIntegration_" +
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
        var rootType = assembly.GetType("Demo.DeferredHost");
        var countingPanelType = assembly.GetType("Demo.CountingPanel");

        Assert.NotNull(ownerType);
        Assert.NotNull(rootType);
        Assert.NotNull(countingPanelType);

        return new RuntimeFixture(ownerType!, rootType!, countingPanelType!);
    }

    private sealed class RuntimeFixture
    {
        private readonly Type _ownerType;
        private readonly PropertyInfo _createdCountProperty;
        private readonly PropertyInfo _rootContentProperty;
        private readonly MethodInfo _initializeMethod;

        public RuntimeFixture(
            Type ownerType,
            Type rootType,
            Type countingPanelType)
        {
            _ownerType = ownerType;
            var createdCountProperty = countingPanelType.GetProperty(
                "CreatedCount",
                BindingFlags.Public | BindingFlags.Static);
            var rootContentProperty = rootType.GetProperty(
                "Content",
                BindingFlags.Public | BindingFlags.Instance);
            var initializeMethod = ownerType.GetMethod(
                "InitializeForTest",
                BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(createdCountProperty);
            Assert.NotNull(rootContentProperty);
            Assert.NotNull(initializeMethod);

            _createdCountProperty = createdCountProperty;
            _rootContentProperty = rootContentProperty;
            _initializeMethod = initializeMethod;
        }

        public int CreatedTreeCount => Assert.IsType<int>(
            _createdCountProperty.GetValue(obj: null));

        public object CreateOwner()
        {
            var owner = Activator.CreateInstance(_ownerType);

            Assert.NotNull(owner);
            return owner;
        }

        public IDeferredContent CreateDeferredContent(object owner)
        {
            _initializeMethod.Invoke(owner, parameters: null);
            var component = Assert.IsAssignableFrom<global::Akbura.AkburaControl>(owner);
            var root = Assert.IsAssignableFrom<Control>(component.Child);

            return Assert.IsAssignableFrom<IDeferredContent>(
                _rootContentProperty.GetValue(root));
        }
    }
}
