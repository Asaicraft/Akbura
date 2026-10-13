using Akbura.ComponentTree;
using Avalonia.Controls;
using Avalonia.Headless;
using System.Collections.Immutable;
using System.Reflection;

namespace Akbura.HotReloadIntegrationTests;

public sealed class StateHotReloadTests
{
    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task StateSurvivesRealHotReload()
    {
        var testName = typeof(StateHotReloadTests).FullName + "." + nameof(StateSurvivesRealHotReload);
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(OriginalSource, OwnerSource);
        using var hotReload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        var processId = Environment.ProcessId;
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(StateHotReloadTests).Assembly);
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(hotReload.ComponentType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(owner.Child);
                var oldStates = GetStates(owner);
                var count = Assert.IsType<State<int>>(Assert.Single(oldStates));
                count.Value = 42;
                Assert.Equal("before:42", text.Text);
                Assert.Equal(1, ReadInitializations(owner));
                Assert.DoesNotContain(hotReload.ComponentType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
                    field => field.Name.StartsWith("__state_", StringComparison.Ordinal));

                hotReload.ApplyUpdate(compilation.Update(UpdatedSource));
                // Prove the IL update and Akbura's application refresh are separate.
                Assert.Equal("before:42", text.Text);
                hotReload.DispatchMetadataUpdateHandlers();

                var newStates = GetStates(owner);
                Assert.Equal(2, newStates.Length);
                Assert.Same(count, newStates[0]);
                Assert.Equal(42, count.Value);
                Assert.True(Assert.IsType<State<bool>>(newStates[1]).Value);
                Assert.Equal("visible", newStates[1].Info!.Name);
                Assert.Equal("after:42:True", Assert.IsType<TextBlock>(owner.Child).Text);
                Assert.Same(text, owner.Child);
                Assert.Same(owner, window.Content);
                Assert.Same(hotReload.ComponentType, owner.GetType());
                Assert.Same(hotReload.Assembly, owner.GetType().Assembly);
                Assert.Equal(processId, Environment.ProcessId);
                Assert.Equal(1, ReadInitializations(owner));
                Assert.Equal(1, hotReload.AppliedUpdates);

                Assert.IsType<State<bool>>(newStates[1]).Value = false;
                Assert.Equal("after:42:False", text.Text);
                count.Value++;
                Assert.Equal("after:43:False", text.Text);
                Assert.True(newStates == GetStates(owner));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static ImmutableArray<State> GetStates(AkburaControl owner) =>
        Assert.IsType<ImmutableArray<State>>(owner.GetType().GetProperty("StatesForTest")!.GetValue(owner));

    private static int ReadInitializations(AkburaControl owner) =>
        Assert.IsType<int>(owner.GetType().GetProperty("Initializations")!.GetValue(owner));

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task TwentyUpdates_PreserveStatesAcrossInitializerEditsAndReordering()
    {
        var testName = typeof(StateHotReloadTests).FullName + "." + nameof(TwentyUpdates_PreserveStatesAcrossInitializerEditsAndReordering);
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(OriginalSource, OwnerSource);
        using var hotReload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(StateHotReloadTests).Assembly);
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(hotReload.ComponentType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(owner.Child);
                var count = Assert.IsType<State<int>>(Assert.Single(GetStates(owner)));
                count.Value = 42;
                State<bool>? visible = null;
                for (var revision = 1; revision <= 20; revision++)
                {
                    var declarations = revision % 2 == 0
                        ? $"state bool visible = true;\nstate int count = {100 + revision};"
                        : $"state int count = {100 + revision};\nstate bool visible = true;";
                    var source = "using Avalonia.Controls;\nnamespace Demo;\n" + declarations + "\n" +
                        $"<TextBlock Text={{\"revision {revision}:\" + count.ToString() + \":\" + visible.ToString()}} />";
                    hotReload.ApplyUpdate(compilation.Update(source));
                    hotReload.DispatchMetadataUpdateHandlers();
                    var states = GetStates(owner);
                    Assert.Equal(2, states.Length);
                    Assert.Same(count, Assert.Single(states, state => state.Info!.Name == "count"));
                    var currentVisible = Assert.IsType<State<bool>>(Assert.Single(states, state => state.Info!.Name == "visible"));
                    if (visible == null)
                    {
                        visible = currentVisible;
                        visible.Value = false;
                    }
                    Assert.Same(visible, currentVisible);
                    Assert.Equal(42, count.Value);
                    Assert.False(currentVisible.Value);
                    Assert.Equal($"revision {revision}:42:False", text.Text);
                    Assert.Same(owner, window.Content);
                    Assert.Same(text, owner.Child);
                    Assert.Same(hotReload.Assembly, owner.GetType().Assembly);
                    Assert.Equal(1, ReadInitializations(owner));
                    Assert.True(states == GetStates(owner));
                }
                Assert.Equal(20, hotReload.AppliedUpdates);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task TypeChangeRenameAndRemoval_ReconcileTheSameLiveComponent()
    {
        var testName = typeof(StateHotReloadTests).FullName + "." + nameof(TypeChangeRenameAndRemoval_ReconcileTheSameLiveComponent);
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(CreateSource("state string value = \"first\";", "value"), OwnerSource);
        using var hotReload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(StateHotReloadTests).Assembly);
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(hotReload.ComponentType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(owner.Child);
                var count = Assert.IsType<State<int>>(GetStates(owner)[0]);
                var originalValue = Assert.IsType<State<string>>(GetStates(owner)[1]);
                count.Value = 42;
                originalValue.Value = "edited";
                Assert.Equal("42:edited", text.Text);

                hotReload.ApplyUpdate(compilation.Update(CreateSource("state int value = 7;", "value")));
                hotReload.DispatchMetadataUpdateHandlers();
                var changedValue = Assert.IsType<State<int>>(GetStates(owner)[1]);
                Assert.Same(count, GetStates(owner)[0]);
                Assert.NotSame(originalValue, changedValue);
                Assert.Equal("42:7", text.Text);
                changedValue.Value = 8;
                Assert.Equal("42:8", text.Text);

                hotReload.ApplyUpdate(compilation.Update(CreateSource("state int renamed = 9;", "renamed")));
                hotReload.DispatchMetadataUpdateHandlers();
                var renamedValue = Assert.IsType<State<int>>(GetStates(owner)[1]);
                Assert.Equal("renamed", renamedValue.Info!.Name);
                Assert.NotSame(changedValue, renamedValue);
                Assert.Equal("42:9", text.Text);

                hotReload.ApplyUpdate(compilation.Update(CreateSource(string.Empty, null)));
                hotReload.DispatchMetadataUpdateHandlers();
                Assert.Same(count, Assert.Single(GetStates(owner)));
                Assert.Equal("42", text.Text);
                Assert.Same(owner, window.Content);
                Assert.Same(text, owner.Child);
                Assert.Same(hotReload.Assembly, owner.GetType().Assembly);
                Assert.Equal(1, ReadInitializations(owner));
                Assert.Equal(3, hotReload.AppliedUpdates);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        static string CreateSource(string declaration, string? value) =>
            "using Avalonia.Controls;\nnamespace Demo;\nstate int count = CreateInitialCount();\n" + declaration +
            "\n<TextBlock Text={count.ToString()" + (value == null ? "" : " + \":\" + " + value + ".ToString()") + "} />";
    }

    private const string OriginalSource =
        """
        using Avalonia.Controls;
        namespace Demo;
        state int count = CreateInitialCount();
        <TextBlock Text={"before:" + count.ToString()} />
        """;

    private const string UpdatedSource =
        """
        using Avalonia.Controls;
        namespace Demo;
        state int count = CreateInitialCount();
        state bool visible = true;
        <TextBlock Text={"after:" + count.ToString() + ":" + visible.ToString()} />
        """;

    private const string OwnerSource =
        """
        using Akbura;
        using Akbura.ComponentTree;
        using Akbura.Engine;
        using System.Collections.Immutable;
        namespace Demo;
        public partial class Counter : AkburaControl
        {
            public Counter() : base(AkburaEngine.Empty) { }
            public int Initializations { get; private set; }
            public ImmutableArray<State> StatesForTest => GetStates();
            private int CreateInitialCount() { Initializations++; return 0; }
        }
        """;
}
