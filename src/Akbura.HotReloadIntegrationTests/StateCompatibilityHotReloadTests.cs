using Akbura.ComponentTree;
using Avalonia.Controls;
using Avalonia.Headless;
using System.Collections;
using System.Collections.Immutable;
using System.Reflection;

namespace Akbura.HotReloadIntegrationTests;

public sealed class StateCompatibilityHotReloadTests
{
    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task HookTypeChangeRenameAndReorder_ReleaseOldAliasesAndKeepOrdinaryValue()
    {
        var testName = TestName(nameof(HookTypeChangeRenameAndReorder_ReleaseOldAliasesAndKeepOrdinaryValue));
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(Source(
            "state int observed = useObservable(Source, -1);\nstate bool unrelated = true;",
            "observed.ToString()"), OwnerSource);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        await Dispatch(() =>
        {
            var owner = CreateOwner(reload);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(owner.Child);
                var count = Assert.IsType<State<int>>(States(owner)[0]);
                var originalHook = Assert.IsType<State<int>>(States(owner)[1]);
                var unrelated = Assert.IsType<State<bool>>(States(owner)[2]);
                count.Value = 42;

                reload.ApplyUpdate(compilation.Update(Source(
                    "state bool unrelated = false;\nstate string observed = useSelect(count, value => value.ToString());", "observed")));
                reload.DispatchMetadataUpdateHandlers();
                var changedHook = Assert.IsType<State<string>>(States(owner)[2]);
                Assert.Same(unrelated, States(owner)[1]);
                Assert.True(unrelated.Value);
                Assert.Equal("42", changedHook.Value);
                Assert.False(originalHook.IsAttached);
                Assert.Equal(0, Read<int>(owner, "Subscribers"));
                Assert.Same(count, States(owner)[0]);
                var renders = Read<int>(owner, "RenderCalls");
                originalHook.Value = 123;
                Invoke(owner, "PublishStale", -100);
                Assert.Equal(renders, Read<int>(owner, "RenderCalls"));

                reload.ApplyUpdate(compilation.Update(Source(
                    "state string label = useSelect(count, value => \"renamed:\" + value.ToString());\n" +
                    "state bool unrelated = false;", "label")));
                reload.DispatchMetadataUpdateHandlers();
                var states = States(owner);
                var renamedHook = Assert.IsType<State<string>>(states[1]);
                Assert.Same(count, states[0]);
                Assert.Same(unrelated, states[2]);
                Assert.NotSame(changedHook, renamedHook);
                Assert.False(changedHook.IsAttached);
                Assert.Equal("renamed:42", text.Text);
                AssertStoreCounts(owner, ordinary: 2, hooks: 1);
                renders = Read<int>(owner, "RenderCalls");
                changedHook.Value = "stale";
                Assert.Equal(renders, Read<int>(owner, "RenderCalls"));
                count.Value = 43;
                Assert.Equal("renamed:43", text.Text);
                Assert.True(states == States(owner));
                Assert.Same(owner, window.Content);
                Assert.Same(text, owner.Child);
                Assert.Equal(2, reload.AppliedUpdates);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task TwentyObservableHookReloads_KeepOneSubscriptionAndRejectStaleCallbacks()
    {
        var testName = TestName(nameof(TwentyObservableHookReloads_KeepOneSubscriptionAndRejectStaleCallbacks));
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(ObservableSource(0), OwnerSource);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        await Dispatch(() =>
        {
            var owner = CreateOwner(reload);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(owner.Child);
                var count = Assert.IsType<State<int>>(States(owner)[0]);
                count.Value = 42;
                Assert.Equal(1, Read<int>(owner, "Subscribers"));
                var previousHook = States(owner)[1];

                for (var revision = 1; revision <= 20; revision++)
                {
                    reload.ApplyUpdate(compilation.Update(ObservableSource(revision)));
                    reload.DispatchMetadataUpdateHandlers();
                    var states = States(owner);
                    var currentHook = Assert.IsType<State<int>>(states[revision % 2 == 0 ? 0 : 1]);
                    Assert.Same(count, states[revision % 2 == 0 ? 1 : 0]);
                    Assert.NotSame(previousHook, currentHook);
                    Assert.False(previousHook.IsAttached);
                    Assert.True(currentHook.IsAttached);
                    Assert.Equal(1, Read<int>(owner, "Subscribers"));
                    Assert.Equal(revision + 1, Read<int>(owner, "Subscriptions"));
                    Assert.Equal(revision, Read<int>(owner, "Disposals"));
                    AssertStoreCounts(owner, ordinary: 1, hooks: 1);

                    Invoke(owner, "Publish", revision);
                    Assert.Equal($"{revision}:42:{revision}", text.Text);
                    Invoke(owner, "PublishStale", -100);
                    Assert.Equal($"{revision}:42:{revision}", text.Text);
                    Assert.Equal(revision, currentHook.Value);
                    owner.InvalidState();
                    Assert.True(states == States(owner));
                    Assert.Equal(1, Read<int>(owner, "Subscribers"));
                    Assert.Same(owner, window.Content);
                    Assert.Same(text, owner.Child);
                    previousHook = currentHook;
                }

                reload.ApplyUpdate(compilation.Update(Source("", "count.ToString()")));
                reload.DispatchMetadataUpdateHandlers();
                Assert.Same(count, Assert.Single(States(owner)));
                Assert.Equal(0, Read<int>(owner, "Subscribers"));
                Assert.Equal(21, Read<int>(owner, "Disposals"));
                AssertStoreCounts(owner, ordinary: 1, hooks: 0);
                Invoke(owner, "Publish", -200);
                Invoke(owner, "PublishStale", -300);
                Assert.Equal("42", text.Text);

                reload.ApplyUpdate(compilation.Update(ObservableSource(21)));
                reload.DispatchMetadataUpdateHandlers();
                Assert.Equal(1, Read<int>(owner, "Subscribers"));
                Invoke(owner, "Publish", 21);
                Assert.Equal("21:42:21", text.Text);
                var resumedHook = States(owner)[1];
                window.Content = null;
                Assert.Equal(0, Read<int>(owner, "Subscribers"));
                window.Content = owner;
                Assert.Equal(1, Read<int>(owner, "Subscribers"));
                Assert.Same(resumedHook, States(owner)[1]);
                Assert.Equal(Read<int>(owner, "Subscriptions") - 1, Read<int>(owner, "Disposals"));
                Assert.Equal(22, reload.AppliedUpdates);
            }
            finally
            {
                window.Close();
            }
            Assert.Equal(0, Read<int>(owner, "Subscribers"));
            Assert.Equal(Read<int>(owner, "Subscriptions"), Read<int>(owner, "Disposals"));
        });
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task FailedHookPreparation_DiscardsPartialAliasesAndRetriesTheSameDelta()
    {
        var testName = TestName(nameof(FailedHookPreparation_DiscardsPartialAliasesAndRetriesTheSameDelta));
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(ObservableSource(0), OwnerSource);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        await Dispatch(() =>
        {
            var owner = CreateOwner(reload);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(owner.Child);
                var count = Assert.IsType<State<int>>(States(owner)[0]);
                count.Value = 42;
                var oldHook = States(owner)[1];
                Set(owner, "ThrowDuringPreparation", true);
                reload.ApplyUpdate(compilation.Update(Source(
                    "state int observed = useObservable(Source, -1);\n" +
                    "state int second = useObservable(GetSourceForTest(), -2);",
                    "count.ToString() + \":\" + observed.ToString() + \":\" + second.ToString()")));

                AssertReloadFailure(reload, "prepare failed");
                Assert.Equal(0, Read<int>(owner, "Subscribers"));
                Assert.False(oldHook.IsAttached);
                Assert.Equal(42, count.Value);
                AssertStoreCounts(owner, ordinary: 1, hooks: 0);
                Set(owner, "ThrowDuringPreparation", false);
                owner.InvalidState();

                var states = States(owner);
                Assert.Equal(3, states.Length);
                Assert.Same(count, states[0]);
                Assert.Equal(2, Read<int>(owner, "Subscribers"));
                AssertStoreCounts(owner, ordinary: 1, hooks: 2);
                Invoke(owner, "Publish", 7);
                Assert.Equal("42:7:7", text.Text);
                Invoke(owner, "PublishStale", -100);
                Assert.Equal("42:7:7", text.Text);
                owner.InvalidState();
                Assert.True(states == States(owner));
                Assert.Equal(2, Read<int>(owner, "Subscribers"));
                Assert.Same(owner, window.Content);
                Assert.Same(text, owner.Child);
                Assert.Equal(1, reload.AppliedUpdates);
            }
            finally
            {
                window.Close();
            }
            Assert.Equal(0, Read<int>(owner, "Subscribers"));
            Assert.Equal(Read<int>(owner, "Subscriptions"), Read<int>(owner, "Disposals"));
        });
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task NewDeltaAfterFailedReload_PrunesProvisionalOrdinaryBindingResources()
    {
        var testName = TestName(nameof(NewDeltaAfterFailedReload_PrunesProvisionalOrdinaryBindingResources));
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(Source("", "count.ToString()"), OwnerSource);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        await Dispatch(() =>
        {
            var owner = CreateOwner(reload);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(owner.Child);
                var count = Assert.IsType<State<int>>(Assert.Single(States(owner)));
                count.Value = 42;
                Set(owner, "ThrowDuringRender", true);
                reload.ApplyUpdate(compilation.Update(Source(
                    "state int streamed = out Source;\nstate int observed = useObservable(Source, -1);",
                    "count.ToString() + \":\" + streamed.ToString() + \":\" + observed.ToString()")));
                AssertReloadFailure(reload, "render failed");
                var provisional = Read<ImmutableArray<State>>(owner, "LastPreparedStates");
                var abandoned = Assert.IsType<State<int>>(provisional[1]);
                Assert.Equal(1, Read<int>(owner, "Subscribers"));
                AssertStoreCounts(owner, ordinary: 2, hooks: 0);
                Assert.Equal(42, count.Value);

                Set(owner, "ThrowDuringRender", false);
                reload.ApplyUpdate(compilation.Update(Source("", "\"recovered:\" + count.ToString()")));
                reload.DispatchMetadataUpdateHandlers();
                Assert.Same(count, Assert.Single(States(owner)));
                Assert.Equal(0, Read<int>(owner, "Subscribers"));
                AssertStoreCounts(owner, ordinary: 1, hooks: 0);
                Assert.Equal("recovered:42", text.Text);
                var abandonedValue = abandoned.Value;
                Invoke(owner, "Publish", -100);
                Invoke(owner, "PublishStale", -200);
                Assert.Equal(abandonedValue, abandoned.Value);
                var rendersAfterRemoval = Read<int>(owner, "RenderCalls");
                abandoned.Value = 123;
                Assert.Equal(rendersAfterRemoval, Read<int>(owner, "RenderCalls"));
                Assert.False(abandoned.IsAttached);

                reload.ApplyUpdate(compilation.Update(Source("state int streamed = out Source;",
                    "count.ToString() + \":\" + streamed.ToString()")));
                reload.DispatchMetadataUpdateHandlers();
                Assert.NotSame(abandoned, States(owner)[1]);
                Assert.Equal(1, Read<int>(owner, "Subscribers"));
                Invoke(owner, "Publish", 8);
                Assert.Equal("42:8", text.Text);
                AssertStoreCounts(owner, ordinary: 2, hooks: 0);
                Assert.Same(owner, window.Content);
                Assert.Same(text, owner.Child);
                Assert.Equal(3, reload.AppliedUpdates);
            }
            finally
            {
                window.Close();
            }
            Assert.Equal(0, Read<int>(owner, "Subscribers"));
            Assert.Equal(Read<int>(owner, "Subscriptions"), Read<int>(owner, "Disposals"));
        });
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task DetachedOwner_CatchesUpWithLatestStateShapeAfterMultipleDeltas()
    {
        var testName = TestName(nameof(DetachedOwner_CatchesUpWithLatestStateShapeAfterMultipleDeltas));
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(ObservableSource(0), OwnerSource);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        await Dispatch(() =>
        {
            var owner = CreateOwner(reload);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(owner.Child);
                var count = Assert.IsType<State<int>>(States(owner)[0]);
                count.Value = 42;
                var oldHook = States(owner)[1];
                window.Content = null;
                Assert.Equal(0, Read<int>(owner, "Subscribers"));

                reload.ApplyUpdate(compilation.Update(Source("state bool temporary = true;", "temporary.ToString()")));
                reload.DispatchMetadataUpdateHandlers();
                reload.ApplyUpdate(compilation.Update(Source("state string label = \"latest\";", "label")));
                reload.DispatchMetadataUpdateHandlers();
                reload.ApplyUpdate(compilation.Update(Source(
                    "state string label = \"latest\";\nstate int observed = useObservable(Source, -1);\n" +
                    "state int projected = useSelect(count, value => value * 5);",
                    "label + \":\" + count.ToString() + \":\" + projected.ToString() + \":\" + observed.ToString()")));
                reload.DispatchMetadataUpdateHandlers();
                Assert.Equal(0, Read<int>(owner, "Subscribers"));
                Assert.Equal(1, Read<int>(owner, "Subscriptions"));

                window.Content = owner;
                var states = States(owner);
                Assert.Equal(4, states.Length);
                Assert.Same(count, states[0]);
                Assert.Equal("latest", Assert.IsType<State<string>>(states[1]).Value);
                Assert.Equal(210, Assert.IsType<State<int>>(states[3]).Value);
                Assert.NotSame(oldHook, states[2]);
                Assert.False(oldHook.IsAttached);
                Assert.Equal(1, Read<int>(owner, "Subscribers"));
                AssertStoreCounts(owner, ordinary: 2, hooks: 2);
                Invoke(owner, "Publish", 9);
                Assert.Equal("latest:42:210:9", text.Text);
                count.Value = 43;
                Assert.Equal("latest:43:215:9", text.Text);
                Assert.True(states == States(owner));
                Assert.Same(text, owner.Child);
                Assert.Same(owner, window.Content);
                Assert.Equal(3, reload.AppliedUpdates);
            }
            finally
            {
                window.Close();
            }
            Assert.Equal(0, Read<int>(owner, "Subscribers"));
            Assert.Equal(Read<int>(owner, "Subscriptions"), Read<int>(owner, "Disposals"));
        });
    }

    private static string TestName(string method) => typeof(StateCompatibilityHotReloadTests).FullName + "." + method;

    private static Task Dispatch(Action action) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(StateCompatibilityHotReloadTests).Assembly)
            .Dispatch(action, CancellationToken.None);

    private static AkburaControl CreateOwner(ClrHotReloadSession reload) =>
        Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(reload.ComponentType));

