using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.UnitTests;

public sealed class ComponentLifecycleWriterTests
{
    [Fact]
    public void WriteMembers_NormalRootWritesLifecycleInRuntimeOrder()
    {
        const string component =
            """
            using Avalonia.Controls;

            RenderBeforeUpdate();

            <Border Width={GetWidth()} />
            """;
        const string csharp =
            """
            namespace Demo;

            public partial class PlannerView
            {
                private void RenderBeforeUpdate()
                {
                }

                private double GetWidth() => 42;
            }
            """;
        using var fixture = CreateFixture(component, csharp, currentIndent: 4);
        var lifecycleWriter = fixture.CreateWriter();
        ref readonly var plan = ref fixture.Plan;
        var lifecycle = plan.Lifecycle;
        ref readonly var root = ref plan.Elements.ItemRef(lifecycle.RootElementId);

        Assert.True(lifecycle.HasRootElement);
        Assert.False(lifecycle.UsesFallbackRoot);
        Assert.False(lifecycleWriter.WriteSupportFields(plan));
        Assert.Equal(4, fixture.CodeWriter.CurrentIndent);

        lifecycleWriter.WriteMembers(plan);

        Assert.Equal(4, fixture.CodeWriter.CurrentIndent);
        var output = fixture.CodeWriter.GetText().ToString();
        var methods = SplitLifecycleMethods(output);
        var creation = root.Identifier + " = new global::Avalonia.Controls.Border();";
        var rootReturn = "return " + root.Identifier + ";";

        Assert.Contains(
            "protected override global::Avalonia.Controls.Control FirstUpdate()",
            methods.FirstUpdate,
            StringComparison.Ordinal);
        Assert.Contains(creation, methods.FirstUpdate, StringComparison.Ordinal);
        Assert.DoesNotContain("GetWidth()", methods.FirstUpdate, StringComparison.Ordinal);
        Assert.Contains(
            root.Identifier +
            ".Bind(global::Avalonia.StyledElement.DataContextProperty, " +
            "global::Avalonia.AvaloniaObjectExtensions.GetObservable(" +
            "this, global::Avalonia.StyledElement.DataContextProperty));",
            methods.FirstUpdate,
            StringComparison.Ordinal);
        Assert.Contains(rootReturn, methods.FirstUpdate, StringComparison.Ordinal);

        Assert.Contains(
            "protected override global::Avalonia.Controls.Control Update()",
            methods.Update,
            StringComparison.Ordinal);
        Assert.DoesNotContain(creation, methods.Update, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(methods.Update, "GetWidth()"));
        Assert.Contains(rootReturn, methods.Update, StringComparison.Ordinal);

        var renderIndex = methods.Update.IndexOf(
            "RenderBeforeUpdate();",
            StringComparison.Ordinal);
        var propertyIndex = methods.Update.IndexOf("GetWidth()", StringComparison.Ordinal);
        var returnIndex = methods.Update.LastIndexOf(rootReturn, StringComparison.Ordinal);

        Assert.True(renderIndex >= 0, output);
        Assert.True(propertyIndex > renderIndex, output);
        Assert.True(returnIndex > propertyIndex, output);
        Assert.Equal(1, CountOccurrences(output, creation));
        AssertBalancedSourceMappings(output);
    }

    [Fact]
    public void WriteSupportFields_BaseUriFeedsComponentMarkupContext()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border Background=${DynamicResource AccentBrush} />
            """;
        using var fixture = CreateFixture(
            component,
            resourcePath: "Views/PlannerView.akbura",
            currentIndent: 4);
        var lifecycleWriter = fixture.CreateWriter();
        ref readonly var plan = ref fixture.Plan;
        ref readonly var root = ref plan.Elements.ItemRef(
            plan.Lifecycle.RootElementId);

        Assert.True(plan.Lifecycle.RequiresBaseUri);
        Assert.True(lifecycleWriter.WriteSupportFields(plan));
        Assert.Equal(4, fixture.CodeWriter.CurrentIndent);

        lifecycleWriter.WriteMembers(plan);

        Assert.Equal(4, fixture.CodeWriter.CurrentIndent);
        var output = fixture.CodeWriter.GetText().ToString();

        Assert.Contains(
            "private static readonly global::System.Uri __akburaBaseUri =",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"avares://\" + typeof(global::Demo.PlannerView).Assembly.GetName().Name + " +
            "\"/Views/PlannerView.akbura\"",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "targetObject: " + root.Identifier,
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "intermediateRootObject: this",
            output,
            StringComparison.Ordinal);
        Assert.Contains("baseUri: __akburaBaseUri", output, StringComparison.Ordinal);
        Assert.Contains(
            "directParentsStack: new global::System.Object[] { this, " +
            root.Identifier + " }",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "fallbackServiceProvider:",
            output,
            StringComparison.Ordinal);
        AssertBalancedSourceMappings(output);
    }

    [Fact]
    public void WriteMembers_InvalidRootShapeUsesSinglePersistentFallback()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border />
            <Button />
            """;
        using var fixture = CreateFixture(component, currentIndent: 4);
        var lifecycleWriter = fixture.CreateWriter();
        ref readonly var plan = ref fixture.Plan;

