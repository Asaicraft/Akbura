using Akbura.Akcss;
using Akbura.ComponentTree;
using Akbura.HotReload;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReactiveUtilityConditionGenerationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    public static TheoryData<string> Expressions => new()
    {
        "true",
        "Vm.Enabled",
        "Vm.Profile?.Enabled == true",
        "Vm.Enabled && Vm.Count > 0",
        "Vm.Items[Vm.SelectedIndex].Enabled",
        "(Vm.Enabled && Vm.Count > 5) || Vm.Profile?.Enabled == true",
        "Vm.Input.IsReadOnly",
        "Vm.Count switch { > 2 => true, _ => false }",
        "Vm.Name == $\"{Vm.FirstName} {Vm.LastName}\"",
        "Vm.Format(Vm.FirstName, Vm.LastName) == \"hidden\"",
    };

    [Theory]
    [MemberData(nameof(Expressions))]
    public void GeneratedCSharp_CompilesTrackedConditionsInEveryMode(string expression)
    {
        for (var mode = 0; mode < 3; mode++)
        {
            var compiled = MarkupExpressionBindingRuntimeTests.Compile(
                "<TextBlock ${x.bind " + expression + "}:hidden/>", mode,
                "using Akbura.Styles.akcss; state Model Vm = new();");
            Assert.Contains("reactiveCondition:", compiled.Source, StringComparison.Ordinal);
            Assert.Contains("ExpressionBindingContext", compiled.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("condition: () =>", compiled.Source, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedStateCondition_ChangesVisibilityWithoutRender(int mode)
    {
        var compiled = Compile("<StackPanel><TextBlock ${x.bind width > 1000}:hidden/>" +
            "<TextBlock {width > 1000}:hidden/></StackPanel>", mode,
            "state double width = 800;", """
            public partial class ExpressionDemo
            {
                public void SetWidth(double value) => width = value;
                public System.IDisposable PauseRendersForTest() => SuppressUpdates();
            }
            """);
        Assert.Contains(".State(", compiled.Source, StringComparison.Ordinal);
        if (mode == 0) output.WriteLine(compiled.Source);
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            var panel = Assert.IsType<StackPanel>(((AkburaControl)component).Child);
            var text = Assert.IsType<TextBlock>(panel.Children[0]);
            var frameCondition = Assert.IsType<TextBlock>(panel.Children[1]);
            var renders = (int)component.RenderCalls;
            Assert.True(renders > 0);
            Assert.True(text.IsVisible);
            Assert.True(frameCondition.IsVisible);
            // State changes still request ordinary component updates. Hold those updates
            // back to prove that the observed condition, unlike the frame condition,
            // applies every transition without executing any component render.
            using ((IDisposable)component.PauseRendersForTest())
            {
                component.SetWidth(1100d);
                Assert.False(text.IsVisible);
                component.SetWidth(1400d);
                Assert.False(text.IsVisible);
                component.SetWidth(800d);
                Assert.True(text.IsVisible);
                component.SetWidth(1100d);
                Assert.False(text.IsVisible);
                Assert.True(frameCondition.IsVisible);
                Assert.Equal(renders, (int)component.RenderCalls);
            }
            // Releasing the scope preserves the existing batched state-render behavior.
            Assert.Equal(renders + 1, (int)component.RenderCalls);
            Assert.False(text.IsVisible);
            Assert.False(frameCondition.IsVisible);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedPropertyCondition_IsReactiveAndStableAcrossOrdinaryFrames(int mode)
    {
        var compiled = Compile("<TextBlock ${x.bind Vm.Enabled}:hidden/>", mode);
        Assert.Contains(".Property(", compiled.Source, StringComparison.Ordinal);
        await OnUi(() =>
        {
            dynamic component = Create(compiled), source = component.ModelForTest;
            var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
            var candidate = Assert.IsType<AkcssUtilityCandidateActivator>(Assert.Single(AkburaControl.GetAkcssStyles(text)));
            var renders = (int)component.RenderCalls;
            Assert.True(text.IsVisible);
            source.Enabled = true;
            Assert.False(text.IsVisible);
            source.Enabled = false;
            Assert.True(text.IsVisible);
            Assert.Equal(renders, (int)component.RenderCalls);
            var adds = (int)source.Adds;
            component.RenderAgain();
            Assert.Same(candidate, Assert.Single(AkburaControl.GetAkcssStyles(text)));
            Assert.Equal(adds, (int)source.Adds);
            Assert.Equal(0, (int)source.Removes);
            source.Enabled = true;
            Assert.False(text.IsVisible);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedParameterAndServiceConditions_TrackTheirExistingDescriptors(int mode)
    {
        var compiled = Compile("<StackPanel><TextBlock ${x.bind Hide}:hidden/>" +
            "<TextBlock ${x.bind Service.Enabled}:hidden/></StackPanel>", mode,
            "param bool Hide = false; inject Model Service;", """
            public partial class ExpressionDemo
            {
                public void SetHideForTest(bool value) => Hide = value;
                public System.IDisposable PauseRendersForTest() => SuppressUpdates();
            }
            """);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic source = component.ModelForTest, service = source.Create(), next = source.Create();
            var control = (AkburaControl)component;
            var descriptor = Assert.IsAssignableFrom<InjectService>(((object)component).GetType()
                .GetField("ServiceProperty")!.GetValue(component));
            control.SetValue(descriptor.AvaloniaProperty, (object)service);
            component.InitializeForTest();
            var panel = Assert.IsType<StackPanel>(control.Child);
            var renders = (int)component.RenderCalls;
            Assert.True(panel.Children[0].IsVisible);
            Assert.True(panel.Children[1].IsVisible);
            using ((IDisposable)component.PauseRendersForTest())
            {
                component.SetHideForTest(true);
                Assert.False(panel.Children[0].IsVisible);
                service.Enabled = true;
                Assert.False(panel.Children[1].IsVisible);
                control.SetValue(descriptor.AvaloniaProperty, (object)next);
                Assert.True(panel.Children[1].IsVisible);
                Assert.Equal((int)service.Adds, (int)service.Removes);
                next.Enabled = true;
                Assert.False(panel.Children[1].IsVisible);
                component.SetHideForTest(false);
                Assert.True(panel.Children[0].IsVisible);
                Assert.Equal(renders, (int)component.RenderCalls);
            }
            Assert.False(panel.Children[1].IsVisible);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedNestedAndIndexedConditions_RewireTheirActualDependencies(int mode)
    {
        var compiled = Compile("<StackPanel><TextBlock ${x.bind Vm.Profile?.Enabled == true}:hidden/>" +
            "<TextBlock ${x.bind Vm.Items[Vm.SelectedIndex].Enabled}:hidden/></StackPanel>", mode);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic source = component.ModelForTest, first = source.Create(), second = source.Create();
            source.Profile = first;
            source.Items.Add(first);
            source.Items.Add(second);
            component.InitializeForTest();
            var panel = Assert.IsType<StackPanel>(((AkburaControl)component).Child);
            var renders = (int)component.RenderCalls;
            first.Enabled = true;
            Assert.False(panel.Children[0].IsVisible);
            Assert.False(panel.Children[1].IsVisible);
            source.Profile = second;
            source.SelectedIndex = 1;
            Assert.True(panel.Children[0].IsVisible);
            Assert.True(panel.Children[1].IsVisible);
            Assert.Equal((int)first.Adds, (int)first.Removes);
            first.Enabled = false;
            second.Enabled = true;
            Assert.False(panel.Children[0].IsVisible);
            Assert.False(panel.Children[1].IsVisible);
            source.Profile = null;
            Assert.True(panel.Children[0].IsVisible);
            Assert.Equal(renders, (int)component.RenderCalls);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedCascade_CompetingConditionsRestoreTheCorrectBackground(int mode)
    {
        var compiled = Compile("<Border bg-slate-700 ${x.bind Vm.Enabled}:bg-blue-500 ${x.bind Vm.Count > 0}:bg-red-500/>", mode);
        await OnUi(() =>
        {
            dynamic component = Create(compiled), source = component.ModelForTest;
            var target = Assert.IsType<Border>(((AkburaControl)component).Child);
            target.Resources["--color-slate-700"] = Avalonia.Media.Brushes.SlateGray;
            target.Resources["--color-blue-500"] = Avalonia.Media.Brushes.Blue;
            target.Resources["--color-red-500"] = Avalonia.Media.Brushes.Red;
            var baseline = target.Background;
            Assert.NotNull(baseline);
            var renders = (int)component.RenderCalls;
            source.Enabled = true;
            var selected = target.Background;
            Assert.NotEqual(baseline, selected);
            source.Count = 1;
            var error = target.Background;
            Assert.NotEqual(selected, error);
            source.Enabled = false;
            Assert.Equal(error, target.Background);
            source.Count = 0;
            Assert.Equal(baseline, target.Background);
            Assert.Equal(renders, (int)component.RenderCalls);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedOrdinaryCondition_RemainsFrameEvaluated(int mode)
    {
        var compiled = Compile("<StackPanel><TextBlock {Vm.Enabled}:hidden/>" +
            "<TextBlock ${x.bind Vm.Enabled}:hidden/></StackPanel>", mode);
        Assert.Contains("condition: () =>", compiled.Source, StringComparison.Ordinal);
        await OnUi(() =>
        {
            dynamic component = Create(compiled), source = component.ModelForTest;
            var panel = Assert.IsType<StackPanel>(((AkburaControl)component).Child);
            source.Enabled = true;
            Assert.True(panel.Children[0].IsVisible);
            Assert.False(panel.Children[1].IsVisible);
            component.RenderAgain();
            Assert.False(panel.Children[0].IsVisible);
            source.Enabled = false;
            Assert.False(panel.Children[0].IsVisible);
            Assert.True(panel.Children[1].IsVisible);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedRenderLocal_RefreshesCallbackWithoutRetainingPreviousSource(int mode)
    {
        var compiled = Compile("var current = Vm.Profile!; <TextBlock ${x.bind current.Enabled}:hidden/>", mode);
        Assert.Contains("RefreshReactiveCondition(", compiled.Source, StringComparison.Ordinal);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic source = component.ModelForTest, first = source.Create(), next = source.Create();
            source.Profile = first;
            component.InitializeForTest();
            var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
            var candidate = Assert.Single(AkburaControl.GetAkcssStyles(text));
            next.Enabled = true;
            source.Profile = next;
            Assert.True(text.IsVisible); // Capturing a local is not observing its initializer.
            component.RenderAgain();
            Assert.False(text.IsVisible);
            Assert.Same(candidate, Assert.Single(AkburaControl.GetAkcssStyles(text)));
            Assert.Equal((int)first.Adds, (int)first.Removes);
            next.Enabled = false;
            Assert.True(text.IsVisible);
            first.Enabled = true;
            Assert.True(text.IsVisible);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedNamedReceiver_ObservesTheRealControl(int mode)
    {
        var compiled = Compile("<TextBlock x.Name=\"text\" Width=\"800\" ${x.bind text.Width > 1000}:hidden/>", mode);
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
            var renders = (int)component.RenderCalls;
            Assert.True(text.IsVisible);
            text.Width = 1100;
            Assert.False(text.IsVisible);
            text.Width = 800;
            Assert.True(text.IsVisible);
            Assert.Equal(renders, (int)component.RenderCalls);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedKeyedReplacement_ReleasesTheRetiredItem(int mode)
    {
        var compiled = Compile("<StackPanel>$foreach (var item in Vm.Items; key: item.Count) { <StackPanel>" +
            "<TextBlock ${x.bind Vm.Enabled}:hidden/><TextBlock ${x.bind item.Enabled}:hidden/>" +
            "</StackPanel> }</StackPanel>", mode);
        await OnUi(() =>
        {
            var component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            var retired = ReplaceItem(component);
            for (var attempt = 0; attempt < 8 && retired.IsAlive; attempt++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
            }
            Assert.False(retired.IsAlive, "A retained reactive utility callback must not keep the retired keyed item.");
            GC.KeepAlive(component);
            return true;
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReplaceItem(object owner)
    {
        dynamic component = owner, source = component.ModelForTest, retired = source.Create();
        retired.Count = 7;
        source.Items.Add(retired);
        component.InitializeForTest();
        var panel = Assert.IsType<StackPanel>(((AkburaControl)component).Child);
        var root = Assert.IsType<StackPanel>(Assert.Single(panel.Children));
        dynamic current = source.Create();
        current.Count = 7;
        current.Enabled = true;
        source.Items[0] = current;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(root, Assert.Single(panel.Children));
        Assert.False(root.Children[1].IsVisible);
        Assert.Equal((int)retired.Adds, (int)retired.Removes);
        retired.Enabled = true;
        current.Enabled = false;
        Assert.True(root.Children[1].IsVisible);
        source.Enabled = true;
        Assert.False(root.Children[0].IsVisible);
        return new WeakReference((object)retired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedInactiveBranch_StopsObservingRemovedControls(int mode)
    {
        var compiled = Compile("<StackPanel>$if (Vm.Enabled) { <TextBlock ${x.bind Vm.Profile!.Enabled}:hidden/> }</StackPanel>", mode);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic source = component.ModelForTest, profile = source.Create();
            source.Enabled = true;
            source.Profile = profile;
            component.InitializeForTest();
            var panel = Assert.IsType<StackPanel>(((AkburaControl)component).Child);
            var text = Assert.IsType<TextBlock>(Assert.Single(panel.Children));
            profile.Enabled = true;
            Assert.False(text.IsVisible);
            source.Enabled = false;
            component.RenderAgain();
            Assert.Empty(panel.Children);
            Assert.Equal((int)profile.Adds, (int)profile.Removes);
            var removedValue = text.IsVisible;
            profile.Enabled = false;
            profile.Enabled = true;
            Assert.Equal(removedValue, text.IsVisible);
            source.Enabled = true;
            component.RenderAgain();
            Assert.False(Assert.Single(panel.Children).IsVisible);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedTemplateInstances_HaveIndependentConditionOwnership(int mode)
    {
        var compiled = Compile("<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"Model\" x.ItemName=\"person\">" +
            "<TextBlock ${x.bind person.Enabled}:hidden/>" +
            "</ItemsControl.ItemTemplate></ItemsControl>", mode);
        await OnUi(() =>
        {
            dynamic component = Create(compiled), source = component.ModelForTest;
            dynamic first = source.Create(), second = source.Create();
            var items = Assert.IsType<ItemsControl>(((AkburaControl)component).Child);
            var template = Assert.IsAssignableFrom<IDataTemplate>(items.ItemTemplate);
            var firstText = Assert.IsType<TextBlock>(template.Build((object)first));
            var secondText = Assert.IsType<TextBlock>(template.Build((object)second));
            var window = new Window { Content = new StackPanel { Children = { firstText, secondText } } };
            try
            {
                window.Show();
                first.Enabled = true;
                Assert.False(firstText.IsVisible);
                Assert.True(secondText.IsVisible);
                second.Enabled = true;
                Assert.False(secondText.IsVisible);
            }
            finally { window.Close(); }
            Assert.Equal((int)first.Adds, (int)first.Removes);
            Assert.Equal((int)second.Adds, (int)second.Removes);
            return true;
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedStructuralRetirement_CannotBeRevivedByVisualReattachment(int mode)
    {
        var compiled = Compile("<TextBlock ${x.bind Vm.Enabled}:hidden/>", mode);
        await OnUi(() =>
        {
            dynamic component = Create(compiled), source = component.ModelForTest;
            var control = (AkburaControl)component;
            var window = new Window { Content = control };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(control.Child);
                var state = Assert.IsType<AkburaRenderState>(((object)component).GetType()
                    .GetField("__akburaRenderState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                state.Dispose();
                Assert.Equal((int)source.Adds, (int)source.Removes);
                var retiredValue = text.IsVisible;
                window.Content = null;
                window.Content = control;
                source.Enabled = true;
                Assert.Equal(retiredValue, text.IsVisible);
                Assert.Equal((int)source.Adds, (int)source.Removes);
            }
            finally { window.Close(); }
            return true;
        });
    }

    private static MarkupExpressionBindingRuntimeTests.CompiledComponent Compile(string markup, int mode,
        string declarations = "", string additionalHost = "") => MarkupExpressionBindingRuntimeTests.Compile(
            "MarkRender(); " + markup, mode, "using Akbura.Styles.akcss; state Model Vm = new(); " + declarations,
            "public partial class ExpressionDemo { public int RenderCalls; public void MarkRender() => RenderCalls++; } " + additionalHost);

    private static dynamic Create(MarkupExpressionBindingRuntimeTests.CompiledComponent compiled)
    {
        dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
        component.InitializeForTest();
        return component;
    }

    private static async Task OnUi(Func<bool> action) =>
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(action, CancellationToken.None);
}
