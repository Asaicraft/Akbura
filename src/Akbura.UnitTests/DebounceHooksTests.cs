using System.Collections.Immutable;
using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.Hooks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DebounceHooksTests
{
    [Fact]
    public Task ValueChanges_RestartTheFullDelayAndKeepTheResultState() =>
        OnDispatcher(async () =>
        {
            var clock = new ManualTimeProvider();
            var component = new DebounceComponent();
            State<int>? result = null;
            component.RenderFrame = control => result = DebounceHooks.useDebounce(
                control, control.Source, TimeSpan.FromMilliseconds(300), clock);
            component.InitializeForTest();
            var initialResult = Assert.IsType<State<int>>(result);
            Assert.Equal(1, initialResult.Value);

            clock.Advance(200);
            component.Source.Value = 2;
            Assert.Same(initialResult, result);
            Assert.Equal(1, result.Value);
            Assert.Equal(2, clock.CreatedTimerCount);

            clock.Advance(299);
            await DrainDispatcher();
            Assert.Equal(1, result.Value);

            var publication = ObserveValue(result, 2);
            clock.Advance(1);
            await publication;
            await DrainDispatcher();

            Assert.Same(initialResult, result);
            Assert.Equal(2, result.Value);
            Assert.Equal(2, clock.CreatedTimerCount);
            Assert.Equal(0, clock.ActiveTimerCount);
        });

    [Fact]
    public Task Selector_InitializesOnceAndRetainsTheLaunchingFramesSelector() =>
        OnDispatcher(async () =>
        {
            var clock = new ManualTimeProvider();
            var component = new DebounceComponent();
            State<int>? result = null;
            var multiplier = 10;
            var selectorCalls = 0;
            component.RenderFrame = control =>
            {
                var frameMultiplier = multiplier;
                result = DebounceHooks.useDebounce(
                    control,
                    control.Source,
                    value =>
                    {
                        selectorCalls++;
                        return value * frameMultiplier;
                    },
                    TimeSpan.FromMilliseconds(300),
                    clock);
            };
            component.InitializeForTest();
            Assert.Equal(10, result!.Value);
            Assert.Equal(1, selectorCalls);

            component.Source.Value = 2;
            clock.Advance(200);
            multiplier = 100;
            component.RenderAgain();
            Assert.Equal(1, selectorCalls);
            Assert.Equal(2, clock.CreatedTimerCount);

            var publication = ObserveValue(result, 20);
            clock.Advance(100);
            await publication;

            Assert.Equal(20, result.Value);
            Assert.Equal(2, selectorCalls);
        });

    [Fact]
    public Task MultipleDebouncesAndComponents_HaveIndependentResultsAndTimers() =>
        OnDispatcher(async () =>
        {
            var clock = new ManualTimeProvider();
            var first = new DebounceComponent();
            var second = new DebounceComponent();
            State<int>? fast = null;
            State<int>? slow = null;
            State<int>? other = null;
            first.RenderFrame = control =>
            {
                fast = DebounceHooks.useDebounce(
                    control, control.Source, TimeSpan.FromMilliseconds(100), clock);
                slow = DebounceHooks.useDebounce(
                    control, control.Source, TimeSpan.FromMilliseconds(300), clock);
            };
            second.RenderFrame = control => other = DebounceHooks.useDebounce(
                control, control.Source, TimeSpan.FromMilliseconds(100), clock);
            first.InitializeForTest();
            second.InitializeForTest();
            Assert.NotSame(fast, slow);
            Assert.NotSame(fast, other);

            first.Source.Value = 7;
            second.Source.Value = 9;
            var fastPublication = ObserveValue(fast!, 7);
            var otherPublication = ObserveValue(other!, 9);
            clock.Advance(100);
            await Task.WhenAll(fastPublication, otherPublication);
            Assert.Equal(1, slow!.Value);

            var slowPublication = ObserveValue(slow, 7);
            clock.Advance(200);
            await slowPublication;
            Assert.Equal(7, fast!.Value);
            Assert.Equal(9, other!.Value);
        });

    [Fact]
    public Task ReplacingTheSourceWithAnEqualValue_RestartsTheDelay() =>
        OnDispatcher(async () =>
        {
            var clock = new ManualTimeProvider();
            var component = new DebounceComponent();
            var selectorCalls = 0;
            var selectedAgain = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            component.RenderFrame = control => DebounceHooks.useDebounce(
                control,
                control.Source,
                value =>
                {
                    selectorCalls++;
                    if (selectorCalls == 2)
                    {
                        selectedAgain.SetResult();
                    }

                    return value;
                },
                TimeSpan.FromMilliseconds(300),
                clock);
            component.InitializeForTest();
            clock.Advance(200);

            component.Source = new State<int>(1);
            component.RenderAgain();
            Assert.Equal(2, clock.CreatedTimerCount);
            Assert.Equal(1, clock.ActiveTimerCount);
            clock.Advance(100);
            await DrainDispatcher();
            Assert.Equal(1, selectorCalls);

            clock.Advance(200);
            await selectedAgain.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, selectorCalls);
        });

    [Fact]
    public Task DelayChanges_RestartEvenWithUnchangedDependencies() =>
        OnDispatcher(async () =>
        {
            var clock = new ManualTimeProvider();
            var component = new DebounceComponent();
            var delay = TimeSpan.FromMilliseconds(300);
            var completed = new TaskCompletionSource();
            component.RenderFrame = control => DebounceHooks.useDebounce(
                control,
                _ =>
                {
                    completed.SetResult();
                    return Task.CompletedTask;
                },
                delay,
                [],
                clock);
            component.InitializeForTest();
            clock.Advance(200);
            delay = TimeSpan.FromMilliseconds(1000);
            component.RenderAgain();

            clock.Advance(999);
            await DrainDispatcher();
            Assert.False(completed.Task.IsCompleted);

            clock.Advance(1);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, clock.CreatedTimerCount);
        });

    [Fact]
    public Task CallbackDependencies_AreCopiedAndUnrelatedRendersKeepTheLaunchingCallback() =>
        OnDispatcher(async () =>
        {
            var clock = new ManualTimeProvider();
            var component = new DebounceComponent();
            var completed = new TaskCompletionSource<int>();
            var renderedValue = 1;
            component.RenderFrame = control =>
            {
                var snapshot = renderedValue;
                object?[] dependencies = [42];
                DebounceHooks.useDebounce(
                    control,
                    _ =>
                    {
                        completed.SetResult(snapshot);
                        return Task.CompletedTask;
                    },
                    TimeSpan.FromMilliseconds(300),
                    dependencies,
                    clock);
                dependencies[0] = 100;
            };
            component.InitializeForTest();
            clock.Advance(200);
            renderedValue = 2;
            component.RenderAgain();
            Assert.Equal(1, clock.CreatedTimerCount);

            clock.Advance(100);
            Assert.Equal(1, await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public Task ZeroDelay_PublicCallbackOverloadsAlwaysQueueOnTheUiThread(int overload) =>
        OnDispatcher(async () =>
        {
            var component = new DebounceComponent();
            var completed = new TaskCompletionSource<bool>();
            Action callback = () => completed.SetResult(Dispatcher.UIThread.CheckAccess());
            Func<CancellationToken, Task> asyncCallback = _ =>
            {
                callback();
                return Task.CompletedTask;
            };
            component.RenderFrame = control =>
            {
                switch (overload)
                {
                    case 0:
                        control.useDebounce(callback, 0, []);
                        break;
                    case 1:
                        control.useDebounce(callback, TimeSpan.Zero, []);
                        break;
                    case 2:
                        control.useDebounce(asyncCallback, 0, []);
                        break;
                    default:
                        control.useDebounce(asyncCallback, TimeSpan.Zero, []);
                        break;
                }

                Assert.False(completed.Task.IsCompleted);
            };
            component.InitializeForTest();
            Assert.False(completed.Task.IsCompleted);
            Assert.True(await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        });

    [Fact]
    public Task DependencyChange_CancelsTheCallbackAlreadyQueuedOnTheDispatcher() =>
        OnDispatcher(async () =>
        {
            var component = new DebounceComponent();
            var values = new List<int>();
            component.RenderFrame = control =>
            {
                var value = control.Source.Value;
                control.useDebounce(() => values.Add(value), 0, [value]);
            };
            component.InitializeForTest();
            component.Source.Value = 2;
            await DrainDispatcher();
            Assert.Equal([2], values);
        });

    [Fact]
    public Task DependencyChange_CancelsAnAlreadyRunningAsyncCallback() =>
        OnDispatcher(async () =>
        {
            var component = new DebounceComponent();
            var started = new TaskCompletionSource<CancellationToken>();
            var stopped = new TaskCompletionSource();
            var restarted = new TaskCompletionSource();
            component.RenderFrame = control =>
            {
                var value = control.Source.Value;
                control.useDebounce(
                    async cancellationToken =>
                    {
                        if (value == 1)
                        {
                            started.SetResult(cancellationToken);
                            try
                            {
                                await Task.Delay(Timeout.Infinite, cancellationToken);
                            }
                            finally
                            {
                                stopped.SetResult();
                            }
                        }
                        else
                        {
                            restarted.SetResult();
                        }
                    },
                    0,
                    [value]);
            };
            component.InitializeForTest();
            var firstToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(firstToken.IsCancellationRequested);

            component.Source.Value = 2;
            await Task.WhenAll(stopped.Task, restarted.Task).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(firstToken.IsCancellationRequested);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AsyncFailureAndNullTask_AreObservedByTheEffectRuntime(bool returnNull) =>
        OnDispatcher(async () =>
        {
            var component = new DebounceComponent();
            var started = new TaskCompletionSource();
            var callbackCompletion = new TaskCompletionSource();
            var observed = new TaskCompletionSource<Exception>();
            void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
            {
                args.Handled = true;
                observed.TrySetResult(args.Exception);
            }

            Dispatcher.UIThread.UnhandledException += OnUnhandledException;
            try
            {
                component.RenderFrame = control => control.useDebounce(
                    _ =>
                    {
                        started.SetResult();
                        return returnNull ? null! : callbackCompletion.Task;
                    },
                    0,
                    []);
                component.InitializeForTest();
                await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if (!returnNull)
                {
                    callbackCompletion.SetException(new ExpectedCallbackException());
                }

                var exception = await observed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if (returnNull)
                {
                    Assert.IsType<InvalidOperationException>(exception);
                    Assert.Contains("null Task", exception.Message);
                }
                else
                {
                    Assert.IsType<ExpectedCallbackException>(exception);
                }
            }
            finally
            {
                Dispatcher.UIThread.UnhandledException -= OnUnhandledException;
            }
        });

    [Fact]
    public Task DetachAndReattach_CancelPendingWorkAndRestartFromTheLatestSource() =>
        OnDispatcher(async () =>
        {
            var clock = new ManualTimeProvider();
            var component = new DebounceComponent();
            State<int>? result = null;
            component.RenderFrame = control => result = DebounceHooks.useDebounce(
                control, control.Source, TimeSpan.FromMilliseconds(300), clock);
            var window = new Window { Content = component };
            window.Show();
            try
            {
                var originalResult = Assert.IsType<State<int>>(result);
                component.Source.Value = 2;
                clock.Advance(200);
                window.Content = null;
                Assert.Equal(0, clock.ActiveTimerCount);

                component.Source.Value = 3;
                clock.Advance(1000);
                await DrainDispatcher();
                Assert.Equal(1, originalResult.Value);
                Assert.Equal(0, clock.ActiveTimerCount);

                window.Content = component;
                Assert.Same(originalResult, result);
                Assert.Equal(1, clock.ActiveTimerCount);
                clock.Advance(299);
                await DrainDispatcher();
                Assert.Equal(1, result.Value);

                var publication = ObserveValue(result, 3);
                clock.Advance(1);
                await publication;
                Assert.Equal(3, result.Value);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void InvalidDelays_AreRejectedBeforeRegisteringAnyHooks()
    {
        var component = new DebounceComponent();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            component.useDebounce(component.Source, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            component.useDebounce(component.Source, static value => value, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            component.useDebounce(static () => { }, -1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            component.useDebounce(static _ => Task.CompletedTask, -1, []));

        var tooLong = TimeSpan.FromMilliseconds(int.MaxValue) + TimeSpan.FromTicks(1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            component.useDebounce(component.Source, tooLong));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            component.useDebounce(component.Source, static value => value, tooLong));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            component.useDebounce(static () => { }, tooLong, []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            component.useDebounce(static _ => Task.CompletedTask, tooLong, []));
    }

    private static async Task OnDispatcher(Func<Task> test)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(async () =>
        {
            await test();
            return true;
        }, CancellationToken.None);
    }

    private static async Task DrainDispatcher()
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
    }

    private static async Task ObserveValue(State<int> state, int expected)
    {
        var completion = new TaskCompletionSource();
        using var subscription = state.Subscribe(value =>
        {
            if (value == expected)
            {
                completion.TrySetResult();
            }
        });
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class DebounceComponent : AkburaControl
    {
        private static readonly StateInfo<int> s_sourceInfo = new("source", static _ => 1);
        private readonly ImmutableArray<State> _states;
        private readonly Border _root = new();

        public DebounceComponent()
            : base(AkburaEngine.Empty)
        {
            Source = CreateState(s_sourceInfo);
            _states = [Source];
        }

        public State<int> Source { get; set; }

        public Action<DebounceComponent>? RenderFrame { get; set; }

        public void InitializeForTest() => base.OnInitialized();

        public void RenderAgain() => InvalidState();

        protected override Control FirstUpdate() => _root;

        protected override Control Update()
        {
            RenderFrame?.Invoke(this);
            return _root;
        }

        protected override ImmutableArray<Parameter> GetParameters() => [];

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];

        protected override ImmutableArray<InjectService> GetServices() => [];

        protected override ImmutableArray<State> GetStates() => _states;
    }

    private sealed class ExpectedCallbackException : Exception;

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private long _ticks;

        public int CreatedTimerCount => _timers.Count;

        public int ActiveTimerCount => _timers.Count(timer => timer.DueAt != long.MaxValue);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(int milliseconds)
        {
            _ticks += TimeSpan.FromMilliseconds(milliseconds).Ticks;
            foreach (var timer in _timers.ToArray())
            {
                if (timer.DueAt <= _ticks)
                {
                    timer.Fire();
                }
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider clock,
            TimerCallback callback,
            object? state) : ITimer
        {
            public long DueAt { get; private set; } = long.MaxValue;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                DueAt = dueTime == Timeout.InfiniteTimeSpan
                    ? long.MaxValue
                    : clock._ticks + dueTime.Ticks;
                return true;
            }

            public void Fire()
            {
                DueAt = long.MaxValue;
                callback(state);
            }

            public void Dispose() => DueAt = long.MaxValue;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