    private static ImmutableArray<State> States(AkburaControl owner) => Read<ImmutableArray<State>>(owner, "StatesForTest");

    private static T Read<T>(AkburaControl owner, string property) =>
        Assert.IsType<T>(owner.GetType().GetProperty(property)!.GetValue(owner));

    private static void Set(AkburaControl owner, string property, bool value) =>
        owner.GetType().GetProperty(property)!.SetValue(owner, value);

    private static void Invoke(AkburaControl owner, string method, int value) =>
        owner.GetType().GetMethod(method)!.Invoke(owner, [value]);

    private static void AssertReloadFailure(ClrHotReloadSession reload, string message)
    {
        var exception = Assert.Throws<TargetInvocationException>(reload.DispatchMetadataUpdateHandlers);
        Assert.Equal(message, Assert.IsType<InvalidOperationException>(exception.InnerException).Message);
    }

    private static void AssertStoreCounts(AkburaControl owner, int ordinary, int hooks)
    {
        var store = Read<StateCollection>(owner, "StateStoreForTest");
        Assert.Equal(ordinary, Assert.IsAssignableFrom<IDictionary>(typeof(StateCollection)
            .GetField("_states", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)).Count);
        Assert.Equal(hooks, Assert.IsAssignableFrom<IDictionary>(typeof(StateCollection)
            .GetField("_hookStates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)).Count);
        Assert.Null(typeof(StateCollection).GetField("_stagedHookStates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(store));
    }

    private static string ObservableSource(int revision)
    {
        var ordinary = "state int count = " + revision + ";";
        var hook = "state int observed = useObservable(Source, -1);";
        return Header + (revision > 0 && revision % 2 == 0 ? hook + "\n" + ordinary : ordinary + "\n" + hook) +
            "\n<TextBlock Text={ReadText(\"" + revision + ":\" + count.ToString() + \":\" + observed.ToString())} />";
    }

    private static string Source(string declarations, string expression) =>
        Header + "state int count = 0;\n" + declarations + "\n<TextBlock Text={ReadText(" + expression + ")} />";

    private const string Header = "using Avalonia.Controls;\nusing Akbura.Hooks;\nnamespace Demo;\n";

    private const string OwnerSource =
        """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using Akbura;
        using Akbura.ComponentTree;
        using Akbura.Engine;
        namespace Demo;
        public partial class Counter : AkburaControl
        {
            public Counter() : base(AkburaEngine.Empty) { }
            public CountingSource Source { get; } = new();
            public int Subscribers => Source.Subscribers;
            public int Subscriptions => Source.Subscriptions;
            public int Disposals => Source.Disposals;
            public bool ThrowDuringRender { get; set; }
            public bool ThrowDuringPreparation { get; set; }
            public int RenderCalls { get; private set; }
            public ImmutableArray<State> StatesForTest => GetStates();
            public ImmutableArray<State> LastPreparedStates { get; private set; }
            public StateCollection StateStoreForTest => RuntimeStates;
            public void Publish(int value) => Source.Publish(value);
            public void PublishStale(int value) => Source.PublishStale(value);
            private CountingSource GetSourceForTest()
            {
                if (ThrowDuringPreparation)
                {
                    throw new InvalidOperationException("prepare failed");
                }
                return Source;
            }
            private string ReadText(string text)
            {
                RenderCalls++;
                LastPreparedStates = GetStates();
                if (ThrowDuringRender)
                {
                    throw new InvalidOperationException("render failed");
                }
                return text;
            }
        }
        public sealed class CountingSource : IObservable<int>
        {
            private readonly List<IObserver<int>> _active = new();
            private readonly List<IObserver<int>> _disposed = new();
            public int Subscribers => _active.Count;
            public int Subscriptions { get; private set; }
            public int Disposals { get; private set; }
            public IDisposable Subscribe(IObserver<int> observer)
            {
                _active.Add(observer);
                Subscriptions++;
                return new Subscription(this, observer);
            }
            public void Publish(int value)
            {
                foreach (var observer in _active.ToArray())
                {
                    observer.OnNext(value);
                }
            }
            public void PublishStale(int value)
            {
                foreach (var observer in _disposed.ToArray())
                {
                    observer.OnNext(value);
                }
            }
            private sealed class Subscription(CountingSource source, IObserver<int> observer) : IDisposable
            {
                public void Dispose()
                {
                    if (source._active.Remove(observer))
                    {
                        source._disposed.Add(observer);
                        source.Disposals++;
                    }
                }
            }
        }
        """;
}
