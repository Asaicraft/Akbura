using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.UnitTests;

public sealed class ComponentScopeWriterPhaseTests
{
    private const string ChildComponent =
        """
        param bind string Value = "";
        """;

    [Fact]
    public void WriteComponentInitialState_WritesOnlyInitialState()
    {
        using var fixture = CreateFixture(CreateComponentMarkup());
        ref readonly var scope = ref fixture.Plan.Scopes.ItemRef(0);
        var output = WriteScope(fixture, scope, ScopeWriteMode.ComponentInitial);
        var child = GetElement(fixture.Plan, scope, "Child");

        Assert.Contains("new global::Avalonia.Controls.StackPanel()", output);
        Assert.Contains(".BeginInit();", output);
        Assert.Contains(".EndInit();", output);
        Assert.Contains("WidthProperty", output);
        Assert.DoesNotContain("HeightProperty", output, StringComparison.Ordinal);
        Assert.DoesNotContain("caption", output, StringComparison.Ordinal);
        Assert.Equal(
            1,
            CountOccurrences(
                output,
                child.Identifier + ".Value = value;"));
        Assert.Contains(".PropertyChanged +=", output);
        Assert.Contains("SetAkcssStyles(", output);
        Assert.DoesNotContain("ExecuteAkcssStyles(", output, StringComparison.Ordinal);
        Assert.Equal(6, fixture.CodeWriter.CurrentIndent);
    }

    [Fact]
    public void WriteLocalInitialState_WritesCurrentDynamicStateWithoutRepeatingBoth()
    {
        using var fixture = CreateFixture(CreateLocalMarkup());
        var template = Assert.Single(fixture.Plan.Templates);
        ref readonly var scope =
            ref fixture.Plan.Scopes.ItemRef(template.ScopeId);
        var output = WriteScope(fixture, scope, ScopeWriteMode.LocalInitial);
        var child = GetElement(fixture.Plan, scope, "Child");

        Assert.Contains("new global::Avalonia.Controls.StackPanel()", output);
        Assert.Contains(".BeginInit();", output);
        Assert.Contains(".EndInit();", output);
        Assert.Contains("WidthProperty", output);
        Assert.Contains("HeightProperty", output);
        Assert.Contains("caption", output);
        Assert.Equal(
            1,
            CountOccurrences(
                output,
                child.Identifier + ".Value = value;"));
        Assert.Contains(".PropertyChanged +=", output);
        Assert.Contains("SetAkcssStyles(", output);
        Assert.DoesNotContain("ExecuteAkcssStyles(", output, StringComparison.Ordinal);
        Assert.Equal(6, fixture.CodeWriter.CurrentIndent);
    }

    [Fact]
    public void WriteUpdateState_WritesRuntimeStateOnly()
    {
        using var fixture = CreateFixture(CreateComponentMarkup());
        ref readonly var scope = ref fixture.Plan.Scopes.ItemRef(0);
        var output = WriteScope(fixture, scope, ScopeWriteMode.Update);
        var child = GetElement(fixture.Plan, scope, "Child");

        Assert.DoesNotContain("new global::", output, StringComparison.Ordinal);
        Assert.DoesNotContain(".BeginInit();", output, StringComparison.Ordinal);
        Assert.DoesNotContain(".EndInit();", output, StringComparison.Ordinal);
        Assert.DoesNotContain("WidthProperty", output, StringComparison.Ordinal);
        Assert.Contains("HeightProperty", output);
        Assert.Contains("caption", output);
        Assert.Equal(
            1,
            CountOccurrences(
                output,
                child.Identifier + ".Value = value;"));
        Assert.DoesNotContain(".PropertyChanged +=", output, StringComparison.Ordinal);
        Assert.DoesNotContain("SetAkcssStyles(", output, StringComparison.Ordinal);
        Assert.Contains("ExecuteAkcssStyles(", output);
        Assert.Equal(6, fixture.CodeWriter.CurrentIndent);
    }

    private static string CreateComponentMarkup()
    {
        return
            """
            using Avalonia.Controls;

            state string value = "";
            state double height = 42;
            state string caption = "Ready";

            @akcss {
                @using Avalonia.Controls;

                .card { Width: 10; }
            }

            <StackPanel>
                <Child bind:Value={value} />
                <Button Width="10" Height={height} class="card">{caption}</Button>
            </StackPanel>
            """;
    }

