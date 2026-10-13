using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.Hooks;
using Akbura.HotReload;
using Avalonia;
using Avalonia.Controls;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class HookStateHotReloadLifecycleTests
{
    [Fact]
    public async Task SuspendedRefresh_IsAcknowledgedOnlyAfterItsHookFrameCommits()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var component = new SuspendedRefreshComponent();
            var window = new Window { Content = component };
            component.HostWindow = window;

            try
            {
                window.Show();
                var previousState = component.HookState;
                var committedFrames = component.CommittedFrames;
                Assert.NotNull(previousState);

                AkburaHotReloadRuntime.Refresh<SuspendedRefreshComponent>(
                    static control => control.DetachDuringUpdate = true);

                Assert.Null(window.Content);
                Assert.Null(component.HookState);
                Assert.False(previousState.IsAttached);
                Assert.Equal(committedFrames, component.CommittedFrames);
                Assert.Equal(1, component.AbortedFrames);

                component.ThrowOnUpdate = true;
                var failure = Assert.Throws<InvalidOperationException>(() => window.Content = component);
                Assert.Equal("Reattached hook frame failed.", failure.Message);
                component.ThrowOnUpdate = false;
                var attemptsBeforeRetry = component.UpdateAttempts;

                Assert.True(AkburaHotReloadRuntime.ApplyPendingRefreshes(component));
                Assert.Equal(attemptsBeforeRetry + 1, component.UpdateAttempts);
                Assert.NotNull(component.HookState);
                Assert.True(component.HookState.IsAttached);
                Assert.NotSame(previousState, component.HookState);
                Assert.Equal(committedFrames + 1, component.CommittedFrames);
                Assert.False(AkburaHotReloadRuntime.ApplyPendingRefreshes(component));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private sealed class SuspendedRefreshComponent : AkburaControl
    {
        public SuspendedRefreshComponent() : base(AkburaEngine.Empty)
        {
        }

        public Window? HostWindow { get; set; }

        public bool DetachDuringUpdate { get; set; }

        public bool ThrowOnUpdate { get; set; }

        public int UpdateAttempts { get; private set; }

        public int CommittedFrames { get; private set; }

        public int AbortedFrames { get; private set; }

        public State<int>? HookState => RuntimeStates.GetBoundOrNull<int>("hook");

        protected override void PrepareHookStates()
        {
            RuntimeStates.BeginFrame();
            RuntimeStates.StageHook("hook", BindHookState(this.useHookState(1), HookState));
        }

        protected override void CommitHookStates()
        {
            RuntimeStates.CommitFrame();
            CommittedFrames++;
        }

        protected override void AbortHookStates()
        {
            RuntimeStates.AbortFrame();
            AbortedFrames++;
        }

        protected override void ResetHookStatesForHotReload()
        {
            RuntimeStates.ResetHookBindingsForHotReload();
        }

        protected override Control FirstUpdate() => new Border();

        protected override Control Update()
        {
            UpdateAttempts++;
            if (ThrowOnUpdate)
            {
                throw new InvalidOperationException("Reattached hook frame failed.");
            }

            if (DetachDuringUpdate)
            {
                DetachDuringUpdate = false;
                HostWindow!.Content = null;
            }

            return new Border();
        }

        protected override ImmutableArray<Parameter> GetParameters() => [];

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];

        protected override ImmutableArray<InjectService> GetServices() => [];

        protected override ImmutableArray<State> GetStates() =>
            RuntimeStates.GetSnapshot(() => HookState is { } state ? [state] : []);
    }
}
