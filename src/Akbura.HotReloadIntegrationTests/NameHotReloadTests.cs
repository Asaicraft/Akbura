using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

namespace Akbura.HotReloadIntegrationTests;

public sealed class NameHotReloadTests
{
    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task NameTransitions_ReplaceOnlyTheChangedNodeAndReleaseOldEventsAndLookups() =>
        RunScenario(nameof(NameTransitions_ReplaceOnlyTheChangedNodeAndReleaseOldEventsAndLookups), false);

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task ReleaseConditional_NameTransitionsKeepTheCompatibleTree() =>
        RunScenario(nameof(ReleaseConditional_NameTransitionsKeepTheCompatibleTree), true);

    private static async Task RunScenario(string method, bool release)
    {
        var testName = typeof(NameHotReloadTests).FullName + "." + method;
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }
        var compilation = new AkburaCompilationSession(TransitionSource("old", 0, release), OwnerSource, debug: !release);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        await HeadlessUnitTestSession.GetOrStartForAssembly(typeof(NameHotReloadTests).Assembly).Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(reload.ComponentType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                var originalSibling = Assert.IsType<TextBlock>(root.Children[1]);
                var previous = Assert.IsAssignableFrom<TextBlock>(root.Children[0]);
                var previousName = "old";
                var scope = Assert.IsAssignableFrom<INameScope>(NameScope.GetNameScope(root));
                var names = new string?[] { "renamed", "renamed", null, "added", "added" };
                for (var index = 0; index < names.Length; index++)
                {
                    var name = names[index];
                    reload.ApplyUpdate(compilation.Update(TransitionSource(name, index + 1, release)));
                    if (release)
                    {
                        owner.InvalidState();
                    }
                    else
                    {
                        reload.DispatchMetadataUpdateHandlers();
                    }
                    var current = Assert.IsAssignableFrom<TextBlock>(root.Children[0]);
                    var currentScope = Assert.IsAssignableFrom<INameScope>(NameScope.GetNameScope(root));
                    Assert.Same(owner, window.Content);
                    Assert.Same(root, owner.Child);
                    Assert.Same(originalSibling, root.Children[1]);
                    Assert.Equal(name, current.Name);
                    Assert.Equal(name == null ? 0 : 1, NameAssignments(current));
                    Assert.Equal("Dashboard " + (index + 1), current.Text);
                    if (name == previousName)
                    {
                        Assert.Same(previous, current);
                        Assert.Same(scope, currentScope);
                    }
                    else
                    {
                        Assert.NotSame(previous, current);
                        Assert.Null(previous.GetVisualParent());
                        Assert.NotSame(scope, currentScope);
                        if (previousName != null)
                        {
                            Assert.Null(currentScope.Find(previousName));
                        }
                        var pings = Pings(owner);
                        previous.GetType().GetMethod("RaisePing")!.Invoke(previous, null);
                        Assert.Equal(pings, Pings(owner));
                    }
                    if (name != null)
                    {
                        Assert.Same(current, currentScope.Find(name));
                        Assert.Same(current, reload.ComponentType.GetProperty(name,
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner));
                    }
                    var beforePing = Pings(owner);
                    current.GetType().GetMethod("RaisePing")!.Invoke(current, null);
                    Assert.Equal(beforePing + 1, Pings(owner));
                    previous = current;
                    previousName = name;
                    scope = currentScope;
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task ConditionalNames_KeepTheActiveLookupAndCleanRetiredBranches()
    {
        var testName = typeof(NameHotReloadTests).FullName + "." + nameof(ConditionalNames_KeepTheActiveLookupAndCleanRetiredBranches);
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }
        var compilation = new AkburaCompilationSession(ConditionalSource(0), OwnerSource);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        await HeadlessUnitTestSession.GetOrStartForAssembly(typeof(NameHotReloadTests).Assembly).Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(reload.ComponentType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                var stable = root.Children[0];
                var branch = Assert.IsType<StackPanel>(root.Children[1]);
                var title = Assert.IsAssignableFrom<TextBlock>(branch.Children[0]);
                var target = Assert.IsType<TextBlock>(branch.Children[1]);
                var scope = Assert.IsAssignableFrom<INameScope>(NameScope.GetNameScope(branch));
                Assert.Same(title, scope.Find("dashboardTitle"));
                reload.ApplyUpdate(compilation.Update(ConditionalSource(1)));
                reload.DispatchMetadataUpdateHandlers();
                Assert.Same(root, owner.Child);
                Assert.Same(stable, root.Children[0]);
                Assert.Same(branch, root.Children[1]);
                Assert.Same(title, branch.Children[0]);
                Assert.Same(scope, NameScope.GetNameScope(branch));
                Assert.Equal("Dashboard 1", title.Text);
                Assert.Equal(title.Text, target.Text);
                Assert.Equal(1, NameAssignments(title));
                reload.ComponentType.GetProperty("Enabled")!.SetValue(owner, false);
                owner.InvalidState();
                Assert.Null(branch.GetVisualParent());
                var alternative = Assert.IsAssignableFrom<TextBlock>(root.Children[1]);
                var alternativeScope = Assert.IsAssignableFrom<INameScope>(NameScope.GetNameScope(alternative));
                Assert.Same(alternative, alternativeScope.Find("alternative"));
                Assert.Null(alternativeScope.Find("dashboardTitle"));
                reload.ComponentType.GetProperty("Enabled")!.SetValue(owner, true);
                owner.InvalidState();
                var returnedBranch = Assert.IsType<StackPanel>(root.Children[1]);
                Assert.NotSame(branch, returnedBranch);
                var returnedTitle = Assert.IsAssignableFrom<TextBlock>(returnedBranch.Children[0]);
                Assert.Equal(1, NameAssignments(returnedTitle));
                Assert.Same(returnedTitle, NameScope.GetNameScope(returnedBranch)!.Find("dashboardTitle"));
                Assert.Null(NameScope.GetNameScope(returnedBranch)!.Find("alternative"));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task TwentyPropertyUpdates_KeepStyledNamedControlsWithoutCallingTheNameSetter()
    {
        var testName = typeof(NameHotReloadTests).FullName + "." + nameof(TwentyPropertyUpdates_KeepStyledNamedControlsWithoutCallingTheNameSetter);
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(Source(0), OwnerSource);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        await HeadlessUnitTestSession.GetOrStartForAssembly(typeof(NameHotReloadTests).Assembly).Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(reload.ComponentType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                var title = Assert.IsAssignableFrom<TextBlock>(root.Children[0]);
                var sibling = Assert.IsType<TextBlock>(root.Children[1]);
                var binding = Assert.IsType<TextBlock>(root.Children[2]);
                var scope = Assert.IsAssignableFrom<INameScope>(NameScope.GetNameScope(root));
                var state = Assert.IsType<Akbura.ComponentTree.State<int>>(reload.ComponentType.GetProperty("StateForTest")!.GetValue(owner));
                state.Value = 42;
                Assert.Same(title, scope.Find("dashboardTitle"));
                Assert.Same(sibling, scope.Find("sibling"));
                Assert.Equal(title.Text, binding.Text);
                Assert.Equal("dashboardTitle", title.Name);
                Assert.Equal(1, NameAssignments(title));
                title.ApplyStyling();
                Assert.Throws<InvalidOperationException>(() => title.Name = title.Name);
                for (var revision = 1; revision <= 20; revision++)
                {
                    reload.ApplyUpdate(compilation.Update(Source(revision)));
                    reload.DispatchMetadataUpdateHandlers();
                    owner.InvalidState();
                    Assert.Same(owner, window.Content);
                    Assert.Same(root, owner.Child);
                    Assert.Same(title, root.Children[0]);
                    Assert.Same(sibling, root.Children[1]);
                    Assert.Same(binding, root.Children[2]);
                    Assert.Same(scope, NameScope.GetNameScope(root));
                    Assert.Same(title, scope.Find("dashboardTitle"));
                    Assert.Same(sibling, scope.Find("sibling"));
                    Assert.Same(root, title.GetVisualParent());
                    Assert.Equal(Caption(revision), title.Text);
                    Assert.Same(state, reload.ComponentType.GetProperty("StateForTest")!.GetValue(owner));
                    Assert.Equal(42, state.Value);
                    Assert.Equal("dashboardTitle", title.Name);
                    Assert.Equal(1, NameAssignments(title));
                    Assert.Equal(title.Text, binding.Text);
                    title.Text = "reactive " + revision;
                    Assert.Equal(title.Text, binding.Text);
                }
                Assert.Equal(20, reload.AppliedUpdates);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task TemplateInstances_KeepIndependentNamesAndLiveBindingsAcrossPropertyEdits()
    {
        var testName = typeof(NameHotReloadTests).FullName + "." + nameof(TemplateInstances_KeepIndependentNamesAndLiveBindingsAcrossPropertyEdits);
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }
        var compilation = new AkburaCompilationSession(TemplateSource(0), OwnerSource);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        await HeadlessUnitTestSession.GetOrStartForAssembly(typeof(NameHotReloadTests).Assembly).Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(reload.ComponentType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                var hosts = root.Children.Cast<Avalonia.Controls.Presenters.ContentPresenter>().ToArray();
                var roots = hosts.Select(host => Assert.IsType<StackPanel>(host.Child)).ToArray();
                var titles = roots.Select(local => Assert.IsAssignableFrom<TextBlock>(local.Children[0])).ToArray();
                var scopes = roots.Select(local => Assert.IsAssignableFrom<INameScope>(NameScope.GetNameScope(local))).ToArray();
                Assert.NotSame(titles[0], titles[1]);
                Assert.NotSame(scopes[0], scopes[1]);
                reload.ApplyUpdate(compilation.Update(TemplateSource(1)));
                reload.DispatchMetadataUpdateHandlers();
                for (var index = 0; index < hosts.Length; index++)
                {
                    Assert.Same(roots[index], hosts[index].Child);
                    Assert.Same(titles[index], roots[index].Children[0]);
                    Assert.Same(scopes[index], NameScope.GetNameScope(roots[index]));
                    Assert.Same(titles[index], scopes[index].Find("localTitle"));
                    Assert.Equal("Dashboard 1", titles[index].Text);
                    Assert.Equal(1, NameAssignments(titles[index]));
                    Assert.Equal(titles[index].Text, Assert.IsType<TextBlock>(roots[index].Children[1]).Text);
                }
                titles[0].Text = "first only";
                Assert.Equal("first only", Assert.IsType<TextBlock>(roots[0].Children[1]).Text);
                Assert.Equal("Dashboard 1", Assert.IsType<TextBlock>(roots[1].Children[1]).Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task NamedForeachItems_PropertyEditsKeepEachStyledOccurrence()
    {
        var testName = typeof(NameHotReloadTests).FullName + "." + nameof(NamedForeachItems_PropertyEditsKeepEachStyledOccurrence);
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }
        var compilation = new AkburaCompilationSession(ForeachSource(0), OwnerSource);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        await HeadlessUnitTestSession.GetOrStartForAssembly(typeof(NameHotReloadTests).Assembly).Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(reload.ComponentType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                var titles = root.Children.Cast<TextBlock>().ToArray();
                Assert.Equal(2, titles.Length);
                reload.ApplyUpdate(compilation.Update(ForeachSource(1)));
                reload.DispatchMetadataUpdateHandlers();
                Assert.Same(root, owner.Child);
                for (var index = 0; index < titles.Length; index++)
                {
                    Assert.Same(titles[index], root.Children[index]);
                    Assert.Equal("row", titles[index].Name);
                    Assert.Equal(1, NameAssignments(titles[index]));
                    Assert.Equal("Dashboard " + (index + 1) + " revision 1", titles[index].Text);
                    Assert.Same(titles[index], NameScope.GetNameScope(titles[index])!.Find("row"));
                }
                Assert.NotSame(NameScope.GetNameScope(titles[0]), NameScope.GetNameScope(titles[1]));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static int NameAssignments(TextBlock element) =>
        Assert.IsType<int>(element.GetType().GetProperty("NameAssignments")!.GetValue(element));

    private static int Pings(AkburaControl owner) =>
        Assert.IsType<int>(owner.GetType().GetProperty("Pings")!.GetValue(owner));

    private static string TransitionSource(string? name, int revision, bool conditional) =>
        "using Avalonia.Controls; using Demo; namespace Demo; <StackPanel><TrackedTextBlock " +
        (name == null ? "" : "x.Name=\"" + name + "\" ") + "Text=\"Dashboard " + revision + "\" Ping={OnPing} />" +
        "<TextBlock x.Name=\"sibling\" Text=\"untouched\" />" +
        (conditional ? "$if (Enabled) { <Border /> }" : "") + "</StackPanel>";

    private static string ConditionalSource(int revision) =>
        "using Avalonia.Controls; using Demo; namespace Demo; <StackPanel><TextBlock x.Name=\"stable\" />" +
        "$if (Enabled) { <StackPanel><TrackedTextBlock x.Name=\"dashboardTitle\" Text=\"Dashboard " + revision + "\" />" +
        "<TextBlock Text=${ReflectionBinding Path=Text, ElementName={ActiveName}} /></StackPanel> }" +
        "$else { <TrackedTextBlock x.Name=\"alternative\" Text=\"alternative\" /> }</StackPanel>";

    private static string TemplateSource(int revision)
    {
        var template = "<ContentPresenter Content={Item}><ContentPresenter.ContentTemplate x.DataType=\"object\" x.ItemName=\"item\">" +
            "<StackPanel><TrackedTextBlock x.Name=\"localTitle\" Text=\"Dashboard " + revision + "\" />" +
            "<TextBlock Text=${ReflectionBinding Path=Text, ElementName={LocalName}} />" +
            "$if (Enabled) { <Border /> }</StackPanel></ContentPresenter.ContentTemplate></ContentPresenter>";
        return "using Avalonia.Controls; using Avalonia.Controls.Presenters; using Demo; namespace Demo; <StackPanel>" +
            template + template + "</StackPanel>";
    }

    private static string ForeachSource(int revision) =>
        "using Avalonia.Controls; using Demo; namespace Demo; <StackPanel>$foreach (var item in Items) { " +
        "<TrackedTextBlock x.Name=\"row\" Text={\"Dashboard \" + item.ToString() + \" revision " + revision + "\"} /> }</StackPanel>";

    private static string Source(int revision) =>
        "using Avalonia.Controls; using Demo; namespace Demo; state int retained = 7; <StackPanel>" +
        "<TrackedTextBlock x.Name=\"dashboardTitle\" Text=\"" + Caption(revision) + "\" Width=\"" + (100 + revision) + "\" />" +
        "<TextBlock x.Name=\"sibling\" Text=\"untouched\" />" +
        "<TextBlock Text=${ReflectionBinding Path=Text, ElementName={ActiveName}} /></StackPanel>";

    private static string Caption(int revision) =>
        revision == 0 ? "Dashboard" : revision == 1 ? "Dashboard Overview" : "Dashboard " + revision;

    private const string OwnerSource =
        """
        using Akbura;
        using Akbura.Engine;
        using Avalonia.Controls;
        namespace Demo;
        public partial class Counter : AkburaControl
        {
            public Counter() : base(AkburaEngine.Empty) { }
            public string ActiveName => "dashboardTitle";
            public string LocalName => "localTitle";
            public object Item { get; } = new();
            public int[] Items { get; } = [1, 2];
            public bool Enabled { get; set; } = true;
            public int Pings { get; private set; }
            public object StateForTest => GetStates()[0];
            public void OnPing() { Pings++; }
        }
        public class TrackedTextBlock : TextBlock
        {
            public event System.Action? Ping;
            public void RaisePing() { Ping?.Invoke(); }
            public int NameAssignments { get; private set; }
            public new string? Name
            {
                get => base.Name;
                set
                {
                    NameAssignments++;
                    base.Name = value;
                }
            }
        }
        """;
}
