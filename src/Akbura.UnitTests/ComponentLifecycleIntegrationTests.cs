using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Avalonia.Headless;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComponentLifecycleIntegrationTests
{
    [Fact]
    public async Task GeneratedLifecycle_CreatesTreeOnceAndEvaluatesDynamicStateOnlyInUpdate()
    {
        var fixture = CreateRuntimeFixture();

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var owner = fixture.CreateOwner();
                var root = fixture.InvokeFirstUpdate(owner);

                Assert.Equal(1, fixture.CreatedRootCount);
                Assert.Equal(0, fixture.GetMessageCallCount(owner));
                Assert.Equal(0, fixture.GetRenderCallCount(owner));
                Assert.Null(Assert.IsType<TextBlock>(Assert.Single(root.Children)).Text);

                var dataContext = new object();
                Assert.IsAssignableFrom<Control>(owner).DataContext = dataContext;
                Assert.Same(dataContext, root.DataContext);

                var firstUpdateRoot = fixture.InvokeUpdate(owner);
                var textBlock = Assert.IsType<TextBlock>(Assert.Single(root.Children));

                Assert.Same(root.Value, firstUpdateRoot.Value);
                Assert.Equal(1, fixture.CreatedRootCount);
                Assert.Equal(1, fixture.GetMessageCallCount(owner));
                Assert.Equal(1, fixture.GetRenderCallCount(owner));
                Assert.Equal("Initial", textBlock.Text);

                fixture.SetMessage(owner, "Changed");
                var secondUpdateRoot = fixture.InvokeUpdate(owner);

                Assert.Same(root.Value, secondUpdateRoot.Value);
                Assert.Equal(1, fixture.CreatedRootCount);
                Assert.Equal(2, fixture.GetMessageCallCount(owner));
                Assert.Equal(2, fixture.GetRenderCallCount(owner));
                Assert.Equal("Changed", textBlock.Text);
            },
            CancellationToken.None);
    }

    private static RuntimeFixture CreateRuntimeFixture()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            RenderCallCount++;

            <CountingPanel>
                <TextBlock Text={GetMessage()} />
            </CountingPanel>
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
                private string message = "Initial";

                public PlannerView()
                    : base(AkburaEngine.Empty)
                {
                }

                public int GetMessageCallCount { get; private set; }

                public int RenderCallCount { get; private set; }

                public Control InvokeFirstUpdate() => FirstUpdate();

                public Control InvokeUpdate() => Update();

                public void SetMessage(string value)
                {
                    message = value;
                }

                private string GetMessage()
                {
                    GetMessageCallCount++;
                    return message;
                }

            }
            """;
        var semanticFixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            csharp);
        using var codeWriter = new CodeWriter("\r\n");
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticFixture.SemanticModel.GetSymbolInfo(
                semanticFixture.ComponentTree.GetRoot()).Symbol);
        using var componentWriter = new ComponentWriter(
            codeWriter,
            componentSymbol,
            semanticFixture.SemanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>());

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

        componentWriter.WriteLifecycleMembers();
        codeWriter.WriteLine();
        componentWriter.WriteDescriptorMembers();

        codeWriter.CurrentIndent = 0;
        codeWriter.WriteLine("}");

        var generatedSource = codeWriter.GetText().ToString();
        AssertBalancedSourceMappings(generatedSource);
        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "PlannerView.Lifecycle.Runtime.g.cs");
        var runtimeCompilation = semanticFixture.CSharpCompilation
            .AddSyntaxTrees(generatedTree)
            .WithAssemblyName(
                "ComponentLifecycleIntegration_" +
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
        var rootType = assembly.GetType("Demo.CountingPanel");

        Assert.NotNull(ownerType);
        Assert.NotNull(rootType);
        return new RuntimeFixture(ownerType!, rootType!);
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
        private readonly Type _rootType;
        private readonly PropertyInfo _createdCountProperty;
        private readonly PropertyInfo _getMessageCallCountProperty;
        private readonly PropertyInfo _renderCallCountProperty;
        private readonly MethodInfo _firstUpdateMethod;
        private readonly MethodInfo _updateMethod;
        private readonly MethodInfo _setMessageMethod;

        public RuntimeFixture(Type ownerType, Type rootType)
        {
            _ownerType = ownerType;
            _rootType = rootType;
            _createdCountProperty = GetProperty(rootType, "CreatedCount");
            _getMessageCallCountProperty = GetProperty(
                ownerType,
                "GetMessageCallCount");
            _renderCallCountProperty = GetProperty(ownerType, "RenderCallCount");
            _firstUpdateMethod = GetMethod(ownerType, "InvokeFirstUpdate");
            _updateMethod = GetMethod(ownerType, "InvokeUpdate");
            _setMessageMethod = GetMethod(ownerType, "SetMessage");
        }

        public int CreatedRootCount => Assert.IsType<int>(
            _createdCountProperty.GetValue(obj: null));

        public object CreateOwner()
        {
            var owner = Activator.CreateInstance(_ownerType);

            Assert.NotNull(owner);
            return owner;
        }

        public CountingPanelMarker InvokeFirstUpdate(object owner)
        {
            return CreateRootMarker(_firstUpdateMethod.Invoke(owner, parameters: null));
        }

        public CountingPanelMarker InvokeUpdate(object owner)
        {
            return CreateRootMarker(_updateMethod.Invoke(owner, parameters: null));
        }

        public int GetMessageCallCount(object owner)
        {
            return Assert.IsType<int>(_getMessageCallCountProperty.GetValue(owner));
        }

        public int GetRenderCallCount(object owner)
        {
            return Assert.IsType<int>(_renderCallCountProperty.GetValue(owner));
        }

        public void SetMessage(object owner, string value)
        {
            _setMessageMethod.Invoke(owner, [value]);
        }

        private CountingPanelMarker CreateRootMarker(object? value)
        {
            Assert.NotNull(value);
            Assert.Equal(_rootType, value!.GetType());
            return new CountingPanelMarker(Assert.IsAssignableFrom<StackPanel>(value));
        }

        private static PropertyInfo GetProperty(Type type, string name)
        {
            return type.GetProperty(
                       name,
                       BindingFlags.Public |
                       BindingFlags.Instance |
                       BindingFlags.Static) ??
                throw new InvalidOperationException(
                    "Generated runtime property was not found: " + name);
        }

        private static MethodInfo GetMethod(Type type, string name)
        {
            return type.GetMethod(
                       name,
                       BindingFlags.Public | BindingFlags.Instance) ??
                throw new InvalidOperationException(
                    "Generated runtime method was not found: " + name);
        }
    }

    private sealed class CountingPanelMarker
    {
        public CountingPanelMarker(StackPanel value)
        {
            Value = value;
        }

        public StackPanel Value { get; }

        public Avalonia.Controls.Controls Children => Value.Children;

        public object? DataContext => Value.DataContext;
    }
}
