using System.Collections.Immutable;
using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.Hooks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AnimationHooksTests
{
    [Fact]
    public Task AnimationFactory_RunsAfterTheFrameAndDoesNotRestartForFreshDelegates() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var runner = new RecordingRunner();
        var factories = 0;
        component.RenderFrame = control => AnimationHooks.useAnimation(
            control, control.Target, () =>
            {
                Assert.False(control.IsRendering);
                factories++;
                return new Animation();
            }, [], true, runner.Run);

        component.InitializeForTest();
        component.RenderAgain();
        component.RenderAgain();

        Assert.Equal(1, factories);
        Assert.Single(runner.Runs);
        Assert.False(runner.Runs[0].Token.IsCancellationRequested);
    });

    [Fact]
    public Task DependencyChanges_CancelThePreviousRunAndCopyTheDependencyArray() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var runner = new RecordingRunner();
        object?[] dependencies = [1];
        component.RenderFrame = control => AnimationHooks.useAnimation(
            control, control.Target, static () => new Animation(), dependencies, true, runner.Run);

        component.InitializeForTest();
        dependencies[0] = 2;
        component.RenderAgain();

        Assert.Equal(2, runner.Runs.Count);
        Assert.True(runner.Runs[0].Token.IsCancellationRequested);
        Assert.False(runner.Runs[1].Token.IsCancellationRequested);
        component.RenderAgain();
        Assert.Equal(2, runner.Runs.Count);
    });

    [Fact]
    public Task AbortedFrames_NeitherCreateAnAnimationNorCancelTheCommittedRun() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var runner = new RecordingRunner();
        var version = 0;
        var factories = 0;
        component.RenderFrame = control => AnimationHooks.useAnimation(
            control, control.Target, () =>
            {
                factories++;
                return new Animation();
            }, [version], true, runner.Run);
        component.InitializeForTest();

        version++;
        component.FailRender = true;
        Assert.Throws<ExpectedRenderException>(component.RenderAgain);

        Assert.Equal(1, factories);
        Assert.Single(runner.Runs);
        Assert.False(runner.Runs[0].Token.IsCancellationRequested);
        component.FailRender = false;
        component.RenderAgain();
        Assert.Equal(2, factories);
        Assert.True(runner.Runs[0].Token.IsCancellationRequested);
    });

    [Fact]
    public Task DisabledAnimation_SkipsTheFactoryAndPreservesBaseValues() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var runner = new RecordingRunner();
        var enabled = false;
        var factories = 0;
        component.Target.Opacity = 0.4;
        component.RenderFrame = control => AnimationHooks.useAnimation(
            control, control.Target, () =>
            {
                factories++;
                return new Animation();
            }, [], enabled, runner.Run);
        component.InitializeForTest();
        Assert.Empty(runner.Runs);
        Assert.Equal(0, factories);
        Assert.Equal(0.4, component.Target.Opacity);

        enabled = true;
        component.RenderAgain();
        Assert.Equal(1, factories);
        Assert.Single(runner.Runs);
        enabled = false;
        component.RenderAgain();

        Assert.True(runner.Runs[0].Token.IsCancellationRequested);
        Assert.Single(runner.Runs);
        Assert.Equal(0.4, component.Target.Opacity);
    });

    [Fact]
    public Task ReplacingTheTarget_RestartsOnlyAfterTheReplacementIsCommitted() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var runner = new RecordingRunner();
        Animatable target = component.Target;
        component.RenderFrame = control => AnimationHooks.useAnimation(
            control, target, static () => new Animation(), [], true, runner.Run);
        component.InitializeForTest();
        var replacement = new Border();
        target = replacement;
        component.FailRender = true;
        Assert.Throws<ExpectedRenderException>(component.RenderAgain);
        Assert.Single(runner.Runs);
        Assert.False(runner.Runs[0].Token.IsCancellationRequested);

        component.FailRender = false;
        component.RenderAgain();
        Assert.Equal(2, runner.Runs.Count);
        Assert.True(runner.Runs[0].Token.IsCancellationRequested);
        Assert.Same(replacement, runner.Runs[1].Target);
    });

    [Fact]
    public Task TargetDetach_StopsPlaybackWithoutReportingAnEffectFailure() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var runner = new RecordingRunner();
        component.RenderFrame = control => AnimationHooks.useAnimation(
            control, control.Target, static () => new Animation(), [], true, runner.Run);
        var window = new Window { Content = component };
        try
        {
            window.Show();
            Assert.Single(runner.Runs);
            component.Root.Child = null;
            Assert.True(runner.Runs[0].Token.IsCancellationRequested);
            Dispatcher.UIThread.RunJobs();
            Assert.True(component.IsAttachedToVisualTree());
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task OwnerDetach_CancelsAndReattachmentCreatesAFreshAnimation() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var runner = new RecordingRunner();
        component.RenderFrame = control => AnimationHooks.useAnimation(
            control, control.Target, static () => new Animation(), [], true, runner.Run);
        var window = new Window { Content = component };
        try
        {
            window.Show();
            window.Content = null;
            Assert.True(runner.Runs[0].Token.IsCancellationRequested);
            window.Content = component;
            Assert.Equal(2, runner.Runs.Count);
            Assert.False(runner.Runs[1].Token.IsCancellationRequested);
        }
        finally
        {
            window.Close();
        }

        Assert.True(runner.Runs[1].Token.IsCancellationRequested);
    });

    [Fact]
    public Task FailedFirstFrame_DoesNotStartPlayback() => OnDispatcher(() =>
    {
        var component = new HookComponent { FailRender = true };
        var runner = new RecordingRunner();
        var factories = 0;
        component.RenderFrame = control => AnimationHooks.useAnimation(
            control, control.Target, () =>
            {
                factories++;
                return new Animation();
            }, [], true, runner.Run);

        Assert.Throws<ExpectedRenderException>(component.InitializeForTest);
        Assert.Equal(0, factories);
        Assert.Empty(runner.Runs);
        component.FailRender = false;
        component.InitializeForTest();
        Assert.Equal(1, factories);
        Assert.Single(runner.Runs);
    });

    [Fact]
    public Task Resolver_UsesTheNewTargetCreatedAfterRenderStatements() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var runner = new RecordingRunner();
        Animatable? target = null;
        var replacement = new Border();
        var resolutions = 0;
        component.RenderFrame = control =>
        {
            AnimationHooks.useAnimation(control, () =>
            {
                Assert.False(control.IsRendering);
                resolutions++;
                return target;
            }, static () => new Animation(), [], true, runner.Run);
            target = replacement;
        };
        component.InitializeForTest();
        Assert.Same(replacement, runner.Runs[0].Target);
        component.RenderAgain();
        Assert.Single(runner.Runs);

        replacement = new Border();
        component.RenderAgain();

        Assert.Equal(2, runner.Runs.Count);
        Assert.True(runner.Runs[0].Token.IsCancellationRequested);
        Assert.Same(replacement, runner.Runs[1].Target);
        Assert.Equal(3, resolutions);
    });

    [Fact]
    public Task Resolver_AbsentAndDisabledTargetsAreCachedWithoutCallingTheFactory() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var runner = new RecordingRunner();
        Animatable? target = null;
        var enabled = false;
        var factories = 0;
        component.RenderFrame = control => AnimationHooks.useAnimation(control, () => target, () =>
        {
            factories++;
            return new Animation();
        }, [], enabled, runner.Run);
        component.InitializeForTest();
        target = component.Target;
        enabled = true;
        component.RenderAgain();
        Assert.Single(runner.Runs);
        Assert.Equal(1, factories);

        target = null;
        component.RenderAgain();
        Assert.True(runner.Runs[0].Token.IsCancellationRequested);
        Assert.Single(runner.Runs);
        Assert.Equal(1, factories);
        target = new Border();
        component.RenderAgain();
        Assert.Equal(2, runner.Runs.Count);
        Assert.Equal(2, factories);
    });

    [Fact]
    public Task AbortedResolver_IsNeverEvaluated() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var runner = new RecordingRunner();
        var resolutions = 0;
        component.RenderFrame = control => AnimationHooks.useAnimation(control, () =>
        {
            resolutions++;
            return control.Target;
        }, static () => new Animation(), [], true, runner.Run);
        component.InitializeForTest();
        component.FailRender = true;

        Assert.Throws<ExpectedRenderException>(component.RenderAgain);
        Assert.Equal(1, resolutions);
        Assert.Single(runner.Runs);
        Assert.False(runner.Runs[0].Token.IsCancellationRequested);
    });

    [Fact]
    public Task FactoryFailures_AreReportedAndTheSameDependenciesCanBeRetried() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var runner = new RecordingRunner();
        var fail = true;
        component.RenderFrame = control => AnimationHooks.useAnimation(control, control.Target, () =>
        {
            if (fail)
            {
                throw new ExpectedAnimationException();
            }

            return new Animation();
        }, [], true, runner.Run);
        component.InitializeForTest();
        Assert.Throws<ExpectedAnimationException>(() => Dispatcher.UIThread.RunJobs());
        Assert.Empty(runner.Runs);

        fail = false;
        component.RenderAgain();
        Assert.Single(runner.Runs);
    });

    [Fact]
    public Task InfiniteAnimations_AreRejectedBeforeStartingTheEngine() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var runner = new RecordingRunner();
        component.RenderFrame = control => AnimationHooks.useAnimation(
            control, control.Target,
            static () => new Animation { IterationCount = IterationCount.Infinite }, [], true, runner.Run);
        component.InitializeForTest();

        Assert.Throws<NotSupportedException>(() => Dispatcher.UIThread.RunJobs());
        Assert.Empty(runner.Runs);
    });

    [Fact]
    public Task NullFactoriesAndRunnerFailures_AreNotSilentlyIgnored() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var runner = new RecordingRunner();
        component.RenderFrame = control => AnimationHooks.useAnimation(
            control, control.Target, static () => null!, [], true, runner.Run);
        component.InitializeForTest();
        Assert.Throws<InvalidOperationException>(() => Dispatcher.UIThread.RunJobs());
        Assert.Empty(runner.Runs);

        var failure = new ExpectedAnimationException();
        component.RenderFrame = control => AnimationHooks.useAnimation(
            control, control.Target, static () => new Animation(), [], true,
            (_, _, _) => Task.FromException(failure));
        component.RenderAgain();
        Assert.Same(failure, Assert.Throws<ExpectedAnimationException>(() => Dispatcher.UIThread.RunJobs()));
    });

    private static async Task OnDispatcher(Action test)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(test, CancellationToken.None);
    }

    private sealed class RecordingRunner
    {
        public List<RecordedRun> Runs { get; } = [];

        public Task Run(Animation animation, Animatable target, CancellationToken token)
        {
            var completion = new TaskCompletionSource();
            Runs.Add(new RecordedRun(target, token));
            token.Register(() => completion.TrySetResult());
            return completion.Task;
        }
    }

    private sealed record RecordedRun(Animatable Target, CancellationToken Token);

    private sealed class HookComponent : AkburaControl
    {
        public HookComponent() : base(AkburaEngine.Empty)
        {
            Root.Child = Target;
        }

        public Border Root { get; } = new();
        public Border Target { get; } = new();
        public Action<HookComponent>? RenderFrame { get; set; }
        public bool FailRender { get; set; }
        public bool IsRendering { get; private set; }
        public void InitializeForTest() => base.OnInitialized();
        public void RenderAgain() => InvalidState();
        protected override Control FirstUpdate() => Root;

        protected override Control Update()
        {
            IsRendering = true;
            try
            {
                RenderFrame?.Invoke(this);
                if (FailRender)
                {
                    throw new ExpectedRenderException();
                }

                return Root;
            }
            finally
            {
                IsRendering = false;
            }
        }

        protected override ImmutableArray<Parameter> GetParameters() => [];
        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];
        protected override ImmutableArray<InjectService> GetServices() => [];
        protected override ImmutableArray<State> GetStates() => [];
    }

    private sealed class ExpectedRenderException : Exception;
    private sealed class ExpectedAnimationException : Exception;
}
