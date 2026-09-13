using System.Collections.Immutable;
using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.Hooks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AnimationControllerTests
{
    [Fact]
    public Task UseAnimate_IsStableAndIndependentAndInactiveDuringInitialRender() =>
        OnDispatcher(async () =>
        {
            var component = new HookComponent();
            State<AnimationController>? first = null;
            State<AnimationController>? second = null;
            component.RenderFrame = control =>
            {
                first = control.useAnimate();
                second = control.useAnimate();
                if (control.RenderCount == 1)
                {
                    Assert.Throws<InvalidOperationException>(() =>
                    {
                        _ = first.Value.RunAsync(new Border(), CreateAnimation());
                    });
                }
            };
            component.InitializeForTest();
            var controller = first!.Value;
            component.RenderAgain();
            Assert.Same(controller, first.Value);
            Assert.NotSame(controller, second!.Value);
            controller.Stop();
            second.Value.Stop();
            await DrainDispatcher();
        });

    [Fact]
    public Task UseAnimate_AbortedInitialFrameNeverActivatesAController() =>
        OnDispatcher(async () =>
        {
            var component = new HookComponent { FailFrame = true };
            State<AnimationController>? result = null;
            component.RenderFrame = control => result = control.useAnimate();
            Assert.Throws<ExpectedFrameException>(component.InitializeForTest);
            var provisional = result!.Value;
            Assert.Throws<InvalidOperationException>(provisional.Stop);
            component.FailFrame = false;
            component.InitializeForTest();
            Assert.NotSame(provisional, result.Value);
            result.Value.Stop();
            Assert.Throws<InvalidOperationException>(provisional.Stop);
            await DrainDispatcher();
        });

    [Fact]
    public Task UseAnimate_OwnerDetachStopsRunsAndReattachReactivatesStableController() =>
        OnDispatcher(async () =>
        {
            var component = new HookComponent();
            State<AnimationController>? result = null;
            component.RenderFrame = control => result = control.useAnimate();
            var window = new Window { Content = component };
            window.Show();
            try
            {
                var controller = result!.Value;
                var run = controller.RunAsync(new Border(), CreateAnimation());
                window.Content = null;
                await AssertCanceled(run);
                Assert.Throws<InvalidOperationException>(controller.Stop);
                window.Content = component;
                await DrainDispatcher();
                Assert.Same(controller, result.Value);
                controller.Stop();
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task UseAnimate_HotReloadLeavesTheOldControllerInactive() =>
        OnDispatcher(async () =>
        {
            var component = new HookComponent();
            State<AnimationController>? result = null;
            component.RenderFrame = control => result = control.useAnimate();
            component.InitializeForTest();
            var previous = result!.Value;
            var run = previous.RunAsync(new Border(), CreateAnimation());
            component.ApplyHotReload(static _ => { });
            await AssertCanceled(run);
            Assert.NotSame(previous, result.Value);
            Assert.Throws<InvalidOperationException>(previous.Stop);
            result.Value.Stop();
        });

    [Fact]
    public Task SameTargetOverlappingPropertiesCancelOnlyEarlierOwnedRuns() =>
        OnDispatcher(async () =>
        {
            var runner = new ManualRunner();
            var controller = new AnimationController(runner.RunAsync);
            using var lifetime = controller.Activate(true);
            var target = new Border();
            var opacity = controller.RunAsync(target, CreateAnimation());
            var margin = controller.RunAsync(target, CreateAnimation(Layoutable.MarginProperty));
            var newerOpacity = controller.RunAsync(target, CreateAnimation());
            await AssertCanceled(opacity);
            Assert.False(runner.Runs[1].Token.IsCancellationRequested);
            Assert.False(runner.Runs[2].Token.IsCancellationRequested);
            runner.Complete(1);
            runner.Complete(2);
            await margin;
            await newerOpacity;
        });

    [Fact]
    public Task OverlapAcrossDifferentControllersOrTargetsDoesNotCancel() =>
        OnDispatcher(async () =>
        {
            var runner = new ManualRunner();
            var first = new AnimationController(runner.RunAsync);
            var second = new AnimationController(runner.RunAsync);
            using var firstLifetime = first.Activate(true);
            using var secondLifetime = second.Activate(true);
            var target = new Border();
            var a = first.RunAsync(target, CreateAnimation());
            var b = first.RunAsync(new Border(), CreateAnimation());
            var c = second.RunAsync(target, CreateAnimation());
            Assert.All(runner.Runs, run => Assert.False(run.Token.IsCancellationRequested));
            first.Stop(target);
            await AssertCanceled(a);
            Assert.False(runner.Runs[1].Token.IsCancellationRequested);
            Assert.False(runner.Runs[2].Token.IsCancellationRequested);
            runner.Complete(1);
            runner.Complete(2);
            await Task.WhenAll(b, c);
        });

    [Fact]
    public Task StopAllCancelsEveryRunAndNormalEngineCancellationIsNotSuccess() =>
        OnDispatcher(async () =>
        {
            var runner = new ManualRunner();
            var controller = new AnimationController(runner.RunAsync);
            using var lifetime = controller.Activate(true);
            var first = controller.RunAsync(new Border(), CreateAnimation());
            var second = controller.RunAsync(new Border(), CreateAnimation());
            controller.Stop();
            await AssertCanceled(first);
            await AssertCanceled(second);
            Assert.All(runner.Runs, run => Assert.True(run.Token.IsCancellationRequested));
            controller.Stop();
        });

    [Fact]
    public Task LifetimeCleanupCancelsAndRejectsSubsequentOperations() =>
        OnDispatcher(async () =>
        {
            var runner = new ManualRunner();
            var controller = new AnimationController(runner.RunAsync);
            var lifetime = controller.Activate(true);
            var run = controller.RunAsync(new Border(), CreateAnimation());
            lifetime.Dispose();
            lifetime.Dispose();
            await AssertCanceled(run);
            Assert.Throws<InvalidOperationException>(controller.Stop);
            Assert.Throws<InvalidOperationException>(() => controller.Stop(new Border()));
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = controller.RunAsync(new Border(), CreateAnimation());
            });
        });

    [Fact]
    public Task ChildDetachCancelsEvenWhenControllerLifetimeIsStillActive() =>
        OnDispatcher(async () =>
        {
            var runner = new ManualRunner();
            var controller = new AnimationController(runner.RunAsync);
            using var lifetime = controller.Activate(true);
            var panel = new StackPanel();
            var target = new Border();
            panel.Children.Add(target);
            var window = new Window { Content = panel };
            window.Show();
            try
            {
                var run = controller.RunAsync(target, CreateAnimation());
                panel.Children.Remove(target);
                await AssertCanceled(run);
                controller.Stop();
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task ExternalWorkerCancellationIsDeliveredOnTheUiThread() =>
        OnDispatcher(async () =>
        {
            var runner = new ManualRunner();
            var controller = new AnimationController(runner.RunAsync);
            using var lifetime = controller.Activate(true);
            using var cancellation = new CancellationTokenSource();
            var run = controller.RunAsync(new Border(), CreateAnimation(), cancellation.Token);
            var callbackOnUi = false;
            using var registration = runner.Runs[0].Token.Register(() =>
                callbackOnUi = Dispatcher.UIThread.CheckAccess());
            await Task.Run(cancellation.Cancel);
            await AssertCanceled(run);
            Assert.True(callbackOnUi);
        });

    [Fact]
    public Task AlreadyCanceledTokensAndInfiniteAnimationsNeverStartTheEngine() =>
        OnDispatcher(async () =>
        {
            var runner = new ManualRunner();
            var controller = new AnimationController(runner.RunAsync);
            using var lifetime = controller.Activate(true);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
            {
                _ = controller.RunAsync(new Border(), CreateAnimation(), cancellation.Token);
            });
            var infinite = CreateAnimation();
            infinite.IterationCount = IterationCount.Infinite;
            Assert.Throws<NotSupportedException>(() =>
            {
                _ = controller.RunAsync(new Border(), infinite);
            });
            Assert.Empty(runner.Runs);
            await DrainDispatcher();
        });

    [Fact]
    public Task InvalidSettersDoNotCancelAnExistingValidRun() =>
        OnDispatcher(async () =>
        {
            var runner = new ManualRunner();
            var controller = new AnimationController(runner.RunAsync);
            using var lifetime = controller.Activate(true);
            var target = new Border();
            var valid = controller.RunAsync(target, CreateAnimation());
            var invalid = CreateAnimation();
            invalid.Children[1].Setters.Add(new Setter());
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = controller.RunAsync(target, invalid);
            });
            Assert.False(runner.Runs[0].Token.IsCancellationRequested);
            Assert.Single(runner.Runs);
            runner.Complete(0);
            await valid;
        });

    [Fact]
    public Task DisabledMotionPreservesBaseValuesEvenWithForwardFill() =>
        OnDispatcher(async () =>
        {
            var runner = new ManualRunner();
            var controller = new AnimationController(runner.RunAsync);
            using var lifetime = controller.Activate(false);
            var target = new Border { Opacity = 0.7 };
            var animation = CreateAnimation();
            animation.FillMode = FillMode.Forward;
            await controller.RunAsync(target, animation);
            Assert.Empty(runner.Runs);
            Assert.Equal(0.7, target.Opacity);
        });

    [Fact]
    public Task EngineFailuresPropagateAndReleaseTheOwnedRun() =>
        OnDispatcher(async () =>
        {
            var failure = new ExpectedAnimationException();
            var controller = new AnimationController((_, _, _) => Task.FromException(failure));
            using var lifetime = controller.Activate(true);
            var actual = await Assert.ThrowsAsync<ExpectedAnimationException>(() =>
                controller.RunAsync(new Border(), CreateAnimation()));
            Assert.Same(failure, actual);
            controller.Stop();
        });

    [Fact]
    public Task NullEngineTasksAreRejectedAndReleaseTheOwnedRun() =>
        OnDispatcher(async () =>
        {
            var controller = new AnimationController((_, _, _) => null!);
            using var lifetime = controller.Activate(true);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                controller.RunAsync(new Border(), CreateAnimation()));
            controller.Stop();
        });

    [Fact]
    public Task PublicOperationsRejectWorkerThreadAccess() =>
        OnDispatcher(async () =>
        {
            var controller = new AnimationController();
            using var lifetime = controller.Activate(true);
            var target = new Border();
            var animation = CreateAnimation();
            await Task.Run(() =>
            {
                Assert.Throws<InvalidOperationException>(controller.Stop);
                Assert.Throws<InvalidOperationException>(() => controller.Stop(target));
                Assert.Throws<InvalidOperationException>(() =>
                {
                    _ = controller.RunAsync(target, animation);
                });
            });
        });

    [Theory]
    [InlineData(FillMode.None, 0.7)]
    [InlineData(FillMode.Forward, 1.0)]
    public Task RealAvaloniaAnimationCompletesWithoutRenderingTheOwner(
        FillMode fillMode,
        double expectedOpacity) => OnDispatcher(async () =>
        {
            var component = new HookComponent();
            State<AnimationController>? result = null;
            component.RenderFrame = control => result = control.useAnimate();
            var window = new Window { Content = component };
            window.Show();
            try
            {
                var renderCount = component.RenderCount;
                var target = component.Root;
                target.Opacity = 0.7;
                var animation = CreateAnimation();
                animation.Duration = TimeSpan.Zero;
                animation.FillMode = fillMode;
                var run = result!.Value.RunAsync(target, animation);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                await DrainDispatcher();
                await run.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(expectedOpacity, target.Opacity);
                Assert.Equal(renderCount, component.RenderCount);
            }
            finally
            {
                window.Close();
            }
        });

    private static Animation CreateAnimation(AvaloniaProperty? property = null)
    {
        property ??= Visual.OpacityProperty;
        object initialValue = property == Layoutable.MarginProperty ? new Thickness(0) : 0d;
        object finalValue = property == Layoutable.MarginProperty ? new Thickness(10) : 1d;
        return new Animation
        {
            Duration = TimeSpan.FromSeconds(10),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(property, initialValue) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(property, finalValue) }
                }
            }
        };
    }

    private static async Task AssertCanceled(Task run)
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(run.IsCanceled);
    }

    private static async Task DrainDispatcher()
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
    }

    private static async Task OnDispatcher(Func<Task> test)
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(async () =>
        {
            await test();
            return true;
        }, CancellationToken.None);
    }

    private sealed class ManualRunner
    {
        public List<Run> Runs { get; } = [];

        public Task RunAsync(Animation animation, Animatable target, CancellationToken token)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = token.Register(() => completion.TrySetResult());
            Runs.Add(new Run(token, completion, registration));
            return completion.Task;
        }

        public void Complete(int index)
        {
            Runs[index].Registration.Dispose();
            Runs[index].Completion.TrySetResult();
        }

        public sealed record Run(
            CancellationToken Token,
            TaskCompletionSource Completion,
            CancellationTokenRegistration Registration);
    }

    private sealed class HookComponent() : AkburaControl(AkburaEngine.Empty)
    {
        public Border Root { get; } = new();

        public Action<HookComponent>? RenderFrame { get; set; }

        public int RenderCount { get; private set; }

        public bool FailFrame { get; set; }

        public void InitializeForTest() => base.OnInitialized();

        public void RenderAgain() => InvalidState();

        protected override Control FirstUpdate() => Root;

        protected override Control Update()
        {
            RenderCount++;
            RenderFrame?.Invoke(this);
            if (FailFrame)
            {
                throw new ExpectedFrameException();
            }

            return Root;
        }

        protected override ImmutableArray<Parameter> GetParameters() => [];

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];

        protected override ImmutableArray<InjectService> GetServices() => [];

        protected override ImmutableArray<State> GetStates() => [];
    }

    private sealed class ExpectedFrameException : Exception;

    private sealed class ExpectedAnimationException : Exception;
}