        Assert.False(plan.Lifecycle.HasRootElement);
        Assert.True(plan.Lifecycle.UsesFallbackRoot);
        Assert.True(lifecycleWriter.WriteSupportFields(plan));
        Assert.Equal(4, fixture.CodeWriter.CurrentIndent);

        lifecycleWriter.WriteMembers(plan);

        Assert.Equal(4, fixture.CodeWriter.CurrentIndent);
        var output = fixture.CodeWriter.GetText().ToString();
        var methods = SplitLifecycleMethods(output);

        Assert.Contains(
            "private global::Avalonia.Controls.Control __generatedRoot = null!;",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "__generatedRoot = new global::Avalonia.Controls.Control();",
            methods.FirstUpdate,
            StringComparison.Ordinal);
        Assert.Contains(
            "((global::System.ComponentModel.ISupportInitialize)__generatedRoot).BeginInit();",
            methods.FirstUpdate,
            StringComparison.Ordinal);
        Assert.Contains(
            "((global::System.ComponentModel.ISupportInitialize)__generatedRoot).EndInit();",
            methods.FirstUpdate,
            StringComparison.Ordinal);
        Assert.Contains("return __generatedRoot;", methods.FirstUpdate, StringComparison.Ordinal);

        Assert.DoesNotContain(
            "new global::Avalonia.Controls.Control()",
            methods.Update,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BeginInit", methods.Update, StringComparison.Ordinal);
        Assert.DoesNotContain("EndInit", methods.Update, StringComparison.Ordinal);
        Assert.Contains("return __generatedRoot;", methods.Update, StringComparison.Ordinal);
        Assert.Equal(
            1,
            CountOccurrences(
                output,
                "__generatedRoot = new global::Avalonia.Controls.Control();"));
        Assert.DoesNotContain(
            ".Bind(global::Avalonia.StyledElement.DataContextProperty",
            output,
            StringComparison.Ordinal);
    }

    private static WriterFixture CreateFixture(
        string component,
        string? additionalCSharp = null,
        string resourcePath = "PlannerView.akbura",
        int currentIndent = 0)
    {
        var semanticFixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            additionalCSharp);
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticFixture.SemanticModel.GetSymbolInfo(
                semanticFixture.ComponentTree.GetRoot()).Symbol);
        var plan = ComponentPlanner.Create(
            componentSymbol,
            semanticFixture.SemanticModel,
            new Dictionary<AkburaSyntax, string>());
        var bindingEnvironment = semanticFixture.CreateBindingEnvironment();
        var sourceMap = new ComponentGenerationSourceMap(
            Assert.IsType<ComponentSyntaxTree>(semanticFixture.ComponentTree));
        var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = currentIndent,
        };

        return new WriterFixture(
            plan,
            bindingEnvironment,
            sourceMap,
            codeWriter,
            resourcePath);
    }

    private static LifecycleMethods SplitLifecycleMethods(string output)
    {
        const string updateSignature =
            "protected override global::Avalonia.Controls.Control Update()";
        var updateStart = output.IndexOf(updateSignature, StringComparison.Ordinal);

        Assert.True(updateStart >= 0, output);
        return new LifecycleMethods(
            output[..updateStart],
            output[updateStart..]);
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

    private readonly record struct LifecycleMethods(
        string FirstUpdate,
        string Update);

    private sealed class WriterFixture : IDisposable
    {
        private readonly BindingWriterEnvironment _bindingEnvironment;
        private readonly ComponentGenerationSourceMap _sourceMap;
        private readonly ComponentPlan _plan;
        private readonly string _resourcePath;

        public WriterFixture(
            ComponentPlan plan,
            BindingWriterEnvironment bindingEnvironment,
            ComponentGenerationSourceMap sourceMap,
            CodeWriter codeWriter,
            string resourcePath)
        {
            _plan = plan;
            _bindingEnvironment = bindingEnvironment;
            _sourceMap = sourceMap;
            CodeWriter = codeWriter;
            _resourcePath = resourcePath;
        }

        public ref readonly ComponentPlan Plan => ref _plan;

        public CodeWriter CodeWriter { get; }

        public ComponentLifecycleWriter CreateWriter()
        {
            return new ComponentLifecycleWriter(
                CodeWriter,
                in _bindingEnvironment,
                _sourceMap,
                "global::Demo.PlannerView",
                _resourcePath);
        }

        public void Dispose()
        {
            CodeWriter.Dispose();
        }
    }
}
