using Akbura.ComponentTree;
using Avalonia.Controls;
using Avalonia.Headless;
using System.Collections.Immutable;
using System.Reflection;

namespace Akbura.HotReloadIntegrationTests;

public sealed class HookHotReloadTests
{
    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task HookAdditionChangesAndRemoval_PreserveTheOwnerAndOrdinaryState()
    {
        var testName = typeof(HookHotReloadTests).FullName + "." + nameof(HookAdditionChangesAndRemoval_PreserveTheOwnerAndOrdinaryState);
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(CreateSource(2), OwnerSource);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HookHotReloadTests).Assembly);
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(reload.ComponentType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(owner.Child);
                var count = Assert.IsType<State<int>>(GetStates(owner)[0]);
                count.Value = 42;
                Assert.Equal("84", text.Text);
                var originalHook = GetStates(owner)[1];
                AssertNoStateFields(reload.ComponentType);

                reload.ApplyUpdate(compilation.Update(CreateSource(2, includeSecond: true)));
                reload.DispatchMetadataUpdateHandlers();
                var added = GetStates(owner);
                Assert.Equal(3, added.Length);
                Assert.Same(count, added[0]);
                Assert.NotSame(originalHook, added[1]);
                Assert.False(originalHook.IsAttached);
                Assert.True(added[1].IsAttached);
                Assert.True(added[2].IsAttached);
                Assert.Equal("84:85", text.Text);
                owner.InvalidState();
                Assert.True(added == GetStates(owner));

                reload.ApplyUpdate(compilation.Update(CreateSource(3, includeSecond: true)));
                reload.DispatchMetadataUpdateHandlers();
                var changed = GetStates(owner);
                Assert.Same(count, changed[0]);
                Assert.NotSame(added[1], changed[1]);
                Assert.NotSame(added[2], changed[2]);
                Assert.False(added[1].IsAttached);
                Assert.False(added[2].IsAttached);
                Assert.Equal("126:127", text.Text);

                reload.ApplyUpdate(compilation.Update(CreateSource(3)));
                reload.DispatchMetadataUpdateHandlers();
                var removed = GetStates(owner);
                Assert.Equal(2, removed.Length);
                Assert.Same(count, removed[0]);
                Assert.False(changed[1].IsAttached);
                Assert.False(changed[2].IsAttached);
                Assert.Equal("126", text.Text);
                count.Value = 43;
                Assert.Equal("129", text.Text);
                Assert.True(removed == GetStates(owner));

                reload.ApplyUpdate(compilation.Update(CreateSource(null)));
                reload.DispatchMetadataUpdateHandlers();
                Assert.Same(count, Assert.Single(GetStates(owner)));
                Assert.False(removed[1].IsAttached);
                Assert.Equal("43", text.Text);
                Assert.Same(owner, window.Content);
                Assert.Same(text, owner.Child);
                Assert.Same(reload.Assembly, owner.GetType().Assembly);
                AssertNoStateFields(reload.ComponentType);
                Assert.Equal(4, reload.AppliedUpdates);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task FailedFrames_RestoreSnapshotsAndRetryRealHotReloadWithFreshHookAliases()
    {
        var testName = typeof(HookHotReloadTests).FullName + "." + nameof(FailedFrames_RestoreSnapshotsAndRetryRealHotReloadWithFreshHookAliases);
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(CreateSource(2), OwnerSource);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HookHotReloadTests).Assembly);
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(reload.ComponentType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(owner.Child);
                var committed = GetStates(owner);
                var count = Assert.IsType<State<int>>(committed[0]);
                SetThrow(owner, true);
                Assert.Equal("render failed", Assert.Throws<InvalidOperationException>(() => count.Value = 42).Message);
                Assert.True(committed == GetStates(owner));
                Assert.True(committed[1].IsAttached);
                Assert.Equal("0", text.Text);
                SetThrow(owner, false);
                owner.InvalidState();
                Assert.True(committed == GetStates(owner));
                Assert.Equal("84", text.Text);

                reload.ApplyUpdate(compilation.Update(CreateSource(3, includeSecond: true)));
                SetThrow(owner, true);
                var failure = Assert.Throws<TargetInvocationException>(reload.DispatchMetadataUpdateHandlers);
                Assert.Equal("render failed", Assert.IsType<InvalidOperationException>(failure.InnerException).Message);
                var provisional = Assert.IsType<ImmutableArray<State>>(owner.GetType()
                    .GetProperty("LastPreparedStates")!.GetValue(owner));
                Assert.Equal(3, provisional.Length);
                Assert.Same(count, provisional[0]);
                Assert.False(provisional[1].IsAttached);
                Assert.False(provisional[2].IsAttached);
                Assert.False(committed[1].IsAttached);
                Assert.Equal(42, count.Value);

                SetThrow(owner, false);
                owner.InvalidState();
                var retried = GetStates(owner);
                Assert.Equal(3, retried.Length);
                Assert.Same(count, retried[0]);
                Assert.NotSame(provisional[1], retried[1]);
                Assert.NotSame(provisional[2], retried[2]);
                Assert.True(retried[1].IsAttached);
                Assert.True(retried[2].IsAttached);
                Assert.Equal("126:127", text.Text);
                Assert.Same(owner, window.Content);
                Assert.Same(text, owner.Child);
                Assert.True(retried == GetStates(owner));
                AssertNoStateFields(reload.ComponentType);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static ImmutableArray<State> GetStates(AkburaControl owner) =>
        Assert.IsType<ImmutableArray<State>>(owner.GetType().GetProperty("StatesForTest")!.GetValue(owner));

    private static void SetThrow(AkburaControl owner, bool value) =>
        owner.GetType().GetProperty("ThrowFromRender")!.SetValue(owner, value);

    private static void AssertNoStateFields(Type type) => Assert.DoesNotContain(
        type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
        field => field.Name is "__states" or "__previousHookStates" ||
            field.Name.StartsWith("__state_", StringComparison.Ordinal) ||
            field.Name.StartsWith("__previousHookState_", StringComparison.Ordinal));

    private static string CreateSource(int? multiplier, bool includeSecond = false)
    {
        var declarations = "using Avalonia.Controls;\nusing Akbura.Hooks;\nnamespace Demo;\nstate int count = 0;\n";
        var expression = "count.ToString()";
        if (multiplier is { } factor)
        {
            declarations += $"state int first = useSelect(count, value => value * {factor});\n";
            expression = "first.ToString()";
            if (includeSecond)
            {
                declarations += "state int second = useSelect(first, value => value + 1);\n";
                expression += " + \":\" + second.ToString()";
            }
        }

        return declarations + "<TextBlock Text={ReadText(" + expression + ")} />";
    }

    private const string OwnerSource =
        """
        using System;
        using Akbura;
        using Akbura.ComponentTree;
        using Akbura.Engine;
        using System.Collections.Immutable;
        namespace Demo;
        public partial class Counter : AkburaControl
        {
            public Counter() : base(AkburaEngine.Empty) { }
            public bool ThrowFromRender { get; set; }
            public ImmutableArray<State> StatesForTest => GetStates();
            public ImmutableArray<State> LastPreparedStates { get; private set; }
            private string ReadText(string value)
            {
                LastPreparedStates = GetStates();
                if (ThrowFromRender)
                {
                    throw new InvalidOperationException("render failed");
                }
                return value;
            }
        }
        """;
}
