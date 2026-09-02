using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.UnitTests;

public sealed class ComponentLifecycleRegressionTests
{
    [Fact]
    public void LocalDeclarations_RunInBothPhasesWithoutRunningHooksOrStatementsDuringFirstUpdate()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Hooks;

            string prefix = "ready";
            var value = prefix + "!";
            RecordRender();
            useControlValue(Width);

            <Child Value={value} />
            """;
        const string csharp =
            """
            using Akbura.CompilerAnotations;
            using Avalonia;
            using Avalonia.Controls;

            namespace Demo
            {
                public partial class PlannerView : Control
                {
                    private void RecordRender()
                    {
                    }
                }
            }

            namespace Hooks
            {
                public static class ControlHooks
                {
                    [UseHook]
                    public static void useControlValue<T>(
                        [Self] T control,
                        AvaloniaProperty<double> property)
                        where T : Control
                    {
                    }
                }
            }
            """;
        const string child =
            """
            param string Value = "";
            """;
        using var fixture = CreateFixture(component, csharp, child);
        var statements = fixture.Writer.Plan.RenderStatements;

        Assert.Collection(
            statements,
            prefix => Assert.Equal(ComponentRenderStatementPhase.Both, prefix.Phase),
            value => Assert.Equal(ComponentRenderStatementPhase.Both, value.Phase),
            render => Assert.Equal(ComponentRenderStatementPhase.Update, render.Phase),
            hook =>
            {
                Assert.Equal(ComponentRenderStatementKind.UseHookInvocation, hook.Kind);
                Assert.Equal(ComponentRenderStatementPhase.Update, hook.Phase);
            });

        var methods = SplitLifecycleMethods(fixture.WriteLifecycle());
        var prefixIndex = methods.FirstUpdate.IndexOf(
            "string prefix = \"ready\";",
            StringComparison.Ordinal);
        var valueIndex = methods.FirstUpdate.IndexOf(
            "var value = prefix + \"!\";",
            StringComparison.Ordinal);
        var assignmentIndex = methods.FirstUpdate.IndexOf(
            ".Value = value;",
            StringComparison.Ordinal);

        Assert.True(prefixIndex >= 0, methods.FirstUpdate);
        Assert.True(valueIndex > prefixIndex, methods.FirstUpdate);
        Assert.True(assignmentIndex > valueIndex, methods.FirstUpdate);
        Assert.DoesNotContain("RecordRender();", methods.FirstUpdate, StringComparison.Ordinal);
        Assert.DoesNotContain("useControlValue", methods.FirstUpdate, StringComparison.Ordinal);

        Assert.Contains("string prefix = \"ready\";", methods.Update, StringComparison.Ordinal);
        Assert.Contains("var value = prefix + \"!\";", methods.Update, StringComparison.Ordinal);
        Assert.Contains("RecordRender();", methods.Update, StringComparison.Ordinal);
        Assert.Contains("useControlValue", methods.Update, StringComparison.Ordinal);
        Assert.Contains(".Value = value;", methods.Update, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitRootDataContext_SuppressesSyntheticBinding()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border DataContext={GetRootDataContext()} />
            """;
        const string csharp =
            """
            namespace Demo;

            public partial class PlannerView
            {
                private object GetRootDataContext() => new object();
            }
            """;
        using var fixture = CreateFixture(component, csharp);

        Assert.True(fixture.Writer.Plan.Lifecycle.HasExplicitRootDataContext);

        var output = fixture.WriteLifecycle();

