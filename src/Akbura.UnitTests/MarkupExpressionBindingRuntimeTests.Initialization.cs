using Akbura.ComponentTree;
using Akbura.Language.CodeGeneration;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.UnitTests;

public sealed partial class MarkupExpressionBindingRuntimeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedStableInitialization_StateParamServiceAndSelfNameStayReactive(int mode)
    {
        var compiled = Compile("""
            <StackPanel>
                <TextBlock x.Name="text" Text=${x.bind $"Current width: {text.Width}"} Width=${x.bind Hello + 200 + a} />
                <TextBlock Text=${x.bind Vm.Name} />
                <TextBlock Text={Vm.Count.ToString()} />
            </StackPanel>
            """, mode, "inject Model Vm; param int Hello = 200; state int a = 11;", """
            public partial class ExpressionDemo
            {
                public void SetHello(int value) => Hello = value;
                public void SetA(int value) => a = value;
            }
            """);
        var methods = CSharpSyntaxTree.ParseText(compiled.Source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>();
        _output.WriteLine("Actual generated initialization/update methods:\n" + string.Join("\n", methods
            .Where(method => method.Identifier.ValueText is "FirstUpdate" or "Update" or "__AkburaHotReloadUpdateInitialValues")
            .Select(method => method.ToFullString())));
        var update = Assert.Single(methods.Where(method => method.Identifier.ValueText == "Update"));
        Assert.DoesNotContain(".__AkburaBindExpression<", update.ToFullString());
        Assert.DoesNotContain(".ReconcileExpressionBinding<", update.ToFullString());
        if (mode == 0)
        {
            var initial = Assert.Single(methods.Where(method => method.Identifier.ValueText == "FirstUpdate"));
            Assert.Equal(3, initial.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Count(call => call.Expression.ToString().Contains(".__AkburaBindExpression<", StringComparison.Ordinal)));
        }
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            var control = (AkburaControl)component;
            var descriptor = Assert.IsAssignableFrom<InjectService>(((object)component).GetType().GetField("VmProperty")!.GetValue(component));
            dynamic vm = Activator.CreateInstance(compiled.Assembly.GetType("Demo.Model")!)!;
            control.SetValue(descriptor.AvaloniaProperty, (object)vm);
            component.InitializeForTest();
            var panel = Assert.IsType<StackPanel>(control.Child);
            var text = Assert.IsType<TextBlock>(panel.Children[0]);
            Assert.Equal(411d, text.Width);
            Assert.Equal("Current width: 411", text.Text);
            Assert.Equal("Initial", ((TextBlock)panel.Children[1]).Text);
            Assert.Equal(1, (int)vm.NameReads);
            var countReads = (int)vm.CountReads;
            var adds = (int)vm.Adds;
            for (var frame = 0; frame < 3; frame++) component.RenderAgain();
            Assert.Equal(1, (int)vm.NameReads);
            Assert.Equal(adds, (int)vm.Adds);
            Assert.Equal(countReads + 3, (int)vm.CountReads); // Ordinary {expression} still renders.
            component.SetHello(300);
            Assert.Equal(511d, text.Width);
            Assert.Equal("Current width: 511", text.Text);
            component.SetA(20);
            Assert.Equal(520d, text.Width);
            Assert.Equal("Current width: 520", text.Text);
            text.Width = 123;
            Assert.Equal("Current width: 123", text.Text);
            vm.Name = "Changed";
            Assert.Equal("Changed", ((TextBlock)panel.Children[1]).Text);
            Assert.Equal(2, (int)vm.NameReads);
            dynamic next = vm.Create();
            next.Name = "Replacement";
            control.SetValue(descriptor.AvaloniaProperty, (object)next);
            Assert.Equal("Replacement", ((TextBlock)panel.Children[1]).Text);
            Assert.Equal((int)vm.Adds, (int)vm.Removes);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedStableInitialization_AvaloniaHookRemainsReactive(int mode)
    {
        // Avalonia Height is double, so its hook is State<double>, not State<int>.
        var compiled = Compile("<TextBlock x.Name=\"text\" Text=${x.bind $\"Current width: {text.Width}\"} Width=${x.bind Hello + 200 + a} />",
            mode, "using Akbura.Hooks; state Model Vm = new(); param int Hello = 200; state double a = useAvaloniaProperty(Height);");
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            var control = (AkburaControl)component;
            control.Height = 11;
            var window = new Window { Content = control };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(control.Child);
                Assert.Equal(411d, text.Width);
                Assert.Equal("Current width: 411", text.Text);
                control.Height = 20;
                Dispatcher.UIThread.RunJobs();
                Assert.Same(text, control.Child);
                Assert.Equal(420d, text.Width);
                Assert.Equal("Current width: 420", text.Text);
            }
            finally { window.Close(); }
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedStableInitialization_RenderLocalCaptureRefreshesInsteadOfGoingStale(int mode)
    {
        var compiled = Compile("var current = Vm.Profile!; <TextBlock Text=${x.bind current.Name} />", mode);
        _output.WriteLine(compiled.Source);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic vm = component.ModelForTest, first = vm.Create(), next = vm.Create();
            first.Name = "First";
            next.Name = "Next";
            vm.Profile = first;
            component.InitializeForTest();
            var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
            Assert.Equal("First", text.Text);
            vm.Profile = next;
            component.RenderAgain();
            Assert.Same(text, ((AkburaControl)component).Child);
            Assert.Equal("Next", text.Text);
            Assert.Equal((int)first.Adds, (int)first.Removes);
            next.Name = "Reactive next";
            _output.WriteLine($"Next subscribers: {(int)next.Adds} added, {(int)next.Removes} removed; active: {(string)next.ActiveHandlers}");
            Assert.Equal("Reactive next", text.Text);
            first.Name = "Retired";
            Assert.Equal("Reactive next", text.Text);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedStableInitialization_ShadowingStateUsesTheRealRenderLocal(int mode)
    {
        var compiled = Compile("var Vm = Other; <TextBlock Text=${x.bind Vm.Name} />", mode,
            additionalHost: "public partial class ExpressionDemo { public Model Other { get; set; } = new(); }");
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            dynamic originalState = component.ModelForTest, local = component.Other;
            var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
            originalState.Name = "Not the local";
            Assert.Equal("Initial", text.Text);
            local.Name = "Actual local";
            Assert.Equal("Actual local", text.Text);
            dynamic next = local.Create();
            next.Name = "Replacement local";
            component.Other = next;
            component.RenderAgain();
            Assert.Equal("Replacement local", text.Text);
            Assert.Equal((int)local.Adds, (int)local.Removes);
            return true;
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedStableInitialization_ActualStructuralRevisionStillReconciles(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.Name} />", mode);
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            dynamic vm = component.ModelForTest;
            var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
            var state = ReadRenderState((object)component);
            var reads = (int)vm.NameReads;
            var adds = (int)vm.Adds;
            component.RenderAgain();
            Assert.Equal(reads, (int)vm.NameReads);
            state.Invalidate();
            component.RenderAgain();
            Assert.Same(text, ((AkburaControl)component).Child);
            Assert.Equal(reads + 1, (int)vm.NameReads);
            Assert.Equal(adds, (int)vm.Adds);
            vm.Name = "After revision";
            Assert.Equal("After revision", text.Text);
            state.Dispose();
            Assert.Equal((int)vm.Adds, (int)vm.Removes);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedStableInitialization_ForeachOwnsStableAndRefreshedBindingsSeparately(int mode)
    {
        var compiled = Compile("""
            <StackPanel>$foreach (var item in Vm.Items)
            {
                <StackPanel>
                    <TextBlock Text=${x.bind Vm.Name} />
                    <TextBlock Text=${x.bind item.Name + ":" + index} />
                </StackPanel>
            }</StackPanel>
            """, mode);
        _output.WriteLine(compiled.Source);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic vm = component.ModelForTest, first = vm.Create(), second = vm.Create();
            first.Name = "First";
            second.Name = "Second";
            vm.Items.Add(first);
            vm.Items.Add(second);
            component.InitializeForTest();
            var panel = Assert.IsType<StackPanel>(((AkburaControl)component).Child);
            var firstPanel = Assert.IsType<StackPanel>(panel.Children[0]);
            var secondPanel = Assert.IsType<StackPanel>(panel.Children[1]);
            var retiredText = Assert.IsType<TextBlock>(firstPanel.Children[0]);
            var reads = (int)vm.NameReads;
            component.RenderAgain();
            Assert.Equal(reads, (int)vm.NameReads);
            vm.Items.Move(0, 1);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(firstPanel, panel.Children[1]);
            Assert.Same(secondPanel, panel.Children[0]);
            Assert.Equal(reads, (int)vm.NameReads);
            Assert.Equal("First:1", ((TextBlock)firstPanel.Children[1]).Text);
            vm.Items.Remove(first);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((int)first.Adds, (int)first.Removes);
            var retired = retiredText.Text;
            vm.Name = "Survivor";
            Assert.Equal(retired, retiredText.Text);
            Assert.Equal("Survivor", ((TextBlock)secondPanel.Children[0]).Text);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedStableInitialization_IsolatedCallbacksKeepOwnerAndSelfReferences(int mode)
    {
        var compiled = Compile("""
            <StackPanel>$foreach (var item in Vm.Items)
            {
                <TextBlock x.Name="text" Text=${x.bind $"{text.Width}:{Service.Name}:{Stable.Name}"} Width=${x.bind Hello + a} />
            }</StackPanel>
            """, mode, "state Model Vm = new(); inject Model Service; param int Hello = 200; state int a = 11;", """
            public partial class ExpressionDemo
            {
                public readonly Model Stable = new();
                public void SetHello(int value) => Hello = value;
            }
            """);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic vm = component.ModelForTest, service = vm.Create();
            service.Name = "Service";
            vm.Items.Add(vm.Create());
            var control = (AkburaControl)component;
            var descriptor = Assert.IsAssignableFrom<InjectService>(((object)component).GetType().GetField("ServiceProperty")!.GetValue(component));
            control.SetValue(descriptor.AvaloniaProperty, (object)service);
            component.InitializeForTest();
            var text = Assert.IsType<TextBlock>(Assert.Single(Assert.IsType<StackPanel>(control.Child).Children));
            Assert.Equal("211:Service:Initial", text.Text);
            var reads = (int)service.NameReads;
            component.RenderAgain();
            Assert.Equal(reads, (int)service.NameReads);
            component.SetHello(300);
            Assert.Equal("311:Service:Initial", text.Text);
            service.Name = "Changed service";
            Assert.Equal("311:Changed service:Initial", text.Text);
            dynamic stable = component.Stable;
            stable.Name = "Changed stable";
            Assert.Equal("311:Changed service:Changed stable", text.Text);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedStableInitialization_KeyedReplacementDoesNotRetainRetiredItem(int mode)
    {
        var compiled = Compile("""
            <StackPanel>$foreach (var item in Vm.Items; key: item.Count)
            {
                <StackPanel>
                    <TextBlock Text=${x.bind Vm.Name} />
                    <TextBlock Text=${x.bind item.Name} />
                </StackPanel>
            }</StackPanel>
            """, mode);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            var retired = ReplaceKeyedInitializationItem((object)component);
            AssertCollected([retired]);
            GC.KeepAlive((object)component);
            return true;
        });
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ReplaceKeyedInitializationItem(object owner)
    {
        dynamic component = owner, vm = component.ModelForTest, retired = vm.Create();
        retired.Count = 7;
        vm.Items.Add(retired);
        component.InitializeForTest();
        var panel = Assert.IsType<StackPanel>(((AkburaControl)component).Child);
        var root = panel.Children[0];
        dynamic current = vm.Create();
        current.Count = 7;
        current.Name = "Current item";
        vm.Items[0] = current;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(root, panel.Children[0]);
        Assert.Equal("Current item", ((TextBlock)((StackPanel)root).Children[1]).Text);
        Assert.Equal((int)retired.Adds, (int)retired.Removes);
        return new WeakReference((object)retired);
    }
}
