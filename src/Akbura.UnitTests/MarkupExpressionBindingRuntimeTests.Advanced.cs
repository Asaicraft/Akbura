using Akbura.ComponentTree;
using Akbura.HotReload;
using Avalonia.Controls;
using Avalonia.Threading;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Akbura.UnitTests;

public sealed partial class MarkupExpressionBindingRuntimeTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public MarkupExpressionBindingRuntimeTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedShortCircuit_DoesNotReadOrObserveInactiveOperands(int mode)
    {
        var compiled = Compile("""
            <StackPanel>
                <TextBlock IsVisible=${x.bind Vm.Enabled && Vm.Count > 0} />
                <TextBlock IsVisible=${x.bind !Vm.Enabled || Vm.Count > 0} />
                <TextBlock Text=${x.bind Vm.Profile?.Name ?? Vm.Name} />
            </StackPanel>
            """, mode);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic vm = component.ModelForTest;
            dynamic profile = vm.Create();
            profile.Name = "Profile";
            vm.Profile = profile;
            vm.Count = 1;
            component.InitializeForTest();
            var panel = Assert.IsType<StackPanel>(((AkburaControl)component).Child);
            Assert.False(panel.Children[0].IsVisible);
            Assert.True(panel.Children[1].IsVisible);
            Assert.Equal(0, (int)vm.CountReads);
            Assert.Equal(0, (int)vm.NameReads);
            vm.Enabled = true;
            Assert.True(panel.Children[0].IsVisible);
            Assert.True(panel.Children[1].IsVisible);
            Assert.Equal(2, (int)vm.CountReads);
            vm.Count = 0;
            Assert.False(panel.Children[0].IsVisible);
            Assert.False(panel.Children[1].IsVisible);
            vm.Enabled = false;
            var countReads = (int)vm.CountReads;
            vm.Count = 3;
            Assert.Equal(countReads, (int)vm.CountReads);
            vm.Name = "Fallback";
            Assert.Equal(0, (int)vm.NameReads);
            vm.Profile = null;
            Assert.Equal("Fallback", ((TextBlock)panel.Children[2]).Text);
            Assert.Equal((int)profile.Adds, (int)profile.Removes);
            var reads = (int)vm.NameReads;
            profile.Name = "Obsolete";
            Assert.Equal(reads, (int)vm.NameReads);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedStates_ConvertValuesAndReplaceIndependentRoots(int mode)
    {
        var compiled = Compile("""
            <StackPanel>
                <TextBlock Text=${x.bind Counter.ToString()} />
                <TextBlock Width=${x.bind Counter + 1} />
                <TextBlock Text=${x.bind Other.Name + ":" + Vm.Name} />
                <TextBlock Text=${x.bind "Constant"} />
            </StackPanel>
            """, mode, "state Model Vm = new(); state Model Other = new(); state int Counter = 1;", """
            public partial class ExpressionDemo
            {
                public void SetCounter(int value) => Counter = value;
                public void ReplaceVm(Model value) => Vm = value;
                public Model OtherForTest => Other;
            }
            """);
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            dynamic first = component.ModelForTest, other = component.OtherForTest;
            var panel = Assert.IsType<StackPanel>(((AkburaControl)component).Child);
            Assert.Equal("1", ((TextBlock)panel.Children[0]).Text);
            Assert.Equal(2d, panel.Children[1].Width);
            Assert.Equal("Initial:Initial", ((TextBlock)panel.Children[2]).Text);
            Assert.Equal("Constant", ((TextBlock)panel.Children[3]).Text);
            var adds = (int)first.Adds;
            component.SetCounter(4);
            Assert.Equal("4", ((TextBlock)panel.Children[0]).Text);
            Assert.Equal(5d, panel.Children[1].Width);
            Assert.Equal(adds, (int)first.Adds);
            other.Name = "Other";
            Assert.Equal("Other:Initial", ((TextBlock)panel.Children[2]).Text);
            dynamic next = first.Create();
            next.Name = "Replacement";
            component.ReplaceVm(next);
            Assert.Equal("Other:Replacement", ((TextBlock)panel.Children[2]).Text);
            Assert.Equal((int)first.Adds, (int)first.Removes);
            Assert.Equal(1, (int)next.Adds - (int)next.Removes);
            first.Name = "Obsolete";
            Assert.Equal("Other:Replacement", ((TextBlock)panel.Children[2]).Text);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedAvaloniaProperty_UsesTheRegisteredSource(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.Input.Text ?? \"Empty\"} />", mode);
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            dynamic vm = component.ModelForTest;
            var input = Assert.IsType<TextBox>((object)vm.Input);
            var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
            Assert.Equal("Empty", text.Text);
            input.Text = "Typed";
            Assert.Equal("Typed", text.Text);
            input.Text = null;
            Assert.Equal("Empty", text.Text);
            component.RenderAgain();
            input.Text = "After render";
            Assert.Equal("After render", text.Text);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedInjectedService_ReconcilesReplacement(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Service.Name} />", mode,
            "state Model Vm = new(); inject Model Service;");
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            var descriptor = Assert.IsAssignableFrom<InjectService>(
                ((object)component).GetType().GetField("ServiceProperty")!.GetValue(component));
            dynamic first = Activator.CreateInstance(compiled.Assembly.GetType("Demo.Model")!)!;
            var control = (AkburaControl)component;
            control.SetValue(descriptor.AvaloniaProperty, (object)first);
            component.InitializeForTest();
            var text = Assert.IsType<TextBlock>(control.Child);
            first.Name = "Service change";
            Assert.Equal("Service change", text.Text);
            dynamic next = first.Create();
            next.Name = "New service";
            control.SetValue(descriptor.AvaloniaProperty, (object)next);
            Assert.Equal("New service", text.Text);
            Assert.Equal((int)first.Adds, (int)first.Removes);
            first.Name = "Old service";
            Assert.Equal("New service", text.Text);
            next.Name = "New service change";
            Assert.Equal("New service change", text.Text);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedCollection_ResetAndReplacementReleaseOldSelection(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.Items.Count > Vm.SelectedIndex ? Vm.Items[Vm.SelectedIndex].Name : \"Empty\"} />", mode);
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            dynamic vm = component.ModelForTest, selected = vm.Create();
            var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
            Assert.Equal("Empty", text.Text);
            selected.Name = "Selected";
            vm.Items.Add(selected);
            Assert.Equal("Selected", text.Text);
            vm.Items.Clear();
            Assert.Equal("Empty", text.Text);
            Assert.Equal((int)selected.Adds, (int)selected.Removes);
            vm.Items.Add(selected);
            dynamic oldCollection = vm.Items;
            vm.ReplaceItems();
            Assert.Equal("Empty", text.Text);
            Assert.Equal((int)selected.Adds, (int)selected.Removes);
            oldCollection.Add(vm.Create());
            selected.Name = "Retired selection";
            Assert.Equal("Empty", text.Text);
            vm.Items.Add(selected);
            Assert.Equal("Retired selection", text.Text);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedBackgroundNotifications_DiscardRetiredAndDetachedCallbacks(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.Profile?.Name ?? \"Empty\"} />", mode);
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            dynamic vm = component.ModelForTest, first = vm.Create();
            vm.Profile = first;
            var control = (AkburaControl)component;
            var text = Assert.IsType<TextBlock>(control.Child);
            var window = new Window { Content = control };
            try
            {
                window.Show();
                var writes = 0;
                text.PropertyChanged += (_, change) =>
                {
                    if (change.Property != TextBlock.TextProperty) return;
                    Assert.True(Dispatcher.UIThread.CheckAccess());
                    writes++;
                };
                RunDedicatedWorker(() => { first.Name = "Worker"; });
                Assert.Equal("Initial", text.Text);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Worker", text.Text);
                Assert.Equal(1, writes);
                RunDedicatedWorker(() => { first.Name = "Obsolete queued"; });
                dynamic next = first.Create();
                next.Name = "Replacement";
                vm.Profile = next;
                var reads = (int)next.NameReads;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Replacement", text.Text);
                Assert.Equal(reads, (int)next.NameReads);
                RunDedicatedWorker(() => { next.Name = "Queued before detach"; });
                window.Content = null;
                var detachedWrites = writes;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(detachedWrites, writes);
                Assert.Equal("Replacement", text.Text);
                Assert.Equal((int)next.Adds, (int)next.Removes);
                window.Content = control;
                Assert.Equal("Queued before detach", text.Text);
            }
            finally { window.Close(); }
            return true;
        });
    }

    private static void RunDedicatedWorker(Action action)
    {
        Assert.True(Dispatcher.UIThread.CheckAccess());
        var uiThreadId = Environment.CurrentManagedThreadId;
        ExceptionDispatchInfo? failure = null;
        // Blocking a Task.Run task may inline it on the waiting UI thread.
        // A dedicated thread makes queued-delivery assertions deterministic.
        var worker = new Thread(() =>
        {
            try
            {
                Assert.NotEqual(uiThreadId, Environment.CurrentManagedThreadId);
                Assert.False(Dispatcher.UIThread.CheckAccess());
                action();
            }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        }) { IsBackground = true };
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(10)), "The notification worker did not complete.");
        failure?.Throw();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedClrSetter_ReentrantNotificationIsDrainedWithoutRecursion(int mode)
    {
        var compiled = Compile("<ReentrantTarget Value=${x.bind Vm.Name} />", mode, additionalHost: """
            public sealed class ReentrantTarget : Avalonia.Controls.Control
            {
                private string? _value;
                public int Writes;
                public Action<string?>? Written;
                public string? Value { get => _value; set { Writes++; _value = value; Written?.Invoke(value); } }
            }
            """);
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            dynamic vm = component.ModelForTest, target = ((AkburaControl)component).Child!;
            var depth = 0;
            target.Written = (Action<string?>)(value =>
            {
                Assert.Equal(0, depth);
                depth++;
                try { if (value == "Intermediate") vm.Name = "Final"; }
                finally { depth--; }
            });
            vm.Name = "Intermediate";
            Assert.Equal("Final", (string)target.Value);
            Assert.Equal(3, (int)target.Writes);
            Assert.Equal(3, (int)vm.NameReads);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedSynchronousSubscriptionNotification_IsNotLostOrDuplicated(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.Name} />", mode);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic vm = component.ModelForTest;
            vm.NotifyDuringSubscribe = true;
            component.InitializeForTest();
            var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
            Assert.Equal("Initial", text.Text);
            Assert.Equal(1, (int)vm.Adds);
            Assert.Equal(2, (int)vm.NameReads);
            vm.Name = "Updated";
            Assert.Equal("Updated", text.Text);
            Assert.Equal(1, (int)vm.Adds);
            Assert.Equal(3, (int)vm.NameReads);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedTemplates_HaveIndependentLifetimes(int mode)
    {
        var compiled = Compile("""
            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Model" x.ItemName="item">
                    <TextBlock Text=${x.bind item.Name} />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """, mode);
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            dynamic vm = component.ModelForTest, first = vm.Create(), second = vm.Create();
            first.Name = "First";
            second.Name = "Second";
            var items = Assert.IsType<ItemsControl>(((AkburaControl)component).Child);
            var template = Assert.IsAssignableFrom<Avalonia.Controls.Templates.IDataTemplate>(items.ItemTemplate);
            var firstText = Assert.IsType<TextBlock>(template.Build((object)first));
            var secondText = Assert.IsType<TextBlock>(template.Build((object)second));
            var panel = new StackPanel { Children = { firstText, secondText } };
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                Assert.Equal("First", firstText.Text);
                Assert.Equal("Second", secondText.Text);
                Assert.Equal(1, (int)first.Adds - (int)first.Removes);
                Assert.Equal(1, (int)second.Adds - (int)second.Removes);
                panel.Children.Remove(firstText);
                Assert.Equal((int)first.Adds, (int)first.Removes);
                first.Name = "Detached template";
                second.Name = "Active template";
                Assert.Equal("First", firstText.Text);
                Assert.Equal("Active template", secondText.Text);
                panel.Children.Add(firstText);
                Assert.Equal("Detached template", firstText.Text);
                Assert.Equal(1, (int)first.Adds - (int)first.Removes);
            }
            finally { window.Close(); }
            Assert.Equal((int)first.Adds, (int)first.Removes);
            Assert.Equal((int)second.Adds, (int)second.Removes);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedConditionalLifetime_RetiresEachInactiveBinding(int mode)
    {
        var compiled = Compile("<Border>$if (Shown) { <TextBlock Text=${x.bind Vm.Name} /> }</Border>", mode,
            "state Model Vm = new(); state bool Shown = true;", """
            public partial class ExpressionDemo { public void ShowBranch(bool shown) => Shown = shown; }
            """);
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            dynamic vm = component.ModelForTest;
            var border = Assert.IsType<Border>(((AkburaControl)component).Child);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var retired = Assert.IsType<TextBlock>(border.Child);
                Assert.Equal(1, (int)vm.Adds - (int)vm.Removes);
                component.ShowBranch(false);
                Assert.Null(border.Child);
                Assert.Equal((int)vm.Adds, (int)vm.Removes);
                var retiredValue = retired.Text;
                var reads = (int)vm.NameReads;
                vm.Name = "Branch " + cycle;
                Assert.Equal(reads, (int)vm.NameReads);
                Assert.Equal(retiredValue, retired.Text);
                component.ShowBranch(true);
                Assert.Equal("Branch " + cycle, Assert.IsType<TextBlock>(border.Child).Text);
                Assert.Equal(1, (int)vm.Adds - (int)vm.Removes);
            }
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedForeach_ReusesControlsAndRefreshesCapturedIndexes(int mode)
    {
        var compiled = Compile("""
            <StackPanel>
                $foreach (var item in Vm.Items)
                {
                    var prefix = "Item:";
                    <TextBlock Text=${x.bind prefix + item.Name + ":" + index} />
                }
            </StackPanel>
            """, mode);
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
            var firstText = Assert.IsType<TextBlock>(panel.Children[0]);
            var secondText = Assert.IsType<TextBlock>(panel.Children[1]);
            Assert.Equal("Item:First:0", firstText.Text);
            Assert.Equal("Item:Second:1", secondText.Text);
            var adds = (int)first.Adds;
            component.RenderAgain();
            component.RenderAgain();
            Assert.Equal(adds, (int)first.Adds);
            vm.Items.Move(0, 1);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(secondText, panel.Children[0]);
            Assert.Same(firstText, panel.Children[1]);
            Assert.Equal("Item:First:1", firstText.Text);
            first.Name = "Changed";
            Assert.Equal("Item:Changed:1", firstText.Text);
            vm.Items.Remove(first);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(panel.Children);
            Assert.Equal((int)first.Adds, (int)first.Removes);
            var retiredValue = firstText.Text;
            first.Name = "Retired";
            Assert.Equal(retiredValue, firstText.Text);
            second.Name = "Survivor";
            Assert.Equal("Item:Survivor:0", secondText.Text);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedLocals_DoNotShadowCapturedExpressionNames(int mode)
    {
        var compiled = Compile("""
            <StackPanel>
                $foreach (var item in Vm.Items)
                {
                    var __expressionValue = "Value:";
                    var __expressionBaseline = "Baseline:";
                    <ReentrantTarget Value=${x.bind __expressionValue + __expressionBaseline + item.Name} />
                }
            </StackPanel>
            """, mode, additionalHost: """
            public sealed class ReentrantTarget : Avalonia.Controls.Control
            {
                public string Value { get; set; } = "";
            }
            """);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic vm = component.ModelForTest, item = vm.Create();
            vm.Items.Add(item);
            component.InitializeForTest();
            var panel = Assert.IsType<StackPanel>(((AkburaControl)component).Child);
            dynamic target = Assert.Single(panel.Children);
            Assert.Equal("Value:Baseline:Initial", (string)target.Value);
            item.Name = "Changed";
            Assert.Equal("Value:Baseline:Changed", (string)target.Value);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedForeach_IndexAndNameofUseTheirDeclaredMeanings(int mode)
    {
        var compiled = Compile("""
            <StackPanel>
                $foreach (var item in Vm.Items)
                {
                    <TextBlock Text=${x.bind $"{index}:{nameof(index)}"} />
                }
            </StackPanel>
            """, mode);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic vm = component.ModelForTest;
            vm.Items.Add(vm.Create());
            vm.Items.Add(vm.Create());
            component.InitializeForTest();
            var panel = Assert.IsType<StackPanel>(((AkburaControl)component).Child);
            var first = Assert.IsType<TextBlock>(panel.Children[0]);
            var second = Assert.IsType<TextBlock>(panel.Children[1]);
            Assert.Equal("0:index", first.Text);
            Assert.Equal("1:index", second.Text);
            vm.Items.Move(0, 1);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(second, panel.Children[0]);
            Assert.Same(first, panel.Children[1]);
            Assert.Equal("0:index", second.Text);
            Assert.Equal("1:index", first.Text);
            return true;
        });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task GeneratedInitialTargetFailure_DetachesObserversAndPreservesErrors(int mode, bool throwOnRemove)
    {
        var compiled = Compile("<RejectingTarget Value=${x.bind Vm.Name} />", mode, additionalHost: """
            public sealed class RejectingTarget : Avalonia.Controls.Control
            {
                private string? _value;
                public string? Value
                {
                    get => _value;
                    set
                    {
                        if (value == "Initial") throw new InvalidOperationException("Target rejected the initial value.");
                        _value = value;
                    }
                }
            }
            """);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic vm = component.ModelForTest;
            vm.ThrowOnRemove = throwOnRemove;
            var failure = Record.Exception(() => { component.InitializeForTest(); });
            Assert.NotNull(failure);
            _output.WriteLine(failure.ToString());
            Assert.Contains("Target rejected the initial value.", failure.ToString());
            if (throwOnRemove)
            {
                Assert.IsType<AggregateException>(failure);
                Assert.Contains("Subscription removal failed.", failure.ToString());
            }
            else Assert.IsType<InvalidOperationException>(failure);
            Assert.Equal(1, (int)vm.Adds);
            Assert.Equal((int)vm.Adds, (int)vm.Removes);
            var reads = (int)vm.NameReads;
            vm.Name = "Must not retry the failed target";
            Assert.Equal(reads, (int)vm.NameReads);
            return true;
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedCleanupFailure_StillReleasesAllObservers(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.FirstName + Vm.Profile!.Name} />", mode);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic vm = component.ModelForTest, profile = vm.Create();
            vm.Profile = profile;
            component.InitializeForTest();
            var state = ReadRenderState((object)component);
            var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
            vm.ThrowOnRemove = true;
            Assert.Throws<AggregateException>(state.Dispose);
            Assert.Equal((int)vm.Adds, (int)vm.Removes);
            Assert.Equal((int)profile.Adds, (int)profile.Removes);
            var retiredValue = text.Text;
            profile.Name = "Retired";
            Assert.Equal(retiredValue, text.Text);
            state.Dispose();
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedReceiverReplacement_DoesNotRetainTheOldGraph(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.Profile?.Name ?? \"Empty\"} />", mode);
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            var old = ReplaceObservedProfile((object)component);
            for (var attempt = 0; attempt < 3 && old.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(old.IsAlive);
            Assert.Equal("Current", Assert.IsType<TextBlock>(((AkburaControl)component).Child).Text);
            GC.KeepAlive((object)component);
            return true;
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReplaceObservedProfile(object owner)
    {
        dynamic component = owner;
        dynamic vm = component.ModelForTest, old = vm.Create();
        vm.Profile = old;
        dynamic current = vm.Create();
        current.Name = "Current";
        vm.Profile = current;
        return new WeakReference((object)old);
    }

    private static AkburaRenderState ReadRenderState(object component) =>
        Assert.IsType<AkburaRenderState>(component.GetType().GetField("__akburaRenderState",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));

    [Fact]
    public async Task StructuralRevision_ReleasesObsoleteCallbackCapturesAfterCommit()
    {
        await OnUi(() =>
        {
            using var state = new AkburaRenderState();
            var obsolete = InstallCapturedExpressionRevision(state, "first");
            var text = state.GetRequired<TextBlock>(0);
            InstallCapturedExpressionRevision(state, "second");
            Assert.Same(text, state.GetRequired<TextBlock>(0));
            Assert.Equal("second", text.Text);
            AssertCollected(obsolete);
            GC.KeepAlive(state);
            return true;
        });
    }

    [Fact]
    public async Task StructuralRevision_AbortRestoresPreviousCallbacksAndSubscriptions()
    {
        await OnUi(() =>
        {
            using var state = new AkburaRenderState();
            var previous = InstallCapturedExpressionRevision(state, "first");
            var source = Assert.IsType<CapturedExpressionValue>(previous[0].Target);
            var text = state.GetRequired<TextBlock>(0);
            var replacement = InstallCapturedExpressionRevision(state, "second", complete: false);
            var replacementSource = Assert.IsType<CapturedExpressionValue>(replacement[0].Target);
            Assert.Equal("second", text.Text);
            Assert.Equal(0, source.Subscribers);
            Assert.Equal(1, replacementSource.Subscribers);
            state.AbortRevision();
            Assert.Equal("first", text.Text);
            Assert.Equal(1, source.Subscribers);
            Assert.Equal(0, replacementSource.Subscribers);
            source.Name = "Restored";
            source.Notify();
            Assert.Equal("Restored", text.Text);
            replacementSource.Name = "Retired";
            replacementSource.Notify();
            Assert.Equal("Restored", text.Text);
            return true;
        });
    }

    [Fact]
    public async Task StructuralRevision_RemovalReleasesCallbacksAndRestoresBaseline()
    {
        await OnUi(() =>
        {
            using var state = new AkburaRenderState();
            var obsolete = InstallCapturedExpressionRevision(state, "first");
            var text = state.GetRequired<TextBlock>(0);
            BeginExpressionRevision(state, "removed");
            state.CompleteRevision();
            Assert.Same(text, state.GetRequired<TextBlock>(0));
            Assert.Equal("Baseline", text.Text);
            AssertCollected(obsolete);
            GC.KeepAlive(state);
            return true;
        });
    }

    private static void BeginExpressionRevision(AkburaRenderState state, string revision) =>
        Assert.True(state.BeginRevision(revision,
            static builder => builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(TextBlock), null, "TextBlock")),
            static _ => new TextBlock { Text = "Baseline" }));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] InstallCapturedExpressionRevision(AkburaRenderState state, string revision, bool complete = true)
    {
        BeginExpressionRevision(state, revision);
        var text = state.GetRequired<TextBlock>(0);
        var source = new CapturedExpressionValue { Name = revision };
        var writerCapture = new object();
        state.ReconcileExpressionBinding(0, "Text", revision,
            context => context.Property(source, 1, nameof(CapturedExpressionValue.Name)).Name,
            value => { GC.KeepAlive(writerCapture); text.Text = value; }, text, TextBlock.TextProperty);
        if (complete) state.CompleteRevision();
        return [new WeakReference(source), new WeakReference(writerCapture)];
    }

    private static void AssertCollected(IEnumerable<WeakReference> references)
    {
        for (var attempt = 0; attempt < 3 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.All(references, reference => Assert.False(reference.IsAlive));
    }

    private sealed class CapturedExpressionValue : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public int Subscribers => PropertyChanged?.GetInvocationList().Length ?? 0;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }

    [Fact]
    public async Task GeneratedNotificationHotPath_MeasuresAllocationAndSubscriptionStability()
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.Name} />", 0);
        _output.WriteLine("Actual generated simple binding:\n" + string.Join("\n", compiled.Source.Split('\n')
            .Where(line => line.Contains(".__AkburaBindExpression<", StringComparison.Ordinal))));
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            object vm = component.ModelForTest;
            var setName = vm.GetType().GetProperty("Name")!.SetMethod!.CreateDelegate<Action<string>>(vm);
            for (var warmup = 0; warmup < 100; warmup++) setName((warmup & 1) == 0 ? "A" : "B");
            const int iterations = 5_000;
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            for (var update = 0; update < iterations; update++) setName((update & 1) == 0 ? "A" : "B");
            var elapsed = Stopwatch.GetElapsedTime(started);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            Assert.Equal(1, (int)((dynamic)vm).Adds);
            Assert.Equal(0, (int)((dynamic)vm).Removes);
            Assert.Equal(iterations + 101, (int)((dynamic)vm).NameReads);
#if DEBUG
            const string configuration = "Debug";
#else
            const string configuration = "Release";
#endif
            _output.WriteLine($"ReleaseDirect generated binding; {iterations} updates after 100 warmups; " +
                $"{elapsed.TotalNanoseconds / iterations:F1} ns/update; {(double)allocated / iterations:F1} bytes/update; " +
                $"one stable INPC observer; includes model notification and Avalonia TextBlock write; {configuration} runtime build; approximate integration measurement, not an isolated benchmark.");
            return true;
        });
    }
}
