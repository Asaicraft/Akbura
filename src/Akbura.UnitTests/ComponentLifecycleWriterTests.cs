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
    public void WriteMembers_HotReloadReplaysOnlyInitialPropertyValues()
    {
        const string component =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBlock Text="Before"
                           Width={GetWidth()} />
                <TextBlock Text=${Binding Name} />
                <Border Background=${DynamicResource AccentBrush} />
            </StackPanel>
            """;
        const string csharp =
            """
            namespace Demo;

            public partial class PlannerView
            {
                private double GetWidth() => 42;
            }
            """;
        using var fixture = CreateFixture(
            component,
            csharp,
            currentIndent: 4);
        var lifecycleWriter = fixture.CreateWriter();
        ref readonly var plan = ref fixture.Plan;

        lifecycleWriter.WriteMembers(plan);

        Assert.Equal(4, fixture.CodeWriter.CurrentIndent);
        var output = fixture.CodeWriter.GetText().ToString();
        var methods = SplitLifecycleMethods(output);

        Assert.Contains("\"Before\"", methods.FirstUpdate, StringComparison.Ordinal);
        Assert.Contains("\"Name\"", methods.FirstUpdate, StringComparison.Ordinal);
        Assert.Contains("\"AccentBrush\"", methods.FirstUpdate, StringComparison.Ordinal);
        Assert.DoesNotContain("GetWidth()", methods.FirstUpdate, StringComparison.Ordinal);

        Assert.DoesNotContain("\"Before\"", methods.Update, StringComparison.Ordinal);
        Assert.Contains("GetWidth()", methods.Update, StringComparison.Ordinal);

        Assert.Contains("\"Before\"", methods.HotReload, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Name\"", methods.HotReload, StringComparison.Ordinal);
        Assert.DoesNotContain("\"AccentBrush\"", methods.HotReload, StringComparison.Ordinal);
        Assert.DoesNotContain("GetWidth()", methods.HotReload, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "new global::Avalonia.Controls.",
            methods.HotReload,
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

    [Fact]
    public void WriteSupportFields_DebugStructuralKeepsFallbackFieldStable()
    {
        using var valid = CreateFixture(
            """
            using Avalonia.Controls;

            <Border />
            """,
            currentIndent: 4,
            generationMode: ComponentGenerationMode.DebugStructural);
        using var fallback = CreateFixture(
            """
            using Avalonia.Controls;

            <Border />
            <Button />
            """,
            currentIndent: 4,
            generationMode: ComponentGenerationMode.DebugStructural);
        var validWriter = valid.CreateWriter();
        var fallbackWriter = fallback.CreateWriter();
        ref readonly var validPlan = ref valid.Plan;
        ref readonly var fallbackPlan = ref fallback.Plan;

        Assert.False(validPlan.Lifecycle.UsesFallbackRoot);
        Assert.True(fallbackPlan.Lifecycle.UsesFallbackRoot);
        Assert.True(validWriter.WriteSupportFields(validPlan));
        Assert.True(fallbackWriter.WriteSupportFields(fallbackPlan));

        const string field =
            "private global::Avalonia.Controls.Control __generatedRoot = null!;";
        Assert.Equal(
            1,
            CountOccurrences(valid.CodeWriter.GetText().ToString(), field));
        Assert.Equal(
            1,
            CountOccurrences(fallback.CodeWriter.GetText().ToString(), field));
        Assert.Equal(4, valid.CodeWriter.CurrentIndent);
        Assert.Equal(4, fallback.CodeWriter.CurrentIndent);
    }

    [Fact]
    public void WriteMembers_DebugStructuralGuardsRenderRevisionWithAbort()
    {
        using var fixture = CreateFixture(
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBlock Text="Title" />
            </StackPanel>
            """,
            currentIndent: 4,
            generationMode: ComponentGenerationMode.DebugStructural);
        var lifecycleWriter = fixture.CreateWriter();
        ref readonly var plan = ref fixture.Plan;

        lifecycleWriter.WriteMembers(plan);

        Assert.Equal(4, fixture.CodeWriter.CurrentIndent);
        var methods = SplitLifecycleMethods(
            fixture.CodeWriter.GetText().ToString());

        AssertRenderRevisionGuard(methods.FirstUpdate);
        AssertRenderRevisionGuard(methods.Update);
    }

    [Fact]
    public void WriteMembers_DebugStructuralPreparesBeforeContentPresenterRefresh()
    {
        using var fixture = CreateFixture(
            """
            using Avalonia.Controls.Presenters;

            <ContentPresenter x.Name="presenter" Content="Title" />
            """,
            currentIndent: 4,
            generationMode: ComponentGenerationMode.DebugStructural);
        var lifecycleWriter = fixture.CreateWriter();
        ref readonly var plan = ref fixture.Plan;

        lifecycleWriter.WriteMembers(plan);

        var methods = SplitLifecycleMethods(
            fixture.CodeWriter.GetText().ToString());

        AssertPreparedBeforeRefresh(methods.FirstUpdate);
        AssertPreparedBeforeRefresh(methods.Update);
    }

    [Fact]
    public void WriteMembers_DebugStructuralOwnsStylesOutsideInitialValueGuard()
    {
        using var fixture = CreateFixture(
            """
            using Avalonia.Controls;

            @akcss {
                @using Avalonia.Controls;

                .card { Width: 20; }
            }

            <Border class="card" />
            """,
            currentIndent: 4,
            generationMode: ComponentGenerationMode.DebugStructural);
        var lifecycleWriter = fixture.CreateWriter();
        ref readonly var plan = ref fixture.Plan;

        lifecycleWriter.WriteMembers(plan);

        var methods = SplitLifecycleMethods(
            fixture.CodeWriter.GetText().ToString());
        var guardStart = methods.HotReload.IndexOf(
            ".ShouldApplyInitialValues(0)",
            StringComparison.Ordinal);
        var guardOpen = methods.HotReload.IndexOf(
            '{',
            guardStart);
        var guardClose = methods.HotReload.IndexOf(
            '}',
            guardOpen);
        var applyStyles = methods.HotReload.IndexOf(
            ".ApplyAkcssStylesOperation(",
            guardClose,
            StringComparison.Ordinal);

        Assert.True(guardStart >= 0, methods.HotReload);
        Assert.True(guardOpen > guardStart, methods.HotReload);
        Assert.True(guardClose > guardOpen, methods.HotReload);
        Assert.True(applyStyles > guardClose, methods.HotReload);
        Assert.Contains(
            ".ApplyAkcssStylesOperation(",
            methods.FirstUpdate,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ApplyAkcssStylesOperation",
            methods.Update,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ReplaceAkcssStylesForHotReload",
            methods.FirstUpdate + methods.HotReload,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMembers_DebugStructuralOwnsImplicitRootDataContextBinding()
    {
        using var fixture = CreateFixture(
            """
            using Avalonia.Controls;

            <Border />
            """,
            currentIndent: 4,
            generationMode: ComponentGenerationMode.DebugStructural);
        var lifecycleWriter = fixture.CreateWriter();
        ref readonly var plan = ref fixture.Plan;

        lifecycleWriter.WriteMembers(plan);

        var methods = SplitLifecycleMethods(
            fixture.CodeWriter.GetText().ToString());
        const string applyBinding = ".ApplyObservableBindingOperation(";
        const string operationSlot =
            "\"property:Avalonia.StyledElement.DataContextProperty\"";

        Assert.Contains(
            ".ShouldApplyOwnedOperation(",
            methods.FirstUpdate,
            StringComparison.Ordinal);
        Assert.Contains(applyBinding, methods.FirstUpdate, StringComparison.Ordinal);
        Assert.Contains(operationSlot, methods.FirstUpdate, StringComparison.Ordinal);
        Assert.Contains(
            ".ShouldApplyOwnedOperation(",
            methods.Update,
            StringComparison.Ordinal);
        Assert.Contains(applyBinding, methods.Update, StringComparison.Ordinal);
        Assert.Contains(operationSlot, methods.Update, StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".Bind(global::Avalonia.StyledElement.DataContextProperty",
            methods.FirstUpdate,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".Bind(global::Avalonia.StyledElement.DataContextProperty",
            methods.Update,
            StringComparison.Ordinal);
    }

    private static WriterFixture CreateFixture(
        string component,
        string? additionalCSharp = null,
        string resourcePath = "PlannerView.akbura",
        int currentIndent = 0,
        ComponentGenerationMode generationMode =
            ComponentGenerationMode.ReleaseDirect)
    {
        var semanticFixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            additionalCSharp);
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticFixture.SemanticModel.GetSymbolInfo(
                semanticFixture.ComponentTree.GetRoot()).Symbol);
        var moduleTypeNames = new Dictionary<AkburaSyntax, string>();
        var moduleId = 0;
        foreach (var inlineAkcss in semanticFixture.ComponentTree.GetRoot()
                     .Members.OfType<InlineAkcssBlockSyntax>())
        {
            moduleTypeNames.Add(
                inlineAkcss,
                "global::Demo.GeneratedStyles" + moduleId++);
        }

        var plan = ComponentPlanner.Create(
            componentSymbol,
            semanticFixture.SemanticModel,
            moduleTypeNames,
            generationMode);
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
            resourcePath,
            generationMode);
    }

    private static LifecycleMethods SplitLifecycleMethods(string output)
    {
        const string updateSignature =
            "protected override global::Avalonia.Controls.Control Update()";
        const string hotReloadSignature =
            "private void __AkburaHotReloadUpdateInitialValues()";
        var updateStart = output.IndexOf(updateSignature, StringComparison.Ordinal);
        var hotReloadStart = output.IndexOf(
            hotReloadSignature,
            StringComparison.Ordinal);

        Assert.True(updateStart >= 0, output);
        Assert.True(hotReloadStart > updateStart, output);
        return new LifecycleMethods(
            output[..updateStart],
            output[updateStart..hotReloadStart],
            output[hotReloadStart..]);
    }

    private static void AssertBalancedSourceMappings(string output)
    {
        var mappingCount = CountOccurrences(output, "#line (");

        Assert.NotEqual(0, mappingCount);
        Assert.Equal(mappingCount, CountOccurrences(output, "#line default"));
        Assert.Equal(mappingCount, CountOccurrences(output, "#line hidden"));
        Assert.Contains("\"PlannerView.akbura\"", output, StringComparison.Ordinal);
    }

    private static void AssertRenderRevisionGuard(string method)
    {
        const string beginRevision =
            "var __akburaRenderRevisionChanged = __AkburaEnsureRenderTree();";
        const string prepareRevision =
            "__akburaRenderState.PrepareRevisionCompletion();";
        const string completeRevision =
            "__akburaRenderState.CompleteRevision();";
        const string abortRevision =
            "__akburaRenderState.AbortRevision(__exception);";

        var beginIndex = method.IndexOf(beginRevision, StringComparison.Ordinal);
        var tryIndex = method.IndexOf("try", StringComparison.Ordinal);
        var prepareIndex = method.IndexOf(
            prepareRevision,
            StringComparison.Ordinal);
        var completeIndex = method.IndexOf(
            completeRevision,
            StringComparison.Ordinal);
        var catchIndex = method.IndexOf(
            "catch (global::System.Exception __exception)",
            StringComparison.Ordinal);
        var abortIndex = method.IndexOf(abortRevision, StringComparison.Ordinal);
        var rethrowIndex = method.IndexOf("throw;", StringComparison.Ordinal);

        Assert.True(beginIndex >= 0, method);
        Assert.True(tryIndex > beginIndex, method);
        Assert.True(prepareIndex > tryIndex, method);
        Assert.True(completeIndex > prepareIndex, method);
        Assert.True(catchIndex > completeIndex, method);
        Assert.True(abortIndex > catchIndex, method);
        Assert.True(rethrowIndex > abortIndex, method);
        Assert.Equal(1, CountOccurrences(method, beginRevision));
        Assert.Equal(1, CountOccurrences(method, prepareRevision));
        Assert.Equal(1, CountOccurrences(method, completeRevision));
        Assert.Equal(1, CountOccurrences(method, abortRevision));
        Assert.Equal(1, CountOccurrences(method, "throw;"));
    }

    private static void AssertPreparedBeforeRefresh(string method)
    {
        const string prepareRevision =
            "__akburaRenderState.PrepareRevisionCompletion();";
        const string refresh = ".UpdateChild();";
        const string completeRevision =
            "__akburaRenderState.CompleteRevision();";

        var prepareIndex = method.IndexOf(
            prepareRevision,
            StringComparison.Ordinal);
        var refreshIndex = method.IndexOf(refresh, StringComparison.Ordinal);
        var completeIndex = method.IndexOf(
            completeRevision,
            StringComparison.Ordinal);

        Assert.True(prepareIndex >= 0, method);
        Assert.True(refreshIndex > prepareIndex, method);
        Assert.True(completeIndex > refreshIndex, method);
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
        string Update,
        string HotReload);

    private sealed class WriterFixture : IDisposable
    {
        private readonly BindingWriterEnvironment _bindingEnvironment;
        private readonly ComponentGenerationSourceMap _sourceMap;
        private readonly ComponentPlan _plan;
        private readonly string _resourcePath;
        private readonly ComponentGenerationMode _generationMode;

        public WriterFixture(
            ComponentPlan plan,
            BindingWriterEnvironment bindingEnvironment,
            ComponentGenerationSourceMap sourceMap,
            CodeWriter codeWriter,
            string resourcePath,
            ComponentGenerationMode generationMode)
        {
            _plan = plan;
            _bindingEnvironment = bindingEnvironment;
            _sourceMap = sourceMap;
            CodeWriter = codeWriter;
            _resourcePath = resourcePath;
            _generationMode = generationMode;
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
                _resourcePath,
                _generationMode);
        }

        public void Dispose()
        {
            CodeWriter.Dispose();
        }
    }
}