    private static string CreateLocalMarkup()
    {
        return
            """
            using Avalonia.Controls;

            state string value = "";
            state double height = 42;
            state string caption = "Ready";

            @akcss {
                @using Avalonia.Controls;

                .card { Width: 10; }
            }

            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <StackPanel>
                        <Child bind:Value={value} />
                        <Button Width="10" Height={height} class="card">{caption}</Button>
                    </StackPanel>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
    }

    private static ScopeWriterFixture CreateFixture(string component)
    {
        var baseFixture = AkcssActivatorPlannerTests.CreateFixture(component);
        var childTree = AkburaSyntaxTree.ParseText(
            ChildComponent,
            "Child.akbura");
        var compilation = new AkburaCompilation(
            baseFixture.CSharpCompilation,
            [baseFixture.ComponentTree, childTree],
            rootNamespace: "Demo");
        var semanticFixture = new AkcssActivatorPlannerTests.PlannerFixture(
            baseFixture.CSharpCompilation,
            baseFixture.ComponentTree,
            externalAkcssTree: null,
            compilation.GetSemanticModel(baseFixture.ComponentTree));
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticFixture.SemanticModel.GetSymbolInfo(
                semanticFixture.ComponentTree.GetRoot()).Symbol);
        var moduleTypeNames = new Dictionary<AkburaSyntax, string>();

        foreach (var block in semanticFixture.ComponentTree.GetRoot()
                     .Members.OfType<InlineAkcssBlockSyntax>())
        {
            moduleTypeNames.Add(block, "global::Demo.ScopeWriterStyles");
        }

        var plan = ComponentPlanner.Create(
            componentSymbol,
            semanticFixture.SemanticModel,
            moduleTypeNames);
        return new ScopeWriterFixture(semanticFixture, plan);
    }

    private static string WriteScope(
        ScopeWriterFixture fixture,
        in ComponentScopePlan scope,
        ScopeWriteMode mode)
    {
        var rootId = fixture.Plan.ScopeRootElementIds[scope.Roots.Start];
        ref readonly var root = ref fixture.Plan.Elements.ItemRef(rootId);
        var environment = fixture.SemanticFixture.CreateBindingEnvironment();
        var sourceMap = new ComponentGenerationSourceMap(
            Assert.IsType<ComponentSyntaxTree>(
                fixture.SemanticFixture.ComponentTree));
        var writer = new ComponentScopeWriter(
            fixture.CodeWriter,
            in environment,
            sourceMap,
            "global::Demo.PlannerView");
        var context = new ComponentScopeWriteContext(
            intermediateRootExpression: root.Identifier,
            baseUriExpression: "__akburaBaseUri",
            fallbackServiceProviderExpression: null,
            nameScopeExpression:
                scope.Kind == ComponentElementScopeKind.Component
                    ? null
                    : "__nameScope",
            scopeId: scope.Id,
            parentStackTraversalKind:
                MarkupParentStackTraversalKind.ExactScope,
            elements: fixture.Plan.Elements.AsSpan(),
            elementReferences:
                fixture.Plan.ElementReferences.AsSpan());

        switch (mode)
        {
            case ScopeWriteMode.ComponentInitial:
                writer.WriteComponentInitialState(
                    fixture.Plan,
                    scope,
                    context);
                break;
            case ScopeWriteMode.LocalInitial:
                writer.WriteLocalInitialState(
                    fixture.Plan,
                    scope,
                    context);
                break;
            case ScopeWriteMode.Update:
                writer.WriteUpdateState(
                    fixture.Plan,
                    scope,
                    context);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown component scope write mode.");
        }

        return fixture.CodeWriter.GetText().ToString();
    }

    private static ComponentElementPlan GetElement(
        in ComponentPlan plan,
        in ComponentScopePlan scope,
        string tagName)
    {
        var elements = plan.Elements;
        return Assert.Single(
            plan.ScopeElementIds
                .AsSpan(scope.Elements.Start, scope.Elements.Length)
                .ToArray()
                .Select(elementId => elements[elementId]),
            element => string.Equals(
                element.Syntax.StartTag?.Name.ToFullString().Trim(),
                tagName,
                StringComparison.Ordinal));
    }

    private static int CountOccurrences(
        string text,
        string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(
                   value,
                   index,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class ScopeWriterFixture : IDisposable
    {
        public ScopeWriterFixture(
            AkcssActivatorPlannerTests.PlannerFixture semanticFixture,
            ComponentPlan plan)
        {
            SemanticFixture = semanticFixture;
            Plan = plan;
            CodeWriter = new CodeWriter
            {
                CurrentIndent = 6,
            };
        }

        public AkcssActivatorPlannerTests.PlannerFixture SemanticFixture { get; }

        public ComponentPlan Plan { get; }

        public CodeWriter CodeWriter { get; }

        public void Dispose()
        {
            CodeWriter.Dispose();
        }
    }

    private enum ScopeWriteMode : byte
    {
        ComponentInitial,
        LocalInitial,
        Update,
    }
}