        Assert.DoesNotContain(
            ".Bind(global::Avalonia.StyledElement.DataContextProperty",
            output,
            StringComparison.Ordinal);
        Assert.Contains("GetRootDataContext()", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RootDataContextPropertyElement_SuppressesSyntheticBinding()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border>
                <Border.DataContext>
                    <TextBlock />
                </Border.DataContext>
            </Border>
            """;
        using var fixture = CreateFixture(component);

        Assert.True(fixture.Writer.Plan.Lifecycle.HasExplicitRootDataContext);

        var output = fixture.WriteLifecycle();

        Assert.DoesNotContain(
            ".Bind(global::Avalonia.StyledElement.DataContextProperty",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Avalonia.StyledElement.DataContextProperty",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnrelatedAttachedDataContext_DoesNotSuppressSyntheticBinding()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            <Border Attached.DataContext="shadow" />
            """;
        const string csharp =
            """
            namespace Demo;

            public sealed class AttachedProperty<T>
            {
            }

            public static class Attached
            {
                public static readonly AttachedProperty<string> DataContextProperty = null!;

                public static string GetDataContext(Avalonia.Controls.Control target) => "";

                public static void SetDataContext(
                    Avalonia.Controls.Control target,
                    string value)
                {
                }
            }
            """;
        using var fixture = CreateFixture(component, csharp);

        Assert.False(fixture.Writer.Plan.Lifecycle.HasExplicitRootDataContext);

        var output = fixture.WriteLifecycle();

        Assert.Contains(
            "global::Demo.Attached.SetDataContext",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Bind(global::Avalonia.StyledElement.DataContextProperty",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NestedDataContext_DoesNotSuppressSyntheticRootBinding()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border>
                <TextBlock DataContext={GetNestedDataContext()} />
            </Border>
            """;
        const string csharp =
            """
            namespace Demo;

            public partial class PlannerView
            {
                private object GetNestedDataContext() => new object();
            }
            """;
        using var fixture = CreateFixture(component, csharp);

        Assert.False(fixture.Writer.Plan.Lifecycle.HasExplicitRootDataContext);

        var output = fixture.WriteLifecycle();

        Assert.Contains(
            ".Bind(global::Avalonia.StyledElement.DataContextProperty",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContentPresenters_RefreshAfterComponentStateOnly()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Controls.Presenters;
            using Demo;

            <StackPanel>
                <ContentPresenter x.Name="presenter" Content={GetContent()} />
                <DerivedPresenter x.Name="derivedPresenter" />
                <ItemsControl>
                    <ItemsControl.ItemTemplate>
                        <ContentPresenter x.Name="templatePresenter" />
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            """;
        const string csharp =
            """
            using Avalonia.Controls.Presenters;

            namespace Demo;

            public sealed class DerivedPresenter : ContentPresenter
            {
            }

            public partial class PlannerView
            {
                private object? GetContent() => null;
            }
            """;
        using var fixture = CreateFixture(component, csharp);
        ref readonly var plan = ref fixture.Writer.Plan;
        var presenter = GetElement(plan, "ContentPresenter", isLocal: false);
        var derived = GetElement(plan, "DerivedPresenter", isLocal: false);
        var templatePresenter = GetElement(plan, "ContentPresenter", isLocal: true);

        Assert.True(plan.Lifecycle.HasComponentContentPresenters);
        Assert.True(presenter.RequiresContentPresenterRefresh);
        Assert.True(derived.RequiresContentPresenterRefresh);
        Assert.False(templatePresenter.RequiresContentPresenterRefresh);

        var methods = SplitLifecycleMethods(fixture.WriteLifecycle());

        Assert.Equal(1, CountOccurrences(methods.FirstUpdate, "presenter.UpdateChild();"));
        Assert.Equal(1, CountOccurrences(methods.FirstUpdate, "derivedPresenter.UpdateChild();"));
        Assert.DoesNotContain("templatePresenter.UpdateChild();", methods.FirstUpdate, StringComparison.Ordinal);
        Assert.True(
            methods.FirstUpdate.LastIndexOf(".EndInit();", StringComparison.Ordinal) <
            methods.FirstUpdate.IndexOf("presenter.UpdateChild();", StringComparison.Ordinal),
            methods.FirstUpdate);
        Assert.True(
            methods.FirstUpdate.IndexOf("presenter.UpdateChild();", StringComparison.Ordinal) <
            methods.FirstUpdate.LastIndexOf("return ", StringComparison.Ordinal),
            methods.FirstUpdate);

        Assert.Equal(1, CountOccurrences(methods.Update, "presenter.UpdateChild();"));
        Assert.Equal(1, CountOccurrences(methods.Update, "derivedPresenter.UpdateChild();"));
        Assert.DoesNotContain("templatePresenter.UpdateChild();", methods.Update, StringComparison.Ordinal);
        Assert.True(
            methods.Update.IndexOf("GetContent()", StringComparison.Ordinal) <
            methods.Update.IndexOf("presenter.UpdateChild();", StringComparison.Ordinal),
            methods.Update);
        Assert.True(
            methods.Update.IndexOf("presenter.UpdateChild();", StringComparison.Ordinal) <
            methods.Update.LastIndexOf("return ", StringComparison.Ordinal),
            methods.Update);
    }

    private static ComponentElementPlan GetElement(
        in ComponentPlan plan,
        string tagName,
        bool isLocal)
    {
        return Assert.Single(
            plan.Elements,
            element =>
                element.IsLocal == isLocal &&
                string.Equals(
                    element.Syntax.StartTag?.Name.ToFullString().Trim(),
                    tagName,
                    StringComparison.Ordinal));
    }

    private static LifecycleFixture CreateFixture(
        string component,
        string? additionalCSharp = null,
        string? childComponent = null)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            additionalCSharp);

        if (childComponent != null)
        {
            var childTree = AkburaSyntaxTree.ParseText(
                childComponent,
                "Child.akbura");
            var compilation = new AkburaCompilation(
                fixture.CSharpCompilation,
                [fixture.ComponentTree, childTree],
                rootNamespace: "Demo");
            fixture = new AkcssActivatorPlannerTests.PlannerFixture(
                fixture.CSharpCompilation,
                fixture.ComponentTree,
                externalAkcssTree: null,
                compilation.GetSemanticModel(fixture.ComponentTree));
        }

        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol);
        var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 4,
        };
        var writer = new ComponentWriter(
            codeWriter,
            componentSymbol,
            fixture.SemanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>());

        return new LifecycleFixture(codeWriter, writer);
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

    private sealed class LifecycleFixture : IDisposable
    {
        public LifecycleFixture(
            CodeWriter codeWriter,
            ComponentWriter writer)
        {
            CodeWriter = codeWriter;
            Writer = writer;
        }

        public CodeWriter CodeWriter { get; }

        public ComponentWriter Writer { get; }

        public string WriteLifecycle()
        {
            Writer.WriteLifecycleFields();
            Writer.WriteLifecycleMembers();
            return CodeWriter.GetText().ToString();
        }

        public void Dispose()
        {
            CodeWriter.Dispose();
        }
    }
}
